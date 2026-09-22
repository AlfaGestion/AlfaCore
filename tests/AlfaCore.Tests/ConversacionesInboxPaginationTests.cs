using System.Text.RegularExpressions;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Caracteriza el fix de paginación server-side del inbox de Conversaciones (bug: en tenants de alto
/// volumen, "Todas" + "Cerrada" solo mostraba las 50 conversaciones más recientes -- Offset/Limit
/// nunca avanzaban desde la UI -- sin forma de llegar al histórico).
///
/// No existe en este repo un fixture de base de datos por tenant (CONV_CONVERSACIONES, CONV_ESTADOS,
/// etc.) contra el que correr una integración real, así que -- siguiendo el mismo patrón que
/// <see cref="WhatsAppTenantIsolationTests"/> usa para otras garantías de Conversaciones -- estos tests
/// verifican la fuente (servicio + UI) para fijar la forma exacta del fix, y ejercitan la aritmética de
/// paginación real (<see cref="PagedResult{T}"/>) con los tamaños de "CASOS A PROBAR" del pedido.
/// </summary>
public sealed class ConversacionesInboxPaginationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string ServiceSource => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
    private static string ContractSource => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "IConversacionesService.cs"));
    private static string PageSource => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));

    // ---- PagedResult<T>: aritmética real de página/offset (20 / 50 / 51 / 120 resultados) ----

    [Theory]
    [InlineData(20, 50, 1, 1, false, false)]   // 20 resultados -> página única
    [InlineData(50, 50, 1, 1, false, false)]   // 50 resultados -> página única (el límite exacto no debe habilitar "Siguiente")
    [InlineData(51, 50, 1, 2, false, true)]    // 51 resultados -> habilita "Siguiente"
    [InlineData(120, 50, 1, 3, false, true)]   // 120 resultados, página 1 de 3
    [InlineData(120, 50, 2, 3, true, true)]    // 120 resultados, página 2 de 3 (Anterior y Siguiente habilitados)
    [InlineData(120, 50, 3, 3, true, false)]   // 120 resultados, última página -> "Siguiente" deshabilitado
    public void PagedResult_CalculaPaginasYNavegacionEsperada(int total, int pageSize, int pageNumber, int expectedTotalPages, bool expectedHasPrev, bool expectedHasNext)
    {
        var result = new PagedResult<ConversacionInboxItemDto>
        {
            Items = [],
            Total = total,
            PageSize = pageSize,
            PageNumber = pageNumber
        };

        Assert.Equal(expectedTotalPages, result.TotalPages);
        Assert.Equal(expectedHasPrev, result.HasPrev);
        Assert.Equal(expectedHasNext, result.HasNext);
    }

    // ---- Backend: OFFSET/FETCH real + COUNT(*) OVER() sin total inventado ----

    [Fact]
    public void GetInboxPagedAsync_UsaOffsetFetchYCountOverParaElTotal()
    {
        var service = ServiceSource;

        Assert.Contains("public Task<PagedResult<ConversacionInboxItemDto>> GetInboxPagedAsync(ConversacionesInboxFilters filters, CancellationToken ct = default)", service, StringComparison.Ordinal);
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY", service, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) OVER() AS TotalCount", service, StringComparison.Ordinal);

        // El WHERE (con el filtro de Estado) debe seguir aplicándose ANTES del OFFSET/FETCH: el bloque
        // fromWhereSql (FROM + WHERE) se arma una sola vez y se interpola en la consulta paginada antes
        // del ORDER BY + OFFSET/FETCH -- no hay un TOP recortando resultados antes del filtro de estado.
        var offsetFetchIndex = service.IndexOf("OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY", StringComparison.Ordinal);
        var whereIndex = service.IndexOf("@CodigoEstado IS NULL", StringComparison.Ordinal);
        Assert.True(whereIndex >= 0, "No se encontró el filtro de @CodigoEstado en el WHERE del inbox.");
        Assert.True(whereIndex < offsetFetchIndex, "El filtro de Estado debe evaluarse antes del OFFSET/FETCH.");

        // Nunca se inventa un total: si la página pedida vino vacía (Offset corrido más allá del total
        // real), se hace un COUNT real reutilizando el mismo WHERE en vez de asumir Total = 0.
        Assert.Contains("SELECT COUNT(1)", service, StringComparison.Ordinal);
        Assert.Contains("AddInboxParameters(countCmd)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInboxAsync_EsWrapperDeCompatibilidadSobreGetInboxPagedAsync()
    {
        var service = ServiceSource;
        Assert.Contains("=> (await GetInboxPagedAsync(filters, ct)).Items;", service, StringComparison.Ordinal);
    }

    [Fact]
    public void IConversacionesService_DeclaraGetInboxPagedAsync()
    {
        Assert.Contains("Task<PagedResult<ConversacionInboxItemDto>> GetInboxPagedAsync(ConversacionesInboxFilters filters, CancellationToken ct = default);", ContractSource, StringComparison.Ordinal);
    }

    // ---- UI: navegación Siguiente/Anterior avanza y retrocede exactamente por el tamaño de página ----

    [Fact]
    public void Navegacion_SiguienteYAnteriorMuevenOffsetPorPageSize()
    {
        var page = PageSource;

        Assert.Contains("_filters.Offset += InboxPageSize;", page, StringComparison.Ordinal);
        Assert.Contains("_filters.Offset = Math.Max(0, _filters.Offset - InboxPageSize);", page, StringComparison.Ordinal);

        // El orden (más reciente primero, salvo cola_espera) no cambia con este fix: dado que "Siguiente"
        // solo avanza Offset dentro del mismo ORDER BY ya existente, la página 2 necesariamente contiene
        // filas más viejas que la página 1 -- exactamente el histórico que antes era inalcanzable.
        Assert.Contains("BuildInboxOrderByClause(filters.Orden)", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PagerUi_MuestraRangoYDeshabilitaBotonesEnLosExtremos()
    {
        var page = PageSource;

        Assert.Contains("private bool InboxHasPrevPage => InboxPageNumber > 1;", page, StringComparison.Ordinal);
        Assert.Contains("private bool InboxHasNextPage => InboxPageNumber < InboxTotalPages;", page, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!InboxHasPrevPage || _loadingInbox)\"", page, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!InboxHasNextPage || _loadingInbox)\"", page, StringComparison.Ordinal);
    }

    // ---- Reset de página en cambios de filtro (no se queda pidiendo página 4 de un filtro viejo) ----

    [Theory]
    [InlineData("ChangeModeAsync")]        // Todas / mías / sin_asignar / pendientes / cerradas
    [InlineData("ApplyInboxFiltersAsync")] // botón "Aplicar" de Estado / Técnico
    [InlineData("HandleTechnicianModeChangedAsync")]
    [InlineData("HandleTechnicianFilterChangedAsync")]
    [InlineData("ToggleColaEsperaAsync")]  // orden
    [InlineData("ChangeCanalAsync")]
    [InlineData("SelectWhatsAppInboxNumeroAsync")]
    [InlineData("ClearConversationFiltersAsync")]
    [InlineData("ClearMobileAdvancedFiltersAsync")]
    [InlineData("ApplyMobileAdvancedFiltersAsync")]
    public void HandlersDeFiltro_ReseteanPaginaAntesDeRecargar(string methodName)
    {
        var body = ExtractMethodBody(PageSource, methodName);
        Assert.Contains("ResetInboxPaging()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetInboxSearchScope_ReseteaPagina()
    {
        // Búsqueda: se llama desde HandleSearchInputAsync (cada tecla) y ClearInboxSearchAsync.
        var body = ExtractMethodBody(PageSource, "ResetInboxSearchScope");
        Assert.Contains("ResetInboxPaging();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetInboxPaging_PoneOffsetEnCero()
    {
        Assert.Contains("private void ResetInboxPaging() => _filters.Offset = 0;", PageSource, StringComparison.Ordinal);
    }

    // ---- Auto-refresh: refresca la página actual sin resetear ni perder la selección ----

    [Fact]
    public void AutoRefreshAsync_NoReseteaPaginaYPreservaSeleccion()
    {
        var body = ExtractMethodBody(PageSource, "AutoRefreshAsync");

        // Usa RefreshInboxAsync directo (no LoadInboxAsync/ResetInboxPaging): mantiene _filters.Offset
        // tal cual estaba, así que el usuario no vuelve a página 1 solo porque el poll disparó.
        Assert.Contains("RefreshInboxAsync(showLoading: false, allowSelectionChange: false, trackNewMessages: true)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetInboxPaging", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshInboxAsync_ActualizaElTotalEnCadaRefresh()
    {
        var body = ExtractMethodBody(PageSource, "RefreshInboxAsync");

        Assert.Contains("GetInboxPagedAsync(_filters, token)", body, StringComparison.Ordinal);
        Assert.Contains("_inboxTotal = pagedInbox.Total;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RecargarAsync_NoReseteaPagina()
    {
        // "Recargar" manual: debe refrescar la página actual, no saltar a la 1.
        var body = ExtractMethodBody(PageSource, "RecargarAsync");
        Assert.DoesNotContain("ResetInboxPaging", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Extrae el cuerpo de un método por nombre, entre su primera "{" y la "}" que la cierra (contando
    /// llaves), asumiendo que no hay otra declaración con el mismo nombre antes en el archivo.
    /// </summary>
    private static string ExtractMethodBody(string source, string methodName)
    {
        var match = Regex.Match(source, $@"\b{Regex.Escape(methodName)}\s*\([^)]*\)\s*(=>|\{{)", RegexOptions.Multiline);
        Assert.True(match.Success, $"No se encontró el método '{methodName}'.");

        if (match.Groups[1].Value == "=>")
        {
            var semicolonIndex = source.IndexOf(';', match.Index + match.Length);
            Assert.True(semicolonIndex > 0, $"No se encontró el fin de la expresión de '{methodName}'.");
            return source[match.Index..(semicolonIndex + 1)];
        }

        var openBraceIndex = match.Index + match.Length - 1;
        var depth = 0;
        for (var i = openBraceIndex; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[match.Index..(i + 1)];
            }
        }

        throw new InvalidOperationException($"No se pudo cerrar el cuerpo de '{methodName}'.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
