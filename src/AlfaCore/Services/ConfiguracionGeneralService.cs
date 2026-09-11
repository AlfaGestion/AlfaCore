using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Equivalente web de FrmAsistenteConfig.frm (C:\dev\ALFAVB6\v_ta_config\FrmAsistenteConfig.frm),
// solapas "Datos empresa" y "Logo y Estilo" -- verificado campo por campo contra el .frm y contra
// datos reales en TA_CONFIGURACION antes de escribir esto (no se inventó ninguna clave). Notas:
//   - El campo mostrado como "Codigo Fiscal N°" graba en la clave NroGanancias: es un desprolijo
//     histórico del form original (el label se cambió alguna vez, la clave nunca), se mantiene
//     así porque es lo que el resto del sistema realmente lee.
//   - "Condición de IVA" sale de TA_CONDIVA (tabla real de referencia), no de una lista fija.
//   - El logo vive en TA_LOGOS (fila fija IDLOGO='LOGOEMPRESA'), no en TA_CONFIGURACION. A
//     diferencia del escritorio (que además deja un logo.jpg en una carpeta local), acá se guarda
//     únicamente como blob -- no depende de ninguna carpeta del servidor.
//   - El selector de "Estilo del sistema" (Azul/Negro/Luna/Royale/iTunes) del form original no se
//     porta: es una preferencia de skin guardada en un .ini local por PC, ni siquiera es un dato
//     de servidor, y no aplica al tema fijo de AlfaDesign.
public sealed class ConfiguracionGeneralService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IConfiguracionGeneralService
{
    private const string ModuleName = "ConfiguracionGeneral";
    private const string LogoId = "LOGOEMPRESA";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<ConfiguracionEmpresaDto> GetEmpresaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetEmpresa", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var valores = await ReadConfigMapAsync(cn, EmpresaClaves, token);

