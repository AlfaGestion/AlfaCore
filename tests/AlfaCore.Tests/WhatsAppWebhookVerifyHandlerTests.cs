using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Incidente Base4271/Base4264 (2026-09): tras corregir dos falsos negativos del inspector
/// (WEBHOOK_ROUTE_MATCHED/NotSupportedException y la reconstrucción paralela de
/// RoutingSourceInspection.ToRoutingConfiguration()), el self-check productivo mostró un callback
/// correctamente resuelto (VERIFY_TOKEN_PRESENT=True, VERIFY_TOKEN_SOURCE=GLOBAL, CALLBACK_PATH
/// correcto) pero la verificación GET real seguía devolviendo 401. Una primera instrumentación
/// (commit 7d2686d4) escribía el trazo en Path.GetTempPath(); en producción, corriendo como Windows
/// Service bajo una cuenta con nombre, ese directorio resultó no determinístico -- confirmado el 401
/// real (RESULT_STATUS=401_MISMATCH), no apareció ningún archivo. Ahora escribe bajo
/// AppContext.BaseDirectory\diagnostics -- misma carpeta que ya usa
/// AppExceptionLoggingMiddleware.TryWriteWebhookFailureDiagnostic -- y estos tests prueban de punta a
/// punta que el archivo se crea de verdad (no sólo que no explota) tanto en el caso de match como en
/// el de mismatch. Ejercitan directamente el handler REAL de producción
/// (Program.HandleWhatsAppVerifyAsync, hecho internal sólo para esto) -- no una reimplementación --
/// para fijar su contrato de match/mismatch/no-configurado/modo, y ReadValue (también hecho internal)
/// para fijar la precedencia tenant/fallback sin depender de una conexión SQL real.
/// </summary>
public class WhatsAppWebhookVerifyHandlerTests
{
    private const string GlobalVerifyToken = "global-fallback-verify-token-non-empty";

    private static IConfiguration BuildConfiguration(string? verifyToken)
    {
        var data = new Dictionary<string, string?>();
        if (verifyToken is not null)
            data["WhatsApp:VerifyToken"] = verifyToken;
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private static DefaultHttpContext BuildRequestContext(string mode, string verifyToken, string challenge)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(
            $"?hub.mode={Uri.EscapeDataString(mode)}&hub.verify_token={Uri.EscapeDataString(verifyToken)}&hub.challenge={Uri.EscapeDataString(challenge)}");
        return context;
    }

    /// <summary>Nunca abre una conexión SQL real: GetConnectionString() vacío hace que
    /// HasTenantVerifyTokenAsync devuelva false sin tocar la red -- basta para fijar el contrato de
    /// HandleWhatsAppVerifyAsync (match/mismatch/no-configurado/modo/escritura del trazo), que es lo
    /// que estos tests cubren. La precedencia tenant/fallback en sí ya la cubren los tests de
    /// ReadValue más abajo.</summary>
    private sealed class NoopSessionService : ISessionService
    {
        public event Action? SessionChanged { add { } remove { } }
        public string GetConnectionString() => string.Empty;
        public SessionDto? GetActiveSession() => null;
        public void SetWebhookOverride(SessionDto session) { }
        public void ClearWebhookOverride() { }
        public IReadOnlyList<SessionDto> GetAllSessions() => [];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() { }
    }

    /// <summary>Directorio real donde HandleWhatsAppVerifyAsync escribe el trazo (AppContext.BaseDirectory
    /// -- el propio bin de test, siempre escribible) -- mismo cálculo que el código de producción, para
    /// probar de punta a punta que el archivo se crea de verdad, no sólo que no explota.</summary>
    private static string TraceFilePath()
        => Path.Combine(AppContext.BaseDirectory, "diagnostics", $"webhook-verify-trace-{DateTime.UtcNow:yyyyMMdd}.jsonl");

    private static JsonElement ReadLastTraceLine()
    {
        var path = TraceFilePath();
        Assert.True(File.Exists(path), $"Se esperaba que existiera el archivo de trazo: {path}");
        var lastLine = File.ReadLines(path).Last();
        return JsonDocument.Parse(lastLine).RootElement.Clone();
    }

