using System.Globalization;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Reclamo de saldos a clientes (reemplaza RP_SaldosDeClientes, D6008, del legacy): arma la lista de clientes
/// con deuda, prepara el mensaje por tramo de atraso con el detalle de comprobantes y lo envía por email
/// (cuenta de correo de Alfa Gestión vía <see cref="CorreoSalienteService"/>) y/o WhatsApp (plantilla aprobada vía
/// <see cref="IConversacionesService"/>). Solo lee datos de ventas; escribe únicamente su bitácora y su configuración.
/// </summary>
public sealed class ReclamoSaldosService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions,
    IAppEventService events, CorreoSalienteService correo, IConversacionesService conversaciones,
    IConversacionesConfigService conversacionesConfig, ICentralBasesService centralBases, ICentralClientesService centralClientes,
    IPortalClienteAutoLoginService portalAutoLogin)
{
    public const string MenuKey = "D6008";
    private const string Grupo = "COBRANZAS";
    private const string Prefijo = "RECLAMO-SALDOS-";
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    private sealed class Usuario { public string Nombre { get; set; } = ""; public string Unidad { get; set; } = ""; public bool Admin { get; set; } }

    private async Task<T> Ejecutar<T>(string accion, string mensajeError, Func<SqlConnection, Usuario, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _))
            throw new InvalidOperationException("Iniciá sesión en la base donde querés reclamar saldos.");
        try
        {
            if (!await permissions.HasAccessAsync(MenuKey, ct))
                throw new InvalidOperationException("No tenés permiso para reclamar saldos a clientes.");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            var usuario = await cn.QuerySingleOrDefaultAsync<Usuario>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(NOMBRE)) Nombre, LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad, CONVERT(bit,ISNULL(AdminCentral,0)) Admin
                FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@usuario);
                """, new { usuario = users.GetCurrentUserName() }, cancellationToken: ct))
                ?? throw new InvalidOperationException("El usuario actual no está definido en la base activa.");
            return await work(cn, usuario, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            if (ex is InvalidOperationException) throw;
            var code = await events.LogErrorAsync("ReclamoSaldos", accion, ex, mensajeError, ct: ct);
            throw new AppUserFacingException(mensajeError, code, ex);
        }
    }

    // ---------------------------------------------------------------- Configuración

    public Task<ReclamoConfig> ConfigAsync(CancellationToken ct = default)
        => Ejecutar("Config", "No se pudo leer la configuración del reclamo.", async (cn, _, token) => await LeerConfigAsync(cn, token), ct);

    private static async Task<ReclamoConfig> LeerConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var filas = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) Clave,
                   COALESCE(NULLIF(CONVERT(nvarchar(max),ValorAux),''), NULLIF(LTRIM(RTRIM(VALOR)),''), '') Valor
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) LIKE N'RECLAMO-SALDOS-%' OR UPPER(LTRIM(RTRIM(CLAVE)))=N'CONV_COBRANZA_FORMA_PAGO';
            """, cancellationToken: ct))).ToDictionary(x => x.Clave, x => x.Valor);
        string V(string k) => filas.TryGetValue(Prefijo + k, out var v) ? v : string.Empty;
        int I(string k, int def) => int.TryParse(V(k), out var n) && n >= 0 ? n : def;
        var cfg = new ReclamoConfig
        {
            DiasHasta1 = I("DIAS-HASTA1", 15),
            DiasHasta2 = I("DIAS-HASTA2", 45),
            FormaPago = V("FORMA-PAGO").Length > 0 ? V("FORMA-PAGO") : filas.GetValueOrDefault("CONV_COBRANZA_FORMA_PAGO") ?? string.Empty,
            SaldoMinimo = decimal.TryParse(V("SALDO-MINIMO"), NumberStyles.Number, CultureInfo.InvariantCulture, out var sm) ? sm : 1m,
            Tolerancia = decimal.TryParse(V("TOLERANCIA"), NumberStyles.Number, CultureInfo.InvariantCulture, out var tl) && tl >= 0 ? tl : 1m,
            DiasEntreEnvios = I("DIAS-ENTRE-ENVIOS", 7),
            PortalDias = Math.Max(1, I("PORTAL-DIAS", 15)),
            IdPlantillaWhatsApp = long.TryParse(V("WA-PLANTILLA"), out var pl) ? pl : 0
        };
        foreach (var t in cfg.Tramos) { t.Asunto = V($"ASUNTO{t.Numero}"); t.Mensaje = V($"MENSAJE{t.Numero}"); }
        return cfg;
    }

    public Task GuardarConfigAsync(ReclamoConfig cfg, CancellationToken ct = default)
        => Ejecutar("GuardarConfig", "No se pudo guardar la configuración del reclamo.", async (cn, _, token) =>
        {
            if (cfg.DiasHasta1 < 0 || cfg.DiasHasta2 < cfg.DiasHasta1)
                throw new InvalidOperationException("Los días del tramo 2 deben ser mayores o iguales a los del tramo 1.");
            await Set(cn, "DIAS-HASTA1", cfg.DiasHasta1.ToString(CultureInfo.InvariantCulture), token);
            await Set(cn, "DIAS-HASTA2", cfg.DiasHasta2.ToString(CultureInfo.InvariantCulture), token);
            await Set(cn, "FORMA-PAGO", cfg.FormaPago ?? string.Empty, token);
            await Set(cn, "SALDO-MINIMO", cfg.SaldoMinimo.ToString(CultureInfo.InvariantCulture), token);
            await Set(cn, "TOLERANCIA", Math.Max(0, cfg.Tolerancia).ToString(CultureInfo.InvariantCulture), token);
            await Set(cn, "DIAS-ENTRE-ENVIOS", Math.Max(0, cfg.DiasEntreEnvios).ToString(CultureInfo.InvariantCulture), token);
            await Set(cn, "PORTAL-DIAS", Math.Clamp(cfg.PortalDias, 1, 90).ToString(CultureInfo.InvariantCulture), token);
            await Set(cn, "WA-PLANTILLA", cfg.IdPlantillaWhatsApp > 0 ? cfg.IdPlantillaWhatsApp.ToString(CultureInfo.InvariantCulture) : string.Empty, token);
            foreach (var t in cfg.Tramos)
            {
                await Set(cn, $"ASUNTO{t.Numero}", t.Asunto ?? string.Empty, token);
                await Set(cn, $"MENSAJE{t.Numero}", t.Mensaje ?? string.Empty, token);
            }
            return true;
        }, ct);

    // Convención de TA_CONFIGURACION: hasta 150 caracteres en VALOR, más largo en ValorAux. Nunca se borra la tabla.
    private static async Task Set(SqlConnection cn, string clave, string valor, CancellationToken ct, string? descripcion = null)
    {
        var k = Prefijo + clave;
        var largo = valor.Length > 150;
        var n = await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TA_CONFIGURACION SET VALOR=@v, ValorAux=@aux, FechaHora_Modificacion=GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE)))=@k;
            """, new { k, v = largo ? string.Empty : valor, aux = largo ? valor : null }, cancellationToken: ct));
        if (n == 0)
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.TA_CONFIGURACION (GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION,FechaHora_Grabacion) VALUES (@g,@k,@v,@aux,@d,GETDATE());
                """, new { g = Grupo, k, v = largo ? string.Empty : valor, aux = largo ? valor : null, d = descripcion ?? "Reclamo de saldos" }, cancellationToken: ct));
    }

    /// <summary>Saldo máximo (en $) de un comprobante para considerarlo "diferencia mínima" y no reclamarlo. Default 1,00.</summary>
    private static async Task<decimal> ToleranciaAsync(SqlConnection cn, CancellationToken ct)
    {
        var v = await CorreoSalienteService.LeerConfigAsync(cn, Prefijo + "TOLERANCIA", ct);
        return decimal.TryParse(v?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var t) && t >= 0 ? t : 1m;
    }

    // ---------------------------------------------------------------- Consultas

    /// <summary>Comprobantes con saldo pendiente positivo pero dentro de la tolerancia: para que el usuario los ajuste.</summary>
    public Task<IReadOnlyList<ReclamoDiferenciaMinima>> DiferenciasMinimasAsync(CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ReclamoDiferenciaMinima>>("Diferencias", "No se pudieron leer las diferencias mínimas.", async (cn, user, token) =>
        {
            var tol = await ToleranciaAsync(cn, token);
            return (await cn.QueryAsync<ReclamoDiferenciaMinima>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(i.CUENTA)) Cuenta, LTRIM(RTRIM(ISNULL(c.RAZON_SOCIAL,''))) Nombre, LTRIM(RTRIM(ISNULL(i.UNEGOCIO,''))) Unidad,
                    i.FECHA Fecha, LTRIM(RTRIM(i.TC)) Tc,
                    LTRIM(RTRIM(ISNULL(i.SUCURSAL,''))) + '-' + LTRIM(RTRIM(ISNULL(i.NUMERO,''))) + CASE WHEN LTRIM(RTRIM(ISNULL(i.LETRA,'')))<>'' THEN ' ' + LTRIM(RTRIM(i.LETRA)) ELSE '' END Numero,
                    i.IMPORTE Importe, ISNULL(i.PAGO,0) Pago, i.SALDO Saldo
                FROM dbo.VE_CPTES_IMPAGOS i LEFT JOIN dbo.Vt_Clientes c ON c.CODIGO = i.CUENTA
                WHERE i.SALDO > 0 AND i.SALDO <= @tol AND (@admin=1 OR LTRIM(RTRIM(i.UNEGOCIO))=@uUnidad)
                ORDER BY i.SALDO DESC, i.FECHA;
                """, new { tol, admin = user.Admin, uUnidad = user.Unidad }, commandTimeout: 120, cancellationToken: token))).AsList();
        }, ct);

    public Task<IReadOnlyList<ReclamoUnidad>> UnidadesAsync(CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ReclamoUnidad>>("Unidades", "No se pudieron leer las unidades de negocio.", async (cn, user, token) =>
            (await cn.QueryAsync<ReclamoUnidad>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(Codigo)) Codigo, ISNULL(LTRIM(RTRIM(Descripcion)),'') Descripcion
                FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))<>'' AND (@admin=1 OR LTRIM(RTRIM(Codigo))=@unidad) ORDER BY Codigo;
                """, new { admin = user.Admin, unidad = user.Unidad }, cancellationToken: token))).AsList(), ct);

    public Task<IReadOnlyList<ReclamoPlantillaWhatsApp>> PlantillasWhatsAppAsync(CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ReclamoPlantillaWhatsApp>>("Plantillas", "No se pudieron leer las plantillas de WhatsApp.", async (_, _, token) =>
        {
            var lista = await conversaciones.GetTemplatesAsync(new ConversacionPlantillaFilters { EstadoMeta = "APPROVED" }, token);
            var candidatas = lista.Where(p => p.Activa && string.Equals(p.EstadoMeta, "APPROVED", StringComparison.OrdinalIgnoreCase)).ToList();
            // Solo las que existen en la cuenta de WhatsApp del número que envía (se consulta a Meta, con caché corto).
            var validas = new List<ConversacionPlantillaDto>();
            foreach (var p in candidatas)
                if (await PlantillaEnCuentaAsync(p.IdPlantilla, true, token) != false) validas.Add(p);
            return validas
                .Select(p => new ReclamoPlantillaWhatsApp
                {
                    IdPlantilla = p.IdPlantilla, Nombre = p.NombreVisible.Length > 0 ? p.NombreVisible : p.NombreMeta, Cuerpo = p.CuerpoTexto,
                    Variables = ReclamoSaldosPlantilla.ContarVariablesPlantilla(p.CuerpoTexto)
                }).OrderBy(p => p.Nombre).ToList();
        }, ct);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Base, long Plantilla), (bool Existe, DateTime Hasta)> _cachePlantillas = new();

    /// <summary>
    /// ¿Existe la plantilla en la cuenta de WhatsApp (WABA) del número que envía? Se le pregunta a Meta (SyncTemplateStatusAsync).
    /// true = existe, false = Meta confirma que no, null = no se pudo saber (red, credenciales): en ese caso no se bloquea nada.
    /// </summary>
    private async Task<bool?> PlantillaEnCuentaAsync(long idPlantilla, bool usarCache, CancellationToken ct)
    {
        var clave = (sessions.GetActiveSession()?.BaseId ?? 0, idPlantilla);
        if (usarCache && _cachePlantillas.TryGetValue(clave, out var c) && c.Hasta > DateTime.UtcNow) return c.Existe;
        try
        {
            await conversaciones.SyncTemplateStatusAsync(idPlantilla, null, ct);
            _cachePlantillas[clave] = (true, DateTime.UtcNow.AddMinutes(10));
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var texto = ex.InnerException?.Message ?? ex.Message;
            if (texto.Contains("No se encontro la plantilla en Meta", StringComparison.OrdinalIgnoreCase)
                || texto.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                _cachePlantillas[clave] = (false, DateTime.UtcNow.AddMinutes(10));
                return false;
            }
            return null;
        }
    }

    public Task<IReadOnlyList<ReclamoCliente>> BuscarAsync(ReclamoFiltro filtro, CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ReclamoCliente>>("Buscar", "No se pudieron consultar los saldos de clientes.",
            async (cn, user, token) => await BuscarCoreAsync(cn, user, filtro, null, token), ct);

    // Consulta única del listado y de los envíos. Saldo = VE_SALDOSCTA (neto, como el legacy); si se filtra por unidad de
    // negocio (o el usuario está restringido a una) el saldo es la suma de los comprobantes de esa unidad.
    private async Task<List<ReclamoCliente>> BuscarCoreAsync(SqlConnection cn, Usuario user, ReclamoFiltro filtro, IReadOnlyCollection<string>? cuentas, CancellationToken ct)
    {
        var texto = (filtro.Texto ?? string.Empty).Trim();
        var unidad = (filtro.Unidad ?? string.Empty).Trim();
        var restringido = !user.Admin;
        const string sql = """
            SELECT * FROM (
                SELECT LTRIM(RTRIM(c.CODIGO)) Cuenta, LTRIM(RTRIM(ISNULL(c.RAZON_SOCIAL,''))) Nombre,
                    CONVERT(decimal(18,2), CASE WHEN @filtraUnidad=1 THEN ISNULL(x.Suma,0) ELSE s.SALDO - ISNULL(x.Polvo,0) END) Saldo,
                    CONVERT(decimal(18,2), ISNULL(x.Suma,0)) SumaComprobantes, ISNULL(x.Cant,0) CantidadComprobantes,
                    CONVERT(decimal(18,2), ISNULL(x.Vencido,0)) Vencido, ISNULL(x.CantVenc,0) CantidadVencidos,
                    x.MinFecha FechaMasAntigua,
                    CASE WHEN x.MinVtoVencido IS NULL THEN 0 ELSE DATEDIFF(day, x.MinVtoVencido, @hoy) END DiasAtraso,
                    LTRIM(RTRIM(ISNULL(c.IdVendedor,''))) Vendedor, ISNULL(x.Unidades,'') Unidades
                FROM dbo.Vt_Clientes c
                JOIN dbo.VE_SALDOSCTA s ON s.CUENTA = c.CODIGO
                OUTER APPLY (
                    -- Los comprobantes con saldo <= @tol (centavos de diferencia) no cuentan: ni en saldo, ni en cantidad, ni en días de atraso.
                    SELECT SUM(CASE WHEN i.SALDO > @tol THEN i.SALDO ELSE 0 END) Suma,
                        SUM(CASE WHEN i.SALDO > @tol THEN 1 ELSE 0 END) Cant,
                        SUM(CASE WHEN i.SALDO > @tol AND COALESCE(i.VENCIMIENTO,i.FECHA) < @hoy THEN i.SALDO ELSE 0 END) Vencido,
                        SUM(CASE WHEN i.SALDO > @tol AND COALESCE(i.VENCIMIENTO,i.FECHA) < @hoy THEN 1 ELSE 0 END) CantVenc,
                        MIN(CASE WHEN i.SALDO > @tol THEN i.FECHA END) MinFecha,
                        MIN(CASE WHEN i.SALDO > @tol AND COALESCE(i.VENCIMIENTO,i.FECHA) < @hoy THEN COALESCE(i.VENCIMIENTO,i.FECHA) END) MinVtoVencido,
                        MIN(LTRIM(RTRIM(ISNULL(i.UNEGOCIO,'')))) Unidades,
                        SUM(CASE WHEN i.SALDO <= @tol THEN i.SALDO ELSE 0 END) Polvo
                    FROM dbo.VE_CPTES_IMPAGOS i
                    WHERE i.CUENTA = c.CODIGO AND i.SALDO > 0
                        AND (@unidad = '' OR LTRIM(RTRIM(i.UNEGOCIO)) = @unidad)
                        AND (@restringido = 0 OR LTRIM(RTRIM(i.UNEGOCIO)) = @uUnidad)
                ) x
                WHERE s.SALDO >= @saldoMin AND (@bajas = 1 OR ISNULL(c.Dada_De_Baja,0) = 0)
                    AND (@texto = '' OR c.CODIGO LIKE @patron ESCAPE '~' OR c.RAZON_SOCIAL LIKE @patron ESCAPE '~'
                         OR c.NUMERO_DOCUMENTO LIKE @patron ESCAPE '~')
                    /*CUENTAS*/
            ) t
            WHERE t.Saldo >= @saldoMin AND (@filtraUnidad = 0 OR t.CantidadComprobantes > 0) AND t.DiasAtraso >= @diasMin
            ORDER BY t.Saldo DESC, t.Nombre;
            """;
        var tol = await ToleranciaAsync(cn, ct);
        var salida = new List<ReclamoCliente>();
        var lotes = cuentas is null ? [Array.Empty<string>()] : cuentas.Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(1000).ToList();
        foreach (var lote in lotes)
        {
            var q = cuentas is null ? sql : sql.Replace("/*CUENTAS*/", "AND LTRIM(RTRIM(c.CODIGO)) IN @cuentas");
            salida.AddRange(await cn.QueryAsync<ReclamoCliente>(new CommandDefinition(q, new
            {
                hoy = DateTime.Today, tol, unidad, restringido, uUnidad = user.Unidad, filtraUnidad = unidad.Length > 0 || restringido,
                saldoMin = filtro.SaldoMinimo, bajas = filtro.IncluirBajas, texto,
                patron = "%" + texto.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%",
                diasMin = Math.Max(0, filtro.DiasAtrasoMinimo), cuentas = lote
            }, commandTimeout: 120, cancellationToken: ct)));
        }
        await CompletarContactosAsync(cn, salida, ct);
        await CompletarUltimoReclamoAsync(cn, salida, ct);
        return salida.OrderByDescending(c => c.Saldo).ThenBy(c => c.Nombre).ToList();
    }

    private sealed class ContactoRow { public string Cuenta { get; set; } = ""; public string Email { get; set; } = ""; public string Celular { get; set; } = ""; }
    private sealed class ClienteContactoRow { public string Cuenta { get; set; } = ""; public string Mail { get; set; } = ""; public string Fax { get; set; } = ""; }

    private static async Task CompletarContactosAsync(SqlConnection cn, List<ReclamoCliente> clientes, CancellationToken ct)
    {
        if (clientes.Count == 0) return;
        // Contactos marcados como encargados de pagos/cobranzas (directos y auxiliares); si no hay, el MAIL del cliente.
        var contactos = (await cn.QueryAsync<ContactoRow>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CuentaRel)) Cuenta, LTRIM(RTRIM(ISNULL(email,''))) Email, LTRIM(RTRIM(ISNULL(Celular,''))) Celular
            FROM dbo.MA_CONTACTOS WHERE ISNULL(mailPGCB,0)=1 AND ISNULL(CuentaRel,'')<>''
            UNION ALL
            SELECT LTRIM(RTRIM(a.Cuenta)), LTRIM(RTRIM(ISNULL(b.email,''))), LTRIM(RTRIM(ISNULL(b.Celular,'')))
            FROM dbo.MA_CONTACTOS_CUENTAS a JOIN dbo.MA_CONTACTOS b ON a.idcontacto=b.idcontacto WHERE ISNULL(b.mailPGCB,0)=1;
            """, cancellationToken: ct))).ToLookup(x => x.Cuenta, StringComparer.OrdinalIgnoreCase);
        var propios = (await cn.QueryAsync<ClienteContactoRow>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(c.CODIGO)) Cuenta, LTRIM(RTRIM(ISNULL(c.MAIL,''))) Mail, LTRIM(RTRIM(ISNULL(c.FAX,''))) Fax
            FROM dbo.Vt_Clientes c JOIN dbo.VE_SALDOSCTA s ON s.CUENTA=c.CODIGO WHERE s.SALDO>0;
            """, cancellationToken: ct))).ToDictionary(x => x.Cuenta, StringComparer.OrdinalIgnoreCase);
        foreach (var c in clientes)
        {
            var mails = contactos[c.Cuenta].Select(x => x.Email).Where(x => x.Length > 0).ToList();
            if (mails.Count == 0 && propios.TryGetValue(c.Cuenta, out var p)) mails.Add(p.Mail);
            var (validos, _) = CorreoSalienteService.SepararMails(string.Join(";", mails));
            c.Emails = string.Join("; ", validos);
            var tel = contactos[c.Cuenta].Select(x => ReclamoSaldosPlantilla.NormalizarTelefono(x.Celular)).FirstOrDefault(x => x.Length > 0)
                ?? (propios.TryGetValue(c.Cuenta, out var q) ? ReclamoSaldosPlantilla.NormalizarTelefono(q.Fax) : string.Empty);
            c.Whatsapp = tel;
        }
    }

    private sealed class UltimoRow { public string Cuenta { get; set; } = ""; public DateTime FechaHora { get; set; } public string Canal { get; set; } = ""; }

    private static async Task CompletarUltimoReclamoAsync(SqlConnection cn, List<ReclamoCliente> clientes, CancellationToken ct)
    {
        if (clientes.Count == 0) return;
        var filas = await cn.QueryAsync<UltimoRow>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.ALFACORE_RECLAMO_SALDO_ENVIO',N'U') IS NOT NULL
                SELECT LTRIM(RTRIM(e.Cuenta)) Cuenta, e.FechaHora, e.Canal FROM dbo.ALFACORE_RECLAMO_SALDO_ENVIO e
                WHERE e.IdEnvio IN (SELECT MAX(IdEnvio) FROM dbo.ALFACORE_RECLAMO_SALDO_ENVIO WHERE Estado=N'ENVIADO' AND EsPrueba=0 GROUP BY Cuenta);
            """, cancellationToken: ct));
        var mapa = filas.ToDictionary(x => x.Cuenta, StringComparer.OrdinalIgnoreCase);
        foreach (var c in clientes)
            if (mapa.TryGetValue(c.Cuenta, out var u)) { c.UltimoReclamo = u.FechaHora; c.UltimoCanal = u.Canal; }
    }

    private sealed class DetalleRow
    {
        public string Cuenta { get; set; } = ""; public string Unidad { get; set; } = ""; public DateTime Fecha { get; set; }
        public DateTime? Vencimiento { get; set; } public string Tc { get; set; } = ""; public string Sucursal { get; set; } = "";
        public string Numero { get; set; } = ""; public string Letra { get; set; } = "";
        public decimal Importe { get; set; } public decimal Pago { get; set; } public decimal Saldo { get; set; }
    }

    private static async Task<Dictionary<string, List<ReclamoComprobante>>> DetallesAsync(SqlConnection cn, IReadOnlyCollection<string> cuentas, Usuario user, string unidad, CancellationToken ct)
    {
        var res = new Dictionary<string, List<ReclamoComprobante>>(StringComparer.OrdinalIgnoreCase);
        var hoy = DateTime.Today;
        var tol = await ToleranciaAsync(cn, ct);
        foreach (var lote in cuentas.Chunk(800))
        {
            var filas = await cn.QueryAsync<DetalleRow>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(i.CUENTA)) Cuenta, LTRIM(RTRIM(ISNULL(i.UNEGOCIO,''))) Unidad, i.FECHA Fecha, i.VENCIMIENTO Vencimiento,
                    LTRIM(RTRIM(i.TC)) Tc, LTRIM(RTRIM(ISNULL(i.SUCURSAL,''))) Sucursal, LTRIM(RTRIM(ISNULL(i.NUMERO,''))) Numero, LTRIM(RTRIM(ISNULL(i.LETRA,''))) Letra,
                    i.IMPORTE Importe, ISNULL(i.PAGO,0) Pago, i.SALDO Saldo
                FROM dbo.VE_CPTES_IMPAGOS i
                WHERE LTRIM(RTRIM(i.CUENTA)) IN @cuentas AND i.SALDO > @tol
                    AND (@unidad='' OR LTRIM(RTRIM(i.UNEGOCIO))=@unidad) AND (@admin=1 OR LTRIM(RTRIM(i.UNEGOCIO))=@uUnidad)
                ORDER BY i.CUENTA, i.FECHA, i.SUCURSAL, i.NUMERO;
                """, new { cuentas = lote, tol, unidad, admin = user.Admin, uUnidad = user.Unidad }, commandTimeout: 120, cancellationToken: ct));
            foreach (var f in filas)
            {
                var vto = f.Vencimiento ?? f.Fecha;
                if (!res.TryGetValue(f.Cuenta, out var lista)) res[f.Cuenta] = lista = [];
                lista.Add(new ReclamoComprobante
                {
                    Unidad = f.Unidad, Fecha = f.Fecha, Vencimiento = f.Vencimiento, Tc = f.Tc,
                    Numero = $"{f.Sucursal}-{f.Numero}{(f.Letra.Length > 0 ? " " + f.Letra : "")}", Importe = f.Importe, Pago = f.Pago, Saldo = f.Saldo,
                    DiasVencido = vto.Date < hoy ? (int)(hoy - vto.Date).TotalDays : 0
                });
            }
        }
        return res;
    }

    public Task<IReadOnlyList<ReclamoComprobante>> DetalleClienteAsync(string cuenta, string unidad = "", CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ReclamoComprobante>>("Detalle", "No se pudo leer el detalle del cliente.", async (cn, user, token) =>
        {
            var d = await DetallesAsync(cn, [cuenta.Trim()], user, unidad ?? string.Empty, token);
            return d.TryGetValue(cuenta.Trim(), out var l) ? l : [];
        }, ct);

    // ---------------------------------------------------------------- Armado del mensaje

    private sealed record Armado(ReclamoVistaPrevia Vista, string Texto, string WhatsappDetalle, string FormaPago);

    // La forma de pago es la de la unidad de negocio del ÚLTIMO comprobante pendiente, tal como se carga en
    // Archivos → Unidades de negocio ("Formas de pago", clave FORMADEPAGO_UN_<unidad> de TA_CONFIGURACION, la misma que
    // usa el mail de facturación). Si esa unidad no la tiene, se usa el texto general de este módulo.
    private async Task<string> FormaPagoAsync(SqlConnection cn, ReclamoConfig cfg, IReadOnlyList<ReclamoComprobante> cptes, CancellationToken ct)
    {
        var unidad = (cptes.OrderByDescending(c => c.Fecha).Select(c => c.Unidad).FirstOrDefault() ?? string.Empty).Trim();
        if (unidad.Length > 0)
        {
            var deUnidad = (await CorreoSalienteService.LeerConfigAsync(cn, UnidadesNegocioService.Claves(unidad)["FormaDePago"], ct)).Trim();
            if (deUnidad.Length > 0) return deUnidad;
        }
        return cfg.FormaPago.Trim();
    }

    /// <summary>Datos para armar enlaces al Portal Cliente. <see cref="Problema"/> no vacío = no se puede generar (se avisa en la vista previa).</summary>
    private sealed record PortalCtx(string UrlPortal, string IdWeb, int IdBase, string Problema, bool Generar);

    private async Task<PortalCtx> ResolverPortalAsync(bool generar, CancellationToken ct)
    {
        var baseUrl = ((await conversacionesConfig.GetWhatsAppConfigAsync(ct)).PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            return new("", "", 0, "Falta la URL pública del sistema (Conversaciones → Configuración): <PORTAL> se envía vacío.", generar);
        var idBase = sessions.GetActiveSession()?.BaseId ?? 0;
        var baseCentral = idBase > 0 ? await centralBases.GetByIdAsync(idBase, ct) : null;
        var cliente = baseCentral is { IdCliente.Length: > 0 } ? await centralClientes.GetByIdClienteAsync(baseCentral.IdCliente, ct) : null;
        var idWeb = cliente?.IdWeb?.Trim() ?? string.Empty;
        if (idBase <= 0 || idWeb.Length == 0)
            return new("", "", 0, "No se pudo resolver la empresa/base para el enlace del portal: <PORTAL> se envía vacío.", generar);
        return new($"{baseUrl}/{Uri.EscapeDataString(idWeb)}/{idBase}/portal-cliente", idWeb, idBase, "", generar);
    }

    // En vista previa y simulación NO se crea ningún token (no se escribe en la base): se muestra un enlace de ejemplo.
    private async Task<string> EnlacePortalAsync(PortalCtx p, ReclamoConfig cfg, ReclamoCliente cli, CancellationToken ct)
    {
        if (!p.Generar) return p.UrlPortal + "/acceso?token=SE-GENERA-AL-ENVIAR";
        var email = cli.Emails.Split(';')[0].Trim();
        return await portalAutoLogin.GenerarEnlaceAsync(new PortalClienteGenerarAutoLoginRequestDto
        {
            CodigoCliente = cli.Cuenta, RazonSocial = cli.Nombre, Email = email, IdWeb = p.IdWeb, IdBase = p.IdBase,
            UrlBasePortal = email.Length == 0 ? p.UrlPortal : Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(p.UrlPortal, "email", email),
            ExpiracionMinutos = Math.Max(1, cfg.PortalDias) * 24 * 60, Reutilizable = true
        }, ct);
    }

    private async Task<Armado> ArmarAsync(SqlConnection cn, ReclamoConfig cfg, ReclamoCliente cli, IReadOnlyList<ReclamoComprobante> cptes,
        int tramoForzado, IReadOnlyDictionary<string, string> nombresUnidad, PortalCtx portal, CancellationToken ct)
    {
        var tramo = tramoForzado is >= 1 and <= 3 ? tramoForzado : ReclamoSaldosPlantilla.ElegirTramo(cli.DiasAtraso, cfg.DiasHasta1, cfg.DiasHasta2);
        var t = cfg.Tramos.First(x => x.Numero == tramo);
        var advert = new List<string>();
        if (string.IsNullOrWhiteSpace(t.Mensaje)) advert.Add($"El tramo {tramo} no tiene mensaje configurado.");
        var forma = await FormaPagoAsync(cn, cfg, cptes, ct);
        if (forma.Length == 0 && t.Mensaje.Contains("<FORMADEPAGO>", StringComparison.OrdinalIgnoreCase))
            advert.Add("El mensaje usa <FORMADEPAGO> pero no hay forma de pago configurada.");

        var hoy = DateTime.Today;
        var plano = new Dictionary<string, string>
        {
            ["<NOMBRE>"] = cli.Nombre,
            ["<SALDO>"] = ReclamoSaldosPlantilla.Dinero(cli.Saldo),
            ["<VENCIDO>"] = ReclamoSaldosPlantilla.Dinero(cli.Vencido),
            ["<CANTIDAD>"] = cli.CantidadComprobantes.ToString(Ar),
            ["<DIAS>"] = cli.DiasAtraso.ToString(Ar),
            ["<FECHA>"] = hoy.ToString("dd/MM/yyyy"),
            ["<FECHAANTIGUA>"] = cli.FechaMasAntigua?.ToString("dd/MM/yyyy") ?? "—"
        };
        var html = plano.ToDictionary(k => k.Key, k => System.Net.WebUtility.HtmlEncode(k.Value));
        html["<DETALLE>"] = ReclamoSaldosPlantilla.DetalleHtml(cptes, cli, nombresUnidad);
        html["<FORMADEPAGO>"] = System.Net.WebUtility.HtmlEncode(forma).Replace("\r\n", "\n").Replace("\n", "<br>");
        html["<TARJETAS>"] = ReclamoSaldosPlantilla.TarjetasHtml(cli);
        var mensaje = t.Mensaje ?? string.Empty;
        // <PORTAL> es opcional: solo se resuelve si el mensaje lo usa (y recién al enviar se crea el enlace).
        if (mensaje.Contains("<PORTAL>", StringComparison.OrdinalIgnoreCase))
        {
            if (portal.Problema.Length > 0) { advert.Add(portal.Problema); html["<PORTAL>"] = string.Empty; }
            else
            {
                html["<PORTAL>"] = ReclamoSaldosPlantilla.PortalHtml(await EnlacePortalAsync(portal, cfg, cli, ct), DateTime.Today.AddDays(Math.Max(1, cfg.PortalDias)));
                if (!portal.Generar) advert.Add("El enlace del portal es de ejemplo: se genera uno personal para cada cliente al enviar.");
            }
        }
        // Si el mensaje no trae <DETALLE> igual se agrega la tabla: el cliente tiene que ver qué se le reclama.
        if (!mensaje.Contains("<DETALLE>", StringComparison.OrdinalIgnoreCase))
        {
            mensaje += "\n\n<DETALLE>";
            advert.Add("El mensaje no tenía <DETALLE>: se agregó la tabla de comprobantes al final.");
        }
        var cuerpo = "<div style=\"font-family:Arial,sans-serif;font-size:14px;color:#101828;background:#ffffff\">" + ReclamoSaldosPlantilla.ReemplazarHtml(mensaje, html) + "</div>";
        var asunto = ReclamoSaldosPlantilla.ReemplazarTexto(t.Asunto ?? string.Empty, plano).Replace("\r", " ").Replace("\n", " ").Trim();
        if (asunto.Length == 0) asunto = $"Saldo pendiente de pago - {cli.Nombre}";
        if (!cli.TieneEmail) advert.Add("El cliente no tiene email de cobranzas ni email general cargado.");
        if (!cli.TieneWhatsapp) advert.Add("El cliente no tiene un celular/WhatsApp válido cargado.");
        var vista = new ReclamoVistaPrevia
        {
            Cuenta = cli.Cuenta, Nombre = cli.Nombre, Tramo = tramo, Destinatarios = cli.Emails, Asunto = asunto, CuerpoHtml = cuerpo,
            Whatsapp = cli.Whatsapp, Advertencias = advert
        };
        return new Armado(vista, mensaje, ReclamoSaldosPlantilla.DetalleUnaLinea(cptes, cli), forma);
    }

    public Task<ReclamoVistaPrevia> VistaPreviaAsync(string cuenta, int tramoForzado = 0, CancellationToken ct = default)
        => Ejecutar("VistaPrevia", "No se pudo armar la vista previa del reclamo.", async (cn, user, token) =>
        {
            var cfg = await LeerConfigAsync(cn, token);
            var cli = (await BuscarInternoAsync(cn, user, [cuenta.Trim()], token)).FirstOrDefault()
                ?? throw new InvalidOperationException("El cliente ya no tiene saldo pendiente en tus unidades de negocio.");
            var det = await DetallesAsync(cn, [cli.Cuenta], user, string.Empty, token);
            var armado = await ArmarAsync(cn, cfg, cli, det.GetValueOrDefault(cli.Cuenta) ?? [], tramoForzado, await NombresUnidadAsync(cn, token),
                await ResolverPortalAsync(false, token), token);
            if (cfg.IdPlantillaWhatsApp > 0)
            {
                var pl = await conversaciones.GetTemplateAsync(cfg.IdPlantillaWhatsApp, token);
                if (pl is not null)
                    armado.Vista.WhatsappTexto = ReclamoSaldosPlantilla.RellenarPlantilla(pl.CuerpoTexto,
                        ValoresWhatsApp(pl, cli, armado.WhatsappDetalle, armado.FormaPago));
            }
            return armado.Vista;
        }, ct);

    private Task<List<ReclamoCliente>> BuscarInternoAsync(SqlConnection cn, Usuario user, IReadOnlyCollection<string> cuentas, CancellationToken ct)
        => BuscarCoreAsync(cn, user, new ReclamoFiltro { SaldoMinimo = 0.01m, IncluirBajas = true }, cuentas, ct);

    private async Task<Dictionary<string, string>> NombresUnidadAsync(SqlConnection cn, CancellationToken ct)
        => (await cn.QueryAsync<(string Codigo, string Descripcion)>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(Codigo)), ISNULL(LTRIM(RTRIM(Descripcion)),'') FROM dbo.V_TA_UnidadNegocio;", cancellationToken: ct)))
            .GroupBy(x => x.Codigo).ToDictionary(g => g.Key, g => g.First().Descripcion);

    private static IReadOnlyList<string> ValoresWhatsApp(ConversacionPlantillaDto pl, ReclamoCliente cli, string detalleUnaLinea, string forma)
    {
        var n = ReclamoSaldosPlantilla.ContarVariablesPlantilla(pl.CuerpoTexto);
        var porKey = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WhatsAppTemplateVariableCatalog.ClienteNombre] = cli.Nombre,
            [WhatsAppTemplateVariableCatalog.ContactName] = cli.Nombre,
            [WhatsAppTemplateVariableCatalog.ClienteCodigo] = cli.Cuenta,
            [WhatsAppTemplateVariableCatalog.CobranzaDetalleDeuda] = detalleUnaLinea,
            [WhatsAppTemplateVariableCatalog.PagoFormaPago] = ReclamoSaldosPlantilla.LimpiarParaWhatsApp(forma),
            [WhatsAppTemplateVariableCatalog.FechaHoy] = DateTime.Today.ToString("dd/MM/yyyy")
        };
        // Orden por defecto para plantillas sin variables mapeadas: nombre, detalle de la deuda, forma de pago.
        string[] orden = [WhatsAppTemplateVariableCatalog.ClienteNombre, WhatsAppTemplateVariableCatalog.CobranzaDetalleDeuda, WhatsAppTemplateVariableCatalog.PagoFormaPago];
        var valores = new List<string>();
        for (var i = 1; i <= n; i++)
        {
            var key = pl.VariableMappings.TryGetValue(i, out var k) ? k : i <= orden.Length ? orden[i - 1] : string.Empty;
            var v = porKey.TryGetValue(key, out var val) && val.Length > 0 ? val : "-";
            valores.Add(v);
        }
        return valores;
    }

    // ---------------------------------------------------------------- Historial

    /// <summary>Últimos 500 envíos de la bitácora que cumplan el filtro (más recientes primero). Solo lectura.</summary>
    public Task<IReadOnlyList<ReclamoEnvioFila>> EnviosAsync(ReclamoEnvioFiltro filtro, CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ReclamoEnvioFila>>("Envios", "No se pudo leer el historial de reclamos.", async (cn, _, token) =>
        {
            var texto = (filtro.Texto ?? string.Empty).Trim();
            var filas = (await cn.QueryAsync<ReclamoEnvioFila>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.ALFACORE_RECLAMO_SALDO_ENVIO', N'U') IS NOT NULL
                    SELECT TOP (500) IdEnvio, FechaHora, LTRIM(RTRIM(Usuario)) Usuario, LTRIM(RTRIM(Cuenta)) Cuenta, LTRIM(RTRIM(Nombre)) Nombre,
                        Canal, Destino, EsPrueba, Tramo, Saldo, Vencido, Comprobantes, ISNULL(Asunto,'') Asunto, Estado, ISNULL(Detalle,'') Detalle, IdLote, IdConversacion, IdMensaje
                    FROM dbo.ALFACORE_RECLAMO_SALDO_ENVIO
                    WHERE (@desde IS NULL OR FechaHora >= @desde) AND (@hasta IS NULL OR FechaHora < DATEADD(day, 1, @hasta))
                        AND (@canal = '' OR Canal = @canal) AND (@estado = '' OR Estado = @estado) AND (@pruebas = 1 OR EsPrueba = 0)
                        AND (@texto = '' OR Nombre LIKE @patron ESCAPE '~' OR Cuenta LIKE @patron ESCAPE '~' OR Destino LIKE @patron ESCAPE '~' OR Asunto LIKE @patron ESCAPE '~')
                    ORDER BY IdEnvio DESC;
                """, new
            {
                desde = filtro.Desde?.Date, hasta = filtro.Hasta?.Date, canal = (filtro.Canal ?? string.Empty).Trim().ToUpperInvariant(),
                estado = (filtro.Estado ?? string.Empty).Trim().ToUpperInvariant(), pruebas = filtro.IncluirPruebas, texto,
                patron = "%" + texto.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%"
            }, cancellationToken: token))).AsList();
            await CompletarResultadosAsync(cn, filas, token);
            return filas;
        }, ct);

    private sealed record MensajeRow(long IdMensaje, string Estado);
    private sealed record EntranteRow(long IdConversacion, DateTime FechaHora);
    private sealed record TokenRow(string CodigoCliente, DateTime Creacion, DateTime Uso);
    private sealed record CobroRow(string Cuenta, DateTime Grabacion, decimal Importe);
    private sealed record SaldoRow(string Cuenta, decimal Saldo);

    // Señales de seguimiento de cada reclamo. Cada una se arma con su propia consulta y solo si la tabla existe
    // (hay bases sin Conversaciones). Nada se guarda: se calcula al consultar, con los datos de ese momento.
    private static async Task CompletarResultadosAsync(SqlConnection cn, List<ReclamoEnvioFila> filas, CancellationToken ct)
    {
        var reales = filas.Where(f => f.Estado == "ENVIADO").ToList();
        if (reales.Count == 0) return;
        var desde = reales.Min(f => f.FechaHora).AddMinutes(-15);

        var msgIds = reales.Where(f => f.IdMensaje is not null).Select(f => f.IdMensaje!.Value).Distinct().ToList();
        if (msgIds.Count > 0)
        {
            var estados = (await cn.QueryAsync<MensajeRow>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NOT NULL
                    SELECT IdMensaje, ISNULL(EstadoEnvio, N'') Estado FROM dbo.CONV_MENSAJES WHERE IdMensaje IN @ids;
                """, new { ids = msgIds }, cancellationToken: ct))).ToDictionary(x => x.IdMensaje, x => x.Estado);
            foreach (var f in reales.Where(f => f.IdMensaje is not null))
                if (estados.TryGetValue(f.IdMensaje!.Value, out var e)) f.EstadoMensaje = e;

            var convs = reales.Where(f => f.IdConversacion is not null).Select(f => f.IdConversacion!.Value).Distinct().ToList();
            var entrantes = (await cn.QueryAsync<EntranteRow>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NOT NULL
                    SELECT IdConversacion, FechaHora FROM dbo.CONV_MENSAJES
                    WHERE Direction = N'ENTRANTE' AND IdConversacion IN @convs AND FechaHora >= @desde;
                """, new { convs, desde }, cancellationToken: ct))).ToList();
            foreach (var f in reales.Where(f => f.IdConversacion is not null))
            {
                var r = entrantes.Where(x => x.IdConversacion == f.IdConversacion && x.FechaHora > f.FechaHora).OrderBy(x => x.FechaHora).FirstOrDefault();
                if (r is not null) f.Respondio = r.FechaHora;
            }
        }

        var cuentas = reales.Select(f => f.Cuenta).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // El enlace al portal se crea justo antes de mandar el mail: se asocia el acceso del mismo cliente creado en esos minutos.
        var tokens = (await cn.QueryAsync<TokenRow>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN', N'U') IS NOT NULL
                SELECT LTRIM(RTRIM(CodigoCliente)) CodigoCliente, FechaHora_Creacion Creacion, FechaHora_Uso Uso
                FROM dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN
                WHERE LTRIM(RTRIM(CodigoCliente)) IN @cuentas AND FechaHora_Creacion >= @desde AND FechaHora_Uso IS NOT NULL;
            """, new { cuentas, desde }, cancellationToken: ct))).ToList();
        foreach (var f in reales.Where(f => f.Canal == "EMAIL"))
        {
            var t = tokens.Where(x => x.CodigoCliente.Equals(f.Cuenta, StringComparison.OrdinalIgnoreCase)
                    && x.Creacion >= f.FechaHora.AddMinutes(-15) && x.Creacion <= f.FechaHora.AddMinutes(1)).OrderBy(x => x.Uso).FirstOrDefault();
            if (t is not null) f.AbrioPortal = t.Uso;
        }

        var cobros = (await cn.QueryAsync<CobroRow>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.VE_COBRANZAS_REALIZADAS', N'V') IS NOT NULL
                SELECT LTRIM(RTRIM(CUENTA)) Cuenta, FechaHora_Grabacion Grabacion, CONVERT(decimal(18,2), ISNULL(IMPORTE,0)) Importe
                FROM dbo.VE_COBRANZAS_REALIZADAS WHERE LTRIM(RTRIM(CUENTA)) IN @cuentas AND FechaHora_Grabacion >= @desde;
            """, new { cuentas, desde }, cancellationToken: ct))).ToList();
        var saldos = (await cn.QueryAsync<SaldoRow>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.VE_SALDOSCTA', N'V') IS NOT NULL
                SELECT LTRIM(RTRIM(CUENTA)) Cuenta, CONVERT(decimal(18,2), SALDO) Saldo FROM dbo.VE_SALDOSCTA WHERE LTRIM(RTRIM(CUENTA)) IN @cuentas;
            """, new { cuentas }, cancellationToken: ct))).ToDictionary(x => x.Cuenta, x => x.Saldo, StringComparer.OrdinalIgnoreCase);
        foreach (var f in reales)
        {
            f.CobradoDesdeEnvio = cobros.Where(c => c.Cuenta.Equals(f.Cuenta, StringComparison.OrdinalIgnoreCase) && c.Grabacion >= f.FechaHora).Sum(c => c.Importe);
            if (saldos.TryGetValue(f.Cuenta, out var s)) f.SaldoActual = s;
        }
    }

    /// <summary>Lo enviado en un reclamo: cuerpo tal como salió y comprobantes reclamados (los envíos viejos no los tienen).</summary>
    public Task<ReclamoEnvioDetalle> EnvioDetalleAsync(long idEnvio, CancellationToken ct = default)
        => Ejecutar("EnvioDetalle", "No se pudo leer el detalle del envío.", async (cn, _, token) =>
        {
            var d = new ReclamoEnvioDetalle();
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.ALFACORE_RECLAMO_SALDO_ENVIO_CPTE',N'U') IS NULL THEN 0 ELSE 1 END;", cancellationToken: token)) == 0)
                return d;
            d.Cuerpo = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT Cuerpo FROM dbo.ALFACORE_RECLAMO_SALDO_ENVIO WHERE IdEnvio=@idEnvio;", new { idEnvio }, cancellationToken: token)) ?? string.Empty;
            d.Comprobantes = (await cn.QueryAsync<ReclamoComprobante>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(Unidad)) Unidad, LTRIM(RTRIM(Tc)) Tc, LTRIM(RTRIM(Numero)) Numero, ISNULL(Fecha,0) Fecha, Vencimiento, Importe, Saldo, DiasVencido
                FROM dbo.ALFACORE_RECLAMO_SALDO_ENVIO_CPTE WHERE IdEnvio=@idEnvio ORDER BY IdCpte;
                """, new { idEnvio }, cancellationToken: token))).AsList();
            return d;
        }, ct);

    // ---------------------------------------------------------------- Línea de tiempo del cliente

    private sealed record EventoEnvio(long IdEnvio, DateTime FechaHora, string Canal, string Destino, bool EsPrueba, int Tramo, decimal Saldo, string Asunto, string Estado, string Detalle);
    private sealed record EventoMensaje(long IdMensaje, long IdConversacion, DateTime FechaHora, string Direction, string Texto);
    private sealed record EventoCobro(DateTime Fecha, string Tc, string Comprobante, decimal Importe, string Detalle);
    private sealed record EventoPortal(DateTime Uso);

    /// <summary>
    /// Todo lo que pasó con un cliente desde su primer reclamo (hasta 12 meses atrás): reclamos enviados, mensajes de las conversaciones
    /// de WhatsApp asociadas (respuestas del cliente y del equipo), accesos al portal y cobranzas. Más reciente primero. Solo lectura.
    /// </summary>
    public Task<IReadOnlyList<ReclamoEvento>> LineaTiempoAsync(string cuenta, CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ReclamoEvento>>("LineaTiempo", "No se pudo armar el historial del cliente.", async (cn, _, token) =>
        {
            cuenta = (cuenta ?? string.Empty).Trim();
            var eventos = new List<ReclamoEvento>();
            var desde = DateTime.Now.AddMonths(-12);
            var envios = (await cn.QueryAsync<EventoEnvio>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.ALFACORE_RECLAMO_SALDO_ENVIO', N'U') IS NOT NULL
                    SELECT IdEnvio, FechaHora, Canal, Destino, EsPrueba, CONVERT(int, Tramo) Tramo, Saldo, ISNULL(Asunto,'') Asunto, Estado, ISNULL(Detalle,'') Detalle
                    FROM dbo.ALFACORE_RECLAMO_SALDO_ENVIO WHERE LTRIM(RTRIM(Cuenta)) = @cuenta AND FechaHora >= @desde ORDER BY FechaHora DESC;
                """, new { cuenta, desde }, cancellationToken: token))).ToList();
            // Con la trazabilidad (script 2026-11-17-051) se lee también la conversación y el mensaje de cada WhatsApp.
            var conv = (await cn.QueryAsync<(long IdEnvio, long? IdConversacion, long? IdMensaje)>(new CommandDefinition("""
                IF COL_LENGTH(N'dbo.ALFACORE_RECLAMO_SALDO_ENVIO', N'IdConversacion') IS NOT NULL
                    EXEC sys.sp_executesql N'SELECT IdEnvio, IdConversacion, IdMensaje FROM dbo.ALFACORE_RECLAMO_SALDO_ENVIO WHERE LTRIM(RTRIM(Cuenta)) = @cuenta AND FechaHora >= @desde',
                        N'@cuenta nvarchar(30), @desde datetime', @cuenta = @cuenta, @desde = @desde;
                """, new { cuenta, desde }, cancellationToken: token))).ToDictionary(x => x.IdEnvio);
            foreach (var e in envios)
            {
                var (c, m) = conv.TryGetValue(e.IdEnvio, out var x) ? (x.IdConversacion, x.IdMensaje) : (null, null);
                var canal = e.Canal == "WHATSAPP" ? "WhatsApp" : "Email";
                eventos.Add(new ReclamoEvento
                {
                    Fecha = e.FechaHora, Tipo = "RECLAMO", Error = e.Estado == "ERROR", IdConversacion = c, Importe = e.Saldo,
                    Titulo = e.Estado == "ERROR" ? $"Reclamo por {canal} con error" : $"Reclamo por {canal} (tramo {e.Tramo}){(e.EsPrueba ? " · prueba" : string.Empty)}",
                    Detalle = e.Estado == "ERROR" ? e.Detalle : $"A {e.Destino}{(e.Asunto.Length > 0 ? " · " + e.Asunto : string.Empty)}. Saldo reclamado."
                });
            }
            var primero = envios.Where(e => !e.EsPrueba && e.Estado == "ENVIADO").Select(e => (DateTime?)e.FechaHora).DefaultIfEmpty(null).Min();
            if (primero is null) return eventos;

            var convIds = conv.Values.Where(v => v.IdConversacion is not null).Select(v => v.IdConversacion!.Value).Distinct().ToList();
            var propios = conv.Values.Where(v => v.IdMensaje is not null).Select(v => v.IdMensaje!.Value).ToHashSet();
            if (convIds.Count > 0)
            {
                var mensajes = await cn.QueryAsync<EventoMensaje>(new CommandDefinition("""
                    IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NOT NULL
                        SELECT TOP (60) IdMensaje, IdConversacion, FechaHora, Direction, ISNULL(CONVERT(nvarchar(500), Texto), N'') Texto FROM dbo.CONV_MENSAJES
                        WHERE IdConversacion IN @convIds AND FechaHora >= @primero AND Direction IN (N'ENTRANTE', N'SALIENTE') ORDER BY FechaHora DESC;
                    """, new { convIds, primero }, cancellationToken: token));
                foreach (var m in mensajes.Where(m => !propios.Contains(m.IdMensaje)))
                    eventos.Add(new ReclamoEvento
                    {
                        Fecha = m.FechaHora, IdConversacion = m.IdConversacion, Tipo = m.Direction == "ENTRANTE" ? "RESPUESTA" : "MENSAJE",
                        Titulo = m.Direction == "ENTRANTE" ? "El cliente respondió por WhatsApp" : "Mensaje del equipo por WhatsApp", Detalle = m.Texto
                    });
            }

            var portal = await cn.QueryAsync<EventoPortal>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN', N'U') IS NOT NULL
                    SELECT FechaHora_Uso Uso FROM dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN
                    WHERE LTRIM(RTRIM(CodigoCliente)) = @cuenta AND FechaHora_Uso IS NOT NULL AND FechaHora_Uso >= @primero;
                """, new { cuenta, primero }, cancellationToken: token));
            eventos.AddRange(portal.Select(p => new ReclamoEvento { Fecha = p.Uso, Tipo = "PORTAL", Titulo = "Entró al portal de clientes" }));

            var cobros = await cn.QueryAsync<EventoCobro>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.VE_COBRANZAS_REALIZADAS', N'V') IS NOT NULL
                    SELECT FechaHora_Grabacion Fecha, ISNULL(LTRIM(RTRIM(TC)),'') Tc, ISNULL(idcomprobante,'') Comprobante,
                        CONVERT(decimal(18,2), ISNULL(IMPORTE,0)) Importe, ISNULL(LTRIM(RTRIM(DETALLE)),'') Detalle
                    FROM dbo.VE_COBRANZAS_REALIZADAS WHERE LTRIM(RTRIM(CUENTA)) = @cuenta AND FechaHora_Grabacion >= @primero;
                """, new { cuenta, primero }, cancellationToken: token));
            eventos.AddRange(cobros.Select(c => new ReclamoEvento
            {
                Fecha = c.Fecha, Tipo = "COBRANZA", Importe = c.Importe, Titulo = $"Cobranza registrada {c.Tc} {c.Comprobante}".Trim(), Detalle = c.Detalle
            }));
            return eventos.OrderByDescending(e => e.Fecha).ToList();
        }, ct);

    // ---------------------------------------------------------------- Envío

    /// <summary>
    /// Envía (o simula) el reclamo a cada cuenta por el canal pedido. Un error en un cliente no frena a los demás.
    /// <paramref name="progreso"/> recibe cada resultado apenas se conoce, para mostrar avance en pantalla.
    /// </summary>
    public Task<IReadOnlyList<ReclamoEnvioResultado>> EnviarAsync(ReclamoEnvioSolicitud sol, IProgress<ReclamoEnvioResultado>? progreso = null, CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ReclamoEnvioResultado>>("Enviar", "No se pudieron enviar los reclamos.", async (cn, user, token) =>
        {
            if (sol.Cuentas.Count == 0) throw new InvalidOperationException("No hay clientes seleccionados.");
            var enviarMail = sol.Canal is ReclamoCanal.Email or ReclamoCanal.Ambos;
            var enviarWa = sol.Canal is ReclamoCanal.WhatsApp or ReclamoCanal.Ambos;
            var cfg = await LeerConfigAsync(cn, token);
            if (enviarWa && !sol.SoloSimular && cfg.IdPlantillaWhatsApp <= 0)
                throw new InvalidOperationException("Elegí la plantilla de WhatsApp en la configuración del reclamo.");
            if (!string.IsNullOrWhiteSpace(sol.MailPrueba) && !CorreoSalienteService.EsMailValido(sol.MailPrueba))
                throw new InvalidOperationException("El email de prueba no es válido.");
            // Destino elegido a mano para este envío (desde "Ver mensaje"): es un envío real, no de prueba.
            var mailManual = string.Empty;
            if (!string.IsNullOrWhiteSpace(sol.MailDestino))
            {
                var (ok, malos) = CorreoSalienteService.SepararMails(sol.MailDestino);
                if (ok.Count == 0 || malos.Count > 0)
                    throw new InvalidOperationException("El email indicado no es válido" + (malos.Count > 0 ? ": " + string.Join(", ", malos) : ".") );
                mailManual = string.Join("; ", ok);
            }
            var plantilla = enviarWa && cfg.IdPlantillaWhatsApp > 0 ? await conversaciones.GetTemplateAsync(cfg.IdPlantillaWhatsApp, token) : null;
            if (enviarWa && !sol.SoloSimular && plantilla is null)
                throw new InvalidOperationException("La plantilla de WhatsApp configurada ya no existe.");
            // Antes de tocar nada: la plantilla tiene que existir en la cuenta de WhatsApp del número que envía.
            if (enviarWa && !sol.SoloSimular && plantilla is not null && await PlantillaEnCuentaAsync(plantilla.IdPlantilla, false, token) == false)
                throw new InvalidOperationException($"La plantilla \"{plantilla.NombreVisible}\" no existe en la cuenta de WhatsApp del número que envía. Elegí otra en Mensaje y configuración o creala en esa cuenta de Meta.");

            var clientes = await BuscarInternoAsync(cn, user, sol.Cuentas, token);
            var detalles = await DetallesAsync(cn, clientes.Select(c => c.Cuenta).ToList(), user, string.Empty, token);
            var nombresUnidad = await NombresUnidadAsync(cn, token);
            var portalBase = await ResolverPortalAsync(false, token);
            var esPrueba = !string.IsNullOrWhiteSpace(sol.MailPrueba) || !string.IsNullOrWhiteSpace(sol.TelefonoPrueba);
            CorreoSalienteService.CuentaCorreo? cuentaMail = null;
            var resultados = new List<ReclamoEnvioResultado>();
            var lote = Guid.NewGuid();
            void Registrar(ReclamoEnvioResultado r) { resultados.Add(r); progreso?.Report(r); }

            foreach (var cuenta in sol.Cuentas.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                var cli = clientes.FirstOrDefault(c => c.Cuenta.Equals(cuenta.Trim(), StringComparison.OrdinalIgnoreCase));
                if (cli is null)
                {
                    Registrar(new() { Cuenta = cuenta, Canal = sol.Canal.ToString(), Estado = "Omitido", Detalle = "Ya no tiene saldo pendiente en tus unidades de negocio." });
                    continue;
                }
                // Aviso de avance: "Procesando" no es un resultado, solo le dice a la pantalla qué cliente está en curso.
                progreso?.Report(new() { Cuenta = cli.Cuenta, Nombre = cli.Nombre, Estado = "Procesando" });
                var reciente = !sol.IgnorarEnviosRecientes && !esPrueba && cfg.DiasEntreEnvios > 0 && cli.UltimoReclamo is { } ult
                    && (DateTime.Now - ult).TotalDays < cfg.DiasEntreEnvios;
                // El enlace al portal (crea un token en la base) solo se genera si de verdad se va a mandar el mail.
                var hayDestinoMail = !string.IsNullOrWhiteSpace(sol.MailPrueba) || mailManual.Length > 0 || cli.TieneEmail;
                var armado = await ArmarAsync(cn, cfg, cli, detalles.GetValueOrDefault(cli.Cuenta) ?? [], sol.TramoForzado, nombresUnidad,
                    portalBase with { Generar = !sol.SoloSimular && enviarMail && !reciente && hayDestinoMail }, token);

                Traza TrazaDe(string? cuerpo, long? conv = null, long? msg = null) => new(lote, conv, msg, cuerpo, detalles.GetValueOrDefault(cli.Cuenta) ?? []);

                if (enviarMail)
                {
                    var r = new ReclamoEnvioResultado { Cuenta = cli.Cuenta, Nombre = cli.Nombre, Canal = "Email" };
                    try
                    {
                        var destino = !string.IsNullOrWhiteSpace(sol.MailPrueba) ? sol.MailPrueba!.Trim() : mailManual.Length > 0 ? mailManual : cli.Emails;
                        r.Destino = destino;
                        if (reciente) { r.Estado = "Omitido"; r.Detalle = $"Recibió un reclamo el {cli.UltimoReclamo:dd/MM/yyyy} (hace menos de {cfg.DiasEntreEnvios} días)."; }
                        else if (destino.Length == 0) { r.Estado = "Omitido"; r.Detalle = "Sin email."; }
                        else if (sol.SoloSimular) { r.Estado = "Simulado"; r.Detalle = $"Tramo {armado.Vista.Tramo}. No se envió nada."; }
                        else
                        {
                            cuentaMail ??= await correo.ResolverAsync(cn, user.Nombre, token);
                            var (validos, _) = CorreoSalienteService.SepararMails(destino);
                            var asunto = esPrueba ? "[PRUEBA] " + armado.Vista.Asunto : armado.Vista.Asunto;
                            await correo.EnviarAsync(cn, cuentaMail, validos, asunto, armado.Vista.CuerpoHtml, [], token);
                            r.Estado = "Enviado"; r.Detalle = $"Tramo {armado.Vista.Tramo}.";
                            await Bitacora(cn, user, cli, "EMAIL", destino, esPrueba, armado.Vista, "ENVIADO", null, TrazaDe(MascararToken(armado.Vista.CuerpoHtml)), token);
                            await Task.Delay(300, token);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        r.Estado = "Error"; r.Detalle = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                        await events.LogErrorAsync("ReclamoSaldos", "EnviarEmail", ex, "No se pudo enviar el reclamo por email.", ct: token);
                        await Bitacora(cn, user, cli, "EMAIL", r.Destino, esPrueba, armado.Vista, "ERROR", r.Detalle, TrazaDe(MascararToken(armado.Vista.CuerpoHtml)), token);
                    }
                    Registrar(r);
                }

                if (enviarWa)
                {
                    var r = new ReclamoEnvioResultado { Cuenta = cli.Cuenta, Nombre = cli.Nombre, Canal = "WhatsApp" };
                    try
                    {
                        var tel = !string.IsNullOrWhiteSpace(sol.TelefonoPrueba) ? ReclamoSaldosPlantilla.NormalizarTelefono(sol.TelefonoPrueba) : cli.Whatsapp;
                        r.Destino = tel;
                        if (reciente) { r.Estado = "Omitido"; r.Detalle = $"Recibió un reclamo el {cli.UltimoReclamo:dd/MM/yyyy} (hace menos de {cfg.DiasEntreEnvios} días)."; }
                        else if (tel.Length == 0) { r.Estado = "Omitido"; r.Detalle = "Sin celular/WhatsApp válido."; }
                        else if (sol.SoloSimular) { r.Estado = "Simulado"; r.Detalle = $"Tramo {armado.Vista.Tramo}. No se envió nada."; }
                        else
                        {
                            var valores = ValoresWhatsApp(plantilla!, cli, armado.WhatsappDetalle, armado.FormaPago);
                            var conv = await conversaciones.CreateOrGetWhatsAppConversationAsync(new ConversacionCrearWhatsAppRequest
                            { TelefonoWhatsApp = tel, UsuarioAccion = user.Nombre }, token);
                            var msg = await conversaciones.SendTemplateMessageAsync(new ConversacionPlantillaSendRequest
                            {
                                IdConversacion = conv.IdConversacion, IdPlantilla = plantilla!.IdPlantilla, NombreMeta = plantilla.NombreMeta, Idioma = plantilla.Idioma,
                                EsMetaRemota = plantilla.EsMetaRemota, UsuarioAccion = user.Nombre, ValoresVariables = [.. valores]
                            }, token);
                            r.Estado = "Enviado"; r.Detalle = $"Tramo {armado.Vista.Tramo}. Estado Meta: {msg.EstadoEnvio}.";
                            await Bitacora(cn, user, cli, "WHATSAPP", tel, esPrueba, armado.Vista, "ENVIADO", r.Detalle,
                                TrazaDe(ReclamoSaldosPlantilla.RellenarPlantilla(plantilla.CuerpoTexto, valores), conv.IdConversacion, msg.IdMensaje), token);
                            await Task.Delay(500, token);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // ExecuteLoggedAsync envuelve la causa real en AppUserFacingException: mostrarla junto con el código de incidente.
                        var detalleWa = ex is AppUserFacingException { InnerException: { } causa } uf
                            ? $"{causa.Message} (incidente {uf.ErrorCode})" : ex.Message;
                        r.Estado = "Error"; r.Detalle = detalleWa.Length > 300 ? detalleWa[..300] : detalleWa;
                        await events.LogErrorAsync("ReclamoSaldos", "EnviarWhatsApp", ex, "No se pudo enviar el reclamo por WhatsApp.", ct: token);
                        await Bitacora(cn, user, cli, "WHATSAPP", r.Destino, esPrueba, armado.Vista, "ERROR", r.Detalle, TrazaDe(null), token);
                    }
                    Registrar(r);
                }
            }
            return resultados;
        }, ct);

    /// <summary>Lo que se guarda para poder reconstruir un envío: lote, enlace con Conversaciones, cuerpo y comprobantes reclamados.</summary>
    private sealed record Traza(Guid Lote, long? IdConversacion, long? IdMensaje, string? Cuerpo, IReadOnlyList<ReclamoComprobante> Comprobantes);

    // El enlace al portal es una credencial: no se guarda en claro en la bitácora.
    private static string MascararToken(string html)
        => System.Text.RegularExpressions.Regex.Replace(html ?? string.Empty, @"token=[A-Za-z0-9_\-]+", "token=***");

    private static async Task Bitacora(SqlConnection cn, Usuario user, ReclamoCliente cli, string canal, string destino, bool prueba,
        ReclamoVistaPrevia vista, string estado, string? detalle, Traza traza, CancellationToken ct)
    {
        var idEnvio = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO dbo.ALFACORE_RECLAMO_SALDO_ENVIO (Usuario,Cuenta,Nombre,Canal,Destino,EsPrueba,Tramo,Saldo,Vencido,Comprobantes,Asunto,Estado,Detalle,IdLote,IdConversacion,IdMensaje,Cuerpo)
            VALUES (@u,@cuenta,@nombre,@canal,@destino,@prueba,@tramo,@saldo,@vencido,@cptes,@asunto,@estado,@detalle,@lote,@conv,@msg,@cuerpo);
            SELECT CONVERT(bigint, SCOPE_IDENTITY());
            """, new
        {
            u = Trunc(user.Nombre, 60), cuenta = Trunc(cli.Cuenta, 30), nombre = Trunc(cli.Nombre, 150), canal, destino = Trunc(destino, 300), prueba,
            tramo = vista.Tramo, saldo = cli.Saldo, vencido = cli.Vencido, cptes = cli.CantidadComprobantes, asunto = Trunc(vista.Asunto, 300),
            estado, detalle = Trunc(detalle, 500), lote = traza.Lote, conv = traza.IdConversacion, msg = traza.IdMensaje, cuerpo = traza.Cuerpo
        }, cancellationToken: ct));
        if (traza.Comprobantes.Count == 0) return;
        await cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.ALFACORE_RECLAMO_SALDO_ENVIO_CPTE (IdEnvio,Unidad,Tc,Numero,Fecha,Vencimiento,Importe,Saldo,DiasVencido)
            VALUES (@idEnvio,@unidad,@tc,@numero,@fecha,@vto,@importe,@saldo,@dias);
            """, traza.Comprobantes.Select(c => new
        {
            idEnvio, unidad = Trunc(c.Unidad, 10) ?? string.Empty, tc = Trunc(c.Tc, 10) ?? string.Empty, numero = Trunc(c.Numero, 40) ?? string.Empty,
            fecha = (DateTime?)c.Fecha, vto = c.Vencimiento, importe = c.Importe, saldo = c.Saldo, dias = c.DiasVencido
        }).ToList(), cancellationToken: ct));
    }

    private static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