            return new ConfiguracionEmpresaDto
            {
                Nombre = Get(valores, "NOMBRE"),
                Calle = Get(valores, "CALLE"),
                Numero = Get(valores, "NUMERO"),
                Piso = Get(valores, "PISO"),
                Departamento = Get(valores, "DEPARTAMENTO"),
                CodigoPostal = Get(valores, "CPOSTAL"),
                Localidad = Get(valores, "LOCALIDAD"),
                Provincia = Get(valores, "PROVINCIA"),
                Pais = Get(valores, "PAIS"),
                Telefono = Get(valores, "TELEFONO"),
                CondicionIva = Get(valores, "CONDIVAEMPRESA"),
                Cuit = Get(valores, "CUIT"),
                NroIngresosBrutos = Get(valores, "NROINGRESOSBRUTOS"),
                AgenteRetencionIibb = ParseBool(Get(valores, "AGENTEDERETENCION")),
                AgenteRetencionGanancias = ParseBool(Get(valores, "AGENTEDERETENCIONGAN")),
                FechaInicioActividades = ParseFecha(Get(valores, "INICIOACTIVIDADES")),
                PuntoVentaPrincipal = Get(valores, "TPV_SUCURSAL"),
                UnidadNegocio = Get(valores, "UNEGOCIO"),
                CodigoFiscal = Get(valores, "NROGANANCIAS"),
                RegistroIgj = Get(valores, "NROIGJ"),
                RegistroSagpya = Get(valores, "SAGPYA"),
                Pagina = Get(valores, "PAGINA_CONFIGURADA")
            };
        }, "No se pudieron cargar los datos de la empresa.", ct);

    public Task SaveEmpresaAsync(ConfiguracionEmpresaDto dto, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveEmpresa", async token =>
        {
            ArgumentNullException.ThrowIfNull(dto);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await SetConfigAsync(cn, "NOMBRE", dto.Nombre, token);
            await SetConfigAsync(cn, "CALLE", dto.Calle, token);
            await SetConfigAsync(cn, "NUMERO", dto.Numero, token);
            await SetConfigAsync(cn, "PISO", dto.Piso, token);
            await SetConfigAsync(cn, "DEPARTAMENTO", dto.Departamento, token);
            await SetConfigAsync(cn, "CPOSTAL", dto.CodigoPostal, token);
            await SetConfigAsync(cn, "LOCALIDAD", dto.Localidad, token);
            await SetConfigAsync(cn, "PROVINCIA", dto.Provincia, token);
            await SetConfigAsync(cn, "PAIS", dto.Pais, token);
            await SetConfigAsync(cn, "TELEFONO", dto.Telefono, token);
            await SetConfigAsync(cn, "CONDIVAEMPRESA", dto.CondicionIva, token);
            await SetConfigAsync(cn, "CUIT", dto.Cuit, token);
            await SetConfigAsync(cn, "NROINGRESOSBRUTOS", dto.NroIngresosBrutos, token);
            await SetConfigAsync(cn, "AGENTEDERETENCION", dto.AgenteRetencionIibb ? "SI" : "NO", token);
            await SetConfigAsync(cn, "AGENTEDERETENCIONGAN", dto.AgenteRetencionGanancias ? "SI" : "NO", token);
            await SetConfigAsync(cn, "INICIOACTIVIDADES", dto.FechaInicioActividades?.ToString("dd/MM/yyyy") ?? string.Empty, token);
            await SetConfigAsync(cn, "TPV_SUCURSAL", dto.PuntoVentaPrincipal, token);
            await SetConfigAsync(cn, "UNEGOCIO", dto.UnidadNegocio, token);
            await SetConfigAsync(cn, "NROGANANCIAS", dto.CodigoFiscal, token);
            await SetConfigAsync(cn, "NROIGJ", dto.RegistroIgj, token);
            await SetConfigAsync(cn, "SAGPYA", dto.RegistroSagpya, token);
            await SetConfigAsync(cn, "PAGINA_CONFIGURADA", dto.Pagina, token);
        }, "No se pudieron guardar los datos de la empresa.", ct);

    public Task<IReadOnlyList<ConfiguracionCondIvaOptionDto>> GetCondicionesIvaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetCondicionesIva", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await SqlObjectExistsAsync(cn, "dbo.TA_CONDIVA", token))
                return (IReadOnlyList<ConfiguracionCondIvaOptionDto>)Array.Empty<ConfiguracionCondIvaOptionDto>();

            var rows = await cn.QueryAsync<ConfiguracionCondIvaOptionDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(CODIGO)) AS Codigo, ISNULL(DESCRIPCION, '') AS Descripcion
                FROM dbo.TA_CONDIVA ORDER BY DESCRIPCION;
                """, cancellationToken: token));
            return (IReadOnlyList<ConfiguracionCondIvaOptionDto>)rows.AsList();
        }, "No se pudieron cargar las condiciones de IVA.", ct);

    public Task<ConfiguracionLogoDto> GetLogoInfoAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetLogoInfo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var tieneLogo = await SqlObjectExistsAsync(cn, "dbo.TA_LOGOS", token)
                && await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM dbo.TA_LOGOS WHERE IDLOGO = @Id AND IMAGEN IS NOT NULL;",
                    new { Id = LogoId }, cancellationToken: token)) > 0;

            var valores = await ReadConfigMapAsync(cn, LogoClaves, token);
            return new ConfiguracionLogoDto
            {
                TieneLogo = tieneLogo,
                IncluirEnFacturas = ParseBool(Get(valores, "PRINTLOGO_FC_NC_ND")),
                IncluirEnPresupuestos = ParseBool(Get(valores, "PRINTLOGO_PROFORMA")),
                IncluirEnOtrosComprobantes = ParseBool(Get(valores, "PRINTLOGO_OTROS")),
                PosicionIzquierda = ParseBool(Get(valores, "FORMULARIOS_LOGO_IZQUIERDA"))
            };
        }, "No se pudo cargar la configuración del logo.", ct);

    public Task SaveLogoOpcionesAsync(ConfiguracionLogoDto dto, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveLogoOpciones", async token =>
        {
            ArgumentNullException.ThrowIfNull(dto);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await SetConfigAsync(cn, "PRINTLOGO_FC_NC_ND", dto.IncluirEnFacturas ? "SI" : "NO", token);
            await SetConfigAsync(cn, "PRINTLOGO_PROFORMA", dto.IncluirEnPresupuestos ? "SI" : "NO", token);
            await SetConfigAsync(cn, "PRINTLOGO_OTROS", dto.IncluirEnOtrosComprobantes ? "SI" : "NO", token);
            await SetConfigAsync(cn, "FORMULARIOS_LOGO_IZQUIERDA", dto.PosicionIzquierda ? "SI" : "NO", token);
        }, "No se pudo guardar la configuración del logo.", ct);

    public Task<byte[]?> GetLogoBytesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetLogoBytes", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await SqlObjectExistsAsync(cn, "dbo.TA_LOGOS", token))
                return null;

            // Lectura vía SqlDataReader (no ExecuteScalar de Dapper): la columna IMAGEN es del
            // tipo legacy "image", y el camino escalar genérico truncaba el blob en la práctica.
            await using var cmd = new SqlCommand("SELECT TOP (1) IMAGEN FROM dbo.TA_LOGOS WHERE IDLOGO = @Id;", cn);
            cmd.Parameters.AddWithValue("@Id", LogoId);
            await using var reader = await cmd.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) || await reader.IsDBNullAsync(0, token))
                return null;

            return reader.GetFieldValue<byte[]>(0);
        }, "No se pudo cargar el logo.", ct);

    public Task SaveLogoAsync(byte[] contenido, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveLogo", async token =>
        {
            if (contenido is null || contenido.Length == 0)
                throw new InvalidOperationException("El logo está vacío.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM dbo.TA_LOGOS WHERE IDLOGO = @Id;", new { Id = LogoId }, cancellationToken: token));
            if (existe > 0)
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.TA_LOGOS SET IMAGEN = @Imagen WHERE IDLOGO = @Id;",
                    new { Id = LogoId, Imagen = contenido }, cancellationToken: token));
            }
            else
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO dbo.TA_LOGOS (IDLOGO, RUTA, IMAGEN) VALUES (@Id, '', @Imagen);",
                    new { Id = LogoId, Imagen = contenido }, cancellationToken: token));
            }
        }, "No se pudo guardar el logo.", ct);

    public Task DeleteLogoAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("DeleteLogo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.TA_LOGOS WHERE IDLOGO = @Id;", new { Id = LogoId }, cancellationToken: token));
        }, "No se pudo quitar el logo.", ct);

    public Task<ConfiguracionEmailDto> GetEmailAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetEmail", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var valores = await ReadConfigMapAsync(cn, EmailClaves, token);
            return new ConfiguracionEmailDto
            {
                Server = Get(valores, "EMAIL_SERVER"),
                Port = Get(valores, "EMAIL_PORT"),
                Cuenta = Get(valores, "EMAIL_CTA"),
                Password = Get(valores, "EMAIL_PASS"),
                Ssl = ParseBool(Get(valores, "EMAIL_SSL"))
            };
        }, "No se pudo cargar la configuración de correo saliente.", ct);

    public Task SaveEmailAsync(ConfiguracionEmailDto dto, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveEmail", async token =>
        {
            ArgumentNullException.ThrowIfNull(dto);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await SetConfigAsync(cn, "EMAIL_SERVER", (dto.Server ?? string.Empty).Trim(), token);
            await SetConfigAsync(cn, "EMAIL_PORT", (dto.Port ?? string.Empty).Trim(), token);
            await SetConfigAsync(cn, "EMAIL_CTA", (dto.Cuenta ?? string.Empty).Trim(), token);
            await SetConfigAsync(cn, "EMAIL_PASS", (dto.Password ?? string.Empty).Trim(), token);
            await SetConfigAsync(cn, "EMAIL_SSL", dto.Ssl ? "SI" : string.Empty, token);
        }, "No se pudo guardar la configuración de correo saliente.", ct);

    // ---- Helpers privados ----

    private static readonly string[] EmailClaves =
    [
        "EMAIL_SERVER", "EMAIL_PORT", "EMAIL_CTA", "EMAIL_PASS", "EMAIL_SSL"
    ];

    private static readonly string[] EmpresaClaves =
    [
        "NOMBRE", "CALLE", "NUMERO", "PISO", "DEPARTAMENTO", "CPOSTAL", "LOCALIDAD", "PROVINCIA",
        "PAIS", "TELEFONO", "CONDIVAEMPRESA", "CUIT", "NROINGRESOSBRUTOS", "AGENTEDERETENCION",
        "AGENTEDERETENCIONGAN", "INICIOACTIVIDADES", "TPV_SUCURSAL", "UNEGOCIO", "NROGANANCIAS",
        "NROIGJ", "SAGPYA", "PAGINA_CONFIGURADA"
    ];

    private static readonly string[] LogoClaves =
    [
        "PRINTLOGO_FC_NC_ND", "PRINTLOGO_PROFORMA", "PRINTLOGO_OTROS", "FORMULARIOS_LOGO_IZQUIERDA"
    ];

    private static string Get(IReadOnlyDictionary<string, string> valores, string clave)
        => valores.TryGetValue(clave, out var v) ? v : string.Empty;

    private static async Task<Dictionary<string, string>> ReadConfigMapAsync(SqlConnection cn, IReadOnlyList<string> claves, CancellationToken ct)
    {
        var rows = await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) AS Clave, ISNULL(LTRIM(RTRIM(VALOR)), '') AS Valor
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @Claves;
            """, new { Claves = claves }, cancellationToken: ct));
        return rows.ToDictionary(r => r.Clave, r => r.Valor, StringComparer.OrdinalIgnoreCase);
    }

    private async Task SetConfigAsync(SqlConnection cn, string clave, string valor, CancellationToken ct)
    {
        var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;",
            new { Clave = clave.ToUpperInvariant() }, cancellationToken: ct));
        if (existe > 0)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;",
                new { Clave = clave.ToUpperInvariant(), Valor = valor }, cancellationToken: ct));
        }
        else
        {
            await cn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion) VALUES (N'DATOS', @Clave, @Valor, GETDATE());",
                new { Clave = clave.ToUpperInvariant(), Valor = valor }, cancellationToken: ct));
        }
    }

    private async Task<bool> SqlObjectExistsAsync(SqlConnection cn, string objectName, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(@Name) IS NOT NULL THEN 1 ELSE 0 END;",
            new { Name = objectName }, cancellationToken: ct)) == 1;

    private static bool ParseBool(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();
        return v is "1" or "S" or "SI" or "SÍ" or "TRUE" or "T" or "Y";
    }

    private static DateTime? ParseFecha(string? value)
        => DateTime.TryParseExact((value ?? string.Empty).Trim(), "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d
            : (DateTime.TryParse(value, System.Globalization.CultureInfo.GetCultureInfo("es-AR"), System.Globalization.DateTimeStyles.None, out var d2) ? d2 : null);

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ExecuteLoggedAsync(string action, Func<CancellationToken, Task> operation, string userMessage, CancellationToken ct)
        => await ExecuteLoggedAsync(action, async token =>
        {
            await operation(token);
            return true;
        }, userMessage, ct);
}