    /// <summary>Caso obligatorio: Base4271 con VerifyToken de tenant vacío ("" en TA_CONFIGURACION,
    /// no ausente) -- el valor efectivo que ve el handler (ya fusionado por ConversacionesConfigService)
    /// es el fallback global, y el GET con ese mismo token debe devolver 200 + el challenge exacto.</summary>
    [Fact]
    public async Task Base4271_TenantVerifyTokenEmpty_GlobalFallbackMatches_Returns200WithExactChallenge()
    {
        var effectiveConfig = new ConversacionWhatsAppConfigDto { VerifyToken = GlobalVerifyToken };
        var configService = new FixedWhatsAppConfigConversacionesConfigService(effectiveConfig);
        var configuration = BuildConfiguration(GlobalVerifyToken);
        var whatsAppOptions = Options.Create(new WhatsAppOptions { VerifyToken = GlobalVerifyToken });
        var context = BuildRequestContext("subscribe", GlobalVerifyToken, "challenge-base4271-empty");

        var result = await AlfaCore.Program.HandleWhatsAppVerifyAsync(
            context.Request, configService, configuration, whatsAppOptions, new NoopSessionService(), CancellationToken.None);

        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal("challenge-base4271-empty", content.ResponseContent);
        Assert.True(content.StatusCode is null or 200);
    }

    /// <summary>Caso obligatorio: Base4264 con VerifyToken de tenant AUSENTE (la clave ni siquiera
    /// existe en TA_CONFIGURACION) -- mismo resultado esperado que el caso "vacío": el fallback global
    /// funciona igual.</summary>
    [Fact]
    public async Task Base4264_TenantVerifyTokenAbsent_GlobalFallbackMatches_Returns200WithExactChallenge()
    {
        var effectiveConfig = new ConversacionWhatsAppConfigDto { VerifyToken = GlobalVerifyToken };
        var configService = new FixedWhatsAppConfigConversacionesConfigService(effectiveConfig);
        var configuration = BuildConfiguration(GlobalVerifyToken);
        var whatsAppOptions = Options.Create(new WhatsAppOptions { VerifyToken = GlobalVerifyToken });
        var context = BuildRequestContext("subscribe", GlobalVerifyToken, "challenge-base4264-absent");

        var result = await AlfaCore.Program.HandleWhatsAppVerifyAsync(
            context.Request, configService, configuration, whatsAppOptions, new NoopSessionService(), CancellationToken.None);

        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal("challenge-base4264-absent", content.ResponseContent);
        Assert.True(content.StatusCode is null or 200);
    }

    /// <summary>
    /// Test obligatorio (incidente Base4271/Base4264, 2026-09): effective token = A, incoming token = B
    /// -&gt; 401 -- Y el escritor del trazo se invoca de verdad, escribe en AppContext.BaseDirectory\
    /// diagnostics\ (no en %TEMP%, que resultó no determinístico en producción bajo el Windows Service
    /// corriendo como ".\Administrador"), y RESULT_STATUS refleja exactamente 401_MISMATCH.
    /// </summary>
    [Fact]
    public async Task VerifyTokenMismatch_Returns401_AndWritesTraceWithMismatchStatus()
    {
        var effectiveConfig = new ConversacionWhatsAppConfigDto { VerifyToken = GlobalVerifyToken };
        var configService = new FixedWhatsAppConfigConversacionesConfigService(effectiveConfig);
        var configuration = BuildConfiguration(GlobalVerifyToken);
        var whatsAppOptions = Options.Create(new WhatsAppOptions { VerifyToken = GlobalVerifyToken });
        var context = BuildRequestContext("subscribe", "attacker-or-stale-token", "some-challenge");

        var result = await AlfaCore.Program.HandleWhatsAppVerifyAsync(
            context.Request, configService, configuration, whatsAppOptions, new NoopSessionService(), CancellationToken.None, idBase: 4271);

        var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);

