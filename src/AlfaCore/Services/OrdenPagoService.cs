using System.Data;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Orden de Pago a proveedores (OPG con medios del legacy PAGOS.vbp / FrmMvPagos). Con proforma graba PGFP.
// v1: comprobantes + créditos + medios (efectivo, banco, cheque propio) en pesos. Sin retenciones (v2).
public sealed partial class OrdenPagoService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events)
{
    /// <summary>Clave del menú legacy «Orden de pago y Liquidación» (D509012).</summary>
    public const string MenuKey = "D509012";
    public const string TcOrdenPago = "OPG";
    public const string TcProforma = "PGFP";

    private sealed class Usuario
    {
        public string Nombre { get; set; } = "";
        public string Unidad { get; set; } = "";
        public bool Admin { get; set; }
        public string Caja { get; set; } = "";
        public bool VerProforma { get; set; } = true;
    }

    private async Task<T> Ejecutar<T>(string accion, Func<SqlConnection, Usuario, Guid, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var session))
            throw new InvalidOperationException("Iniciá sesión en la base donde querés cargar la orden de pago.");
        var connection = sessions.GetConnectionString();
        try
        {
            if (!await permissions.HasAccessAsync(MenuKey, ct))
                throw new InvalidOperationException("No tenés permiso para cargar órdenes de pago.");
            VerificarSesion(session);
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync(ct);
            var usuario = await LeerUsuarioAsync(cn, null, users.GetCurrentUserName(), ct)
                ?? throw new InvalidOperationException("El usuario actual no está definido en la base activa.");
            return await work(cn, usuario, session, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var mensaje = ex is InvalidOperationException ? ex.Message : accion == "Grabar"
                ? "No se pudo registrar la orden de pago. Conservamos los datos para reintentar."
                : "No se pudieron consultar los datos de la orden de pago.";
            var code = await events.LogErrorAsync("OrdenPago", accion, ex, mensaje, ct: ct);
            throw new AppUserFacingException(mensaje, code, ex);
        }
    }

    private void VerificarSesion(Guid session)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != session)
            throw new OperationCanceledException("La sesión cambió. Abrí nuevamente la orden de pago en la base activa.");
    }

    private static CommandDefinition Cmd(string sql, object? args, SqlTransaction? tx, CancellationToken ct)
        => new(sql, args, tx, commandTimeout: 120, cancellationToken: ct);

    private static async Task<Usuario?> LeerUsuarioAsync(SqlConnection cn, SqlTransaction? tx, string nombre, CancellationToken ct)
    {
        // VerProforma puede no existir en bases viejas: sin la columna, se ve.
        var ver = await cn.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN COL_LENGTH('dbo.TA_USUARIOS','VerProforma') IS NULL THEN 0 ELSE 1 END;", null, tx, ct)) == 1
            ? "CONVERT(bit,CASE WHEN ISNULL(VerProforma,1)=0 THEN 0 ELSE 1 END)" : "CONVERT(bit,1)";
        return await cn.QuerySingleOrDefaultAsync<Usuario>(Cmd($"""
            SELECT LTRIM(RTRIM(NOMBRE)) Nombre,LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,CONVERT(bit,ISNULL(AdminCentral,0)) Admin,
                LTRIM(RTRIM(ISNULL(IdCaja,''))) Caja,{ver} VerProforma
            FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@nombre);
            """, new { nombre }, tx, ct));
    }

    /// <summary>Valor de TA_CONFIGURACION (VALOR, o ValorAux si es largo); "" si no existe.</summary>
    internal static async Task<string> ConfigAsync(SqlConnection cn, SqlTransaction? tx, string clave, CancellationToken ct)
        => (await cn.QueryFirstOrDefaultAsync<string>(Cmd("""
            SELECT TOP(1) COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux),'')
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE)))=UPPER(@clave);
            """, new { clave }, tx, ct)))?.Trim() ?? "";

    // ── Reglas puras (sin base) ──────────────────────────────────────────────────────────────

    /// <summary>Comprobantes proforma de compra: solo se pagan con PGFP.</summary>
    internal static bool EsDebitoProforma(string tc) => tc.Trim() == "FPC";
    /// <summary>Créditos proforma: NCCP y PGFP (solo se aplican a FPC).</summary>
    internal static bool EsCreditoProforma(string tc) => tc.Trim() is "NCCP" or "PGFP";

    /// <summary>Regla del legacy (chkProforma / MezclarProformas): con el tilde solo proformas, sin el tilde solo lo demás.</summary>
    internal static bool CorrespondeAlModo(OrdenPagoDocumento d, bool credito, bool proforma, bool mezcla)
        => mezcla || (credito ? EsCreditoProforma(d.Tc) : EsDebitoProforma(d.Tc)) == proforma;

    private static bool Dos(decimal v) => v == decimal.Round(v, 2);

    /// <summary>
    /// Valida importes y reparte el pago. Dinero = suma de medios; los créditos se aplican a las facturas por orden
    /// (como ActualizaAplicacionNCCB del legacy) y la orden de pago aplica el resto. Lo que sobra de los medios es anticipo.
    /// </summary>
    public static OrdenPagoReparto Repartir(OrdenPagoSolicitud s)
    {
        if (s.Operacion == Guid.Empty || string.IsNullOrWhiteSpace(s.Proveedor) || s.Fecha.TimeOfDay != TimeSpan.Zero)
            throw new InvalidOperationException("Revisá el proveedor y la fecha de la orden de pago.");
        if (s.Detalle?.Trim().Length > 100)
            throw new InvalidOperationException("El detalle de la orden de pago admite hasta 100 caracteres.");
        // Sin comprobantes la orden es un anticipo puro al proveedor: solo con medios de pago y con confirmación.
        if (s.Comprobantes.Count > 50 || s.Comprobantes.Any(x => x.Importe <= 0 || !Dos(x.Importe)) || s.Comprobantes.Select(x => x.Clave).Distinct().Count() != s.Comprobantes.Count)
            throw new InvalidOperationException("Elegí hasta 50 comprobantes distintos, con importes positivos de hasta dos decimales.");
        if (s.Comprobantes.Count == 0 && (s.Creditos.Count > 0 || !s.ConfirmaAnticipo))
            throw new InvalidOperationException("Para pagar sin comprobantes tenés que confirmar que es un anticipo al proveedor, y no aplicar créditos.");
        if (s.Creditos.Count > 50 || s.Creditos.Any(x => x.Importe <= 0 || !Dos(x.Importe)) || s.Creditos.Select(x => x.Clave).Distinct().Count() != s.Creditos.Count)
            throw new InvalidOperationException("Los créditos a aplicar deben ser distintos, con importes positivos de hasta dos decimales.");
        if (s.Medios.Count + s.ChequesTerceros.Count is < 1 or > 40 || s.Medios.Count > 20 || s.Medios.Any(m => string.IsNullOrWhiteSpace(m.Cuenta) || m.Cuenta.Length > 15 || m.Importe <= 0 || !Dos(m.Importe)))
            throw new InvalidOperationException("Cargá al menos un medio de pago o un cheque de cartera, con importes positivos de hasta dos decimales.");
        if (s.ChequesTerceros.Any(c => string.IsNullOrWhiteSpace(c.Cuenta) || string.IsNullOrWhiteSpace(c.IdBanco) || string.IsNullOrWhiteSpace(c.Numero) || c.Importe <= 0 || !Dos(c.Importe))
            || s.ChequesTerceros.Select(c => (c.Cuenta.Trim(), c.IdBanco.Trim(), c.Numero.Trim(), c.Importe)).Distinct().Count() != s.ChequesTerceros.Count)
            throw new InvalidOperationException("Revisá los cheques de cartera elegidos: cada uno una sola vez y con su importe.");
        if (s.Retencion < 0 || !Dos(s.Retencion)) throw new InvalidOperationException("La retención debe ser un importe positivo de hasta dos decimales.");
        if (s.Proforma && s.Retencion != 0) throw new InvalidOperationException("El pago proforma no retiene.");
        var medios = s.Medios.Sum(m => m.Importe) + s.ChequesTerceros.Sum(c => c.Importe);
        var dinero = medios + s.Retencion; var creditos = s.Creditos.Sum(c => c.Importe); var pagar = s.Comprobantes.Sum(c => c.Importe);
        if (dinero + creditos >= 10000000000000000m) throw new InvalidOperationException("El importe de la orden de pago supera el máximo permitido.");
        if (creditos > pagar)
            throw new InvalidOperationException("Los créditos aplicados superan lo que pagás de comprobantes. Bajá el importe de los créditos.");
        var anticipo = dinero + creditos - pagar;
        if (anticipo < 0)
            throw new InvalidOperationException($"Los medios de pago ({medios:N2}){(s.Retencion > 0 ? $", la retención ({s.Retencion:N2})" : "")} y los créditos ({creditos:N2}) no alcanzan para pagar {pagar:N2}. Bajá el importe a pagar o sumá medios.");
        if (anticipo > 0 && !s.ConfirmaAnticipo)
            throw new InvalidOperationException($"Los medios, la retención y los créditos superan lo que pagás en {anticipo:N2}. Confirmá el anticipo o ajustá los importes.");

        var restante = s.Comprobantes.ToDictionary(c => c.Clave, c => c.Importe);
        var porCredito = new List<(OrdenPagoClave, OrdenPagoClave, decimal)>();
        foreach (var credito in s.Creditos)
        {
            var disponible = credito.Importe;
            foreach (var factura in s.Comprobantes)
            {
                if (disponible <= 0) break;
                var aplica = Math.Min(disponible, restante[factura.Clave]);
                if (aplica <= 0) continue;
                porCredito.Add((credito.Clave, factura.Clave, aplica));
                restante[factura.Clave] -= aplica; disponible -= aplica;
            }
        }
        var propias = s.Comprobantes.Where(c => restante[c.Clave] > 0).Select(c => new OrdenPagoImporte(c.Clave, restante[c.Clave])).ToList();
        return new OrdenPagoReparto(porCredito, propias, dinero, creditos, anticipo);
    }

    // ── Consultas ────────────────────────────────────────────────────────────────────────────

    private static class Sql
    {
        private static string Palabra(int n) => $"(CODIGO LIKE @t{n} ESCAPE '~' OR RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @t{n} ESCAPE '~')";
        public static readonly string BuscarProveedor = $"""
            SELECT TOP(20) LTRIM(RTRIM(CODIGO)) Codigo,LTRIM(RTRIM(RAZON_SOCIAL)) Nombre,LTRIM(RTRIM(ISNULL(NUMERO_DOCUMENTO,''))) Documento
            FROM dbo.Vt_Proveedores WHERE ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0
                AND (({Palabra(1)} AND {Palabra(2)} AND {Palabra(3)} AND {Palabra(4)})
                    OR (@documento<>'' AND REPLACE(REPLACE(REPLACE(NUMERO_DOCUMENTO,'-',''),'.',''),' ','') LIKE '%'+@documento+'%'))
            ORDER BY CASE WHEN LTRIM(RTRIM(CODIGO))=@texto THEN 0 WHEN LTRIM(RTRIM(CODIGO)) LIKE @prefijo ESCAPE '~' THEN 1
                          WHEN RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @prefijo ESCAPE '~' THEN 2 ELSE 3 END, RAZON_SOCIAL;
            """;
    }

    public Task<IReadOnlyList<OrdenPagoProveedor>> BuscarProveedoresAsync(string texto, CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<OrdenPagoProveedor>>("BuscarProveedor", async (cn, user, session, token) =>
        {
            texto = (texto ?? "").Trim();
            if (texto.Length < 2) return [];
            // Cada palabra tiene que aparecer en el código, el nombre (sin distinguir tildes ni mayúsculas) o el CUIT, en cualquier orden.
            static string Literal(string v) => v.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
            var palabras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4).Select(Literal).ToArray();
            string T(int i) => i < palabras.Length ? "%" + palabras[i] + "%" : "%";
            var documento = new string(texto.Where(char.IsDigit).ToArray());
            var filas = (await cn.QueryAsync<OrdenPagoProveedor>(Cmd(Sql.BuscarProveedor, new { t1 = T(0), t2 = T(1), t3 = T(2), t4 = T(3), documento, prefijo = Literal(texto) + "%", texto }, null, token))).AsList();
            VerificarSesion(session);
            return filas;
        }, ct);

    public Task<OrdenPagoPreparacion> PrepararAsync(string proveedor, bool proforma, CancellationToken ct = default)
        => Ejecutar("Preparar", async (cn, user, session, token) =>
        {
            var unidad = UnidadDe(user);
            var prov = await cn.QuerySingleOrDefaultAsync<OrdenPagoProveedor>(Cmd("""
                SELECT TOP(1) LTRIM(RTRIM(CODIGO)) Codigo,LTRIM(RTRIM(RAZON_SOCIAL)) Nombre,LTRIM(RTRIM(ISNULL(NUMERO_DOCUMENTO,''))) Documento
                FROM dbo.Vt_Proveedores WHERE LTRIM(RTRIM(CODIGO))=@proveedor AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0;
                """, new { proveedor = proveedor.Trim() }, null, token))
                ?? throw new InvalidOperationException("El proveedor no existe o está dado de baja.");
            var usaProforma = (await ConfigAsync(cn, null, "USAFACTURAPROFORMA", token)).Equals("SI", StringComparison.OrdinalIgnoreCase);
            var puedeProforma = usaProforma && user.VerProforma;
            var mezcla = (await ConfigAsync(cn, null, "MEZCLARPROFORMAS", token)).Equals("SI", StringComparison.OrdinalIgnoreCase);
            if (proforma && !puedeProforma) throw new InvalidOperationException("No tenés habilitado el pago de proformas.");
            var filtraUnidad = (await ConfigAsync(cn, null, "CHEQUESXUNEGOCIO", token)).Equals("SI", StringComparison.OrdinalIgnoreCase);
            var comprobantes = await DocumentosAsync(cn, "CO_CPTES_IMPAGOS", prov.Codigo, filtraUnidad ? unidad : "", false, null, token);
            var creditos = await DocumentosAsync(cn, "CO_CPTES_CREDITOS_PENDIENTES", prov.Codigo, filtraUnidad ? unidad : "", true, null, token);
            var medios = (await cn.QueryAsync<OrdenPagoMedio>(Cmd("""
                SELECT LTRIM(RTRIM(CODIGO)) Cuenta,LTRIM(RTRIM(DESCRIPCION)) Descripcion,LTRIM(RTRIM(MedioDePago)) Tipo
                FROM dbo.MA_CUENTAS WHERE ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 AND LTRIM(RTRIM(MONEDA))='1'
                    AND (LTRIM(RTRIM(MedioDePago)) IN ('EF','DB') OR (LTRIM(RTRIM(MedioDePago))='CH' AND LTRIM(RTRIM(TipoVista))='BC'))
                ORDER BY MedioDePago,DESCRIPCION,CODIGO;
                """, null, null, token))).AsList();
            var cartera = (await cn.QueryAsync<OrdenPagoChequeCartera>(Cmd("""
                SELECT TOP(500) LTRIM(RTRIM(CUENTA)) Cuenta, LTRIM(RTRIM(IdBanco)) IdBanco, LTRIM(RTRIM(ISNULL(BANCO_DESCRIPCION,''))) Banco,
                    LTRIM(RTRIM(NroComprobanteBancario)) Numero, CONVERT(decimal(18,2),IMPORTE) Importe, VENCIMIENTO Vencimiento, ISNULL(Clearing,0) Clearing,
                    LTRIM(RTRIM(ISNULL(CuentaOrigen,''))) Origen, LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad
                FROM dbo.V_ChequesEnCartera
                WHERE Saldo=IMPORTE AND IMPORTE>0 AND ISNULL(ChPropio,0)=0 AND LTRIM(RTRIM(MONEDA))='1' AND (@unidad='' OR LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@unidad)
                ORDER BY VENCIMIENTO, NroComprobanteBancario;
                """, new { unidad = filtraUnidad ? unidad : "" }, null, token))).AsList();
            var chequeras = (await cn.QueryAsync<OrdenPagoChequera>(Cmd("""
                SELECT LTRIM(RTRIM(IDBANCO)) IdBanco,LTRIM(RTRIM(SERIE)) Serie,CONVERT(bigint,NUMERO_DESDE) Desde,CONVERT(bigint,NUMERO_HASTA) Hasta,
                    LTRIM(RTRIM(CuentaBancoDiferido)) Cuenta
                FROM dbo.V_TA_CTRL_CHEQUERAS ORDER BY IDBANCO,SERIE,NUMERO_DESDE;
                """, null, null, token))).AsList();
            var bancos = (await cn.QueryAsync<(string Id, string Nombre)>(Cmd("""
                IF OBJECT_ID(N'dbo.V_TA_Bancos',N'U') IS NULL SELECT TOP 0 N'' Id,N'' Nombre
                ELSE SELECT LTRIM(RTRIM(IdBanco)) Id,LTRIM(RTRIM(ISNULL(Descripcion,''))) Nombre FROM dbo.V_TA_Bancos ORDER BY Descripcion;
                """, null, null, token))).Select(b => new KeyValuePair<string, string>(b.Id, b.Nombre)).ToList();
            VerificarSesion(session);
            return new OrdenPagoPreparacion
            {
                Proveedor = prov, Proforma = proforma, PuedeProforma = puedeProforma, MezclaProforma = mezcla, Unidad = unidad,
                Comprobantes = comprobantes.Where(d => CorrespondeAlModo(d, false, proforma, mezcla)).ToList(),
                Creditos = creditos.Where(d => CorrespondeAlModo(d, true, proforma, mezcla)).ToList(),
                Medios = medios, Chequeras = chequeras, Bancos = bancos, Cartera = cartera,
                RetieneGanancias = await RetieneAsync(cn, null, prov.Codigo, proforma, DateTime.Today, token)
            };
        }, ct);

    private static string UnidadDe(Usuario user)
    {
        if (string.IsNullOrWhiteSpace(user.Unidad))
            throw new InvalidOperationException("Tu usuario no tiene unidad de negocio asignada. Pedí que la configuren para cargar órdenes de pago.");
        return user.Unidad.Trim();
    }

    private static async Task<List<OrdenPagoDocumento>> DocumentosAsync(SqlConnection cn, string vista, string proveedor, string unidad, bool credito, SqlTransaction? tx, CancellationToken ct)
    {
        // Estado (bloqueo para pago) solo existe en la vista de pendientes de pago, no en la de créditos.
        var bloqueo = credito ? "CONVERT(bit,0)" : "CONVERT(bit,MAX(CASE WHEN ISNULL(Estado,0)=1 THEN 1 ELSE 0 END))";
        var filas = (await cn.QueryAsync<OrdenPagoDocumento>(Cmd($"""
            SELECT LTRIM(RTRIM(v.TC)) Tc,v.SUCURSAL Sucursal,v.NUMERO Numero,v.LETRA Letra,MIN(v.FECHA) Fecha,MIN(v.VENCIMIENTO) Vencimiento,
                CONVERT(decimal(18,2),SUM(v.SALDO)) Saldo,LTRIM(RTRIM(MAX(v.UNEGOCIO))) Unidad,LTRIM(RTRIM(MAX(v.MONEDA))) Moneda,{bloqueo} Bloqueado
            FROM dbo.{vista} v
            WHERE LTRIM(RTRIM(v.CUENTA))=@proveedor AND (@unidad='' OR LTRIM(RTRIM(v.UNEGOCIO))=@unidad)
                AND NOT EXISTS(SELECT 1 FROM dbo.V_MV_CPTE c WHERE c.TC=v.TC AND c.SUCURSAL=v.SUCURSAL AND c.NUMERO=v.NUMERO AND c.LETRA=v.LETRA
                    AND c.IDCOMPLEMENTO=0 AND ISNULL(c.ANULADA,0)=1)
            GROUP BY LTRIM(RTRIM(v.TC)),v.SUCURSAL,v.NUMERO,v.LETRA
            HAVING CONVERT(decimal(18,2),SUM(v.SALDO))>0
            ORDER BY MIN(v.FECHA),v.NUMERO;
            """, new { proveedor, unidad }, tx, ct))).AsList();
        foreach (var f in filas)
            f.Motivo = f.Moneda is not ("" or "1") ? "Moneda extranjera: se paga en una próxima versión."
                : f.Bloqueado ? "Comprobante bloqueado para su pago." : "";
        return filas;
    }
}
