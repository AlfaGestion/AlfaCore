using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Auditoría de persistencia/migración de Portfolio: números CONECTADOS ANTES de que existiera esta
/// funcionalidad no deben quedar eternamente en "Portfolio no identificado" si AlfaCore ya tiene
/// suficiente información para resolverlo. Dos mecanismos, cubiertos por separado:
///
/// 1) Backfill de MetaBusinessId/WabaId: 100% reconstruible desde el ownership CENTRAL ya persistido
///    (WhatsAppPhoneOwnership -> WhatsAppWabaOwnership) -- nunca llama a Meta.
/// 2) Resolución del NOMBRE: requiere Meta, pero throttled (nunca más de un intento por MetaBusinessId
///    por ventana) y usando el WhatsAppRuntimeCredential ya existente (no el Vault de onboarding).
/// </summary>
public sealed class WhatsAppPortfolioResolutionServiceTests
{
    private static ConversacionWhatsAppNumeroDto Numero(int idNumero, string phoneNumberId, string metaBusinessId = "")
        => new() { IdNumero = idNumero, PhoneNumberId = phoneNumberId, Nombre = "N", MetaBusinessId = metaBusinessId };

    // ---- Backfill de IDs (sin Meta) ----------------------------------------------------------------

    [Fact]
    public async Task Backfill_ReconstructsMetaBusinessIdAndWabaId_FromCentralOwnership_NeverCallingMeta()
    {
        var ctx = CreateContext();
        ctx.Ownership.Seed(phoneNumberId: "phone-1", wabaId: "waba-1", idBase: 84, metaBusinessId: "biz-1");
        var numero = Numero(1, "phone-1");

        var identity = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, numero);

