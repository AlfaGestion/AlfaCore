using System.Text;
using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Notificaciones de la app central de Mercado Libre de ALFA NET (todos los vendedores llegan a esta URL).
/// Mercado Libre exige responder 200 enseguida (si tarda, reintenta), así que se contesta primero y la
/// notificación se procesa aparte, en la base dueña de la cuenta (user_id = seller_id, según
/// ALFA_CENTRAL.dbo.CONV_CANALES_SOCIALES), con el mismo código que el webhook por base.
/// Mercado Libre no firma las notificaciones: se controla que vengan para nuestra app y que la cuenta
/// esté conectada; el contenido real siempre se vuelve a pedir a la API con el token de esa cuenta.
/// </summary>
public static class MercadoLibreCentralWebhook
{
    public const string Path = "/api/conversaciones/mercadolibre/central/webhook";

    public static async Task<IResult> RecibirAsync(
        HttpRequest request,
        IServiceScopeFactory scopeFactory,
        MetaCanalesSocialesStore store,
        IOptions<MercadoLibreCentralOptions> options,
        ILogger logger,
        CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(ct);

        var aviso = Leer(raw);
        if (aviso is null)
            return Results.Ok();

        if (!string.IsNullOrWhiteSpace(aviso.ApplicationId)
            && !string.Equals(aviso.ApplicationId, options.Value.ClientId?.Trim(), StringComparison.Ordinal))
        {
            logger.LogWarning("Webhook central Mercado Libre: aviso de otra app ({App}); se ignora.", aviso.ApplicationId);
            return Results.Ok();
        }

        if (!TopicoSoportado(aviso.Topic))
            return Results.Ok();

        int? idBase;
        try
        {
            idBase = await store.ResolverBaseAsync(MetaCanalSocial.MercadoLibre, aviso.UserId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook central Mercado Libre: no se pudo consultar a qué base pertenece el vendedor {Vendedor}.", aviso.UserId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); // Mercado Libre reintenta.
        }

        if (idBase is not > 0)
        {
            logger.LogWarning("Webhook central Mercado Libre: el vendedor {Vendedor} no está conectado a ninguna base; se ignora.", aviso.UserId);
            return Results.Ok();
        }

        var headers = request.Headers.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        _ = Task.Run(() => ProcesarAsync(scopeFactory, idBase.Value, raw, headers, logger));
        return Results.Ok();
    }

    private static async Task ProcesarAsync(IServiceScopeFactory scopeFactory, int idBase, string raw, Dictionary<string, string> headers, ILogger logger)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var baseInfo = await scope.ServiceProvider.GetRequiredService<ICentralBasesService>().GetByIdAsync(idBase, CancellationToken.None);
            if (baseInfo is null)
                return;

            scope.ServiceProvider.GetRequiredService<ISessionService>().SetWebhookOverride(new SessionDto
            {
                BaseId = baseInfo.IdBase,
                Nombre = baseInfo.Nombre,
                Servidor = baseInfo.DbServer,
                BaseDatos = baseInfo.DbName,
                Usuario = baseInfo.DbUser,
                Password = baseInfo.DbPassword,
                TrustServerCertificate = true
            });

            using var payload = JsonDocument.Parse(raw);
            await scope.ServiceProvider.GetRequiredService<IConversacionesService>().RegisterIncomingMercadoLibreWebhookAsync(
                new ConversacionWebhookRequest { Payload = payload, RawPayload = raw, Headers = headers }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook central Mercado Libre: falló el procesamiento en la base {IdBase}.", idBase);
        }
    }

    internal static bool TopicoSoportado(string? topic)
        => string.Equals(topic, "questions", StringComparison.OrdinalIgnoreCase)
           || string.Equals(topic, "messages", StringComparison.OrdinalIgnoreCase);