        var trace = ReadLastTraceLine();
        Assert.Equal("401_MISMATCH", trace.GetProperty("RESULT_STATUS").GetString());
        Assert.Equal("4271", trace.GetProperty("ID_BASE").GetString());
        Assert.True(trace.GetProperty("HOST_OPTIONS_TOKEN_PRESENT").GetBoolean());
        Assert.True(trace.GetProperty("EFFECTIVE_TOKEN_PRESENT").GetBoolean());
        Assert.True(trace.GetProperty("INCOMING_TOKEN_PRESENT").GetBoolean());
        Assert.False(trace.GetProperty("TOKENS_MATCH").GetBoolean());
        // Nunca el valor del token en ningún campo del trazo.
        var raw = trace.GetRawText();
        Assert.DoesNotContain(GlobalVerifyToken, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker-or-stale-token", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyTokenNotConfigured_Returns500Problem()
    {
        var effectiveConfig = new ConversacionWhatsAppConfigDto { VerifyToken = string.Empty };
        var configService = new FixedWhatsAppConfigConversacionesConfigService(effectiveConfig);
        var configuration = BuildConfiguration(null);
        var whatsAppOptions = Options.Create(new WhatsAppOptions { VerifyToken = string.Empty });
        var context = BuildRequestContext("subscribe", "any-token", "any-challenge");

        var result = await AlfaCore.Program.HandleWhatsAppVerifyAsync(
            context.Request, configService, configuration, whatsAppOptions, new NoopSessionService(), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);

        var trace = ReadLastTraceLine();
        Assert.Equal("500_NOT_CONFIGURED", trace.GetProperty("RESULT_STATUS").GetString());
    }

    [Fact]
    public async Task WrongMode_ReturnsBadRequestWithoutComparingTokens()
    {
        var effectiveConfig = new ConversacionWhatsAppConfigDto { VerifyToken = GlobalVerifyToken };
        var configService = new FixedWhatsAppConfigConversacionesConfigService(effectiveConfig);
        var configuration = BuildConfiguration(GlobalVerifyToken);
        var whatsAppOptions = Options.Create(new WhatsAppOptions { VerifyToken = GlobalVerifyToken });
        var context = BuildRequestContext("unsubscribe", GlobalVerifyToken, "any-challenge");

        var result = await AlfaCore.Program.HandleWhatsAppVerifyAsync(
            context.Request, configService, configuration, whatsAppOptions, new NoopSessionService(), CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);

        var trace = ReadLastTraceLine();
        Assert.Equal("400_BAD_MODE", trace.GetProperty("RESULT_STATUS").GetString());
    }

    /// <summary>
    /// Test obligatorio (incidente Base4271/Base4264, 2026-09): effective token = A, incoming token = A
    /// -&gt; 200 + challenge exacto -- diseño explícito: el escritor se invoca IGUAL que en el caso de
    /// mismatch (no sólo en la falla), para poder comparar un caso sano (p. ej. Base4264 funcionando)
    /// contra uno roto (Base4271 fallando) con la misma evidencia. RESULT_STATUS = 200_CHALLENGE y
    /// TOKENS_MATCH = true lo distinguen del caso de 401.
    /// </summary>
    [Fact]
    public async Task VerifyTokenMatch_Returns200_AndWritesTraceWithMatchStatus()
    {
        var effectiveConfig = new ConversacionWhatsAppConfigDto { VerifyToken = GlobalVerifyToken };
        var configService = new FixedWhatsAppConfigConversacionesConfigService(effectiveConfig);
        var configuration = BuildConfiguration(GlobalVerifyToken);
        var whatsAppOptions = Options.Create(new WhatsAppOptions { VerifyToken = GlobalVerifyToken });
        var context = BuildRequestContext("subscribe", GlobalVerifyToken, "challenge-match-case");

        var result = await AlfaCore.Program.HandleWhatsAppVerifyAsync(
            context.Request, configService, configuration, whatsAppOptions, new NoopSessionService(), CancellationToken.None, idBase: 4264);

        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal("challenge-match-case", content.ResponseContent);

        var trace = ReadLastTraceLine();
        Assert.Equal("200_CHALLENGE", trace.GetProperty("RESULT_STATUS").GetString());
        Assert.Equal("4264", trace.GetProperty("ID_BASE").GetString());
        Assert.True(trace.GetProperty("TOKENS_MATCH").GetBoolean());
        var raw = trace.GetRawText();
        Assert.DoesNotContain(GlobalVerifyToken, raw, StringComparison.Ordinal);
    }

    /// <summary>Ruta legacy sin token de routing (MapGet("/api/conversaciones/whatsapp/webhook", ...)):
    /// idBase nunca se resuelve -- el trazo debe reportar ID_BASE = "N/A", nunca reventar por falta de
    /// sesión activa (HasTenantVerifyTokenAsync aislado en su propio try/catch).</summary>
    [Fact]
    public async Task LegacyRouteWithoutToken_NoResolvedBase_TraceReportsIdBaseNotAvailable()
    {
        var effectiveConfig = new ConversacionWhatsAppConfigDto { VerifyToken = GlobalVerifyToken };
        var configService = new FixedWhatsAppConfigConversacionesConfigService(effectiveConfig);
        var configuration = BuildConfiguration(GlobalVerifyToken);
        var whatsAppOptions = Options.Create(new WhatsAppOptions { VerifyToken = GlobalVerifyToken });
        var context = BuildRequestContext("subscribe", GlobalVerifyToken, "challenge-legacy-route");

        var result = await AlfaCore.Program.HandleWhatsAppVerifyAsync(
            context.Request, configService, configuration, whatsAppOptions, new NoopSessionService(), CancellationToken.None);

        Assert.IsType<ContentHttpResult>(result);
        var trace = ReadLastTraceLine();
        Assert.Equal("N/A", trace.GetProperty("ID_BASE").GetString());
        Assert.False(trace.GetProperty("TENANT_TOKEN_PRESENT").GetBoolean());
    }

    [Fact]
    public void ReadValue_TenantKeyAbsent_FallsBackToGlobalNonEmptyValue()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var resolved = ConversacionesConfigService.ReadValue(values, "CONV_WHATSAPP_VERIFY_TOKEN", GlobalVerifyToken);

        Assert.Equal(GlobalVerifyToken, resolved);
    }

