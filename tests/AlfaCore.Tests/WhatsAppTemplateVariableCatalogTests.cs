using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Catálogo de variables de plantillas WhatsApp + mapping persistido + resolver central del envío
/// manual (reemplazo de GetTemplateAutoValuesAsync). La mayoría de las dependencias reales (SQL,
/// cuenta corriente) hacen inviable un test de integración sin [SqlIntegrationFact]; siguiendo el
/// mismo patrón que WhatsAppTemplateSendTests/WhatsAppTemplateSelectionTests, se combina (a) tests
/// puros sobre el catálogo (objeto real, sin IO) con (b) aserciones sobre el texto fuente para la
/// lógica que sí depende de SQL.
/// </summary>
public sealed class WhatsAppTemplateVariableCatalogTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // ---------------------------------------------------------------------------------------------
    // Catálogo: objeto real, sin dependencias externas.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Catalog_ContainsExactlyTheConfirmedResolvableVariables_NoFacturaKeys()
    {
        var keys = WhatsAppTemplateVariableCatalog.All.Select(x => x.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(WhatsAppTemplateVariableCatalog.ContactName, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.CobranzaDetalleDeuda, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.PagoFormaPago, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.TareaTitulo, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.TareaTecnicoAsignado, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.TareaAutorAccion, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.TareaFechaHoraRegistro, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.GuardiaTecnico, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.GuardiaInicio, keys);
        Assert.Contains(WhatsAppTemplateVariableCatalog.GuardiaFin, keys);

        // Nunca variables de Facturación: sin resolver real confirmado (100% texto hardcodeado hoy).
        Assert.DoesNotContain(keys, k => k.StartsWith("factura.", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(WhatsAppTemplateVariableCatalog.ContactName, true)]
    [InlineData(WhatsAppTemplateVariableCatalog.CobranzaDetalleDeuda, true)]
    [InlineData(WhatsAppTemplateVariableCatalog.PagoFormaPago, true)]
    [InlineData(WhatsAppTemplateVariableCatalog.TareaTitulo, false)]
    [InlineData(WhatsAppTemplateVariableCatalog.TareaTecnicoAsignado, false)]
    [InlineData(WhatsAppTemplateVariableCatalog.TareaAutorAccion, false)]
    [InlineData(WhatsAppTemplateVariableCatalog.TareaFechaHoraRegistro, false)]
    [InlineData(WhatsAppTemplateVariableCatalog.GuardiaTecnico, false)]
    [InlineData(WhatsAppTemplateVariableCatalog.GuardiaInicio, false)]
    [InlineData(WhatsAppTemplateVariableCatalog.GuardiaFin, false)]
    public void Catalog_OnlyContactCobranzaAndPago_AreResolvableInManualSendFlow(string key, bool expected)
    {
        var definition = WhatsAppTemplateVariableCatalog.Find(key);
        Assert.NotNull(definition);
        Assert.Equal(expected, definition!.CanResolveAutomaticallyInManualSend);
    }

    [Fact]
    public void Catalog_TareasAndGuardiaVariables_CarryRequiredContextForTheSelectorUi()
    {
        foreach (var definition in WhatsAppTemplateVariableCatalog.All)
        {
            var isContextual = definition.Group is WhatsAppTemplateVariableCatalog.GroupTareas or WhatsAppTemplateVariableCatalog.GroupGuardia;
            if (isContextual)
                Assert.False(string.IsNullOrWhiteSpace(definition.RequiredContext));

            // Toda variable expone descripción -- el selector siempre puede mostrar Label + Description.
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            Assert.False(string.IsNullOrWhiteSpace(definition.Label));
        }
    }

    [Fact]
    public void Catalog_FindIsCaseSensitiveAndReturnsNullForUnknownKeys()
    {
        Assert.NotNull(WhatsAppTemplateVariableCatalog.Find(WhatsAppTemplateVariableCatalog.ContactName));
        Assert.Null(WhatsAppTemplateVariableCatalog.Find("contact.NAME"));
        Assert.Null(WhatsAppTemplateVariableCatalog.Find("factura.periodo"));
        Assert.Null(WhatsAppTemplateVariableCatalog.Find(null));
        Assert.False(WhatsAppTemplateVariableCatalog.IsValidKey("no.existe"));
    }

    [Fact]
    public void Catalog_ComponenteBody_IsTheOnlyComponentAcceptedByBusinessLogicToday()
        => Assert.Equal("BODY", WhatsAppTemplateVariableCatalog.ComponenteBody);

    // ---------------------------------------------------------------------------------------------
    // Resolver contact.name: función real, factorizada, sin IO -- se puede probar directo.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolveContactNameAutoValue_PrefersClienteNombre_ThenContacto_ThenVisible_ThenPhone()
    {
        var withCliente = new ConversacionDetalleDto { ClienteNombre = "Cliente SA", ContactoNombre = "Contacto", NombreVisible = "Visible", TelefonoWhatsApp = "54911" };
        Assert.Equal("Cliente SA", ConversacionesService.ResolveContactNameAutoValue(withCliente));

        var withContacto = new ConversacionDetalleDto { ClienteNombre = "", ContactoNombre = "Contacto", NombreVisible = "Visible", TelefonoWhatsApp = "54911" };
        Assert.Equal("Contacto", ConversacionesService.ResolveContactNameAutoValue(withContacto));

        var withVisible = new ConversacionDetalleDto { ClienteNombre = "", ContactoNombre = "", NombreVisible = "Visible", TelefonoWhatsApp = "54911" };
        Assert.Equal("Visible", ConversacionesService.ResolveContactNameAutoValue(withVisible));

        var onlyPhone = new ConversacionDetalleDto { ClienteNombre = "", ContactoNombre = "", NombreVisible = "", TelefonoWhatsApp = "54911" };
        Assert.Equal("54911", ConversacionesService.ResolveContactNameAutoValue(onlyPhone));
    }

    // ---------------------------------------------------------------------------------------------
    // Migración SQL: idempotencia, no destructiva, patrón del proyecto.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Migration_ConvPlantillasVariables_IsIdempotentAndNeverTouchesExistingRows()
    {
        var sql = Read("src", "AlfaCore", "App_Data", "updates", "2026-09-21-001__conv_plantillas_variables.sql");

        Assert.Contains("IF OBJECT_ID(N'dbo.CONV_PLANTILLAS_VARIABLES', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT UQ_CONV_PLANTILLAS_VARIABLES_Posicion UNIQUE (IdPlantilla, Componente, Posicion)", sql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT FK_CONV_PLANTILLAS_VARIABLES_PLANTILLA FOREIGN KEY (IdPlantilla)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM dbo.CONV_PLANTILLAS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE dbo.CONV_PLANTILLAS ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------
    // Resolver central: aserciones de texto fuente (no cruza semánticas, señaliza en vez de inventar).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetTemplateAutoValuesAsync_ResolvesEachPositionAgainstItsOwnMapping_NeverAssumingFixedIndex()
    {
        var body = ReadMethodBody("public Task<ConversacionPlantillaAutoValuesDto> GetTemplateAutoValuesAsync(");

        // El resolver itera posición por posición y consulta el mapping persistido para CADA una --
        // no arma un array posicional fijo var1/var2/var3 como hacía la versión vieja.
        Assert.Contains("mappings.TryGetValue(pos, out var variableKey)", body, StringComparison.Ordinal);
        Assert.Contains("WhatsAppTemplateVariableCatalog.Find(variableKey)", body, StringComparison.Ordinal);
        Assert.Contains("WhatsAppTemplateVariableCatalog.ContactName =>", body, StringComparison.Ordinal);
        Assert.Contains("WhatsAppTemplateVariableCatalog.CobranzaDetalleDeuda =>", body, StringComparison.Ordinal);
        Assert.Contains("WhatsAppTemplateVariableCatalog.PagoFormaPago =>", body, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTemplateAutoValuesAsync_UnresolvablePosition_StopsInsteadOfInventingAValue()
    {
        var body = ReadMethodBody("public Task<ConversacionPlantillaAutoValuesDto> GetTemplateAutoValuesAsync(");

        // Cuando una variable mapeada no tiene resolver en este flujo (Tareas/Guardia) o el resolver no
        // pudo resolver, se marca "stopped" -- nunca se agrega un valor inventado a la lista de
        // resultados para esa posición.
        Assert.Contains("stopped = true", body, StringComparison.Ordinal);
        Assert.Contains("No se puede completar automáticamente desde el envío manual", body, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTemplateAutoValuesAsync_UnmappedPosition_PreservesTheHistoricalHeuristic_ForBackwardCompatibility()
    {
        var body = ReadMethodBody("public Task<ConversacionPlantillaAutoValuesDto> GetTemplateAutoValuesAsync(");

        // Compatibilidad total: una posición SIN mapping se resuelve igual que antes de esta feature
        // (heurístico histórico posición 1/2/3), para no romper plantillas ya existentes sin mapping.
        Assert.Contains("Detalle de deuda pendiente de completar.", body, StringComparison.Ordinal);
        Assert.Contains("Datos de transferencia pendientes de configurar.", body, StringComparison.Ordinal);
        Assert.Contains("$\"Dato {pos}\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveCobranzaDetalleDeudaAutoValue_ReusesTryBuildDebtDetailAsync_DoesNotReimplementIt()
    {
        var body = ReadMethodBody("private async Task<(bool Resolved, string Value, string? Observation)> ResolveCobranzaDetalleDeudaAutoValueAsync(");
        Assert.Contains("await TryBuildDebtDetailAsync(cn, conversation.ClienteCodigo, ct)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePagoFormaPagoAutoValue_ReusesTheSameConfigKeyAsBefore()
    {
        var body = ReadMethodBody("private async Task<(bool Resolved, string Value, string? Observation)> ResolvePagoFormaPagoAutoValueAsync(");
        Assert.Contains("\"CONV_COBRANZA_FORMA_PAGO\"", body, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Persistencia del mapping: nunca infiere, siempre descarta huérfanos contra el texto final.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SaveTemplateVariableMappingsAsync_DiscardsPositionsThatNoLongerExistInTheFinalBody()
    {
        var body = ReadMethodBody("private static async Task SaveTemplateVariableMappingsAsync(");
        Assert.Contains("existingPositions.Contains(kv.Key)", body, StringComparison.Ordinal);
        Assert.Contains("WhatsAppTemplateVariableCatalog.IsValidKey(kv.Value)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveTemplateVariableMappingsAsync_NeverInfersAKeyFromTemplateText()
    {
        var body = ReadMethodBody("private static async Task SaveTemplateVariableMappingsAsync(");

        // La firma no recibe NombreVisible/NombreMeta y el único uso de CuerpoTexto es para detectar
        // qué posiciones {{N}} existen (Regex sobre placeholders), nunca para derivar una VariableKey
        // a partir de substrings del texto (esa heurística de texto es justamente lo que esta feature
        // reemplaza).
        Assert.DoesNotContain(".Contains(\"FACTURA\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Contains(\"COBRANZA\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToUpperInvariant()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveTemplateDraftAsync_PersistsVariableMappingsAfterBothInsertAndUpdate()
    {
        var source = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var methodStart = source.IndexOf("public Task<long> SaveTemplateDraftAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);

        var occurrences = CountOccurrences(methodBody, "SaveTemplateVariableMappingsAsync(cn,");
        Assert.Equal(2, occurrences); // una vez en el path INSERT, una vez en el path UPDATE
    }

    [Fact]
    public void GetTemplateAsync_LoadsPersistedVariableMappings()
    {
        var source = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var methodStart = source.IndexOf("public Task<ConversacionPlantillaDto?> GetTemplateAsync(long idPlantilla, int? expectedBaseId", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("LoadTemplateVariableMappingsAsync(cn, template.IdPlantilla, WhatsAppTemplateVariableCatalog.ComponenteBody, token)", methodBody, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Sync Meta: nunca inventa mapping para plantillas importadas.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void MetaSync_NeverWritesToTheVariableMappingsTable()
    {
        var source = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var methodStart = source.IndexOf("private static async Task<TemplateCatalogSyncAction> UpsertRemoteTemplateAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.DoesNotContain("CONV_PLANTILLAS_VARIABLES", methodBody, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // TareasService / CalendarioService: comportamiento preservado -- nunca pasan por el resolver
    // central de envío manual, siguen armando su array posicional propio como siempre.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TareasService_NeverCallsTheManualSendAutoResolver_KeepsItsOwnPositionalArray()
    {
        var source = Read("src", "AlfaCore", "Services", "TareasService.cs");
        Assert.DoesNotContain("GetTemplateAutoValuesAsync", source, StringComparison.Ordinal);
        Assert.Contains("ValoresVariables = values,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CalendarioService_NeverCallsTheManualSendAutoResolver_KeepsItsOwnPositionalArray()
    {
        var source = Read("src", "AlfaCore", "Services", "CalendarioService.cs");
        Assert.DoesNotContain("GetTemplateAutoValuesAsync", source, StringComparison.Ordinal);
        Assert.Contains("ValoresVariables =", source, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // UI: Insertar variable (BODY únicamente), variables usadas, presets removidos.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TemplatesPage_BodyField_OffersInsertVariableMenu()
    {
        var source = ReadPageSource();
        var bodyFieldStart = source.IndexOf("id=\"tpl-body\"", StringComparison.Ordinal);
        Assert.True(bodyFieldStart >= 0);

        var window = source[Math.Max(0, bodyFieldStart - 1500)..bodyFieldStart];
        Assert.Contains("BuildInsertVariableMenuItems()", window, StringComparison.Ordinal);
        Assert.Contains("AlfaActionMenu", window, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesPage_HeaderField_NeverOffersInsertVariable()
    {
        var source = ReadPageSource();

        // El campo Encabezado es un <AlfaInput ... /> autocontenido: el bloque desde su Label hasta su
        // propio cierre "/>" no debe traer ningún control de inserción de variables.
        var headerFieldStart = source.IndexOf("Label=\"Encabezado de texto\"", StringComparison.Ordinal);
        Assert.True(headerFieldStart >= 0);
        var headerFieldEnd = source.IndexOf("/>", headerFieldStart, StringComparison.Ordinal);
        Assert.True(headerFieldEnd > headerFieldStart);

        var headerBlock = source[headerFieldStart..headerFieldEnd];
        Assert.DoesNotContain("BuildInsertVariableMenuItems", headerBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("AlfaActionMenu", headerBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("InsertCatalogVariable", headerBlock, StringComparison.Ordinal);

        // Y en general, la página ofrece un único control "Insertar variable" (el de BODY) -- Meta y el
        // runtime de envío no soportan parámetros de header hoy, así que no hay un segundo control.
        var menuOccurrences = CountOccurrences(source, "<AlfaActionMenu Items=\"@BuildInsertVariableMenuItems()\"");
        Assert.Equal(1, menuOccurrences);
    }

    [Fact]
    public void TemplatesPage_ShowsOnlyVariablesActuallyPresentInTheBody_NotTheWholeCatalog()
    {
        var source = ReadPageSource();

        // El bloque debajo del cuerpo itera "usedVariables"/UsedBodyVariables (derivado de los {{N}}
        // presentes en el texto actual), nunca WhatsAppTemplateVariableCatalog.All directo en el markup.
        Assert.Contains("UsedBodyVariables", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@foreach (var definition in WhatsAppTemplateVariableCatalog.All)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesPage_InsertCatalogVariable_PicksTheSmallestUnusedPosition_NoGapsNoDuplicates()
    {
        var body = ReadPageMethodBody("private int NextBodyVariablePosition()");
        Assert.Contains("while (used.Contains(candidate))", body, StringComparison.Ordinal);
        Assert.Contains("candidate++", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesPage_ManualBodyEdits_PruneOrphanMappings_OnBlurAndOnSave()
    {
        var source = ReadPageSource();
        Assert.Contains("@bind:after=\"PruneOrphanVariableMappings\"", source, StringComparison.Ordinal);

        var saveRequestBody = ReadPageMethodBody("private ConversacionPlantillaSaveRequest BuildSaveRequest()");
        Assert.Contains("PruneOrphanVariableMappings();", saveRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesPage_ReusingTheSameVariableKeyInTwoPositions_IsNotBlockedByInsertLogic()
    {
        // InsertCatalogVariable solo valida la POSICIÓN (mex de las usadas); nunca revisa si la
        // VariableKey ya está usada en otra posición, así que repetirla es válido por diseño.
        var body = ReadPageMethodBody("private void InsertCatalogVariable(string variableKey)");
        Assert.DoesNotContain("VariableMappings.ContainsValue", body, StringComparison.Ordinal);
        Assert.DoesNotContain("already", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemplatesPage_PresetActions_AreCompletelyRemovedFromTheOverflowMenu()
    {
        var source = ReadPageSource();
        foreach (var removedKey in new[] { "\"factura\"", "\"cobranza\"", "\"tarea-asignada\"", "\"tarea-finalizada\"", "\"guardia\"" })
            Assert.DoesNotContain($"Key = {removedKey}", source, StringComparison.Ordinal);

        // Se conservan tal cual: Sincronizar estado / Archivar / Recargar.
        Assert.Contains("Key = \"sincronizar-item\"", source, StringComparison.Ordinal);
        Assert.Contains("Key = \"archivar\"", source, StringComparison.Ordinal);
        Assert.Contains("Key = \"recargar\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesPage_PresetBuildersAndHeuristics_NoLongerExist()
    {
        var source = ReadPageSource();
        foreach (var removed in new[]
        {
            "AplicarPlantillaFacturaMantenimientoAsync",
            "AplicarPlantillaCobranzaAsync",
            "AplicarPlantillaTareaAsignadaAsync",
            "AplicarPlantillaTareaFinalizadaAsync",
            "AplicarPlantillaGuardiaAsync",
            "NewFacturaMantenimientoForm",
            "NewCobranzaForm",
            "NewTareaAsignadaForm",
            "NewTareaFinalizadaForm",
            "NewGuardiaForm",
            "EsPlantillaFacturaMantenimiento",
            "EsPlantillaCobranza",
            "EsPlantillaTareas",
            "EsPlantillaTareaAsignada",
            "EsPlantillaTareaFinalizada",
            "EsPlantillaGuardia",
            "AplicarFiltroModeloSeleccionado",
            "class CrearOpciones",
            "templates-variable-chip\" @onclick",
        })
            Assert.DoesNotContain(removed, source, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string ReadMethodBody(string signaturePrefix)
    {
        var source = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var methodStart = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"No se encontró el método que empieza con: {signaturePrefix}");
        return ExtractMethodBody(source, methodStart);
    }

    private static string ReadPageMethodBody(string signaturePrefix)
    {
        var source = ReadPageSource();
        var methodStart = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"No se encontró el método que empieza con: {signaturePrefix}");
        return ExtractMethodBody(source, methodStart);
    }

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

    private static string Read(params string[] relativeSegments)
        => File.ReadAllText(Path.Combine([RepositoryRoot, .. relativeSegments]));

    private static string ReadPageSource()
        => Read("src", "AlfaCore", "Components", "Pages", "ConversacionesPlantillas.razor");

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