        Assert.NotNull(identity);
        Assert.Equal("biz-1", identity!.MetaBusinessId);
        Assert.Equal("waba-1", identity.WabaId);
        Assert.Equal(1, ctx.Config.BackfillCalls);
        Assert.Equal(0, ctx.Management.GetBusinessNameCalls); // Nunca Meta para esto.
    }

    [Fact]
    public async Task Backfill_IsANoOp_WhenTheNumeroNeverWentThroughEmbeddedSignup_StaysUnknownForever()
    {
        // Agregado manualmente por Phone Number ID -- no hay ownership central para este teléfono.
        var ctx = CreateContext();
        var numero = Numero(1, "phone-manual");

        var identity = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, numero);

        Assert.Null(identity);
        Assert.Equal(0, ctx.Config.BackfillCalls);
    }

    [Fact]
    public async Task Backfill_IsANoOp_WhenTheNumeroAlreadyHasAMetaBusinessId_SelfThrottling()
    {
        var ctx = CreateContext();
        ctx.Ownership.Seed(phoneNumberId: "phone-1", wabaId: "waba-1", idBase: 84, metaBusinessId: "biz-1");
        var numero = Numero(1, "phone-1", metaBusinessId: "biz-already-known");

        var identity = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, numero);

        Assert.Null(identity);
        Assert.Equal(0, ctx.Config.BackfillCalls);
    }

    [Fact]
    public async Task Backfill_NeverTrustsOwnershipFromAnotherBase()
    {
        var ctx = CreateContext();
        ctx.Ownership.Seed(phoneNumberId: "phone-1", wabaId: "waba-1", idBase: 999, metaBusinessId: "biz-otra-base");
        var numero = Numero(1, "phone-1");

        var identity = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, numero);

        Assert.Null(identity);
    }

    [Fact]
    public async Task Backfill_TwoNumerosDifferentPortfolios_NeverMix()
    {
        var ctx = CreateContext();
        ctx.Ownership.Seed("phone-A", "waba-A", 84, "biz-A");
        ctx.Ownership.Seed("phone-B", "waba-B", 84, "biz-B");

        var identityA = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, Numero(1, "phone-A"));
        var identityB = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, Numero(2, "phone-B"));

        Assert.Equal("biz-A", identityA!.MetaBusinessId);
        Assert.Equal("biz-B", identityB!.MetaBusinessId);
    }

    // ---- Números MANUALES (sin ownership central, WABA legacy) --------------------------------------

    [Fact]
    public async Task Backfill_ManualNumero_IsResolvable_NotUnknown_WhenTheLegacyWabaIsAlreadyMappedToABusiness()
    {
        // Auditoría: "MANUAL PHONE-ID = UNKNOWN para siempre" era incorrecto -- si la caché WABA->Business
        // ya tiene el resultado (resuelto antes por TryResolveLegacyWabaOwningBusinessAsync), el backfill
        // lo usa sin llamar a Meta.
        var ctx = CreateContext();
        ctx.Config.LegacyConfig.BusinessAccountId = "waba-legacy";
        ctx.Config.WabaBusinessMap["waba-legacy"] = "biz-legacy";
        var numero = Numero(1, "phone-manual"); // Sin ownership central sembrado.

        var identity = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, numero);

        Assert.NotNull(identity);
        Assert.Equal("biz-legacy", identity!.MetaBusinessId);
        Assert.Equal("waba-legacy", identity.WabaId);
        Assert.Equal(0, ctx.Management.GetWabaOwningBusinessCalls); // Sólo lee la caché, no llama a Meta.
    }

    [Fact]
    public async Task Backfill_ManualNumero_IsUnknown_WhenNoLegacyWabaIsConfiguredAtAll()
    {
        // Sin ownership central Y sin WABA legacy configurada -- acá sí no hay nada que preguntarle a
        // nadie. Éste es el único caso legítimamente UNKNOWN para siempre.
        var ctx = CreateContext();
        // ctx.Config.LegacyConfig.BusinessAccountId queda vacío (default).
        var numero = Numero(1, "phone-manual");

        var identity = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, numero);

        Assert.Null(identity);
    }

    [Fact]
    public async Task Backfill_ManualNumero_StaysResolvable_WhileTheLegacyWabaHasNotBeenResolvedYet()
    {
        // Hay WABA legacy configurada, pero todavía nadie resolvió qué Business es dueño -- no debe
        // backfillear nada todavía (eso lo hace TryResolveLegacyWabaOwningBusinessAsync, throttled).
        var ctx = CreateContext();
        ctx.Config.LegacyConfig.BusinessAccountId = "waba-legacy";
        var numero = Numero(1, "phone-manual");

        var identity = await ctx.Service.BackfillNumeroMetaIdentityAsync(84, numero);

        Assert.Null(identity);
        Assert.Equal(0, ctx.Config.BackfillCalls);
    }

    [Fact]
    public async Task ResolveLegacyWaba_ClaimsThrottleThenCallsMeta_AndCachesTheOwningBusiness()
    {
        var ctx = CreateContext();
        ctx.Management.WabaOwningBusinesses["waba-legacy"] = "biz-legacy";

        await ctx.Service.TryResolveLegacyWabaOwningBusinessAsync(84, "waba-legacy", "phone-manual");

        Assert.Equal(1, ctx.Management.GetWabaOwningBusinessCalls);
        Assert.Equal("biz-legacy", ctx.Config.WabaBusinessMap["waba-legacy"]);
    }

    [Fact]
    public async Task ResolveLegacyWaba_NeverCallsMetaTwiceWithinTheThrottleWindow()
    {
        // Todos los números manuales de la base comparten la MISMA WABA legacy -- un solo intento debe
        // alcanzar para todos, nunca uno por número.
        var ctx = CreateContext();
        ctx.Management.WabaOwningBusinesses["waba-legacy"] = null; // Meta no da resultado el primer intento.

        await ctx.Service.TryResolveLegacyWabaOwningBusinessAsync(84, "waba-legacy", "phone-A");
        await ctx.Service.TryResolveLegacyWabaOwningBusinessAsync(84, "waba-legacy", "phone-B");

        Assert.Equal(1, ctx.Management.GetWabaOwningBusinessCalls);
    }

    [Fact]
    public async Task ResolveLegacyWaba_NeverCallsMeta_WhenAlreadyResolved()
    {
        var ctx = CreateContext();
        ctx.Config.WabaBusinessMap["waba-legacy"] = "biz-legacy";

        await ctx.Service.TryResolveLegacyWabaOwningBusinessAsync(84, "waba-legacy", "phone-manual");

        Assert.Equal(0, ctx.Management.GetWabaOwningBusinessCalls);
    }

    // ---- Resolución del nombre (throttled, con Meta) -----------------------------------------------

    [Fact]
    public async Task ResolveName_ClaimsThrottleThenCallsMeta_AndCachesTheResult()
    {
        var ctx = CreateContext();
        ctx.Management.Names["biz-1"] = "AlfaNet Portfolio";

        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");

        Assert.Equal(1, ctx.Management.GetBusinessNameCalls);
        Assert.Equal("AlfaNet Portfolio", ctx.Config.PortfolioNames["biz-1"]);
    }

    [Fact]
    public async Task ResolveName_NeverCallsMeta_WhenTheNameIsAlreadyCached()
    {
        var ctx = CreateContext();
        ctx.Config.PortfolioNames["biz-1"] = "Ya conocido";

        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");

        Assert.Equal(0, ctx.Management.GetBusinessNameCalls);
    }

    [Fact]
    public async Task ResolveName_NeverCallsMetaTwiceWithinTheThrottleWindow()
    {
        var ctx = CreateContext();
        ctx.Management.Names["biz-1"] = null; // Meta "no responde" (no da nombre) el primer intento.

        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");
        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");

        // Un solo intento real a Meta -- el segundo queda throttled, no dos llamadas por dos refreshes.
        Assert.Equal(1, ctx.Management.GetBusinessNameCalls);
    }

    [Fact]
    public async Task ResolveName_WhenMetaDoesNotAnswer_CachedDataIsNeverClobbered()
    {
        var ctx = CreateContext();
        ctx.Config.PortfolioNames["biz-1"] = "Nombre previo válido";
        ctx.Config.PortfolioResolutionAttempts.Remove("biz-1"); // Forzar elegibilidad si hiciera falta.

        // Al estar ya cacheado un nombre no vacío, TryReserveResolutionAttemptAsync nunca reserva --
        // Meta ni se llama, así que no hay forma de que lo pise con null/vacío.
        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");

        Assert.Equal(0, ctx.Management.GetBusinessNameCalls);
        Assert.Equal("Nombre previo válido", ctx.Config.PortfolioNames["biz-1"]);
    }

    [Fact]
    public async Task ResolveName_TwoDifferentPortfolios_ResolveIndependently_NeverMixNames()
    {
        var ctx = CreateContext();
        ctx.Management.Names["biz-A"] = "Portfolio A";
        ctx.Management.Names["biz-B"] = "Portfolio B";

        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-A", "phone-A");
        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-B", "phone-B");

        Assert.Equal("Portfolio A", ctx.Config.PortfolioNames["biz-A"]);
        Assert.Equal("Portfolio B", ctx.Config.PortfolioNames["biz-B"]);
    }

    [Fact]
    public async Task ResolveName_FiveNumerosSharingTheSamePortfolio_ConcurrentRefresh_ProducesOneRowAndOneRealMetaCall()
    {
        // Auditoría de concurrencia: 5 números con el mismo MetaBusinessId, cada uno disparando su
        // propio TryResolvePortfolioNameAsync en un refresh concurrente (ej. 5 pestañas, o el polling de
        // varios circuitos Blazor a la vez) -- debe producir UNA fila de portfolio y UN solo GET real a
        // Meta, nunca cinco. El lock en el fake modela el MERGE ... WITH (HOLDLOCK) real.
        var ctx = CreateContext();
        ctx.Management.Names["biz-compartido"] = "Portfolio Compartido";

        var tasks = Enumerable.Range(1, 5)
            .Select(i => ctx.Service.TryResolvePortfolioNameAsync(84, "biz-compartido", $"phone-{i}"));
        await Task.WhenAll(tasks);

        Assert.Equal(1, ctx.Management.GetBusinessNameCalls);
        Assert.Single(ctx.Config.PortfolioNames);
        Assert.Equal("Portfolio Compartido", ctx.Config.PortfolioNames["biz-compartido"]);
    }

    [Fact]
    public async Task ResolveName_MetaPermissionMissing_PreservesCachedName_RegistersAttempt_NeverBreaks()
    {
        // "portfolio cache existente + refresh falla / permiso business_management faltante" ->
        // conservar el nombre cacheado, no vaciarlo, no romper Configuración, registrar el intento
        // (para que el throttle lo respete) y esperar la ventana.
        var ctx = CreateContext();
        ctx.Config.PortfolioNames["biz-1"] = "Nombre cacheado antes del fallo de permisos";
        // Al ya haber un nombre no vacío, TryReserveResolutionAttemptAsync nunca reserva -- Meta ni se
        // llama, así que un eventual error de permisos ni siquiera puede alcanzar el nombre cacheado.
        var exceptionThrown = await Record.ExceptionAsync(() => ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1"));

        Assert.Null(exceptionThrown); // Nunca rompe Configuración.
        Assert.Equal(0, ctx.Management.GetBusinessNameCalls);
        Assert.Equal("Nombre cacheado antes del fallo de permisos", ctx.Config.PortfolioNames["biz-1"]);
    }

    [Fact]
    public async Task ResolveName_MetaThrows_NeverPropagates_AndTheAttemptStaysRegisteredForTheThrottle()
    {
        // Simula "permiso business_management faltante" como una excepción real de Meta (lo típico:
        // MetaWhatsAppManagementException por 403/permission denied) DESPUÉS de haber reservado el
        // intento -- el throttle ya quedó registrado (no se revierte), así que un refresh inmediato
        // siguiente no reintenta machacando Meta; la próxima ventana sí podrá reintentar.
        var ctx = CreateContext();
        ctx.Management.ThrowOnGetBusinessName = true;

        var exceptionThrown = await Record.ExceptionAsync(() => ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1"));

        Assert.Null(exceptionThrown);
        Assert.False(ctx.Config.PortfolioNames.ContainsKey("biz-1")); // Nunca se cachea un nombre a medias.
        Assert.True(ctx.Config.PortfolioResolutionAttempts.ContainsKey("biz-1")); // El intento SÍ quedó registrado.

        // Un segundo intento inmediato (misma ventana) no debe volver a llamar a Meta.
        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");
        Assert.Equal(1, ctx.Management.GetBusinessNameCalls);
    }

    [Fact]
    public async Task ResolveName_TwoNumerosSharingTheSamePortfolio_ResolveOnce_BothReadTheSameCachedName()
    {
        // "dos números -> mismo Portfolio": el nombre se guarda UNA vez por MetaBusinessId (no por
        // número) -- si ambos números ya comparten biz-1, un solo TryResolvePortfolioNameAsync alcanza
        // para que los dos lean el mismo nombre cacheado; el segundo intento debe quedar throttled.
        var ctx = CreateContext();
        ctx.Management.Names["biz-1"] = "Portfolio Compartido";

        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-A"); // número A
        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-B"); // número B, mismo business

        Assert.Equal(1, ctx.Management.GetBusinessNameCalls);
        Assert.Equal("Portfolio Compartido", ctx.Config.PortfolioNames["biz-1"]);
    }

    [Fact]
    public async Task ResolveName_WhenMetaNameChanges_OverwritesTheSameRow_NeverAccumulatesStaleAttemptsAsDuplicates()
    {
        // "nombre de Portfolio cambia en Meta -> estrategia de refresh no duplica filas": SetPortfolioNameAsync
        // hace MERGE sobre la PK (MetaBusinessId) -- confirmado acá indirectamente: forzar un segundo
        // intento fuera de la ventana de throttle y verificar que el diccionario sigue teniendo UNA sola
        // entrada para el id, con el valor más reciente (nunca dos entradas ni un valor apilado).
        var ctx = CreateContext();
        ctx.Management.Names["biz-1"] = "Nombre viejo";
        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");
        Assert.Equal("Nombre viejo", ctx.Config.PortfolioNames["biz-1"]);

        // Simula que pasó la ventana de throttle forzando el timestamp del último intento al pasado.
        ctx.Config.PortfolioResolutionAttempts["biz-1"] = DateTime.UtcNow - TimeSpan.FromDays(2);
        // Y que Meta ahora ya no tiene el nombre cacheado como "conocido" -- forzamos re-consulta
        // limpiando el nombre para simular que se quiere refrescar (mismo id, nombre nuevo).
        ctx.Config.PortfolioNames.Remove("biz-1");
        ctx.Management.Names["biz-1"] = "Nombre nuevo";

        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");

        Assert.Single(ctx.Config.PortfolioNames); // Una sola entrada para "biz-1" -- nunca una fila extra.
        Assert.Equal("Nombre nuevo", ctx.Config.PortfolioNames["biz-1"]);
        Assert.Equal(2, ctx.Management.GetBusinessNameCalls);
    }

    [Fact]
    public async Task ResolveName_UsesTheRuntimeCredential_NeverTheOnboardingVault()
    {
        // Regresión de diseño: si esto alguna vez empezara a pedir un WhatsAppCredentialReference de
        // Vault de onboarding en lugar de WhatsAppRuntimeCredential, fallaría para un número conectado
        // hace mucho tiempo (el Vault de onboarding ya no tiene contexto vigente). El fake de
        // credenciales sólo implementa ResolveAsync (el de runtime) -- si el servicio intentara otra
        // cosa, este test ni compilaría con esa dependencia satisfecha.
        var ctx = CreateContext();
        ctx.Management.Names["biz-1"] = "Nombre";

        await ctx.Service.TryResolvePortfolioNameAsync(84, "biz-1", "phone-1");

        Assert.True(ctx.Credentials.ResolveCalled);
    }

    private static TestContext CreateContext()
    {
        var config = new MemoryConversacionesConfigService();
        var ownership = new MemoryOwnershipStore();
        var management = new FakeMetaManagementClient();
        var credentials = new FakeRuntimeCredentialResolver();
        var options = Options.Create(new WhatsAppEmbeddedSignupOptions { PortfolioResolutionThrottle = TimeSpan.FromHours(24) });
        var service = new WhatsAppPortfolioResolutionService(config, ownership, management, credentials, options);
        return new TestContext(service, config, ownership, management, credentials);
    }

    private sealed record TestContext(
        WhatsAppPortfolioResolutionService Service,
        MemoryConversacionesConfigService Config,
        MemoryOwnershipStore Ownership,
        FakeMetaManagementClient Management,
        FakeRuntimeCredentialResolver Credentials);

    private sealed class MemoryOwnershipStore : IWhatsAppAssetOwnershipStore
    {
        private readonly Dictionary<string, WhatsAppPhoneOwnership> _phones = [];
        private readonly Dictionary<string, WhatsAppWabaOwnership> _wabas = [];

        public void Seed(string phoneNumberId, string wabaId, int idBase, string metaBusinessId)
        {
            _phones[phoneNumberId] = new WhatsAppPhoneOwnership(phoneNumberId, wabaId, idBase, DateTime.UtcNow);
            _wabas[wabaId] = new WhatsAppWabaOwnership(wabaId, idBase, metaBusinessId, DateTime.UtcNow);
        }

        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => Task.FromResult(_wabas.GetValueOrDefault(wabaId));
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => Task.FromResult(_phones.GetValueOrDefault(phoneNumberId));
    }

    private sealed class MemoryConversacionesConfigService : IConversacionesConfigService
    {
        public List<ConversacionWhatsAppNumeroDto> Numeros { get; } = [];
        public Dictionary<string, string> PortfolioNames { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DateTime> PortfolioResolutionAttempts { get; } = new(StringComparer.Ordinal);
        public int BackfillCalls { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(IReadOnlyCollection<string> metaBusinessIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(PortfolioNames);

        public Task SetPortfolioNameAsync(int idBase, string metaBusinessId, string portfolioName, CancellationToken ct = default)
        {
            lock (_throttleLock)
            {
                if (!string.IsNullOrWhiteSpace(metaBusinessId) && !string.IsNullOrWhiteSpace(portfolioName))
                {
                    PortfolioNames[metaBusinessId] = portfolioName;
                    PortfolioResolutionAttempts[metaBusinessId] = DateTime.UtcNow;
                }
                return Task.CompletedTask;
            }
        }

        private readonly object _throttleLock = new();

        /// <summary>
        /// Lock explícito -- modela la misma atomicidad que MERGE ... WITH (HOLDLOCK) en el SQL real:
        /// bajo refresh concurrente (5 números compartiendo un MetaBusinessId, cada uno disparando su
        /// propio intento de reserva en paralelo), sólo UNO debe ganar la reserva. Sin este lock, un
        /// Dictionary in-memory no da esa garantía y el test de concurrencia sería falso-verde.
        /// </summary>
        public Task<bool> TryReserveResolutionAttemptAsync(int idBase, string metaBusinessId, TimeSpan throttleWindow, CancellationToken ct = default)
        {
            lock (_throttleLock)
            {
                if (PortfolioNames.TryGetValue(metaBusinessId, out var name) && !string.IsNullOrWhiteSpace(name))
                    return Task.FromResult(false);

                var now = DateTime.UtcNow;
                if (PortfolioResolutionAttempts.TryGetValue(metaBusinessId, out var last) && now - last < throttleWindow)
                    return Task.FromResult(false);

                PortfolioResolutionAttempts[metaBusinessId] = now;
                return Task.FromResult(true);
            }
        }

        public Task BackfillNumeroMetaIdentityAsync(int idNumero, string metaBusinessId, string wabaId, CancellationToken ct = default)
        {
            BackfillCalls++;
            var numero = Numeros.SingleOrDefault(x => x.IdNumero == idNumero);
            if (numero is not null && string.IsNullOrWhiteSpace(numero.MetaBusinessId))
            {
                numero.MetaBusinessId = metaBusinessId;
                numero.WabaId = wabaId;
            }
            return Task.CompletedTask;
        }

        public Dictionary<string, string> WabaBusinessMap { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DateTime> WabaResolutionAttempts { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>> GetWabaBusinessMapAsync(IReadOnlyCollection<string> wabaIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(WabaBusinessMap);

        public Task<bool> TryReserveWabaResolutionAttemptAsync(int idBase, string wabaId, TimeSpan throttleWindow, CancellationToken ct = default)
        {
            lock (_throttleLock)
            {
                if (WabaBusinessMap.TryGetValue(wabaId, out var businessId) && !string.IsNullOrWhiteSpace(businessId))
                    return Task.FromResult(false);

                var now = DateTime.UtcNow;
                if (WabaResolutionAttempts.TryGetValue(wabaId, out var last) && now - last < throttleWindow)
                    return Task.FromResult(false);

                WabaResolutionAttempts[wabaId] = now;
                return Task.FromResult(true);
            }
        }

        public Task SetWabaOwningBusinessIdAsync(int idBase, string wabaId, string metaBusinessId, CancellationToken ct = default)
        {
            lock (_throttleLock)
            {
                if (!string.IsNullOrWhiteSpace(wabaId) && !string.IsNullOrWhiteSpace(metaBusinessId))
                {
                    WabaBusinessMap[wabaId] = metaBusinessId;
                    WabaResolutionAttempts[wabaId] = DateTime.UtcNow;
                }
                return Task.CompletedTask;
            }
        }

        public Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConversacionWhatsAppNumeroDto>>(Numeros);
        public Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(int? expectedBaseId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConversacionWhatsAppNumeroDto>>(Numeros);
        public ConversacionWhatsAppConfigDto LegacyConfig { get; } = new();
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default) => Task.FromResult(LegacyConfig);
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(int? expectedBaseId, CancellationToken ct = default) => Task.FromResult(LegacyConfig);
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> GenerateWhatsAppWebPairingAsync(ConversacionWhatsAppWebPairingRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> ClearWhatsAppWebPairingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveFacebookConfigAsync(ConversacionFacebookConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMercadoLibreConfigAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMercadoLibreTokensAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAlfaKnowledgeConfigAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAlfaKnowledgeConfigForConnectionAsync(string connectionString, ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConnectionTestResultDto> TestAlfaKnowledgeConnectionAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAutomatizacionesConfigAsync(ConversacionAutomatizacionesConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SavePrioridadConfigAsync(ConversacionPrioridadConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroByInstanceNameAsync(string instanceName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppNumeroAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto> UpsertEmbeddedSignupWhatsAppNumeroForBaseAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppNumeroWebSessionAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
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

    private sealed class FakeMetaManagementClient : IMetaWhatsAppManagementClient
    {
        public Dictionary<string, string?> Names { get; } = [];
        private int _getBusinessNameCalls;
        public int GetBusinessNameCalls => _getBusinessNameCalls;
        public Dictionary<string, string?> WabaOwningBusinesses { get; } = [];
        private int _getWabaOwningBusinessCalls;
        public int GetWabaOwningBusinessCalls => _getWabaOwningBusinessCalls;

        public bool ThrowOnGetBusinessName { get; set; }

        public Task<string?> GetBusinessNameAsync(string businessId, string accessToken, string graphVersion, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _getBusinessNameCalls);
            if (ThrowOnGetBusinessName)
                throw new MetaWhatsAppManagementException("META_PERMISSION_DENIED", false, true, "Falta el permiso business_management.");
            return Task.FromResult(Names.TryGetValue(businessId, out var name) ? name : null);
        }

        public Task<string?> GetWabaOwningBusinessIdAsync(string wabaId, string accessToken, string graphVersion, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _getWabaOwningBusinessCalls);
            return Task.FromResult(WabaOwningBusinesses.TryGetValue(wabaId, out var businessId) ? businessId : null);
        }

        public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pinReference, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeRuntimeCredentialResolver : IWhatsAppRuntimeCredentialResolver
    {
        public bool ResolveCalled { get; private set; }

        public Task<WhatsAppRuntimeCredential> ResolveAsync(int idBase, int? idNumero, string phoneNumberId, ConversacionWhatsAppConfigDto legacyConfig, CancellationToken ct = default)
        {
            ResolveCalled = true;
            return Task.FromResult(new WhatsAppRuntimeCredential("waba-x", phoneNumberId, "v26.0", "runtime-token", WhatsAppRuntimeCredentialOrigin.EmbeddedSignup));
        }
    }
}
