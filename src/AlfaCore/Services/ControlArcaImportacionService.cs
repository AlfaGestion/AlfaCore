using System.Data;
using System.Globalization;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IControlArcaImportacionService
{
    Task<ControlArcaImportacionContexto> ContextoAsync(IReadOnlyList<ArcaMisComprobante> comprobantes, string cuitArchivo, CancellationToken ct = default);
    Task<IReadOnlyList<ControlArcaImportacionFila>> ImportarAsync(ControlArcaImportacionSolicitud solicitud, CancellationToken ct = default);
}

/// <summary>
/// Da de alta en el libro de IVA compras los comprobantes recibidos de ARCA que faltan en el sistema, como la opción
/// «Compras ARCA Mis Comprobantes Recibidos» de EM_ImportacionComprobantes: asiento (proveedor al Haber; compras, IVA
/// crédito y otros tributos al Debe; las NC al revés) y C_MV_CPTE, con usuario IMPORTACION. Cada comprobante va en su
/// propia transacción; los ya cargados, de períodos cerrados o fuera de la fecha operativa no se graban.
/// </summary>
public sealed class ControlArcaImportacionService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions,
    ICuentasComercialesService cuentas, IArcaPadronService padron, IAppEventService events) : IControlArcaImportacionService
{
    /// <summary>«Importar Comprobantes» del legacy.</summary>
    public const string MenuImportar = "D016023";
    private const string UsuarioImportacion = "IMPORTACION";
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public Task<ControlArcaImportacionContexto> ContextoAsync(IReadOnlyList<ArcaMisComprobante> comprobantes, string cuitArchivo, CancellationToken ct = default)
        => EjecutarAsync("ContextoImportacion", async cn =>
        {
            var cfg = await ConfigAsync(cn, ct);
            var defaults = await cn.QueryFirstOrDefaultAsync<CuentasDefault>(new CommandDefinition("""
                SELECT TOP (1) LTRIM(RTRIM(ISNULL(NULLIF(LTRIM(RTRIM(Servicios)),''),C_CFI_CTACPRA))) Compras, LTRIM(RTRIM(ISNULL(CFI_IVARI,''))) Iva,
                       LTRIM(RTRIM(ISNULL(C_CFI_PERCIVA,''))) Otros
                FROM dbo.V_TA_INPUT_COMPRAS WHERE LTRIM(RTRIM(IdMotivoCpra))='1' ORDER BY CASE WHEN LTRIM(RTRIM(CondIva))='1' THEN 0 ELSE 1 END;
                """, cancellationToken: ct)) ?? new CuentasDefault();
            var cuentasImputables = (await cn.QueryAsync<CompraCargaOpcion>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(c.CODIGO)) Codigo, LTRIM(RTRIM(c.DESCRIPCION)) Descripcion FROM dbo.MA_CUENTAS c
                WHERE ISNULL(c.TITULO,0)=0 AND ISNULL(c.Dada_De_Baja,0)=0
                  AND NOT EXISTS(SELECT 1 FROM dbo.VT_CLIENTES x WHERE x.CODIGO=c.CODIGO)
                  AND NOT EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES x WHERE x.CODIGO=c.CODIGO)
                ORDER BY c.CODIGO;
                """, cancellationToken: ct))).ToList();
            var unidades = (await cn.QueryAsync<(string Codigo, string Descripcion, string Cuit)>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(Codigo)), LTRIM(RTRIM(ISNULL(DESCRIPCION,''))), ISNULL(LTRIM(RTRIM(CUIT)),'') FROM dbo.V_TA_UnidadNegocio ORDER BY Codigo;",
                cancellationToken: ct))).ToList();
            var puedeCrear = await permissions.HasAccessAsync(ProveedorAltaRapidaService.PermisoProveedor, ct);
            IReadOnlyList<CompraCargaOpcion> condiciones = [];
            if (puedeCrear)
            {
                var catalogo = await cuentas.GetLookupDataAsync(CuentaComercialTipo.Proveedor, ct);
                condiciones = catalogo.CondicionesComerciales.Select(x => new CompraCargaOpcion { Codigo = x.Codigo, Descripcion = x.Descripcion }).ToList();
            }
            var filas = new List<ControlArcaImportacionFila>();
            foreach (var a in comprobantes)
            {
                var fila = NuevaFila(a);
                var prov = await ProveedorPorCuitAsync(cn, a.DocContraparte, ct, null);
                if (prov is not null) { fila.Proveedor = prov.Codigo; fila.ProveedorNombre = prov.Nombre; }
                fila.Bloqueo = await BloqueoAsync(cn, null, a, prov?.Codigo, cfg, ct) ?? "";
                filas.Add(fila);
            }
            var cuit = Digitos(cuitArchivo);
            // Si el CUIT del archivo es el de alguna unidad, solo se puede importar a esas: no se ofrece otra.
            var delCuit = UnidadesDelCuit(cuit, unidades).ToList();
            if (delCuit.Count > 0) unidades = delCuit;
            return new ControlArcaImportacionContexto
            {
                CuentaCompras = defaults.Compras, CuentaIva = defaults.Iva, CuentaOtrosTributos = defaults.Otros,
                CuentaProveedoresVarios = cfg.ProveedoresVarios, Cuentas = cuentasImputables,
                Unidades = unidades.Select(u => new CompraCargaOpcion { Codigo = u.Codigo, Descripcion = u.Descripcion }).ToList(),
                Unidad = unidades.FirstOrDefault(u => cuit.Length > 0 && Digitos(u.Cuit) == cuit).Codigo ?? unidades.FirstOrDefault().Codigo ?? "",
                CondicionesCompra = condiciones, CondicionCompra = condiciones.FirstOrDefault()?.Codigo ?? "",
                PuedeCrearProveedores = puedeCrear, CierreCompras = cfg.Cierre, Filas = filas
            };
        }, ct);

    public Task<IReadOnlyList<ControlArcaImportacionFila>> ImportarAsync(ControlArcaImportacionSolicitud s, CancellationToken ct = default)
        => EjecutarAsync<IReadOnlyList<ControlArcaImportacionFila>>("Importar", async cn =>
        {
            if (s.Comprobantes.Count is 0 or > 500) throw new ReglaException("Elegí entre 1 y 500 comprobantes para importar.");
            var cfg = await ConfigAsync(cn, ct);
            foreach (var (cuenta, nombre) in new[] { (s.CuentaCompras, "compras"), (s.CuentaIva, "IVA crédito fiscal"), (s.CuentaOtrosTributos, "otros tributos / percepciones") })
                if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@c AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0;",
                    new { c = cuenta.Trim() }, cancellationToken: ct)) == 0)
                    throw new ReglaException($"Elegí una cuenta imputable para {nombre}.");
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))=@u;",
                new { u = s.Unidad.Trim() }, cancellationToken: ct)) == 0)
                throw new ReglaException("Elegí la unidad de negocio.");
            var todasLasUnidades = (await cn.QueryAsync<(string Codigo, string Descripcion, string Cuit)>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(Codigo)), LTRIM(RTRIM(ISNULL(DESCRIPCION,''))), ISNULL(LTRIM(RTRIM(CUIT)),'') FROM dbo.V_TA_UnidadNegocio;", cancellationToken: ct))).ToList();
            if (ValidarUnidad(s.CuitArchivo, s.Unidad, todasLasUnidades) is { } errorUnidad) throw new ReglaException(errorUnidad);
            if (s.ProveedoresNuevos == ControlArcaProveedorModo.Varios && cfg.ProveedoresVarios.Length == 0)
                throw new ReglaException("No está configurada la cuenta de proveedores varios (CUENTAPROVEEDORESVARIOS).");
            if (s.ProveedoresNuevos == ControlArcaProveedorModo.Alta && !await permissions.HasAccessAsync(ProveedorAltaRapidaService.PermisoProveedor, ct))
                throw new ReglaException("Tu usuario no tiene permiso para dar de alta proveedores: elegí «proveedores varios» u «omitir».");
            CuentaComercialLookupDataDto? catalogo = null;
            var creados = new Dictionary<string, string>();
            var resultados = new List<ControlArcaImportacionFila>();
            // Mismas tablas que el legacy: MV_Transmisiones (cabecera) y AUX_MV_CTROL_CPTE (una fila por comprobante).
            var transmision = await ArcaTransmisionRegistro.AbrirAsync(cn, users.GetCurrentUserName(), s.Archivo, ArcaTransmisionRegistro.ObservacionRecibidos,
                s.Comprobantes.Min(c => c.Fecha), s.Comprobantes.Max(c => c.Fecha), s.Comprobantes.Count, ct);
            foreach (var a in s.Comprobantes)
            {
                ct.ThrowIfCancellationRequested();
                var fila = NuevaFila(a);
                resultados.Add(fila);
                try
                {
                    var prov = await ProveedorPorCuitAsync(cn, a.DocContraparte, ct, null);
                    if (prov is null && creados.TryGetValue(Digitos(a.DocContraparte), out var codigoCreado))
                        prov = await ProveedorPorCodigoAsync(cn, codigoCreado, ct, null);
                    var bloqueo = await BloqueoAsync(cn, null, a, prov?.Codigo, cfg, ct);
                    if (bloqueo is not null) { fila.Resultado = bloqueo; continue; }
                    if (prov is null)
                    {
                        switch (s.ProveedoresNuevos)
                        {
                            case ControlArcaProveedorModo.Omitir:
                                fila.Resultado = "Proveedor inexistente: no se importó."; continue;
                            case ControlArcaProveedorModo.Varios:
                                prov = await ProveedorPorCodigoAsync(cn, cfg.ProveedoresVarios, ct, null)
                                    ?? throw new ReglaException("La cuenta de proveedores varios no es un proveedor activo.");
                                break;
                            default:
                                catalogo ??= await cuentas.GetLookupDataAsync(CuentaComercialTipo.Proveedor, ct);
                                var codigo = await CrearProveedorAsync(a, s.CondicionCompra, catalogo, ct);
                                creados[Digitos(a.DocContraparte)] = codigo;
                                await ArcaTransmisionRegistro.MarcarCuentaAsync(cn, transmision, codigo, ct);
                                prov = await ProveedorPorCodigoAsync(cn, codigo, ct, null) ?? throw new ReglaException("No se encontró el proveedor recién creado.");
                                fila.Resultado = $"Proveedor {codigo} creado. ";
                                break;
                        }
                        // Con el proveedor definitivo, volver a controlar que no esté cargado.
                        bloqueo = await BloqueoAsync(cn, null, a, prov.Codigo, cfg, ct);
                        if (bloqueo is not null) { fila.Resultado += bloqueo; continue; }
                    }
                    fila.Proveedor = prov.Codigo; fila.ProveedorNombre = prov.Nombre;
                    var asiento = await GrabarAsync(cn, a, prov, s, cfg, transmision, ct);
                    fila.Importado = true;
                    fila.Resultado += $"Importado: asiento {asiento}.";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is ReglaException or AppUserFacingException or AppValidationException or SqlException)
                {
                    if (ex is SqlException)
                    {
                        var incidente = await events.LogErrorAsync("ControlArca", "ImportarComprobante", ex,
                            "No se pudo grabar el comprobante recibido.", new { fila.Comprobante, fila.Cuit }, ct: ct);
                        fila.Resultado += $"No se pudo grabar el comprobante. Revisá el incidente {incidente} con soporte.";
                        continue;
                    }
                    fila.Resultado += ex switch
                    {
                        AppValidationException v => string.Join(" ", v.Validation.Issues.Select(i => i.Message)),
                        _ => ex.Message
                    };
                }
            }
            var importados = resultados.Count(r => r.Importado);
            var log = new System.Text.StringBuilder();
            for (var i = 0; i < resultados.Count; i++)
            {
                var r = resultados[i]; var a = s.Comprobantes[i];
                var mensaje = r.Importado ? ArcaTransmisionRegistro.MensajeAlta : r.Resultado.Trim();
                await ArcaTransmisionRegistro.GrabarControlAsync(cn, transmision, Tc(a.Clase), a.PuntoVenta.ToString("0000", CultureInfo.InvariantCulture),
                    a.NumeroDesde.ToString("00000000", CultureInfo.InvariantCulture), a.Letra, a.Total, r.Proveedor, a.Fecha, r.ProveedorNombre.Length > 0 ? r.ProveedorNombre : a.NombreContraparte, mensaje, ct);
                log.AppendLine($"{r.Comprobante} {a.NombreContraparte}: {mensaje}");
            }
            await ArcaTransmisionRegistro.CerrarAsync(cn, transmision, importados, log.ToString(), ct);
            await events.LogAuditAsync("ControlArca", "ImportarFaltantes", "C_MV_CPTE", $"{importados} de {resultados.Count}",
                $"Importó {importados} de {resultados.Count} comprobantes recibidos de ARCA al libro de IVA compras (transmisión {transmision}).",
                new { Transmision = transmision, s.CuentaCompras, s.CuentaIva, s.CuentaOtrosTributos, s.Unidad, s.ProveedoresNuevos,
                      Comprobantes = resultados.Select(r => new { r.Comprobante, r.Cuit, r.Importado, r.Resultado }) }, CancellationToken.None);
            return resultados;
        }, ct);

    // ── Reglas ──────────────────────────────────────────────────────────────────────────────────

    private static ControlArcaImportacionFila NuevaFila(ArcaMisComprobante a) => new()
    {
        Clave = Clave(a), Comprobante = $"{Tc(a.Clase)} {a.PuntoVenta:00000}-{a.NumeroDesde:00000000} {a.Letra}".Trim(),
        Cuit = a.DocContraparte, Nombre = a.NombreContraparte, Fecha = a.Fecha, Total = a.Total
    };

    internal static string Clave(ArcaMisComprobante a) => $"{a.CodigoTipo}|{a.PuntoVenta}|{a.NumeroDesde}|{Digitos(a.DocContraparte)}";

    internal static string Tc(string clase) => clase switch { "NC" => "NCC", "ND" => "NDC", _ => "FCC" };

    /// <summary>Por qué no se puede importar (null = se puede). Sin proveedor solo controla lo que no depende de él.</summary>
    private static async Task<string?> BloqueoAsync(SqlConnection cn, SqlTransaction? tx, ArcaMisComprobante a, string? proveedor, Config cfg, CancellationToken ct)
    {
        if (a.Clase is not ("FC" or "NC" or "ND")) return "Los recibos no se importan: solo facturas y notas de crédito o débito.";
        if (a.Letra is not ("A" or "B" or "C" or "M")) return $"Letra {a.Letra} no admitida.";
        if (a.NumeroHasta > a.NumeroDesde) return "Es un rango de comprobantes: cargalo a mano.";
        if (a.PuntoVenta is < 1 or > 9999 || a.NumeroDesde is < 1 or > 99999999) return "Punto de venta o número fuera de rango.";
        if (a.Moneda is not ("$" or "PES" or "")) return $"Está en moneda extranjera ({a.Moneda}): cargalo a mano.";
        if (a.Total <= 0) return "Total en cero.";
        if (a.DocContraparte.Length != 11) return "El emisor no tiene CUIT.";
        if (cfg.Cierre is { } cierre && a.Fecha.Date <= cierre.Date) return $"El período está cerrado para compras (cierre {cierre:dd/MM/yyyy}).";
        if (cfg.OperativaDesde is { } d && a.Fecha.Date < d.Date || cfg.OperativaHasta is { } h && a.Fecha.Date > h.Date)
            return "La fecha está fuera de la fecha operativa.";
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.MV_EJERCICIOS WHERE ISNULL(CERRADO,0)=0 AND @f BETWEEN [FECHA DESDE] AND [FECHA HASTA];
            """, new { f = a.Fecha.Date }, tx, cancellationToken: ct)) != 1) return "No hay un ejercicio contable abierto para esa fecha.";
        if (Armar(a, "P", "C", "I", "O").Error is { } error) return error;
        var existente = await CuentaExistenteAsync(cn, tx, a, proveedor, cfg.Equivalencia(Tc(a.Clase)), ct);
        return existente is null ? null : $"Ya está cargado ese comprobante en la cuenta {existente}. Se verificó por CUIT o cuenta; no se vuelve a importar.";
    }

    /// <summary>La identidad fiscal no cambia al pasar de Proveedores varios a una cuenta propia.
    /// Se consulta el documento del movimiento; solo si falta se usa el CUIT del maestro.</summary>
    internal static Task<string?> CuentaExistenteAsync(SqlConnection cn, SqlTransaction? tx, ArcaMisComprobante a,
        string? proveedor, string equivalencia, CancellationToken ct)
        => cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT TOP (1) e.Cuenta FROM (
                SELECT 0 Orden, LTRIM(RTRIM(c.CUENTA)) Cuenta
                FROM dbo.C_MV_CPTE c WITH(UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.VT_PROVEEDORES p ON LTRIM(RTRIM(p.CODIGO))=LTRIM(RTRIM(c.CUENTA))
                WHERE LTRIM(RTRIM(c.TC)) IN (@tc,@equ)
                  AND LTRIM(RTRIM(c.SUCURSAL))=@suc AND LTRIM(RTRIM(c.NUMERO))=@num AND LTRIM(RTRIM(c.LETRA))=@letra
                  AND ((@proveedor<>'' AND LTRIM(RTRIM(c.CUENTA))=@proveedor)
                    OR COALESCE(NULLIF(REPLACE(REPLACE(REPLACE(ISNULL(c.DOCUMENTONUMERO,''),'-',''),' ',''),'.',''),''),
                                REPLACE(REPLACE(REPLACE(ISNULL(p.NUMERO_DOCUMENTO,''),'-',''),' ',''),'.',''))=@cuit)
                UNION ALL
                SELECT 1 Orden, COALESCE(NULLIF(LTRIM(RTRIM(a.CABCUENTA)),''),LTRIM(RTRIM(a.CUENTA))) Cuenta
                FROM dbo.MV_ASIENTOS a WITH(UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.VT_PROVEEDORES p ON LTRIM(RTRIM(p.CODIGO))=COALESCE(NULLIF(LTRIM(RTRIM(a.CABCUENTA)),''),LTRIM(RTRIM(a.CUENTA)))
                WHERE LTRIM(RTRIM(a.TC)) IN (@tc,@equ)
                  AND LTRIM(RTRIM(a.SUCURSAL))=@suc AND LTRIM(RTRIM(a.NUMERO))=@num AND LTRIM(RTRIM(a.LETRA))=@letra
                  AND UPPER(LTRIM(RTRIM(ISNULL(a.LIVA_TIPO,'')))) IN ('','COMPRAS')
                  AND ((@proveedor<>'' AND (LTRIM(RTRIM(a.CUENTA))=@proveedor OR LTRIM(RTRIM(ISNULL(a.CABCUENTA,'')))=@proveedor))
                    OR COALESCE(NULLIF(REPLACE(REPLACE(REPLACE(ISNULL(a.CABCUIT,''),'-',''),' ',''),'.',''),''),
                                REPLACE(REPLACE(REPLACE(ISNULL(p.NUMERO_DOCUMENTO,''),'-',''),' ',''),'.',''))=@cuit)
            ) e ORDER BY e.Orden,e.Cuenta;
            """, new { tc = Tc(a.Clase), equ = equivalencia.Trim(), suc = a.PuntoVenta.ToString("0000"),
                num = a.NumeroDesde.ToString("00000000"), letra = a.Letra.Trim(), proveedor = proveedor?.Trim() ?? "", cuit = Digitos(a.DocContraparte) },
                tx, cancellationToken: ct));

    internal sealed record Armado(IReadOnlyList<ControlArcaAsientoLinea> Lineas, decimal Imputacion, decimal Iva, decimal NetoGravado, decimal NoGravado,
        decimal Exento, decimal Otros, IReadOnlyList<ArcaAlicuota> Alicuotas, string? Error);

    /// <summary>Arma el asiento como el legacy: las A y M discriminan IVA; en B y C todo (menos otros tributos) va a la cuenta de compras.</summary>
    internal static Armado Armar(ArcaMisComprobante a, string proveedor, string compras, string iva, string otros)
    {
        var discrimina = a.Letra is "A" or "M";
        var alicuotas = discrimina ? a.Alicuotas.Where(x => x.Iva != 0 || x.Neto != 0).ToList() : [];
        var ivaTotal = !discrimina ? 0 : alicuotas.Count > 0 ? alicuotas.Sum(x => x.Iva) : a.Iva;
        if (discrimina && alicuotas.Count == 0 && a.Iva != 0) alicuotas.Add(new ArcaAlicuota(21m, a.NetoGravado, a.Iva));
        var otrosTributos = a.OtrosTributos;
        var imputacion = a.Total - ivaTotal - otrosTributos;
        string? error = null;
        if (imputacion < 0 || otrosTributos < 0 || ivaTotal < 0) error = "Los importes de ARCA no cierran (IVA u otros tributos mayores al total).";
        if (alicuotas.Count(x => x.Iva != 0) > 4) error = "Tiene más de cuatro alícuotas de IVA: cargalo a mano.";
        if (a.Total == 0) error = "ARCA lo informa con importe 0: no hay nada que registrar.";
        var proveedorDh = a.Clase == "NC" ? "D" : "H"; var otroDh = proveedorDh == "H" ? "D" : "H";
        var lineas = new List<ControlArcaAsientoLinea> { new(proveedor, proveedorDh, a.Total) };
        if (imputacion != 0) lineas.Add(new(compras, otroDh, imputacion));
        if (ivaTotal != 0) lineas.Add(new(iva, otroDh, ivaTotal));
        if (otrosTributos != 0) lineas.Add(new(otros, otroDh, otrosTributos));
        return new Armado(lineas, imputacion, ivaTotal, discrimina ? a.NetoGravado : 0,
            discrimina ? a.NoGravado : imputacion, discrimina ? a.Exento : 0, otrosTributos,
            // Mismo orden de casilleros que el legacy: 21, 10,5, 27 y después el resto.
            alicuotas.Where(x => x.Iva != 0).OrderBy(x => x.Tasa switch { 21m => 0, 10.5m => 1, 27m => 2, _ => 3 }).ThenByDescending(x => x.Tasa).ToList(), error);
    }

    // ── Grabación ───────────────────────────────────────────────────────────────────────────────

    private async Task<int> GrabarAsync(SqlConnection cn, ArcaMisComprobante a, Proveedor prov, ControlArcaImportacionSolicitud s, Config cfg, int transmision, CancellationToken ct)
    {
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        CommandDefinition Q(string sql, object? args = null) => new(sql, args, tx, commandTimeout: 60, cancellationToken: ct);
        // Mismo bloqueo que la carga de compras: no se cruzan dos grabaciones de compras.
        if (await cn.ExecuteScalarAsync<int>(Q("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'ALFACORE_COMPRAS_REGISTRO',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @r;
            """)) < 0) throw new ReglaException("Hay otra compra guardándose. Reintentá en unos segundos.");
        if (await BloqueoAsync(cn, tx, a, prov.Codigo, cfg, ct) is { } bloqueo) throw new ReglaException(bloqueo);
        var armado = Armar(a, prov.Codigo, s.CuentaCompras.Trim(), s.CuentaIva.Trim(), s.CuentaOtrosTributos.Trim());
        var tc = Tc(a.Clase); var tcAsiento = cfg.Equivalencia(tc);
        var suc = a.PuntoVenta.ToString("0000"); var num = a.NumeroDesde.ToString("00000000"); var id = suc + num + a.Letra;
        var periodo = (await cn.QuerySingleAsync<int>(Q("""
            SELECT PERIODO FROM dbo.MV_EJERCICIOS WHERE ISNULL(CERRADO,0)=0 AND @f BETWEEN [FECHA DESDE] AND [FECHA HASTA];
            """, new { f = a.Fecha.Date }))).ToString(CultureInfo.InvariantCulture);
        // ObtieneTipoRegCont: subdiario del TC si existe en MA_SUBDIARIOS; si no, cuatro blancos (diario general).
        var tipo = await cn.QueryFirstOrDefaultAsync<string>(Q("""
            SELECT TOP (1) LTRIM(RTRIM(m.Subdiario)) FROM dbo.MA_SUBDIARIOSCOMPROBANTES m
            JOIN dbo.MA_SUBDIARIOS x ON LTRIM(RTRIM(x.CODIGO))=LTRIM(RTRIM(m.Subdiario)) WHERE LTRIM(RTRIM(m.TC))=@tc;
            """, new { tc })) ?? "";
        var tipoReg = tipo.Length == 0 ? "    " : CodigoPk.Format(tipo, 4);
        var asiento = await NumeracionAsiento.SiguienteAsync(cn, tx, periodo, a.Fecha.Month, tipo, ct);
        var signo = a.Clase == "NC" ? -1 : 1;
        var al = armado.Alicuotas;
        decimal Tasa(int i) => i < al.Count ? al[i].Tasa : 0; decimal Iva(int i) => i < al.Count ? al[i].Iva * signo : 0;
        var liva = a.Letra == "C" && prov.CondicionIva == "5" && !string.Equals(cfg.MonotributoAlIva, "SI", StringComparison.OrdinalIgnoreCase) ? "" : "Compras";
        var condIva = prov.CondicionIva.Length > 0 ? prov.CondicionIva : a.Letra == "C" ? "5" : "1";
        var p = new DynamicParameters(new
        {
            transmision, tc, tcAsiento, id, suc, num, letra = a.Letra, cuenta = prov.Codigo, nombre = Corto(prov.Nombre, 50), detalle = Corto(prov.Nombre, 200),
            cuit = Digitos(a.DocContraparte), fecha = a.Fecha.Date, total = a.Total, periodo, mes = a.Fecha.Month, asiento, tipo = tipoReg,
            unidad = CodigoPk.Format(s.Unidad, 4), motivo = CodigoPk.Format(prov.Motivo.Length > 0 ? prov.Motivo : "1", 4), ivaCond = CodigoPk.Format(condIva, 4),
            provincia = CodigoPk.Format(prov.Provincia, 4), usuario = UsuarioImportacion, liva,
            livaTotal = a.Total * signo, gravado = armado.NetoGravado * signo, noGravado = armado.NoGravado * signo, exento = armado.Exento * signo,
            perc = armado.Otros * signo, tasa1 = Tasa(0), iva1 = Iva(0), tasa2 = Tasa(1), iva2 = Iva(1), tasa3 = Tasa(2), iva3 = Iva(2), tasa4 = Tasa(3), iva4 = Iva(3),
            ivaTotal = armado.Iva, netoCpte = armado.NetoGravado, noGravCpte = armado.NoGravado,
            cae = a.Cae.Length is > 0 and <= 14 ? a.Cae : null, codigoDgi = CodigoPk.Format(a.CodigoTipo.ToString(CultureInfo.InvariantCulture), 4)
        });
        await cn.ExecuteAsync(Q("""
            INSERT dbo.C_MV_CPTE(TC,IDCOMPROBANTE,CUENTA,FECHA,VENCIMIENTO,FechaSubdiario,NOMBRE,DOMICILIO,LOCALIDAD,IDPROVINCIA,DOCUMENTOTIPO,DOCUMENTONUMERO,
              CONDICIONIVA,IMPORTE,ImporteIva,NetoGravado,NetoNoGravado,AlicIva,ImporteIva2,AlicIva2,SUCURSAL,NUMERO,LETRA,UNEGOCIO,MONEDA,COTIZACION,
              USUARIO,FechaHora_Grabacion,Transmision,COMENTARIOS)
            VALUES(@tc,@id,@cuenta,@fecha,@fecha,@fecha,@nombre,'.','.',@provincia,'   1',@cuit,@ivaCond,@total,@ivaTotal,@netoCpte,@noGravCpte,
              @tasa1,ABS(@iva2),@tasa2,@suc,@num,@letra,@unidad,'   1',1,@usuario,GETDATE(),@transmision,N'Importado desde ARCA (Mis Comprobantes Recibidos) por AlfaCore');
            """, p));
        foreach (var (linea, i) in armado.Lineas.Select((l, i) => (l, i)))
        {
            p.Add("lineaCuenta", linea.Cuenta); p.Add("secuencia", i + 1); p.Add("sentido", linea.DebeHaber); p.Add("importe", linea.Importe); p.Add("cabecera", i == 0);
            await cn.ExecuteAsync(Q(InsertarAsientoSql, p));
        }
        var balance = await cn.ExecuteScalarAsync<decimal>(Q("""
            SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS
            WHERE LTRIM(RTRIM(PERIODO))=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND LTRIM(RTRIM(TIPO_REG))=LTRIM(RTRIM(@tipo));
            """, p));
        if (balance != 0) throw new ReglaException("El asiento no quedó balanceado. No se grabó.");
        await cn.ExecuteAsync(Q("""
            INSERT dbo.V_MV_CPTEACCIONES(TC,IDCOMPROBANTE,IDCOMPLEMENTO,TIPO_ACCION,FECHAHORA,USUARIO,PC,SYSTEMUSER,Periodo,Tipo_Reg,Fecha,Mes_Operativo,NUMEROASIENTO,Cuenta,Proceso)
            VALUES(@tc,@id,0,'A',GETDATE(),@usuarioReal,HOST_NAME(),SYSTEM_USER,@periodo,@tipo,@fecha,@mes,@asiento,@cuenta,N'AlfaCore: importación desde ARCA');
            """, new { tc, id, usuarioReal = users.GetCurrentUserName(), periodo, tipo = tipoReg, fecha = a.Fecha.Date, mes = a.Fecha.Month, asiento, cuenta = prov.Codigo }));
        ct.ThrowIfCancellationRequested();
        tx.Commit();
        return asiento;
    }

    internal const string InsertarAsientoSql = """
        INSERT dbo.MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,COTIZACION,
          PERIODO,CABIMPORTE,VENCIMIENTO,TIPO_REG,FechaHora_Grabacion,FechaSubdiario,USUARIO_LOGEADO,UNEGOCIO,IDMOTIVO,CABCUENTA,CABNOMBRE,CABCUIT,CABCONDIVA,
          LIVA_TIPO,LIVA_TOTAL,LIVA_ImpNetoGrav,LIVA_ImpNetoNGrav,LIVA_EXENTO,LIVA_AlicIVA,LIVA_ImpIVA,LIVA_AlicIVA2,LIVA_ImpIVA2,LIVA_AlicIVA3,LIVA_ImpIVA3,
          LIVA_AlicIVA4,LIVA_ImpIVA4,LIVA_IMPIVAREC,LIVA_ALICIVAREC,LIVA_Ret_Perc,LIVA_Ret_IBtos,LIVA_Ret_Ganancias,NroCAI,CodCpteDGI,Transmision)
        VALUES(@lineaCuenta,@secuencia,@mes,@asiento,@fecha,@detalle,@tcAsiento,@suc,@num,@letra,@sentido,@importe,'   1',1,
          @periodo,@total,@fecha,@tipo,GETDATE(),@fecha,@usuario,@unidad,@motivo,
          CASE WHEN @cabecera=1 THEN @cuenta END,CASE WHEN @cabecera=1 THEN @nombre END,CASE WHEN @cabecera=1 THEN @cuit END,CASE WHEN @cabecera=1 THEN @ivaCond END,
          CASE WHEN @cabecera=1 THEN @liva END,CASE WHEN @cabecera=1 THEN @livaTotal ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @gravado ELSE 0 END,CASE WHEN @cabecera=1 THEN @noGravado ELSE 0 END,CASE WHEN @cabecera=1 THEN @exento ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @tasa1 ELSE 0 END,CASE WHEN @cabecera=1 THEN @iva1 ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @tasa2 ELSE 0 END,CASE WHEN @cabecera=1 THEN @iva2 ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @tasa3 ELSE 0 END,CASE WHEN @cabecera=1 THEN @iva3 ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @tasa4 ELSE 0 END,CASE WHEN @cabecera=1 THEN @iva4 ELSE 0 END,0,0,
          CASE WHEN @cabecera=1 THEN @perc ELSE 0 END,0,0,
          CASE WHEN @cabecera=1 THEN @cae END,CASE WHEN @cabecera=1 THEN @codigoDgi END,@transmision);
        """;

    /// <summary>Mayúsculas, sin tildes, puntos ni espacios: «C.U.I.T.» y «Córdoba» comparan igual que «CUIT» y «CORDOBA».</summary>
    private static IEnumerable<(string Codigo, string Descripcion, string Cuit)> UnidadesDelCuit(string cuit, IEnumerable<(string Codigo, string Descripcion, string Cuit)> unidades)
        => cuit.Length == 0 ? [] : unidades.Where(u => Digitos(u.Cuit) == cuit);

    /// <summary>
    /// Evita cargar un archivo de ARCA a la unidad de negocio equivocada: si el CUIT del archivo es el de alguna unidad, la elegida
    /// tiene que ser una de ellas. Si el CUIT no pertenece a ninguna unidad no se puede saber cuál corresponde y se deja elegir.
    /// </summary>
    internal static string? ValidarUnidad(string? cuitArchivo, string unidad, IEnumerable<(string Codigo, string Descripcion, string Cuit)> unidades)
    {
        var lista = unidades.ToList();
        var propias = UnidadesDelCuit(Digitos(cuitArchivo), lista).ToList();
        if (propias.Count == 0 || propias.Any(u => u.Codigo.Trim() == unidad.Trim())) return null;
        var elegida = lista.FirstOrDefault(u => u.Codigo.Trim() == unidad.Trim());
        return $"El archivo es del CUIT {Digitos(cuitArchivo)}, que corresponde a la unidad de negocio {string.Join(" / ", propias.Select(u => $"{u.Codigo.Trim()} · {u.Descripcion}"))}. "
            + $"No se puede importar a la unidad {(elegida.Codigo is null ? unidad.Trim() : $"{elegida.Codigo.Trim()} · {elegida.Descripcion}")}.";
    }

    private static string Normalizar(string? s)
        => new((s ?? "").Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
            .Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Código de provincia (TA_ESTADOS) a partir del nombre del padrón de ARCA, sin tildes ni puntos («BUENOS AIRES.» en la tabla),
    /// con «Ciudad Autónoma de Buenos Aires» como CAPITAL. Vacío si no se reconoce.
    /// </summary>
    internal static string ProvinciaCodigo(string? nombre, IEnumerable<CuentaComercialLookupOptionDto> provincias)
    {
        var p = Normalizar(nombre);
        if (p.Length == 0) return "";
        var lista = provincias.Select(x => (x.Codigo, Desc: Normalizar(x.Descripcion))).Where(x => x.Desc.Length > 0).ToList();
        if (p.Contains("CIUDADAUTONOMA") || p.Contains("CAPITALFEDERAL") || p == "CABA" || p == "CAPITAL")
            return lista.FirstOrDefault(x => x.Desc == "CAPITAL").Codigo ?? "";
        return lista.FirstOrDefault(x => x.Desc == p).Codigo
            ?? lista.Where(x => x.Desc.Length >= 5 && p.Contains(x.Desc)).OrderByDescending(x => x.Desc.Length).Select(x => x.Codigo).FirstOrDefault() ?? "";
    }

    /// <summary>Tipo de documento CUIT por descripción (acepta «C.U.I.T.»); si no hay, el código 1, que es el que asume el legacy.</summary>
    internal static string TipoDocumentoCuit(IEnumerable<CuentaComercialLookupOptionDto> tipos)
    {
        var lista = tipos.ToList();
        // «C.U.I.T.» exacto primero: hay bases con además «C.U.I.T. (Falta Comprobante)», que no es el de un alta normal.
        return lista.FirstOrDefault(x => Normalizar(x.Descripcion) == "CUIT")?.Codigo
            ?? lista.FirstOrDefault(x => Normalizar(x.Descripcion).Contains("CUIT") && !Normalizar(x.Descripcion).Contains("FALTA"))?.Codigo
            ?? lista.FirstOrDefault(x => x.Codigo.Trim() == "1")?.Codigo ?? "";
    }

    /// <summary>
    /// Condición de IVA del emisor cuando el padrón no la informa. Surge de la letra: A y M los emite un Responsable Inscripto, y la B
    /// también (el receptor es el que no es inscripto); la C la emite un Monotributista (o un exento). Se busca por descripción y,
    /// si la tabla no la tiene así, por los códigos estándar (1 Responsable Inscripto, 5 Monotributo).
    /// </summary>
    internal static string CondicionIvaPorLetra(string letra, IEnumerable<CuentaComercialLookupOptionDto> condiciones)
    {
        var lista = condiciones.ToList();
        var monotributo = letra.Trim().Equals("C", StringComparison.OrdinalIgnoreCase);
        return lista.FirstOrDefault(x => monotributo ? Normalizar(x.Descripcion).Contains("MONOTRIBUT") : Normalizar(x.Descripcion).Contains("RESPONSABLEINSCRIPTO"))?.Codigo
            ?? lista.FirstOrDefault(x => x.Codigo.Trim() == (monotributo ? "5" : "1"))?.Codigo ?? "";
    }

    private static string ProvinciaPorDefecto(string codigo, IEnumerable<CuentaComercialLookupOptionDto> provincias)
        => codigo.Length > 0 ? codigo : provincias.FirstOrDefault(x => x.Codigo.Trim() == "1")?.Codigo ?? "   1";

    private async Task<string> CrearProveedorAsync(ArcaMisComprobante a, string condicionCompra, CuentaComercialLookupDataDto catalogo, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var sesion)) throw new ReglaException("Ingresá en la base activa.");
        if (string.IsNullOrWhiteSpace(condicionCompra)) throw new ReglaException("Elegí la condición de compra para los proveedores nuevos.");
        var cuit = Digitos(a.DocContraparte);
        ArcaPadronPersonaDto? persona = null;
        try { persona = await padron.ConsultarPersonaAsync(cuit, ct); }
        catch (OperationCanceledException) { throw; }
        catch { /* Sin padrón se usan los datos del archivo de ARCA. */ }
        string Opcion(IEnumerable<CuentaComercialLookupOptionDto> lista, Func<CuentaComercialLookupOptionDto, bool> filtro) => lista.FirstOrDefault(filtro)?.Codigo ?? "";
        var ivaPadron = persona?.CondicionIvaCodigo.Trim() ?? "";
        var iva = Opcion(catalogo.CondicionesIva, x => x.Codigo.Trim() == ivaPadron);
        if (iva.Length == 0) iva = CondicionIvaPorLetra(a.Letra, catalogo.CondicionesIva);
        var request = new CuentaComercialSaveRequest
        {
            RazonSocial = Corto(persona?.RazonSocial is { Length: > 0 } r ? r : a.NombreContraparte, 50),
            NumeroDocumento = cuit,
            DocumentoTipoCodigo = TipoDocumentoCuit(catalogo.TiposDocumento),
            IvaCodigo = iva,
            // Como el legacy: la calle se recorta (50) y, si el padrón no da localidad o provincia, «A completar» y la provincia 1.
            Calle = Corto(persona?.Calle ?? "", 50), Localidad = persona?.Localidad is { Length: > 0 } loc ? Corto(loc, 50) : "A completar",
            CodigoPostal = persona?.CodigoPostal ?? "",
            ProvinciaCodigo = ProvinciaPorDefecto(ProvinciaCodigo(persona?.Provincia, catalogo.Provincias), catalogo.Provincias),
            PaisCodigo = Opcion(catalogo.Paises, x => x.Descripcion.Contains("ARGENTIN", StringComparison.OrdinalIgnoreCase)),
            MonedaCodigo = Opcion(catalogo.Monedas, x => x.Codigo.Trim() == "1"),
            CondicionComercialCodigo = condicionCompra, MotivoCompraCodigo = "1", FacturaCompraSinOc = true,
            Observaciones = "Alta automática al importar comprobantes recibidos de ARCA."
        };
        if (request.DocumentoTipoCodigo.Length == 0)
            throw new ReglaException("No se pudo dar de alta el proveedor: no hay un tipo de documento CUIT en la tabla de tipos de documento (TA_TIPODOCUMENTO).");
        if (request.IvaCodigo.Length == 0)
            throw new ReglaException("No se pudo dar de alta el proveedor: no se encontró la condición de IVA (Responsable Inscripto / Monotributo) en la tabla de condiciones de IVA (TA_CONDIVA).");
        return (await cuentas.CrearProveedorEnSesionAsync(request, sesion, ct)).Trim();
    }

    // ── Datos ───────────────────────────────────────────────────────────────────────────────────

    private sealed class CuentasDefault { public string Compras { get; set; } = ""; public string Iva { get; set; } = ""; public string Otros { get; set; } = ""; }

    private sealed class Proveedor
    {
        public string Codigo { get; set; } = ""; public string Nombre { get; set; } = ""; public string CondicionIva { get; set; } = "";
        public string Motivo { get; set; } = ""; public string Provincia { get; set; } = "";
    }

    private const string ProveedorSql = """
        SELECT TOP (1) LTRIM(RTRIM(CODIGO)) Codigo, LTRIM(RTRIM(ISNULL(RAZON_SOCIAL,''))) Nombre, LTRIM(RTRIM(ISNULL(IVA,''))) CondicionIva,
               LTRIM(RTRIM(ISNULL(IdMotivoCpra,''))) Motivo, LTRIM(RTRIM(ISNULL(PROVINCIA,''))) Provincia
        FROM dbo.VT_PROVEEDORES WHERE ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0
        """;

    private static async Task<Proveedor?> ProveedorPorCuitAsync(SqlConnection cn, string cuit, CancellationToken ct, SqlTransaction? tx)
    {
        var d = Digitos(cuit);
        if (d.Length != 11) return null;
        // Como VerificarCuenta del legacy: CUIT sin guiones o con guiones.
        return await cn.QueryFirstOrDefaultAsync<Proveedor>(new CommandDefinition(ProveedorSql + """
             AND LTRIM(RTRIM(NUMERO_DOCUMENTO)) IN (@d, @g) ORDER BY ISNULL(BLOQUEO,0), CODIGO;
            """, new { d, g = $"{d[..2]}-{d[2..10]}-{d[10]}" }, tx, cancellationToken: ct));
    }

    private static Task<Proveedor?> ProveedorPorCodigoAsync(SqlConnection cn, string codigo, CancellationToken ct, SqlTransaction? tx)
        => cn.QueryFirstOrDefaultAsync<Proveedor>(new CommandDefinition(ProveedorSql + " AND LTRIM(RTRIM(CODIGO))=@c;", new { c = codigo.Trim() }, tx, cancellationToken: ct));

    private sealed class Config
    {
        public DateTime? Cierre { get; init; }
        public DateTime? OperativaDesde { get; init; }
        public DateTime? OperativaHasta { get; init; }
        public string ProveedoresVarios { get; init; } = "";
        public string MonotributoAlIva { get; init; } = "";
        public Dictionary<string, string> Valores { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>EQU_FCC / EQU_NCC / EQU_NDC: TC con que va el asiento.</summary>
        public string Equivalencia(string tc) => Valores.GetValueOrDefault("EQU_" + tc) is { Length: > 0 and <= 4 } e ? e : tc;
    }

    private static async Task<Config> ConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var valores = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('CIERRECOMPRAS','FECHAOPERDESDE','FECHAOPERHASTA','CUENTAPROVEEDORESVARIOS','MONOTRIBUTOALIVA','EQU_FCC','EQU_NCC','EQU_NDC');
            """, cancellationToken: ct))).GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.OrdinalIgnoreCase);
        DateTime? Fecha(string k) => DateTime.TryParseExact(valores.GetValueOrDefault(k, ""), ["d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "yyyyMMdd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
        return new Config
        {
            Cierre = Fecha("CIERRECOMPRAS"), OperativaDesde = Fecha("FECHAOPERDESDE"), OperativaHasta = Fecha("FECHAOPERHASTA"),
            ProveedoresVarios = valores.GetValueOrDefault("CUENTAPROVEEDORESVARIOS", ""), MonotributoAlIva = valores.GetValueOrDefault("MONOTRIBUTOALIVA", ""),
            Valores = valores
        };
    }

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
    private static string Corto(string s, int n) => (s ?? "").Trim() is var t && t.Length > n ? t[..n] : (s ?? "").Trim();

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            if (!await permissions.HasAccessAsync(MenuImportar, ct))
                throw new ReglaException("Tu usuario no tiene permiso para importar comprobantes (Importar Comprobantes).");
            await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
            return await operacion(cn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("ControlArca", accion, ex, "No se pudo importar desde ARCA.", ct: ct);
            throw new AppUserFacingException("No se pudo importar desde ARCA.", codigo, ex);
        }
    }
}
