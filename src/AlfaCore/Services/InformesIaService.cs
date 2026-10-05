using AlfaCore.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed partial class InformesIaService(
    IConfiguration configuration,
    ILogger<InformesIaService> logger,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    InformesIaHistoryStore historyStore,
    InformesIaResultStore resultStore,
    ISessionService sessionService,
    IIaUsoRecorder? iaUso = null) : IInformesIaService
{
    private const int MaxRows = 40;

    private static readonly IReadOnlyList<InformeIaSuggestionDto> Suggestions =
    [
        new() { Texto = "Proveedores con mayor crecimiento en el período", Descripcion = "Compara el gasto actual contra el período anterior y ordena por variación.", Categoria = "Ranking" },
        new() { Texto = "Artículos con mayor aumento", Descripcion = "Detecta subas de costo promedio respecto del período anterior.", Categoria = "Variación" },
        new() { Texto = "Rubros con más participación", Descripcion = "Muestra concentración del gasto por rubro en el período filtrado.", Categoria = "Concentración" },
        new() { Texto = "Familias que más crecieron", Descripcion = "Compara ramas familiares contra el período anterior.", Categoria = "Comparación" },
        new() { Texto = "Comprobantes con mayor importe", Descripcion = "Lista los comprobantes más altos del período.", Categoria = "Top" },
        new() { Texto = "Usuarios con más actividad", Descripcion = "Resume quiénes cargaron más comprobantes y monto.", Categoria = "Actividad" },
        new() { Texto = "Concentración del gasto por proveedor", Descripcion = "Participación del gasto entre los principales proveedores.", Categoria = "Concentración" },
        new() { Texto = "Evolución de compras por mes", Descripcion = "Serie mensual de compras consolidada para los últimos 12 meses.", Categoria = "Evolución" },
        new() { Texto = "Compará rubros contra el período anterior", Descripcion = "Diferencia y variación de rubros vs período previo.", Categoria = "Comparación" }
    ];

    private readonly ISessionService _sessionService = sessionService;
    private string _connectionString => _sessionService.GetConnectionString().Length > 0
        ? _sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");
    private readonly ILogger<InformesIaService> _logger = logger;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly InformesIaHistoryStore _historyStore = historyStore;
    private readonly InformesIaResultStore _resultStore = resultStore;

    public Task<IReadOnlyList<InformeIaSuggestionDto>> GetSuggestionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Suggestions);

    public Task<IReadOnlyList<InformeIaHistoryItemDto>> GetHistoryAsync(CancellationToken cancellationToken = default)
        => _historyStore.GetAsync(GetUserKey(), cancellationToken);

    public Task DeleteHistoryItemAsync(Guid id, CancellationToken cancellationToken = default)
        => _historyStore.DeleteAsync(GetUserKey(), id, cancellationToken);

    public Task<InformeIaResultDto?> GetExecutionResultAsync(Guid executionId, CancellationToken cancellationToken = default)
        => _resultStore.GetAsync(executionId, cancellationToken);

    public async Task<InformeIaExecutionDto> ExecuteAndStoreAsync(InformeIaRequestDto request, CancellationToken cancellationToken = default)
    {
        var query = request.Consulta?.Trim() ?? string.Empty;
        var filters = request.Filtros ?? new DashboardFilters();
        var preferencias = request.Preferencias ?? new InformeIaPreferencesDto();
        var executionId = Guid.NewGuid();

        if (string.IsNullOrWhiteSpace(query))
        {
            var fail = Failure("Escribí una consulta para generar el informe.", query, filters, executionId: executionId);
            await _resultStore.SaveAsync(fail, cancellationToken);
            return new InformeIaExecutionDto { ExecutionId = executionId, UrlResultado = $"/compras/informesia/resultado/{executionId}", Resultado = fail };
        }

        var definition = await ResolveDefinitionAsync(query, filters, preferencias, cancellationToken);
        if (definition is null)
        {
            var fail = Failure(
                "No pude resolver esa consulta con las plantillas seguras disponibles. Probá reformularla usando proveedores, rubros, familias, artículos, usuarios, comprobantes o evolución.",
                query,
                filters,
                Suggestions.Take(5).ToList(),
                executionId);
            await _resultStore.SaveAsync(fail, cancellationToken);
            await SaveHistoryAsync(query, false, "sin-resolver", "Consulta no resuelta", executionId, cancellationToken);
            return new InformeIaExecutionDto { ExecutionId = executionId, UrlResultado = $"/compras/informesia/resultado/{executionId}", Resultado = fail };
        }

        if (!InformesIaSqlValidator.TryValidate(definition.Sql, out var validationMessage))
        {
            _logger.LogWarning("InformesIA rechazó SQL generado para '{Consulta}': {Motivo}", query, validationMessage);
            var fail = Failure(validationMessage, query, filters, definition.RelatedSuggestions, executionId);
            await _resultStore.SaveAsync(fail, cancellationToken);
            await SaveHistoryAsync(query, false, "rechazado", definition.Title, executionId, cancellationToken);
            return new InformeIaExecutionDto { ExecutionId = executionId, UrlResultado = $"/compras/informesia/resultado/{executionId}", Resultado = fail };
        }

        try
        {
            var queryResult = await ExecuteQueryAsync(definition, cancellationToken);
            var projected = ApplyOutputPreferences(definition, queryResult);
            var rows = projected.Rows.Select(row => new InformeIaRowDto
            {
                Values = row.Values.Select((value, index) => FormatValue(value, projected.Columns[index].Format)).ToList()
            }).ToList();

            var result = new InformeIaResultDto
            {
                ExecutionId = executionId,
                Exitoso = true,
                ConsultaOriginal = query,
                Titulo = definition.Title,
                Subtitulo = definition.Subtitle,
                TipoResultado = definition.ResultType,
                Resumen = BuildSummary(definition, projected),
                Mensaje = BuildExecutionMessage(definition.AppliedFilterNote, rows.Count == 0 ? "La consulta es válida, pero no devolvió datos con los filtros actuales." : null),
                NotaSeguridad = "InformesIA trabaja en modo solo lectura y solo consulta las vistas autorizadas del dashboard.",
                GeneradoEn = DateTime.Now,
                FiltrosAplicados = CloneFilters(definition.Filters),
                FuentesAutorizadas = definition.Sources,
                Columnas = projected.Columns,
                Filas = rows,
                Grafico = definition.IncludeChart ? BuildChart(definition, projected) : null,
                SugerenciasRelacionadas = definition.RelatedSuggestions,
                SqlGenerado = definition.Sql
            };

            await _resultStore.SaveAsync(result, cancellationToken);
            await SaveHistoryAsync(query, true, definition.ResultType, definition.Title, executionId, cancellationToken);
            return new InformeIaExecutionDto { ExecutionId = executionId, UrlResultado = $"/compras/informesia/resultado/{executionId}", Resultado = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InformesIA falló al ejecutar la consulta '{Consulta}'", query);
            var fail = Failure("No fue posible generar el informe en este momento. Revisá la consulta o ajustá los filtros.", query, filters, definition.RelatedSuggestions, executionId);
            await _resultStore.SaveAsync(fail, cancellationToken);
            await SaveHistoryAsync(query, false, definition.ResultType, definition.Title, executionId, cancellationToken);
            return new InformeIaExecutionDto { ExecutionId = executionId, UrlResultado = $"/compras/informesia/resultado/{executionId}", Resultado = fail };
        }
    }

    private async Task<QueryDefinition?> ResolveDefinitionAsync(string query, DashboardFilters filters, InformeIaPreferencesDto preferencias, CancellationToken cancellationToken)
    {
        var normalized = Normalize(query);
        var (effectiveFilters, temporalNote) = ApplyTemporalFiltersFromQuery(normalized, filters);
        var heuristic = ResolveHeuristic(normalized, effectiveFilters, preferencias);
        if (heuristic is not null)
        {
            return heuristic with { AppliedFilterNote = temporalNote };
        }

        var freeSqlDefinition = await TryBuildFreeSqlDefinitionWithOpenAiAsync(query, effectiveFilters, preferencias, cancellationToken);
        if (freeSqlDefinition is not null)
        {
            return freeSqlDefinition with { AppliedFilterNote = temporalNote };
        }

        var aiIntent = await TryResolveIntentWithOpenAiAsync(query, cancellationToken);
        var aiDefinition = aiIntent is null ? null : ResolveFromIntent(aiIntent, effectiveFilters, preferencias, normalized);
        return aiDefinition is null ? null : aiDefinition with { AppliedFilterNote = temporalNote };
    }

    private QueryDefinition? ResolveHeuristic(string normalized, DashboardFilters filters, InformeIaPreferencesDto preferencias)
    {
        if (ContainsAny(normalized, "listado", "listar", "libro iva", "columnas", "campos"))
            return ResolveFromIntent("comprobantes-ledger", filters, preferencias, normalized);
        if (ContainsAll(normalized, "proveedor", "crec") || ContainsAll(normalized, "proveedor", "variac"))
            return ResolveFromIntent("providers-growth", filters, preferencias, normalized);
        if (ContainsAny(normalized, "articulo", "articulos") && ContainsAny(normalized, "aument", "subi", "precio"))
            return ResolveFromIntent("articles-price-increase", filters, preferencias, normalized);
        if (ContainsAll(normalized, "rubro", "particip") || ContainsAll(normalized, "rubro", "concentr"))
            return ResolveFromIntent("rubros-share", filters, preferencias, normalized);
        if (ContainsAll(normalized, "familia", "crec") || ContainsAll(normalized, "familia", "variac"))
            return ResolveFromIntent("families-growth", filters, preferencias, normalized);
        if (ContainsAll(normalized, "comprobante", "importe") || ContainsAll(normalized, "comprobante", "top"))
            return ResolveFromIntent("top-comprobantes", filters, preferencias, normalized);
        if (ContainsAll(normalized, "usuario", "actividad") || ContainsAll(normalized, "usuario", "comprobante") || ContainsAll(normalized, "usuario", "carg"))
            return ResolveFromIntent("users-activity", filters, preferencias, normalized);
        if (ContainsAll(normalized, "concentr", "proveedor") || ContainsAll(normalized, "gasto", "proveedor"))
            return ResolveFromIntent("provider-concentration", filters, preferencias, normalized);
        if (ContainsAll(normalized, "evolu", "mes") || ContainsAll(normalized, "mensual", "compr"))
            return ResolveFromIntent("monthly-evolution", filters, preferencias, normalized);
        if (ContainsAll(normalized, "compar", "rubro") || ContainsAll(normalized, "rubro", "anterior"))
            return ResolveFromIntent("rubros-vs-prior", filters, preferencias, normalized);

        return null;
    }

    private QueryDefinition? ResolveFromIntent(string intent, DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var definition = intent switch
        {
            "providers-growth" => BuildProvidersGrowth(filters, preferencias, normalized),
            "articles-price-increase" => BuildArticlesPriceIncrease(filters, preferencias, normalized),
            "rubros-share" => BuildRubrosShare(filters, preferencias, normalized),
            "families-growth" => BuildFamiliesGrowth(filters, preferencias, normalized),
            "top-comprobantes" => BuildTopComprobantes(filters, preferencias, normalized),
            "users-activity" => BuildUsersActivity(filters, preferencias, normalized),
            "provider-concentration" => BuildProviderConcentration(filters, preferencias, normalized),
            "monthly-evolution" => BuildMonthlyEvolution(filters, preferencias, normalized),
            "rubros-vs-prior" => BuildRubrosVsPrior(filters, preferencias, normalized),
            "comprobantes-ledger" => BuildComprobantesLedger(filters, preferencias, normalized),
            _ => null
        };

        return definition is null ? null : ApplyUserPreferences(definition, preferencias, normalized);
    }

    private sealed record QueryDefinition(
        string IntentKey,
        string Title,
        string Subtitle,
        string ResultType,
        string Sql,
        DashboardFilters Filters,
        DashboardFilters? PriorFilters,
        List<InformeIaColumnDto> Columns,
        string ChartType,
        string ChartLabelColumn,
        string ChartValueColumn,
        string ChartFormat,
        bool IncludeChart,
        IReadOnlyDictionary<string, string> FieldAliases,
        IReadOnlyList<string> Sources,
        IReadOnlyList<InformeIaSuggestionDto> RelatedSuggestions,
        string? SortField = null,
        bool SortDescending = true,
        IReadOnlyList<string>? RequestedFields = null,
        string? AppliedFilterNote = null);

    private sealed record RawQueryResult(List<InformeIaColumnDto> Columns, List<RawRow> Rows);
    private sealed record RawRow(object?[] Values);
}