    [Fact]
    public void ReadValue_TenantKeyPresentButBlank_FallsBackToGlobalNonEmptyValue()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CONV_WHATSAPP_VERIFY_TOKEN"] = "   "
        };

        var resolved = ConversacionesConfigService.ReadValue(values, "CONV_WHATSAPP_VERIFY_TOKEN", GlobalVerifyToken);

        Assert.Equal(GlobalVerifyToken, resolved);
    }

    [Fact]
    public void ReadValue_TenantKeyPresentAndNonBlank_WinsOverGlobalFallback()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CONV_WHATSAPP_VERIFY_TOKEN"] = "tenant-specific-verify-token"
        };

        var resolved = ConversacionesConfigService.ReadValue(values, "CONV_WHATSAPP_VERIFY_TOKEN", GlobalVerifyToken);

        Assert.Equal("tenant-specific-verify-token", resolved);
    }

    private sealed class FixedWhatsAppConfigConversacionesConfigService(ConversacionWhatsAppConfigDto config) : IConversacionesConfigService
    {
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default) => Task.FromResult(config);
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(int? expectedBaseId, CancellationToken ct = default) => Task.FromResult(config);
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> GenerateWhatsAppWebPairingAsync(ConversacionWhatsAppWebPairingRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> ClearWhatsAppWebPairingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveFacebookConfigAsync(ConversacionFacebookConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMercadoLibreConfigAsync(ConversacionMercadoLibreConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMercadoLibreTokensAsync(ConversacionMercadoLibreConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAlfaKnowledgeConfigAsync(ConversacionAlfaKnowledgeConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAlfaKnowledgeConfigForConnectionAsync(string connectionString, ConversacionAlfaKnowledgeConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConnectionTestResultDto> TestAlfaKnowledgeConnectionAsync(ConversacionAlfaKnowledgeConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAutomatizacionesConfigAsync(ConversacionAutomatizacionesConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SavePrioridadConfigAsync(ConversacionPrioridadConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroByInstanceNameAsync(string instanceName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppNumeroAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto> UpsertEmbeddedSignupWhatsAppNumeroForBaseAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppNumeroWebSessionAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(IReadOnlyCollection<string> metaBusinessIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(IReadOnlyCollection<string> metaBusinessIds, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetPortfolioNameAsync(int idBase, string metaBusinessId, string portfolioName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryReserveResolutionAttemptAsync(int idBase, string metaBusinessId, TimeSpan throttleWindow, CancellationToken ct = default) => throw new NotSupportedException();
        public Task BackfillNumeroMetaIdentityAsync(int idNumero, string metaBusinessId, string wabaId, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetWabaBusinessMapAsync(IReadOnlyCollection<string> wabaIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetWabaBusinessMapAsync(IReadOnlyCollection<string> wabaIds, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryReserveWabaResolutionAttemptAsync(int idBase, string wabaId, TimeSpan throttleWindow, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetWabaOwningBusinessIdAsync(int idBase, string wabaId, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveConversacionAdministradoresAsync(IReadOnlyList<string> usuarios, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionesInboxPreferenceDto> GetInboxPreferenceAsync(string userName, string? sistema, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveInboxPreferenceAsync(string userName, string? sistema, ConversacionesInboxPreferenceDto preference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveReglaAsync(ConversacionReglaDto regla, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteReglaAsync(int idRegla, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
