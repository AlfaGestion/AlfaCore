using System.Collections.Concurrent;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IWhatsAppUsoMetaService
{
    /// <summary>Uso del mes en curso de cada número de WhatsApp de la base activa.</summary>
    Task<IReadOnlyList<WhatsAppUsoMetaDto>> GetUsoDelMesAsync(CancellationToken ct = default);

    /// <summary>Si la cuenta de WhatsApp (WABA) del número tiene método de pago en Meta.</summary>
    Task<WhatsAppMetodoPagoMetaDto> GetMetodoPagoAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default);
}

/// <summary>
/// Contador de mensajes frente a lo que cobra Meta y estado del método de pago de la cuenta de
/// WhatsApp del cliente. Ver <see cref="WhatsAppUsoMetaDto"/>.
/// </summary>
public sealed class WhatsAppUsoMetaService(
    IConfiguration configuration,
    ISessionService sessionService,
    IConversacionesConfigService conversacionesConfig,
    IWhatsAppRuntimeCredentialResolver credentialResolver,
    IMetaWhatsAppManagementClient managementClient,
    IAppEventService appEvents) : IWhatsAppUsoMetaService
{
    private const string WhatsAppManagerUrl = "https://business.facebook.com/wa/manage/home/";
    private const string BillingHubUrl = "https://business.facebook.com/billing_hub/payment_settings/";

    // El estado del pago se consulta a Meta a lo sumo una vez por hora por cuenta (10 min si falló).
    private static readonly ConcurrentDictionary<string, (WhatsAppMetodoPagoMetaEstado Estado, DateTime VenceUtc)> EstadoPagoCache = new(StringComparer.Ordinal);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlyList<WhatsAppUsoMetaDto>> GetUsoDelMesAsync(CancellationToken ct = default)
    {
        var hoy = DateTime.Today;
        var desde = new DateTime(hoy.Year, hoy.Month, 1);
        var hasta = desde.AddMonths(1);

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            await using (var check = new SqlCommand("""
                SELECT CASE WHEN OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL THEN 0 ELSE 1 END,
                       CASE WHEN COL_LENGTH('dbo.CONV_MENSAJES', 'Origen') IS NULL THEN 0 ELSE 1 END
                """, cn))
            await using (var rd = await check.ExecuteReaderAsync(ct))
            {
                await rd.ReadAsync(ct);
                if (rd.GetInt32(0) == 0)
                    return [];
                if (rd.GetInt32(1) == 0)
                    return await LeerUsoAsync(cn, desde, hasta, "CAST(NULL AS nvarchar(30))", ct);
            }

            return await LeerUsoAsync(cn, desde, hasta, "m.Origen", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync("Conversaciones", "WhatsAppUsoMeta", ex,
                "No se pudo calcular el uso de mensajes de WhatsApp del mes.", null, AppEventSeverity.Warning, ct);
            return [];
        }
    }

    private static async Task<IReadOnlyList<WhatsAppUsoMetaDto>> LeerUsoAsync(SqlConnection cn, DateTime desde, DateTime hasta, string origen, CancellationToken ct)
    {
        // "pricing" llega en cada aviso de estado de Meta y queda en PayloadJson: category=service
        // son las respuestas (cuentan para los 1.000 gratis); billable=true es lo que Meta cobra.
        var sql = $"""
            SELECT
                ISNULL(c.IdNumeroWhatsApp, 0),
                SUM(CASE WHEN {origen} IS NULL THEN 1 ELSE 0 END),
                SUM(CASE WHEN {origen} = N'WHATSAPP_BUSINESS_APP' THEN 1 ELSE 0 END),
                SUM(CASE WHEN {origen} IS NULL AND m.PayloadJson LIKE N'%"category":"service"%' THEN 1 ELSE 0 END),
                SUM(CASE WHEN {origen} IS NULL AND m.PayloadJson LIKE N'%"billable":true%' THEN 1 ELSE 0 END),
                SUM(CASE WHEN {origen} IS NULL AND m.PayloadJson LIKE N'%"billable":true%'
                          AND m.PayloadJson NOT LIKE N'%"category":"service"%' THEN 1 ELSE 0 END)
            FROM dbo.CONV_MENSAJES m
            INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
            WHERE c.Canal = N'WHATSAPP'
              AND m.Direction = N'SALIENTE'
              AND ISNULL(m.EstadoEnvio, N'') NOT IN (N'ERROR_ENVIO', N'PENDIENTE')
              AND m.FechaHora >= @Desde
              AND m.FechaHora < @Hasta
            GROUP BY ISNULL(c.IdNumeroWhatsApp, 0)
            """;

        var result = new List<WhatsAppUsoMetaDto>();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@Desde", desde);
        cmd.Parameters.AddWithValue("@Hasta", hasta);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            result.Add(new WhatsAppUsoMetaDto
            {
                IdNumero = rd.GetInt32(0),
                Desde = desde,
                Hasta = hasta,
                EnviadosDesdeAlfaCore = rd.GetInt32(1),
                EnviadosDesdeCelular = rd.GetInt32(2),
                MensajesServicio = rd.GetInt32(3),
                CobradosPorMeta = rd.GetInt32(4),
                PlantillasCobradas = rd.GetInt32(5)
            });
        }

        return result;
    }

    public async Task<WhatsAppMetodoPagoMetaDto> GetMetodoPagoAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default)
    {
        var wabaId = (numero.WabaId ?? string.Empty).Trim();
        var dto = new WhatsAppMetodoPagoMetaDto { UrlCargarPago = BuildUrlCargarPago(wabaId, numero.MetaBusinessId) };
        if (wabaId.Length == 0 || idBase <= 0)
            return dto;

        if (EstadoPagoCache.TryGetValue(wabaId, out var cached) && cached.VenceUtc > DateTime.UtcNow)
        {
            dto.Estado = cached.Estado;
            return dto;
        }

        try
        {
            var legacyConfig = await conversacionesConfig.GetWhatsAppConfigAsync(idBase, ct);
            var credential = await credentialResolver.ResolveAsync(idBase, numero.IdNumero, numero.PhoneNumberId, legacyConfig, ct);
            var fundingId = await managementClient.GetWabaPrimaryFundingIdAsync(wabaId, credential.AccessToken, credential.GraphVersion, ct);
            dto.Estado = fundingId is null ? WhatsAppMetodoPagoMetaEstado.NoCargado : WhatsAppMetodoPagoMetaEstado.Cargado;
            EstadoPagoCache[wabaId] = (dto.Estado, DateTime.UtcNow.AddHours(1));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sólo informativo: si no se puede consultar se muestra "no pudimos verificar" y el botón.
            EstadoPagoCache[wabaId] = (WhatsAppMetodoPagoMetaEstado.Desconocido, DateTime.UtcNow.AddMinutes(10));
            await appEvents.LogErrorAsync("Conversaciones", "WhatsAppMetodoPagoMeta", ex,
                "No se pudo consultar a Meta si la cuenta de WhatsApp tiene método de pago.",
                new { idBase, numero.IdNumero, WabaId = wabaId }, AppEventSeverity.Warning, ct);
        }

        return dto;
    }

    internal static string BuildUrlCargarPago(string? wabaId, string? metaBusinessId)
    {
        var waba = (wabaId ?? string.Empty).Trim();
        if (waba.Length == 0)
            return BillingHubUrl;

        var business = (metaBusinessId ?? string.Empty).Trim();
        return business.Length == 0
            ? $"{WhatsAppManagerUrl}?waba_id={Uri.EscapeDataString(waba)}"
            : $"{WhatsAppManagerUrl}?waba_id={Uri.EscapeDataString(waba)}&business_id={Uri.EscapeDataString(business)}";
    }
}
