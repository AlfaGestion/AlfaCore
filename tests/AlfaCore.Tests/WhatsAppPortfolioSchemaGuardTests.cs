using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Rollout seguro con tenants sin migrar: "la tabla/columna existe" no alcanza como guarda -- un tenant
/// puede quedar en un estado PARCIAL si el motor de actualizaciones (ActualizacionesService) se
/// interrumpe entre el CREATE TABLE y el ALTER TABLE ADD LastResolutionAttemptUtc del mismo script (cada
/// batch corre por separado, no en una única transacción). CanReadPortfolioCache/CanWritePortfolioCache/
/// CanWriteNumeroMetaIdentity son las guardas puras y testeables (sin DB real) que reemplazan el chequeo
/// anterior "portfolioColumns.Count == 0", demasiado laxo: pasaba con CUALQUIER columna presente, no con
/// las específicas que cada sentencia SQL realmente referencia.
/// </summary>
public sealed class WhatsAppPortfolioSchemaGuardTests
{
    private static readonly HashSet<string> NoColumns = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FullPortfolioColumns = new(StringComparer.OrdinalIgnoreCase)
        { "MetaBusinessId", "PortfolioName", "ModifiedAtUtc", "LastResolutionAttemptUtc" };
    private static readonly HashSet<string> FullNumeroIdentityColumns = new(StringComparer.OrdinalIgnoreCase)
        { "MetaBusinessId", "WabaId" };

    // ---- Tenant B completamente sin migrar (caso principal de la auditoría) -------------------------

    [Fact]
    public void UnmigratedTenant_CanReadPortfolioCache_IsFalse_WithNoColumnsAtAll()
        => Assert.False(ConversacionesConfigService.CanReadPortfolioCache(NoColumns));

    [Fact]
    public void UnmigratedTenant_CanWritePortfolioCache_IsFalse_WithNoColumnsAtAll()
        => Assert.False(ConversacionesConfigService.CanWritePortfolioCache(NoColumns));

    [Fact]
    public void UnmigratedTenant_CanWriteNumeroMetaIdentity_IsFalse_WithNoColumnsAtAll()
        => Assert.False(ConversacionesConfigService.CanWriteNumeroMetaIdentity(NoColumns));

    // ---- Tenant en estado PARCIAL (script interrumpido a mitad de camino) ---------------------------

    [Fact]
    public void PartiallyMigratedTenant_CanReadPortfolioCache_IsTrue_EvenWithoutTheNewerColumn()
    {
        // CREATE TABLE corrió (tiene MetaBusinessId/PortfolioName), pero el ALTER TABLE ADD
        // LastResolutionAttemptUtc todavía no -- el SELECT de lectura sólo pide MetaBusinessId/
        // PortfolioName, así que SÍ puede leer con seguridad.
        var partial = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MetaBusinessId", "PortfolioName", "ModifiedAtUtc" };
        Assert.True(ConversacionesConfigService.CanReadPortfolioCache(partial));
    }

    [Fact]
    public void PartiallyMigratedTenant_CanWritePortfolioCache_IsFalse_WithoutLastResolutionAttemptUtc()
    {
        // Éste es el bug real que esto corrige: MERGE (SetPortfolioNameAsync/TryReserveResolutionAttemptAsync)
        // referencia LastResolutionAttemptUtc explícitamente en INSERT y UPDATE -- si la tabla existe
        // pero esa columna todavía no, el viejo chequeo "portfolioColumns.Count == 0" pasaba igual
        // (Count == 3, no 0) y el MERGE tiraba "Invalid column name 'LastResolutionAttemptUtc'".
        var partial = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MetaBusinessId", "PortfolioName", "ModifiedAtUtc" };
        Assert.False(ConversacionesConfigService.CanWritePortfolioCache(partial));
    }

    [Fact]
    public void PartiallyMigratedTenant_CanWriteNumeroMetaIdentity_IsFalse_WithOnlyOneOfTheTwoColumns()
    {
        // ALTER TABLE ADD MetaBusinessId corrió, ADD WabaId todavía no (dos ALTER separados, dos batches).
        var partial = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MetaBusinessId" };
        Assert.False(ConversacionesConfigService.CanWriteNumeroMetaIdentity(partial));
    }

    // ---- Tenant completamente migrado ----------------------------------------------------------------

    [Fact]
    public void FullyMigratedTenant_AllGuardsPass()
    {
        Assert.True(ConversacionesConfigService.CanReadPortfolioCache(FullPortfolioColumns));
        Assert.True(ConversacionesConfigService.CanWritePortfolioCache(FullPortfolioColumns));
        Assert.True(ConversacionesConfigService.CanWriteNumeroMetaIdentity(FullNumeroIdentityColumns));
        Assert.True(ConversacionesConfigService.CanReadWabaBusinessMap(FullWabaMapColumns));
        Assert.True(ConversacionesConfigService.CanWriteWabaBusinessMap(FullWabaMapColumns));
    }

    // ---- Misma auditoría para la tabla WABA->Business (números manuales) ----------------------------

    private static readonly HashSet<string> FullWabaMapColumns = new(StringComparer.OrdinalIgnoreCase)
        { "WabaId", "MetaBusinessId", "ModifiedAtUtc", "LastResolutionAttemptUtc" };

