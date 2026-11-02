using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Consulta de padrón ARCA desde una base. Si la base tiene su propio certificado PADRON (ARCA_CERTIFICADO)
/// consulta con ese; si no, usa la consulta central (<see cref="ArcaPadronCentralService"/>, CUIT de ALFA NET).
/// La búsqueda por DNI siempre va por la central (servicio ws_sr_padron_a13).
/// </summary>
public sealed class ArcaPadronService(
    IArcaConfigService arcaConfig,
    IWsaaClient wsaaClient,
    IHttpClientFactory httpClientFactory,
    ISessionService sessionService,
    IConfiguration configuration,
    IAppEventService appEvents,
    ArcaPadronCentralService central,
    IAppUserSessionService users) : IArcaPadronService
{
    private const string ModuleName = "ArcaPadron";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ArcaPadronPersonaDto> ConsultarPersonaAsync(string cuit, CancellationToken ct = default)
    {
        var digits = ArcaPadronSoap.SoloDigitos(cuit);
        if (digits.Length != 11)
            throw new InvalidOperationException("Ingresá un CUIT válido de 11 dígitos para consultar ARCA.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        if (!await TieneCertificadoPropioAsync(cn, ct))
            return await central.ConsultarCuitAsync(digits, Origen(cn), Usuario, ct);

        ArcaEmisorConfig? emisor = null;
        try
        {
            emisor = await arcaConfig.ResolvePadronEmisorAsync(cn, ct)
                ?? throw new InvalidOperationException("No hay una configuración propia de padrón ARCA con certificado disponible.");
            var ticket = await wsaaClient.ObtenerTicketAsync(cn, emisor, ct, ArcaPadronSoap.ServicioCuit);
            return await ArcaPadronSoap.ConsultarCuitAsync(httpClientFactory.CreateClient("Arca"), ticket, emisor, digits, ct);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is null)
        {
            throw;
        }
        catch (ArcaAutenticacionException ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "ConsultarPersona", ex,
                "No se pudo consultar el contribuyente en ARCA.", new { Cuit = digits }, ct: ct);
            var detalle = ex.ToString();
            var noAutorizado = detalle.Contains("coe.notAuthorized", StringComparison.OrdinalIgnoreCase)
                || detalle.Contains("Computador no autorizado", StringComparison.OrdinalIgnoreCase);
            var certificadoVencido = detalle.Contains("cms.cert.expired", StringComparison.OrdinalIgnoreCase)
                || detalle.Contains("Certificado expirado", StringComparison.OrdinalIgnoreCase);
            var ambiente = emisor?.Ambiente == ArcaAmbiente.Produccion ? "producción" : "homologación";
            var mensaje = noAutorizado
                ? $"ARCA no autorizó el certificado propio de esta base para consultar el padrón. Habilitalo para el servicio ws_sr_constancia_inscripcion en {ambiente}, o quitalo para usar la consulta central."
                : certificadoVencido
                    ? "El certificado propio de esta base para consultar el padrón ARCA está vencido. Reemplazalo o quitalo para usar la consulta central."
                : ex.InnerException is null ? ex.Message : "No se pudo autenticar contra ARCA para consultar el padrón.";
            throw new InvalidOperationException(mensaje, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "ConsultarPersona", ex,
                "No se pudo consultar el contribuyente en ARCA.", new { Cuit = digits }, ct: ct);
            throw new InvalidOperationException("No se pudo consultar el CUIT en ARCA.", ex);
        }
    }

    public async Task<IReadOnlyList<ArcaPadronPersonaDto>> ConsultarDniAsync(string dni, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        return await central.ConsultarDniAsync(dni, Origen(cn), Usuario, ct);
    }

    private string Usuario => users.GetCurrentUserName();

    // Identifica la instalación en el registro y el límite diario de la consulta central.
    private static string Origen(SqlConnection cn) => $"{cn.DataSource}/{cn.Database}";

    private static async Task<bool> TieneCertificadoPropioAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.ARCA_CERTIFICADO', N'U') IS NULL SELECT 0;
            ELSE SELECT COUNT(*) FROM dbo.ARCA_CERTIFICADO
                 WHERE UNegocio = 'PADRON' AND ArchivoCrt IS NOT NULL AND ArchivoKey IS NOT NULL;
            """, cancellationToken: ct)) > 0;
}
