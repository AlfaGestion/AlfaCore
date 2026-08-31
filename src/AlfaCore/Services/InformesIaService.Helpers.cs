using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed partial class InformesIaService
{
    private static InformeIaChartDto? BuildChart(QueryDefinition definition, RawQueryResult result)
    {
        if (result.Rows.Count == 0 || string.Equals(definition.ChartType, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var labelIndex = result.Columns.FindIndex(x => x.Key == definition.ChartLabelColumn);
        var valueIndex = result.Columns.FindIndex(x => x.Key == definition.ChartValueColumn);
        if (labelIndex < 0 || valueIndex < 0)
        {
            return null;
        }

        if (string.Equals(definition.ChartType, "line", StringComparison.OrdinalIgnoreCase))
        {
            return new InformeIaChartDto
            {
                Tipo = "line",
                Titulo = definition.Title,
                Subtitulo = "Representación gráfica del informe",
                Formato = definition.ChartFormat,
                Linea = result.Rows.Select(row => new MonthlyPointDto
                {
                    Periodo = Convert.ToString(row.Values[labelIndex], CultureInfo.InvariantCulture) ?? string.Empty,
                    Total = ConvertToDecimal(row.Values[valueIndex])
                }).Reverse().ToList()
            };
        }

        return new InformeIaChartDto
        {
            Tipo = "bar",
            Titulo = definition.Title,
            Subtitulo = "Representación gráfica del informe",
            Formato = definition.ChartFormat,
            Barras = result.Rows.Select(row => new CategoryTotalDto
            {
                Categoria = Convert.ToString(row.Values[labelIndex], CultureInfo.InvariantCulture) ?? string.Empty,
                Total = ConvertToDecimal(row.Values[valueIndex])
            }).ToList()
        };
    }

    private static string BuildSummary(QueryDefinition definition, RawQueryResult result)
    {
        if (result.Rows.Count == 0)
        {
            return "No se encontraron registros para la consulta y los filtros seleccionados.";
        }

        var firstRow = result.Rows[0];
        return definition.IntentKey switch
        {
            "providers-growth" => $"El proveedor con mejor variación es {AsText(result, firstRow, "Proveedor")} con {AsText(result, firstRow, "Variacion")} respecto del período anterior.",
            "articles-price-increase" => $"El mayor aumento detectado corresponde a {AsText(result, firstRow, "Articulo")}, con una variación de {AsText(result, firstRow, "Variacion")}.",
            "rubros-share" => $"El rubro líder es {AsText(result, firstRow, "Rubro")}, que concentra {AsText(result, firstRow, "Participacion")} del gasto analizado.",
            "families-growth" => $"La familia con mayor crecimiento es {AsText(result, firstRow, "Descripcion")} con una variación de {AsText(result, firstRow, "Variacion")}.",
            "top-comprobantes" => $"El comprobante más alto del período es {AsText(result, firstRow, "Comprobante")} por {AsText(result, firstRow, "Importe")}.",
            "users-activity" => $"El usuario con más actividad es {AsText(result, firstRow, "Usuario")} con {AsText(result, firstRow, "Comprobantes")} comprobantes.",
            "provider-concentration" => $"El proveedor con mayor peso en el gasto es {AsText(result, firstRow, "Proveedor")} con {AsText(result, firstRow, "Participacion")} de participación.",
            "monthly-evolution" => $"La serie mensual muestra {result.Rows.Count} puntos y el último período disponible es {AsText(result, firstRow, "Periodo")}.",
            "rubros-vs-prior" => $"El rubro con mejor variación es {AsText(result, firstRow, "Rubro")} con {AsText(result, firstRow, "Variacion")} frente al período anterior.",
            "free-sql" => $"Se obtuvo un resultado dinámico de {result.Rows.Count} fila(s) usando las vistas autorizadas del dashboard.",
            _ => $"Se obtuvieron {result.Rows.Count} filas para el informe."
        };
    }

    private async Task<string?> TryResolveIntentWithOpenAiAsync(string query, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "gpt-4o-mini";
            }

            var payload = BuildOpenAiChatPayload(
                model,
                new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Clasificá la consulta dentro de una sola clave. Opciones: providers-growth, articles-price-increase, rubros-share, families-growth, top-comprobantes, users-activity, provider-concentration, monthly-evolution, rubros-vs-prior, unsupported. Respondé solo con la clave."
                    },
                    new
                    {
                        role = "user",
                        content = query
                    }
                },
                0);

            using var response = await client.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("InformesIA no pudo clasificar con OpenAI. Status {StatusCode}", response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim().ToLowerInvariant();
            return string.IsNullOrWhiteSpace(content) || content == "unsupported" ? null : content;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "InformesIA siguió sin OpenAI para la consulta '{Consulta}'", query);
            return null;
        }
    }

    private async Task<QueryDefinition?> TryBuildFreeSqlDefinitionWithOpenAiAsync(
        string query,
        DashboardFilters filters,
        InformeIaPreferencesDto preferencias,
        CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "gpt-4o-mini";
            }

            var payload = BuildOpenAiChatPayload(
                model,
                new object[]
                {
                    new
                    {
                        role = "system",
                        content = $$"""
                        Generá una consulta SQL Server de solo lectura para un dashboard de compras.

                        Reglas obligatorias:
                        - Respondé solo JSON válido.
                        - Generá una sola consulta que comience con SELECT.
                        - No uses INSERT, UPDATE, DELETE, DROP, ALTER, EXEC, MERGE, INTO, procedimientos, comentarios ni múltiples sentencias.
                        - Solo podés leer y hacer JOIN entre estas vistas:
                          - vw_compras_cabecera_dashboard
                          - vw_compras_detalle_dashboard
                          - vw_estadisticas_ingresos_diarias
                          - vw_familias_jerarquia
                        - Usá TOP (40) como máximo.
                        - Aplicá los filtros del dashboard usando estos parámetros cuando correspondan:
                          @CurFechaDesde, @CurFechaHasta, @CurProveedor, @CurArticulo, @CurArticuloCodigo,
                          @CurArticuloDescripcion, @CurRubro, @CurFamilia, @CurUsuario, @CurSucursal,
                          @CurDeposito, @CurEstado, @CurTipoComprobante
                        - Para cabecera usá este patrón base en WHERE cuando leas c = vw_compras_cabecera_dashboard:
                          {{BuildHeaderFilterClause("c", "Cur")}}
                        - Para detalle usá este patrón base en WHERE cuando leas d = vw_compras_detalle_dashboard:
                          {{BuildDetailFilterClause("d", "Cur")}}

                        Columnas útiles:
                        - vw_compras_cabecera_dashboard: FECHA, CUENTA, RAZON_SOCIAL, SUCURSAL, USUARIO, TC, IDCOMPROBANTE, EstadoComprobante, IdDeposito, NetoDashboard, IvaDashboard, ImporteDashboard
                        - vw_compras_detalle_dashboard: FECHA, CUENTA, RAZON_SOCIAL, SUCURSAL, USUARIO, TC, IDCOMPROBANTE, IDARTICULO, DESCRIPCION_ARTICULO, RUBRO, FAMILIA, COSTO, CANTIDAD, TotalDashboard, IdDeposito
                        - vw_estadisticas_ingresos_diarias: FECHA, TOTAL, CANTIDAD
                        - vw_familias_jerarquia: IdFamilia, Descripcion, PadreIdFamilia, DescripcionPadre, Nivel

                        Formato de respuesta JSON:
                        {
                          "title": "título corto",
                          "subtitle": "subtítulo corto",
                          "sql": "SELECT TOP (40) ...",
                          "sources": ["vw_compras_detalle_dashboard"],
                          "resultType": "consulta"
                        }

                        Si la consulta no puede resolverse de forma segura con esas vistas, devolvé:
                        { "unsupported": true }
                        """
                    },
                    new
                    {
                        role = "user",
                        content = $$"""
                        Consulta del usuario: {{query}}

                        Filtros actuales:
                        - FechaDesde: {{filters.FechaDesde?.ToString("yyyy-MM-dd") ?? "null"}}
                        - FechaHasta: {{filters.FechaHasta?.ToString("yyyy-MM-dd") ?? "null"}}
                        - Proveedor: {{filters.Proveedor ?? "null"}}
                        - Articulo: {{filters.Articulo ?? "null"}}
                        - ArticuloCodigo: {{filters.ArticuloCodigo ?? "null"}}
                        - ArticuloDescripcion: {{filters.ArticuloDescripcion ?? "null"}}
                        - Rubro: {{filters.Rubro ?? "null"}}
                        - Familia: {{filters.Familia ?? "null"}}
                        - Usuario: {{filters.Usuario ?? "null"}}
                        - Sucursal: {{filters.Sucursal ?? "null"}}
                        - Deposito: {{filters.Deposito ?? "null"}}
                        - Estado: {{filters.Estado ?? "null"}}
                        - TipoComprobante: {{filters.TipoComprobante ?? "null"}}
                        """
                    }
                },
                0,
                new { type = "json_object" });

            using var response = await client.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                JsonContent.Create(payload),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("InformesIA no pudo generar SQL libre con OpenAI. Status {StatusCode}", response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            using var generated = JsonDocument.Parse(content);
            if (generated.RootElement.TryGetProperty("unsupported", out var unsupported)
                && unsupported.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            var sql = generated.RootElement.TryGetProperty("sql", out var sqlProperty)
                ? sqlProperty.GetString()?.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(sql))
            {
                return null;
            }

            var sources = generated.RootElement.TryGetProperty("sources", out var sourcesProperty) && sourcesProperty.ValueKind == JsonValueKind.Array
                ? sourcesProperty.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

            var definition = new QueryDefinition(
                "free-sql",
                generated.RootElement.TryGetProperty("title", out var titleProperty) ? titleProperty.GetString()?.Trim() ?? "Consulta dinámica" : "Consulta dinámica",
                generated.RootElement.TryGetProperty("subtitle", out var subtitleProperty) ? subtitleProperty.GetString()?.Trim() ?? "Resultado generado sobre vistas autorizadas" : "Resultado generado sobre vistas autorizadas",
                generated.RootElement.TryGetProperty("resultType", out var resultTypeProperty) ? resultTypeProperty.GetString()?.Trim() ?? "consulta" : "consulta",
                sql,
                filters,
                null,
                [],
                "none",
                string.Empty,
                string.Empty,
                "texto",
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                sources,
                Suggestions.Take(5).ToList());

            return ApplyUserPreferences(definition, preferencias, Normalize(query));
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "InformesIA no pudo generar SQL libre con OpenAI para '{Consulta}'", query);
            return null;
        }
    }

    private async Task SaveHistoryAsync(string query, bool success, string resultType, string title, Guid executionId, CancellationToken cancellationToken)
    {
        await _historyStore.AppendAsync(GetUserKey(), new InformeIaHistoryItemDto
        {
            Id = Guid.NewGuid(),
            ExecutionId = executionId,
            Consulta = query,
            FechaHora = DateTime.Now,
            Exitosa = success,
            TipoResultado = resultType,
            Titulo = title
        }, cancellationToken);
    }

    private string GetUserKey()
    {
        var user = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        return string.IsNullOrWhiteSpace(user) ? "anonimo" : user.Trim();
    }

    private static DashboardFilters ComputePriorPeriodFilters(DashboardFilters filters)
    {
        var today = DateTime.Today;
        var desde = filters.FechaDesde ?? new DateTime(today.Year, today.Month, 1);
        var hasta = filters.FechaHasta ?? today;
        var span = hasta - desde;
        var antHasta = desde.AddDays(-1);
        var antDesde = antHasta - span;
        var prior = CloneFilters(filters);
        prior.FechaDesde = antDesde;
        prior.FechaHasta = antHasta;
        return prior;
    }

    private static DashboardFilters CloneFilters(DashboardFilters filters) => new()
    {
        FechaDesde = filters.FechaDesde,
        FechaHasta = filters.FechaHasta,
        Proveedor = filters.Proveedor,
        Articulo = filters.Articulo,
        ArticuloCodigo = filters.ArticuloCodigo,
        ArticuloDescripcion = filters.ArticuloDescripcion,
        Rubro = filters.Rubro,
        Familia = filters.Familia,
        Usuario = filters.Usuario,
        Sucursal = filters.Sucursal,
        Deposito = filters.Deposito,
        Estado = filters.Estado,
        TipoComprobante = filters.TipoComprobante
    };

    private static List<InformeIaColumnDto> ColumnSet(params (string Key, string Label, string Format, string Align)[] columns)
        => columns.Select(x => new InformeIaColumnDto { Key = x.Key, Label = x.Label, Format = x.Format, Align = x.Align }).ToList();

    private static IReadOnlyDictionary<string, string> AliasMap(params (string Alias, string Key)[] values)
        => values.ToDictionary(x => Normalize(x.Alias), x => x.Key, StringComparer.OrdinalIgnoreCase);

    private static bool ResolveIncludeChart(InformeIaPreferencesDto preferencias, string normalized)
    {
        if (!preferencias.IncluirGrafico)
        {
            return false;
        }

        return !(ContainsAll(normalized, "sin", "grafico")
            || ContainsAll(normalized, "no", "grafico")
            || ContainsAll(normalized, "solo", "tabla")
            || ContainsAll(normalized, "sin", "chart"));
    }

    private static RawQueryResult ApplyOutputPreferences(QueryDefinition definition, RawQueryResult result)
    {
        var columns = result.Columns;
        var rows = result.Rows.ToList();

        var sortKey = definition.SortField;
        if (!string.IsNullOrWhiteSpace(sortKey))
        {
            var sortIndex = columns.FindIndex(c => c.Key.Equals(sortKey, StringComparison.OrdinalIgnoreCase));
            if (sortIndex >= 0)
            {
                rows = definition.SortDescending
                    ? rows.OrderByDescending(row => SortComparable(row.Values[sortIndex])).ToList()
                    : rows.OrderBy(row => SortComparable(row.Values[sortIndex])).ToList();
            }
        }

        var requested = ExtractRequestedFields(definition);
        if (requested.Count > 0)
        {
            var indices = requested
                .Select(key => columns.FindIndex(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                .Where(index => index >= 0)
                .Distinct()
                .ToList();

            if (indices.Count > 0)
            {
                columns = indices.Select(index => columns[index]).ToList();
                rows = rows.Select(row => new RawRow(indices.Select(index => row.Values[index]).ToArray())).ToList();
            }
        }

        return new RawQueryResult(columns, rows);
    }

    private static List<string> ExtractRequestedFields(QueryDefinition definition)
    {
        if (definition.RequestedFields is null || definition.RequestedFields.Count == 0)
        {
            return [];
        }

        var selected = new List<string>();
        foreach (var field in definition.RequestedFields)
        {
            var normalized = Normalize(field);
            if (definition.FieldAliases.TryGetValue(normalized, out var mapped))
            {
                selected.Add(mapped);
            }
        }

        return selected;
    }

    private static IComparable SortComparable(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return string.Empty;
        }

        return value switch
        {
            DateTime dateTime => dateTime,
            decimal decimalValue => decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => doubleValue,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string? ParseSortField(string normalized, IReadOnlyDictionary<string, string> aliases)
    {
        var marker = new[] { "ordenado por ", "ordenar por ", "orden por ", "mostrar por ", "sort by ", "ordename por " }
            .FirstOrDefault(m => normalized.Contains(m, StringComparison.OrdinalIgnoreCase));

        if (marker is null)
        {
            return null;
        }

        var tail = normalized[(normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length)..];
        foreach (var alias in aliases.Keys.OrderByDescending(x => x.Length))
        {
            if (tail.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
            {
                return aliases[alias];
            }
        }

        return null;
    }

    private static bool ParseSortDescending(string normalized)
        => !ContainsAny(normalized, " asc", " ascendente", " menor a mayor", " de menor a mayor", " de mas chico a mas grande");

    private static IReadOnlyList<string> ParseRequestedFields(string normalized, IReadOnlyDictionary<string, string> aliases)
    {
        var markers = new[]
        {
            "mostrame las columnas",
            "mostrar columnas",
            "solo columnas",
            "solo los campos",
            "solo campos",
            "campos",
            "columnas"
        };

        var marker = markers.FirstOrDefault(m => normalized.Contains(m, StringComparison.OrdinalIgnoreCase));
        if (marker is null)
        {
            if (ContainsAll(normalized, "libro iva"))
            {
                return ["fecha factura", "cuenta", "nombre", "importe sin iva", "iva", "total", "usuario", "fecha y hora"];
            }

            return [];
        }

        var start = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var tail = normalized[(start + marker.Length)..];
        tail = TrimAtFirstDirective(tail);
        tail = tail.Replace(" y ", ",", StringComparison.OrdinalIgnoreCase);
        var tokens = tail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selected = new List<string>();

        foreach (var token in tokens)
        {
            foreach (var alias in aliases.Keys.OrderByDescending(x => x.Length))
            {
                if (token.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add(alias);
                    break;
                }
            }
        }

        return selected.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string TrimAtFirstDirective(string tail)
    {
        var cutMarkers = new[]
        {
            " no generes",
            " sin grafico",
            " sin gráfico",
            " no muestres",
            " no incluir",
            " sin incluir",
            " con grafico",
            " con gráfico",
            " incluir grafico",
            " incluir gráfico",
            " ordenado por ",
            " ordenar por ",
            " orden por ",
            " ordename por "
        };

        var cut = tail.Length;
        foreach (var marker in cutMarkers)
        {
            var index = tail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < cut)
            {
                cut = index;
            }
        }

        return tail[..cut].Trim();
    }

    private static QueryDefinition ApplyUserPreferences(QueryDefinition definition, InformeIaPreferencesDto preferencias, string normalized)
    {
        var requestedFields = preferencias.CamposSolicitados.Count > 0
            ? preferencias.CamposSolicitados
            : ParseRequestedFields(normalized, definition.FieldAliases);

        var explicitSort = !string.IsNullOrWhiteSpace(preferencias.OrdenCampo)
            && definition.FieldAliases.TryGetValue(Normalize(preferencias.OrdenCampo), out var preferredSortKey)
                ? preferredSortKey
                : null;

        var parsedSort = ParseSortField(normalized, definition.FieldAliases);

        return definition with
        {
            IncludeChart = definition.IncludeChart && preferencias.IncluirGrafico,
            RequestedFields = requestedFields,
            SortField = explicitSort ?? parsedSort ?? definition.SortField,
            SortDescending = explicitSort is not null ? preferencias.OrdenDescendente : (parsedSort is not null ? ParseSortDescending(normalized) : definition.SortDescending)
        };
    }

    private static string BuildExecutionMessage(string? appliedFilterNote, string? baseMessage)
    {
        if (string.IsNullOrWhiteSpace(appliedFilterNote))
        {
            return baseMessage ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(baseMessage))
        {
            return appliedFilterNote;
        }

        return $"{appliedFilterNote} {baseMessage}";
    }

    private static (DashboardFilters Filters, string? Note) ApplyTemporalFiltersFromQuery(string normalized, DashboardFilters filters)
    {
        var today = DateTime.Today;
        var currentYear = today.Year;
        var culture = CultureInfo.GetCultureInfo("es-AR");
        var monthMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["enero"] = 1,
            ["febrero"] = 2,
            ["marzo"] = 3,
            ["abril"] = 4,
            ["mayo"] = 5,
            ["junio"] = 6,
            ["julio"] = 7,
            ["agosto"] = 8,
            ["septiembre"] = 9,
            ["setiembre"] = 9,
            ["octubre"] = 10,
            ["noviembre"] = 11,
            ["diciembre"] = 12
        };

        var explicitDates = Regex.Matches(normalized, @"\b(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?\b", RegexOptions.CultureInvariant);
        if (ContainsAny(normalized, "desde ", "entre ") && explicitDates.Count >= 2)
        {
            var fromDate = ParseFlexibleDate(explicitDates[0].Value, today);
            var toDate = ParseFlexibleDate(explicitDates[1].Value, today);
            if (fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value <= toDate.Value ? fromDate.Value : toDate.Value;
                var to = fromDate.Value <= toDate.Value ? toDate.Value : fromDate.Value;
                return ApplyAbsoluteRange(filters, from, to, $"Se priorizó el rango pedido en la consulta: {from:dd/MM/yyyy} a {to:dd/MM/yyyy}.");
            }
        }

        var lastDaysMatch = Regex.Match(normalized, @"\bultimos?\s+(\d{1,3})\s+dias\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (lastDaysMatch.Success && int.TryParse(lastDaysMatch.Groups[1].Value, out var days) && days > 0)
        {
            var from = today.AddDays(-(days - 1));
            return ApplyAbsoluteRange(filters, from, today, $"Se priorizó el período pedido en la consulta: últimos {days} días.");
        }

        var yearMatch = Regex.Match(normalized, @"\ba[nñ]o\s+(20\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var requestedYear))
        {
            return ApplyAbsoluteRange(filters, new DateTime(requestedYear, 1, 1), new DateTime(requestedYear, 12, 31), $"Se priorizó el período pedido en la consulta: año {requestedYear}.");
        }

        var quarter = ParseQuarter(normalized);
        if (quarter.HasValue)
        {
            var quarterYear = ParseYearFromText(normalized) ?? currentYear;
            var startMonth = (quarter.Value - 1) * 3 + 1;
            var from = new DateTime(quarterYear, startMonth, 1);
            var to = new DateTime(quarterYear, startMonth + 2, DateTime.DaysInMonth(quarterYear, startMonth + 2));
            return ApplyAbsoluteRange(filters, from, to, $"Se priorizó el período pedido en la consulta: {OrdinalQuarter(quarter.Value)} trimestre de {quarterYear}.");
        }

        var monthRange = ParseMonthRange(normalized, monthMap, currentYear, filters);
        if (monthRange is not null)
        {
            return monthRange.Value;
        }

        if (ContainsAll(normalized, "mes", "pasado") || ContainsAll(normalized, "mes", "anterior"))
        {
            var baseDate = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            return ApplyAbsoluteRange(filters, new DateTime(baseDate.Year, baseDate.Month, 1), new DateTime(baseDate.Year, baseDate.Month, DateTime.DaysInMonth(baseDate.Year, baseDate.Month)),
                $"Se priorizó el período pedido en la consulta: {culture.DateTimeFormat.GetMonthName(baseDate.Month)} de {baseDate.Year}.");
        }

        if (ContainsAll(normalized, "este", "mes") || ContainsAll(normalized, "mes", "actual"))
        {
            return ApplyAbsoluteRange(filters, new DateTime(today.Year, today.Month, 1), today,
                $"Se priorizó el período pedido en la consulta: {culture.DateTimeFormat.GetMonthName(today.Month)} de {today.Year}.");
        }

        foreach (var month in monthMap)
        {
            var match = Regex.Match(normalized, $@"\b{month.Key}\b(?:\s+de)?\s*(20\d{{2}})?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            var year = currentYear;
            if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var parsedYear))
            {
                year = parsedYear;
            }
            else if (month.Value > today.Month && normalized.Contains("mes de", StringComparison.OrdinalIgnoreCase))
            {
                year = currentYear - 1;
            }

            var from = new DateTime(year, month.Value, 1);
            var to = new DateTime(year, month.Value, DateTime.DaysInMonth(year, month.Value));
            var monthName = culture.DateTimeFormat.GetMonthName(month.Value);
            return ApplyAbsoluteRange(filters, from, to, $"Se priorizó el período pedido en la consulta: {monthName} de {year}.");
        }

        return (filters, null);
    }

    private static (DashboardFilters Filters, string? Note) ApplyAbsoluteRange(DashboardFilters original, DateTime from, DateTime to, string note)
    {
        var effective = CloneFilters(original);
        effective.FechaDesde = from;
        effective.FechaHasta = to;
        return (effective, note);
    }

    private static DateTime? ParseFlexibleDate(string rawValue, DateTime today)
    {
        var parts = rawValue.Split('/');
        if (parts.Length < 2)
        {
            return null;
        }

        if (!int.TryParse(parts[0], out var day) || !int.TryParse(parts[1], out var month))
        {
            return null;
        }

        var year = today.Year;
        if (parts.Length >= 3 && int.TryParse(parts[2], out var parsedYear))
        {
            year = parsedYear < 100 ? 2000 + parsedYear : parsedYear;
        }

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        return new DateTime(year, month, day);
    }

    private static int? ParseYearFromText(string normalized)
    {
        var yearMatch = Regex.Match(normalized, @"\b(20\d{2})\b", RegexOptions.CultureInvariant);
        return yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year) ? year : null;
    }

    private static int? ParseQuarter(string normalized)
    {
        if (ContainsAny(normalized, "primer trimestre", "1er trimestre", "1º trimestre", "q1"))
            return 1;
        if (ContainsAny(normalized, "segundo trimestre", "2do trimestre", "2º trimestre", "q2"))
            return 2;
        if (ContainsAny(normalized, "tercer trimestre", "3er trimestre", "3º trimestre", "q3"))
            return 3;
        if (ContainsAny(normalized, "cuarto trimestre", "4to trimestre", "4º trimestre", "q4"))
            return 4;

        return null;
    }

    private static string OrdinalQuarter(int quarter)
        => quarter switch
        {
            1 => "primer",
            2 => "segundo",
            3 => "tercer",
            4 => "cuarto",
            _ => quarter.ToString(CultureInfo.InvariantCulture)
        };

    private static (DashboardFilters Filters, string? Note)? ParseMonthRange(string normalized, IReadOnlyDictionary<string, int> monthMap, int currentYear, DashboardFilters originalFilters)
    {
        var monthList = monthMap.Keys.OrderByDescending(x => x.Length).ToArray();
        var pattern = $@"\b(?:desde\s+)?({string.Join("|", monthList.Select(Regex.Escape))})(?:\s+de\s+(20\d{{2}}))?\s+(?:hasta|a)\s+({string.Join("|", monthList.Select(Regex.Escape))})(?:\s+de\s+(20\d{{2}}))?\b";
        var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var fromMonthName = match.Groups[1].Value;
        var toMonthName = match.Groups[3].Value;
        if (!monthMap.TryGetValue(fromMonthName, out var fromMonth) || !monthMap.TryGetValue(toMonthName, out var toMonth))
        {
            return null;
        }

        var fromYear = match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out var parsedFromYear) ? parsedFromYear : currentYear;
        var toYear = match.Groups[4].Success && int.TryParse(match.Groups[4].Value, out var parsedToYear) ? parsedToYear : fromYear;

        if (!match.Groups[2].Success && !match.Groups[4].Success && fromMonth > toMonth)
        {
            toYear = currentYear;
            fromYear = currentYear - 1;
        }

        var from = new DateTime(fromYear, fromMonth, 1);
        var to = new DateTime(toYear, toMonth, DateTime.DaysInMonth(toYear, toMonth));
        if (from > to)
        {
            (from, to) = (to, from);
        }

        var culture = CultureInfo.GetCultureInfo("es-AR");
        return ApplyAbsoluteRange(originalFilters, from, to, $"Se priorizó el rango pedido en la consulta: {culture.DateTimeFormat.GetMonthName(from.Month)} de {from.Year} a {culture.DateTimeFormat.GetMonthName(to.Month)} de {to.Year}.");
    }

    private static string BuildHeaderFilterClause(string alias, string prefix) => $"""
        (@{prefix}FechaDesde IS NULL OR {alias}.FECHA >= @{prefix}FechaDesde)
        AND (@{prefix}FechaHasta IS NULL OR {alias}.FECHA < DATEADD(day, 1, @{prefix}FechaHasta))
        AND (@{prefix}Proveedor IS NULL OR {alias}.CUENTA LIKE '%' + @{prefix}Proveedor + '%' OR {alias}.RAZON_SOCIAL LIKE '%' + @{prefix}Proveedor + '%')
        AND (@{prefix}Usuario IS NULL OR {alias}.USUARIO = @{prefix}Usuario)
        AND (@{prefix}Sucursal IS NULL OR {alias}.SUCURSAL = @{prefix}Sucursal)
        AND (@{prefix}Deposito IS NULL OR CONVERT(varchar(20), {alias}.IdDeposito) = @{prefix}Deposito)
        AND (@{prefix}Estado IS NULL OR {alias}.EstadoComprobante = @{prefix}Estado)
        AND (@{prefix}TipoComprobante IS NULL OR {alias}.TC = @{prefix}TipoComprobante)
        AND (
            (@{prefix}Articulo IS NULL AND @{prefix}ArticuloCodigo IS NULL AND @{prefix}ArticuloDescripcion IS NULL AND @{prefix}Rubro IS NULL AND @{prefix}Familia IS NULL)
            OR EXISTS (
                SELECT 1
                FROM vw_compras_detalle_dashboard detailFilter
                WHERE detailFilter.TC = {alias}.TC
                  AND detailFilter.IDCOMPROBANTE = {alias}.IDCOMPROBANTE
                  AND detailFilter.CUENTA = {alias}.CUENTA
                  AND (@{prefix}Articulo IS NULL OR LTRIM(RTRIM(detailFilter.IDARTICULO)) = LTRIM(RTRIM(@{prefix}Articulo)))
                  AND (@{prefix}ArticuloCodigo IS NULL OR LTRIM(RTRIM(detailFilter.IDARTICULO)) = LTRIM(RTRIM(@{prefix}ArticuloCodigo)))
                  AND (@{prefix}ArticuloDescripcion IS NULL OR detailFilter.DESCRIPCION_ARTICULO LIKE '%' + @{prefix}ArticuloDescripcion + '%')
                  AND (@{prefix}Rubro IS NULL OR detailFilter.RUBRO = @{prefix}Rubro)
                  AND (@{prefix}Familia IS NULL OR detailFilter.FAMILIA = @{prefix}Familia)
            )
        )
        """;

    private static string BuildDetailFilterClause(string alias, string prefix) => $"""
        (@{prefix}FechaDesde IS NULL OR {alias}.FECHA >= @{prefix}FechaDesde)
        AND (@{prefix}FechaHasta IS NULL OR {alias}.FECHA < DATEADD(day, 1, @{prefix}FechaHasta))
        AND (@{prefix}Proveedor IS NULL OR {alias}.CUENTA = @{prefix}Proveedor)
        AND (@{prefix}Articulo IS NULL OR LTRIM(RTRIM({alias}.IDARTICULO)) = LTRIM(RTRIM(@{prefix}Articulo)))
        AND (@{prefix}ArticuloCodigo IS NULL OR LTRIM(RTRIM({alias}.IDARTICULO)) = LTRIM(RTRIM(@{prefix}ArticuloCodigo)))
        AND (@{prefix}ArticuloDescripcion IS NULL OR {alias}.DESCRIPCION_ARTICULO LIKE '%' + @{prefix}ArticuloDescripcion + '%')
        AND (@{prefix}Rubro IS NULL OR {alias}.RUBRO = @{prefix}Rubro)
        AND (@{prefix}Familia IS NULL OR {alias}.FAMILIA = @{prefix}Familia)
        AND (@{prefix}Usuario IS NULL OR {alias}.USUARIO = @{prefix}Usuario)
        AND (@{prefix}Sucursal IS NULL OR {alias}.SUCURSAL = @{prefix}Sucursal)
        AND (@{prefix}Deposito IS NULL OR CONVERT(varchar(20), {alias}.IdDeposito) = @{prefix}Deposito)
        AND (@{prefix}TipoComprobante IS NULL OR {alias}.TC = @{prefix}TipoComprobante)
        """;

    private static void AddFilterParameters(SqlCommand command, DashboardFilters filters, string prefix)
    {
        command.Parameters.AddWithValue($"@{prefix}FechaDesde", (object?)filters.FechaDesde ?? DBNull.Value);
        command.Parameters.AddWithValue($"@{prefix}FechaHasta", (object?)filters.FechaHasta ?? DBNull.Value);
        command.Parameters.AddWithValue($"@{prefix}Proveedor", ToDbValue(filters.Proveedor));
        command.Parameters.AddWithValue($"@{prefix}Articulo", ToDbValue(filters.Articulo));
        command.Parameters.AddWithValue($"@{prefix}ArticuloCodigo", ToDbValue(filters.ArticuloCodigo));
        command.Parameters.AddWithValue($"@{prefix}ArticuloDescripcion", ToDbValue(filters.ArticuloDescripcion));
        command.Parameters.AddWithValue($"@{prefix}Rubro", ToDbValue(filters.Rubro));
        command.Parameters.AddWithValue($"@{prefix}Familia", ToDbValue(filters.Familia));
        command.Parameters.AddWithValue($"@{prefix}Usuario", ToDbValue(filters.Usuario));
        command.Parameters.AddWithValue($"@{prefix}Sucursal", ToDbValue(filters.Sucursal));
        command.Parameters.AddWithValue($"@{prefix}Deposito", ToDbValue(filters.Deposito));
        command.Parameters.AddWithValue($"@{prefix}Estado", ToDbValue(filters.Estado));
        command.Parameters.AddWithValue($"@{prefix}TipoComprobante", ToDbValue(filters.TipoComprobante));
    }

    private static object ToDbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static bool ContainsAll(string text, params string[] keywords) => keywords.All(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsAny(string text, params string[] keywords) => keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, object?> BuildOpenAiChatPayload(
        string model,
        object[] messages,
        double? temperature = null,
        object? responseFormat = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages
        };

        if (responseFormat is not null)
            payload["response_format"] = responseFormat;

        if (temperature.HasValue && SupportsCustomTemperature(model))
            payload["temperature"] = temperature.Value;

        return payload;
    }

    private static bool SupportsCustomTemperature(string? model)
        => string.IsNullOrWhiteSpace(model)
           || !model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value)
    {
        var normalized = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private static decimal ConvertToDecimal(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return 0m;
        }

        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
            float floatValue => Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture),
            int intValue => intValue,
            long longValue => longValue,
            _ => decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m
        };
    }

    private static string FormatValue(object? value, string format)
    {
        if (value is null || value == DBNull.Value)
        {
            return "Sin dato";
        }

        var culture = CultureInfo.GetCultureInfo("es-AR");
        return format switch
        {
            "moneda" => ConvertToDecimal(value).ToString("$ #,##0.00", culture),
            "numero" => ConvertToDecimal(value).ToString("#,##0", culture),
            "porcentaje" => $"{(ConvertToDecimal(value) * 100m).ToString("#,##0.0", culture)}%",
            "fecha" when value is DateTime dateTime => dateTime.ToString("dd/MM/yyyy"),
            "fechahora" when value is DateTime dateTime => dateTime.ToString("dd/MM/yyyy HH:mm"),
            "fecha" when DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsedDate) => parsedDate.ToString("dd/MM/yyyy"),
            "fechahora" when DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsedDateTime) => parsedDateTime.ToString("dd/MM/yyyy HH:mm"),
            _ => Convert.ToString(value, culture) ?? string.Empty
        };
    }

    private static string AsText(RawQueryResult result, RawRow row, string key)
    {
        var index = result.Columns.FindIndex(column => column.Key == key);
        return index >= 0 ? FormatValue(row.Values[index], result.Columns[index].Format) : "sin dato";
    }

    private static InformeIaResultDto Failure(string message, string query, DashboardFilters filters, IReadOnlyList<InformeIaSuggestionDto>? suggestions = null, Guid executionId = default)
        => new()
        {
            ExecutionId = executionId,
            Exitoso = false,
            ConsultaOriginal = query,
            Titulo = "Informes IA",
            Subtitulo = "Consulta no resuelta",
            Mensaje = message,
            NotaSeguridad = "Solo se permiten consultas de lectura sobre las vistas autorizadas del dashboard.",
            FiltrosAplicados = CloneFilters(filters),
            FuentesAutorizadas = ["vw_compras_cabecera_dashboard", "vw_compras_detalle_dashboard", "vw_estadisticas_ingresos_diarias", "vw_familias_jerarquia"],
            SugerenciasRelacionadas = suggestions ?? Suggestions.Take(5).ToList()
        };
}