    [Fact]
    public void UnmigratedTenant_WabaBusinessMapGuards_AreFalse_WithNoColumnsAtAll()
    {
        Assert.False(ConversacionesConfigService.CanReadWabaBusinessMap(NoColumns));
        Assert.False(ConversacionesConfigService.CanWriteWabaBusinessMap(NoColumns));
    }

    [Fact]
    public void PartiallyMigratedTenant_CanWriteWabaBusinessMap_IsFalse_WithoutLastResolutionAttemptUtc()
    {
        var partial = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WabaId", "MetaBusinessId", "ModifiedAtUtc" };
        Assert.True(ConversacionesConfigService.CanReadWabaBusinessMap(partial));
        Assert.False(ConversacionesConfigService.CanWriteWabaBusinessMap(partial));
    }

    [Fact]
    public void GetWabaBusinessMapAsync_UsesTheGuard_NotAnInlineCheck()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<IReadOnlyDictionary<string, string>> GetWabaBusinessMapAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("if (!CanReadWabaBusinessMap(columns))", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReserveWabaResolutionAttemptAsync_UsesTheGuard_BeforeTheMerge()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public async Task<bool> TryReserveWabaResolutionAttemptAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("if (!CanWriteWabaBusinessMap(columns))", methodBody, StringComparison.Ordinal);
        var guardIndex = methodBody.IndexOf("CanWriteWabaBusinessMap", StringComparison.Ordinal);
        var mergeIndex = methodBody.IndexOf("MERGE dbo.CONV_WHATSAPP_WABA_BUSINESS_MAP", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && mergeIndex >= 0 && guardIndex < mergeIndex);
    }

    [Fact]
    public void SetWabaOwningBusinessIdAsync_UsesTheGuard_BeforeTheMerge()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public async Task SetWabaOwningBusinessIdAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("if (!CanWriteWabaBusinessMap(columns))", methodBody, StringComparison.Ordinal);
        var guardIndex = methodBody.IndexOf("CanWriteWabaBusinessMap", StringComparison.Ordinal);
        var mergeIndex = methodBody.IndexOf("MERGE dbo.CONV_WHATSAPP_WABA_BUSINESS_MAP", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && mergeIndex >= 0 && guardIndex < mergeIndex);
    }

    [Fact]
    public void FullyMigratedTenant_ExtraUnrelatedColumnsDoNotConfuseTheGuard()
    {
        var withExtras = new HashSet<string>(FullPortfolioColumns, StringComparer.OrdinalIgnoreCase) { "AlgunaColumnaFutura" };
        Assert.True(ConversacionesConfigService.CanWritePortfolioCache(withExtras));
    }

    // ---- Confirmación estructural: los métodos reales usan las guardas nuevas, no el chequeo laxo ----

    [Fact]
    public void GetPortfolioNamesAsync_UsesTheGuard_NotTheLooseCountCheck()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("if (!CanReadPortfolioCache(portfolioColumns))", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("portfolioColumns.Count == 0", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SetPortfolioNameAsync_UsesTheGuard_NotTheLooseCountCheck()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public async Task SetPortfolioNameAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("if (!CanWritePortfolioCache(portfolioColumns))", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("portfolioColumns.Count == 0", methodBody, StringComparison.Ordinal);
        // La guarda debe aparecer ANTES del MERGE -- nunca construir/ejecutar el SqlCommand primero.
        var guardIndex = methodBody.IndexOf("CanWritePortfolioCache", StringComparison.Ordinal);
        var mergeIndex = methodBody.IndexOf("MERGE dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && mergeIndex >= 0 && guardIndex < mergeIndex);
    }

    [Fact]
    public void TryReserveResolutionAttemptAsync_UsesTheGuard_NotTheLooseCountCheck()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public async Task<bool> TryReserveResolutionAttemptAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("if (!CanWritePortfolioCache(portfolioColumns))", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("portfolioColumns.Count == 0", methodBody, StringComparison.Ordinal);
        var guardIndex = methodBody.IndexOf("CanWritePortfolioCache", StringComparison.Ordinal);
        var mergeIndex = methodBody.IndexOf("MERGE dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && mergeIndex >= 0 && guardIndex < mergeIndex);
    }

    [Fact]
    public void BackfillNumeroMetaIdentityAsync_UsesTheGuard_BeforeTheUpdate()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task BackfillNumeroMetaIdentityAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("if (!CanWriteNumeroMetaIdentity(numeroColumns))", methodBody, StringComparison.Ordinal);
        var guardIndex = methodBody.IndexOf("CanWriteNumeroMetaIdentity", StringComparison.Ordinal);
        var updateIndex = methodBody.IndexOf("UPDATE dbo.CONV_WHATSAPP_NUMEROS", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && updateIndex >= 0 && guardIndex < updateIndex);
    }

    private static string ReadServiceSource()
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "AlfaCore", "Services", "ConversacionesConfigService.cs"));

    private static string ExtractMethodBody(string source, int methodStart)
    {
        Assert.True(methodStart >= 0, "No se encontró el método.");
        var openBrace = source.IndexOf('{', methodStart);
        Assert.True(openBrace >= 0);
        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[methodStart..(i + 1)];
            }
        }
        throw new InvalidOperationException("No se pudo delimitar el cuerpo del método.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se pudo ubicar la raíz del repositorio (AlfaCore.sln) desde el directorio de pruebas.");
    }
}
