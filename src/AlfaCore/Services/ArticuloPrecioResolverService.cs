using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text;

namespace AlfaCore.Services;

/// <summary>
/// Implementación extraída tal cual de CrmCotizacionService.ResolvePricingContextInternalAsync/
/// SearchArticulosCoreAsync/SplitPrice (sin cambios de comportamiento) -- CrmCotizacionService
/// pasa a delegar acá, y Cotizaciones usa el mismo servicio.
/// </summary>
public sealed class ArticuloPrecioResolverService : IArticuloPrecioResolverService
{
    public async Task<CrmCotizacionPricingContextDto> ResolveContextAsync(SqlConnection cn, string? clienteCodigo, CancellationToken ct, SqlTransaction? tx = null)
    {
        var usaListas = ParseBool(await ReadConfigAsync(cn, "UsaListasDePrecios", ct, tx));
        var maestroConIva = ParseBool(await ReadConfigAsync(cn, "MaestroArticuloConIVA", ct, tx));
        var fijaListasConIva = ParseBool(await ReadConfigAsync(cn, "FIJALISTASCONIVA", ct, tx));
        var fijaListasSinIva = ParseBool(await ReadConfigAsync(cn, "FIJALISTASSINIVA", ct, tx));
        var claseDefaultRaw = await ReadConfigAsync(cn, "CLASEPRECIOVENTA", ct, tx);
        if (string.IsNullOrWhiteSpace(claseDefaultRaw))
            claseDefaultRaw = await ReadConfigAsync(cn, "ClaseDePrecioDefault", ct, tx);
        var claseDefault = int.TryParse(claseDefaultRaw, out var cd) && cd is >= 1 and <= 8 ? cd : 1;

        var codigo = (clienteCodigo ?? string.Empty).Trim();
        var esConsumidorFinal = false;
        if (string.IsNullOrWhiteSpace(codigo))
        {
            codigo = (await ReadConfigAsync(cn, "CUENTACONSUMIDORFINAL", ct, tx)).Trim();
            esConsumidorFinal = true;
        }

        var cliente = string.IsNullOrWhiteSpace(codigo)
            ? null
            : await cn.QueryFirstOrDefaultAsync<ClienteListaRow>(new CommandDefinition("""
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(IdLista)), '') AS IdLista,
                    ISNULL(Clase, 0) AS Clase,
                    ISNULL(RAZON_SOCIAL, '') AS Nombre
                FROM dbo.Vt_Clientes
                WHERE LTRIM(RTRIM(Codigo)) = @Codigo;
                """, new { Codigo = codigo }, tx, cancellationToken: ct));

        var idLista = cliente?.IdLista?.Trim() ?? string.Empty;
        var clase = cliente is { Clase: >= 1 and <= 8 } ? cliente.Clase : claseDefault;
        var usaLista = usaListas && !string.IsNullOrWhiteSpace(idLista);
        // Listas: si FIJALISTASCONIVA está prendido, los precios de lista ya incluyen IVA
        // (FIJALISTASSINIVA es el flag opuesto, que deja el default en false). Maestro: MaestroArticuloConIVA.
        _ = fijaListasSinIva;
        var preciosConIva = usaLista ? fijaListasConIva : maestroConIva;

        return new CrmCotizacionPricingContextDto
        {
            ClienteCodigo = codigo,
            ClienteNombre = cliente?.Nombre?.Trim() ?? string.Empty,
            EsConsumidorFinal = esConsumidorFinal,
            IdLista = idLista,
            ClasePrecio = clase,
            PreciosConIva = preciosConIva,
            UsaListas = usaLista
        };
    }

