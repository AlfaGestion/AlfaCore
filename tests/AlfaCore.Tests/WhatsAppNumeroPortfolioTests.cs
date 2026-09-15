using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// "Cada número muestra nombre/teléfono pero no indica a qué Portfolio comercial de Meta pertenece" --
/// esto obligaba a entrar a Meta Business Manager para ubicar cada WABA. ResolvePortfolioName/
/// ResolvePortfolioDisplayLine son puros (mismo criterio que BuildPendingConnectionFeedbackPanel) y
/// nunca hacen un GET a Meta -- sólo leen el diccionario ya resuelto en lote por RefreshPortfolioNamesAsync.
/// </summary>
public sealed class WhatsAppNumeroPortfolioTests
{
    private static ConversacionWhatsAppNumeroDto Numero(string nombre, string metaBusinessId)
        => new() { IdNumero = 1, Nombre = nombre, PhoneNumberId = "123", MetaBusinessId = metaBusinessId };

    [Fact]
    public void TwoNumerosWithDifferentPortfolios_NeverMixTheirName()
    {
        var portfolios = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["biz-A"] = "Prueba",
            ["biz-B"] = "Producción",
        };
        var alfaNet = Numero("AlfaNetPapelera", "biz-A");
        var otroNumero = Numero("Otro número", "biz-B");