    internal static AvisoMercadoLibre? Leer(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            return new AvisoMercadoLibre(
                MetaCanalesSocialesClient.ReadString(root, "topic"),
                MetaCanalesSocialesClient.ReadString(root, "user_id"),
                MetaCanalesSocialesClient.ReadString(root, "application_id"),
                MetaCanalesSocialesClient.ReadString(root, "resource"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record AvisoMercadoLibre(string Topic, string UserId, string ApplicationId, string Resource);
}

/// <summary>
/// Renueva el acceso de Mercado Libre de las cuentas conectadas con la app central antes de que venza
/// (dura 6 horas). Corre cada 30 minutos. El refresh token es de un solo uso: se graba el nuevo enseguida.
/// </summary>
public sealed class MercadoLibreCentralHostedService(
    IServiceProvider services,
    IOptions<MercadoLibreCentralOptions> options,
    IAppModeService appMode,
    ILogger<MercadoLibreCentralHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Anticipacion = TimeSpan.FromMinutes(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.Listo && appMode.IsSaaSMode)
                    await RenovarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en la renovación de accesos de Mercado Libre.");
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RenovarAsync(CancellationToken ct)
    {
        IReadOnlyList<MetaCanalConexionDto> porVencer;
        using (var scope = services.CreateScope())
        {
            if (!await scope.ServiceProvider.GetRequiredService<IWorkerAssignmentService>().DebeEjecutarWorkersAsync(ct))
                return;
            var store = scope.ServiceProvider.GetRequiredService<MetaCanalesSocialesStore>();
            if (!store.CentralDisponible)
                return;
            porVencer = await store.GetPorVencerAsync(MetaCanalSocial.MercadoLibre, DateTime.UtcNow.Add(Anticipacion), TimeSpan.FromMinutes(20), ct);
        }

        foreach (var conexion in porVencer)
        {
            ct.ThrowIfCancellationRequested();
            using var scope = services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<MetaCanalesSocialesStore>();
            try
            {
                var baseInfo = await scope.ServiceProvider.GetRequiredService<ICentralBasesService>().GetByIdAsync(conexion.IdBase, ct);
                if (baseInfo is null)
                    continue;

                scope.ServiceProvider.GetRequiredService<ISessionService>().SetWebhookOverride(new SessionDto
                {
                    BaseId = baseInfo.IdBase,
                    Nombre = baseInfo.Nombre,
                    Servidor = baseInfo.DbServer,
                    BaseDatos = baseInfo.DbName,
                    Usuario = baseInfo.DbUser,
                    Password = baseInfo.DbPassword,
                    TrustServerCertificate = true
                });

                var configService = scope.ServiceProvider.GetRequiredService<IConversacionesConfigService>();
                var config = await configService.GetMercadoLibreConfigAsync(ct);
                if (string.IsNullOrWhiteSpace(config.RefreshToken)
                    || !string.Equals(config.SellerId?.Trim(), conexion.CuentaId, StringComparison.Ordinal))
                {
                    await store.RegistrarRenovacionAsync(conexion.IdConexion, null, "La base ya no tiene el acceso de esta cuenta. Volvé a conectar Mercado Libre.", ct);
                    continue;
                }

                var tokens = await scope.ServiceProvider.GetRequiredService<MercadoLibreCentralClient>().RenovarAsync(config.RefreshToken.Trim(), ct);
                config.AccessToken = tokens.AccessToken;
                config.RefreshToken = tokens.RefreshToken;
                await configService.SaveMercadoLibreTokensAsync(config, ct);
                await store.RegistrarRenovacionAsync(conexion.IdConexion, tokens.VenceUtc ?? DateTime.UtcNow.AddHours(6), null, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo renovar el acceso de Mercado Libre de la base {IdBase}.", conexion.IdBase);
                try
                {
                    await store.RegistrarRenovacionAsync(conexion.IdConexion, null,
                        ex is MetaCanalSocialException ? ex.Message : "No se pudo renovar el acceso de Mercado Libre.", ct);
                }
                catch (Exception inner)
                {
                    logger.LogWarning(inner, "No se pudo registrar el error de renovación de Mercado Libre.");
                }
            }
        }
    }
}