    public async Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosAsync(SqlConnection cn, CrmCotizacionPricingContextDto pricing, string texto, int take, CancellationToken ct)
    {
        var clase = Math.Clamp(pricing.ClasePrecio, 1, 8);
        var limit = Math.Clamp(take, 1, 100);

        var palabras = (texto ?? string.Empty).ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var wordFilters = new StringBuilder();
        var parameters = new DynamicParameters();
        parameters.Add("IdLista", pricing.IdLista);
        parameters.Add("Take", limit);
        for (var i = 0; i < palabras.Length; i++)
        {
            parameters.Add($"Like{i}", $"%{palabras[i]}%");
            wordFilters.Append($"""

              AND (
                    UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @Like{i}
                    OR UPPER(LTRIM(RTRIM(a.DESCRIPCION))) LIKE @Like{i}
                    OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, '')))) LIKE @Like{i}
              )
            """);
        }

        var sql = $"""
            SELECT TOP (@Take)
                LTRIM(RTRIM(a.IDARTICULO)) AS IdArticulo,
                ISNULL(LTRIM(RTRIM(a.CODIGOBARRA)), '') AS Codigo,
                ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS Descripcion,
                ISNULL(CAST(a.TasaIVA AS decimal(9,4)), 0) AS TasaIva,
                ISNULL(a.PRECIO{clase}, 0) AS PrecioMaestro,
                ISNULL(p.Precio{clase}, 0) AS PrecioLista
            FROM dbo.V_MA_ARTICULOS a
            LEFT JOIN dbo.V_MA_Precios p
                ON p.IdArticulo = a.IDARTICULO
               AND p.IdLista = @IdLista
               AND p.TipoLista = 'V'
            WHERE ISNULL(a.Suspendido, 0) <> 1
              AND ISNULL(a.SuspendidoV, 0) <> 1
              {wordFilters}
            ORDER BY a.DESCRIPCION, a.IDARTICULO;
            """;

        var rows = await cn.QueryAsync<ArticuloPrecioRow>(new CommandDefinition(sql, parameters, cancellationToken: ct));

        var result = new List<CrmCotizacionArticuloDto>();
        foreach (var row in rows)
        {
            var bruto = pricing.UsaListas && row.PrecioLista > 0 ? row.PrecioLista : row.PrecioMaestro;
            var (neto, conIva) = SplitPrice(bruto, row.TasaIva, pricing.PreciosConIva);
            result.Add(new CrmCotizacionArticuloDto
            {
                IdArticulo = row.IdArticulo,
                Codigo = row.Codigo,
                Descripcion = row.Descripcion,
                TasaIva = decimal.Round(row.TasaIva, 4),
                PrecioUnitarioNeto = neto,
                PrecioUnitarioConIva = conIva
            });
        }

        return result;
    }

    public static (decimal neto, decimal conIva) SplitPrice(decimal bruto, decimal tasa, bool preciosConIva)
    {
        var factor = 1m + tasa / 100m;
        if (preciosConIva)
        {
            var neto = factor == 0 ? bruto : decimal.Round(bruto / factor, 4);
            return (neto, decimal.Round(bruto, 4));
        }
        return (decimal.Round(bruto, 4), decimal.Round(bruto * factor, 4));
    }

    private static async Task<string> ReadConfigAsync(SqlConnection cn, string clave, CancellationToken ct, SqlTransaction? tx = null)
    {
        var value = await cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT TOP (1)
                CASE
                    WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR))
                    WHEN ISNULL(CAST(ValorAux AS nvarchar(150)), '') <> '' THEN LTRIM(RTRIM(CAST(ValorAux AS nvarchar(150))))
                    ELSE ''
                END
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """, new { Clave = clave.ToUpperInvariant() }, tx, cancellationToken: ct));
        return value ?? string.Empty;
    }

    private static bool ParseBool(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();
        return v is "1" or "S" or "SI" or "SÍ" or "TRUE" or "T" or "Y";
    }

    private sealed class ArticuloPrecioRow
    {
        public string IdArticulo { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal TasaIva { get; set; }
        public decimal PrecioMaestro { get; set; }
        public decimal PrecioLista { get; set; }
    }

    private sealed class ClienteListaRow
    {
        public string IdLista { get; set; } = string.Empty;
        public int Clase { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }
}