        Assert.Equal("Prueba", ConversacionesConfiguracion.ResolvePortfolioName(alfaNet, portfolios));
        Assert.Equal("Producción", ConversacionesConfiguracion.ResolvePortfolioName(otroNumero, portfolios));
        Assert.Equal("Portfolio: Prueba", ConversacionesConfiguracion.ResolvePortfolioDisplayLine(alfaNet, portfolios));
        Assert.Equal("Portfolio: Producción", ConversacionesConfiguracion.ResolvePortfolioDisplayLine(otroNumero, portfolios));
    }

    [Fact]
    public void SwitchingSelection_KeepsTheCorrectPortfolioForEachNumero()
    {
        // Simula el ida-y-vuelta de selección en la lista: resolver A, después B, después A de nuevo --
        // el diccionario es el mismo objeto todo el tiempo (no se reconstruye por número), así que el
        // único riesgo real es una implementación que cachee "el último resuelto" en un campo compartido
        // en vez de leer siempre por MetaBusinessId. ResolvePortfolioName no tiene estado propio.
        var portfolios = new Dictionary<string, string>(StringComparer.Ordinal) { ["biz-A"] = "Prueba", ["biz-B"] = "Producción" };
        var a = Numero("A", "biz-A");
        var b = Numero("B", "biz-B");

        Assert.Equal("Prueba", ConversacionesConfiguracion.ResolvePortfolioName(a, portfolios));
        Assert.Equal("Producción", ConversacionesConfiguracion.ResolvePortfolioName(b, portfolios));
        Assert.Equal("Prueba", ConversacionesConfiguracion.ResolvePortfolioName(a, portfolios));
    }

    [Fact]
    public void UnknownPortfolioName_FallsBackToNoIdentificado_NeverBreaksOrThrows()
    {
        // El número SÍ tiene MetaBusinessId (viene de Embedded Signup) pero el nombre todavía no se
        // cacheó (ej. justo después de importar, antes del próximo refresh) -- no debe tirar excepción
        // ni mostrar el ID técnico crudo como si fuera el nombre.
        var portfolios = new Dictionary<string, string>(StringComparer.Ordinal);
        var numero = Numero("AlfaNet", "biz-sin-nombre-todavia");

        var name = ConversacionesConfiguracion.ResolvePortfolioName(numero, portfolios);

        Assert.Equal("no identificado", name);
        Assert.DoesNotContain("biz-sin-nombre-todavia", ConversacionesConfiguracion.ResolvePortfolioDisplayLine(numero, portfolios));
    }

    [Fact]
    public void NumeroWithNoMetaBusinessId_ShowsNoPortfolioLineAtAll()
    {
        // Agregado manualmente por Phone Number ID -- nunca pasó por Embedded Signup, no tiene
        // MetaBusinessId. No debe insinuar un portfolio que no existe (ni el nombre ni el fallback).
        var portfolios = new Dictionary<string, string>(StringComparer.Ordinal) { ["biz-A"] = "Prueba" };
        var numero = Numero("Agregado a mano", metaBusinessId: "");

        Assert.Null(ConversacionesConfiguracion.ResolvePortfolioName(numero, portfolios));
        Assert.Null(ConversacionesConfiguracion.ResolvePortfolioDisplayLine(numero, portfolios));
    }

    [Fact]
    public void WorksTheSameForStandardAndCoexistence()
    {
        // El portfolio es una propiedad del Business/WABA en Meta, no del modo de onboarding con el que
        // se conectó el número (WhatsAppEmbeddedOnboardingMode.Standard vs BusinessAppCoexistence, un
        // concepto que ya no viaja en ConversacionWhatsAppNumeroDto una vez que el número quedó
        // operativo) -- el resolver no distingue: ambos números resuelven su MetaBusinessId igual.
        var portfolios = new Dictionary<string, string>(StringComparer.Ordinal) { ["biz-A"] = "Prueba" };
        var standard = Numero("Número Standard", "biz-A");
        var coexistence = Numero("Número Coexistence", "biz-A");

        Assert.Equal("Prueba", ConversacionesConfiguracion.ResolvePortfolioName(standard, portfolios));
        Assert.Equal("Prueba", ConversacionesConfiguracion.ResolvePortfolioName(coexistence, portfolios));
    }

    [Fact]
    public void EmptyPortfolioNameInCache_IsTreatedAsUnresolved_NotAsAnEmptyName()
    {
        // Defensivo: si por lo que sea el diccionario tuviera una entrada con valor vacío/blanco, no
        // debe mostrarse una línea "Portfolio: " vacía -- debe caer al mismo fallback que "no cacheado".
        var portfolios = new Dictionary<string, string>(StringComparer.Ordinal) { ["biz-A"] = "   " };
        var numero = Numero("Número", "biz-A");

        Assert.Equal("no identificado", ConversacionesConfiguracion.ResolvePortfolioName(numero, portfolios));
    }

    [Fact]
    public void TechnicalId_NeverAppearsInTheClientFacingDisplayLine()
    {
        var portfolios = new Dictionary<string, string>(StringComparer.Ordinal) { ["612345678901234"] = "AlfaNet Portfolio" };
        var numero = Numero("AlfaNet", "612345678901234");

        var line = ConversacionesConfiguracion.ResolvePortfolioDisplayLine(numero, portfolios)!;

        Assert.DoesNotContain("612345678901234", line, StringComparison.Ordinal);
        Assert.Equal("Portfolio: AlfaNet Portfolio", line);
    }

    [Fact]
    public void Classify_Unknown_WhenNeitherMetaBusinessIdNorCentralOwnershipExist()
        => Assert.Equal(WhatsAppPortfolioResolutionStatus.Unknown,
            WhatsAppPortfolioResolutionStatusExtensions.Classify(hasMetaBusinessId: false, hasCentralOwnership: false, hasCachedName: false));

    [Fact]
    public void Classify_Resolvable_WhenNoMetaBusinessIdButCentralOwnershipCanReconstructIt()
        => Assert.Equal(WhatsAppPortfolioResolutionStatus.Resolvable,
            WhatsAppPortfolioResolutionStatusExtensions.Classify(hasMetaBusinessId: false, hasCentralOwnership: true, hasCachedName: false));

    [Fact]
    public void Classify_Resolvable_WhenMetaBusinessIdKnownButNameNotCachedYet()
        => Assert.Equal(WhatsAppPortfolioResolutionStatus.Resolvable,
            WhatsAppPortfolioResolutionStatusExtensions.Classify(hasMetaBusinessId: true, hasCentralOwnership: false, hasCachedName: false));

    [Fact]
    public void Classify_Known_OnlyWhenBothMetaBusinessIdAndNameAreAvailable()
        => Assert.Equal(WhatsAppPortfolioResolutionStatus.Known,
            WhatsAppPortfolioResolutionStatusExtensions.Classify(hasMetaBusinessId: true, hasCentralOwnership: false, hasCachedName: true));

    [Fact]
    public void Classify_NeverKnown_WithoutAMetaBusinessId_EvenIfSomehowANameWereCached()
        => Assert.Equal(WhatsAppPortfolioResolutionStatus.Resolvable,
            WhatsAppPortfolioResolutionStatusExtensions.Classify(hasMetaBusinessId: false, hasCentralOwnership: true, hasCachedName: true));

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ExistingNumerosBackfill_AndThrottledNameResolution_AreWiredIntoTheListRefresh()
    {
        // Un número conectado ANTES de esta funcionalidad (Request D) no debe quedar eternamente en
        // "Portfolio no identificado" -- LoadNumerosAsync debe intentar completar tanto los IDs (sin
        // Meta) como el nombre (throttled) en cada refresh, delegando en el servicio dedicado en vez de
        // reimplementar la lógica inline en el Razor.
        var source = ReadPageSource();

        Assert.Contains("await RefreshNumeroMetaIdentityBackfillAsync(idBase);", source, StringComparison.Ordinal);
        Assert.Contains("await TryResolveMissingPortfolioNamesAsync(idBase);", source, StringComparison.Ordinal);
        Assert.Contains("PortfolioResolution.BackfillNumeroMetaIdentityAsync(idBase, numero, _lifetimeCts.Token)", source, StringComparison.Ordinal);
        Assert.Contains("PortfolioResolution.TryResolvePortfolioNameAsync(idBase, numero.MetaBusinessId, numero.PhoneNumberId, _lifetimeCts.Token)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PortfolioNames_AreFetchedInOneBatch_NeverOnePerRow()
    {
        var source = ReadPageSource();

        Assert.Contains("private async Task RefreshPortfolioNamesAsync()", source, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetPortfolioNamesAsync(", source, StringComparison.Ordinal);
        Assert.Contains("await RefreshPortfolioNamesAsync();", source, StringComparison.Ordinal);

        var occurrences = System.Text.RegularExpressions.Regex.Matches(source, System.Text.RegularExpressions.Regex.Escape("ConfigSvc.GetPortfolioNamesAsync(")).Count;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void PortfolioResolution_NeverCallsMeta_OnlyReadsTheLocalCache()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró GetPortfolioNamesAsync.");
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.DoesNotContain("managementClient", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", methodBody, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS", methodBody, StringComparison.Ordinal);
        // Self-tolerant: si la base todavía no corrió la actualización que crea la tabla, no debe fallar.
        Assert.Contains("if (portfolioColumns.Count == 0)", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SetPortfolioName_IsOpportunistic_NeverTriggersANewMetaCall_AndIsBestEffort()
    {
        // "NO hacer un GET a Meta en cada render" -- y tampoco uno nuevo sólo para cachear el nombre: el
        // import service sólo llama a esto cuando el discovery YA trajo business.Name de una llamada que
        // iba a hacerse de todos modos (ver AuthorizedPhoneForImport.BusinessName).
        var importSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "WhatsAppEmbeddedOperationalImportService.cs"));
        Assert.Contains("if (!string.IsNullOrWhiteSpace(phone.BusinessName))", importSource, StringComparison.Ordinal);
        Assert.Contains("await conversacionesConfig.SetPortfolioNameAsync(activeBaseId, phone.MetaBusinessId, phone.BusinessName, ct);", importSource, StringComparison.Ordinal);

        var configSource = ReadServiceSource();
        var methodStart = configSource.IndexOf("public async Task SetPortfolioNameAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró SetPortfolioNameAsync.");
        var methodBody = ExtractMethodBody(configSource, methodStart);

        // Nunca debe poder tirar abajo el alta operativa del número: todo el cuerpo relevante está
        // envuelto en try/catch, y el catch no vuelve a lanzar.
        Assert.Contains("catch (Exception ex) when (ex is not OperationCanceledException)", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RawMetaBusinessIdAndWabaId_OnlyAppearInsideTheTechnicalDetailsBlock_NeverInClientFacingText()
    {
        // "El ID técnico: no mostrar al cliente" -- confirma que los únicos lugares del Razor que
        // interpolan selectedNumero.MetaBusinessId/WabaId crudos están dentro de <details
        // class="wa-api-technical"> (colapsado, ya usado hoy para "ID interno"/"Phone Number ID").
        var source = ReadPageSource();
        // Ancla única (sólo existe en el bloque de selectedNumero, no en el de apiDraft/edición): busca
        // hacia atrás el <details> que lo contiene, no el primero del archivo.
        var portfolioIdAnchor = source.IndexOf("<dt>Portfolio ID</dt>", StringComparison.Ordinal);
        Assert.True(portfolioIdAnchor >= 0, "No se encontró la fila de Portfolio ID en la sección técnica.");
        var technicalStart = source.LastIndexOf("<details class=\"wa-api-technical\">", portfolioIdAnchor, StringComparison.Ordinal);
        Assert.True(technicalStart >= 0);
        var technicalEnd = source.IndexOf("</details>", portfolioIdAnchor, StringComparison.Ordinal);
        Assert.True(technicalEnd >= 0);
        var technicalBlock = source[technicalStart..technicalEnd];

        Assert.Contains("selectedNumero.WabaId", technicalBlock, StringComparison.Ordinal);
        Assert.Contains("selectedNumero.MetaBusinessId", technicalBlock, StringComparison.Ordinal);
        Assert.Contains("<dt>Portfolio ID</dt>", technicalBlock, StringComparison.Ordinal);
        Assert.Contains("<dt>Portfolio Name</dt>", technicalBlock, StringComparison.Ordinal);
        Assert.Contains("<dt>WABA ID</dt>", technicalBlock, StringComparison.Ordinal);

        // Fuera de ese bloque técnico, .MetaBusinessId/.WabaId crudos no deben aparecer en absoluto --
        // sólo a través de ResolvePortfolioName/ResolvePortfolioDisplayLine (que nunca exponen el ID).
        var outsideTechnical = source.Remove(technicalStart, technicalEnd - technicalStart);
        Assert.DoesNotContain("selectedNumero.MetaBusinessId", outsideTechnical, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedNumero.WabaId", outsideTechnical, StringComparison.Ordinal);
        Assert.DoesNotContain(".MetaBusinessId</dd>", outsideTechnical, StringComparison.Ordinal);
    }

    [Fact]
    public void NewColumnsAndTable_AreDocumentedAsAnIdempotentUpdateScript_NeverAppliedAutomatically()
    {
        var updatePath = Path.Combine(RepositoryRoot, "src", "AlfaCore", "App_Data", "updates", "2026-09-15-001__conversaciones_whatsapp_business_portfolio.sql");
        Assert.True(File.Exists(updatePath), "Falta el script de actualización versionado.");
        var sql = File.ReadAllText(updatePath);

        Assert.Contains("COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'MetaBusinessId') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WabaId') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS", sql, StringComparison.Ordinal);
        // Puede MENCIONAR Vault/ownership en comentarios (para aclarar que el mecanismo justamente NO
        // los usa -- el backfill LEE ownership central, la resolución de nombre usa el runtime
        // credential, no el Vault de onboarding), pero ningún DDL/DML real de este script debe crear,
        // alterar ni escribir nada con esos nombres -- sólo la tabla de números (propia de la base
        // tenant, no ALFA_CENTRAL) y una tabla nueva e independiente.
        var ddlLines = sql.Split('\n').Where(line =>
            line.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase)
            || line.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase)
            || line.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
            || line.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
            || line.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(ddlLines);
        Assert.All(ddlLines, line =>
        {
            Assert.DoesNotContain("Ownership", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Vault", line, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string ReadPageSource()
        => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));

    private static string ReadServiceSource()
        => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesConfigService.cs"));

    private static string ExtractMethodBody(string source, int methodStart)
    {
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
