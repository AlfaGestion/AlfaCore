using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Acciones por lote del listado de articulos: aumento %, asignar rubro, marca, familia o utilidad.
// Se graba articulo por articulo (no un UPDATE masivo) por dos motivos:
//  - TR_BlockMassUpdate_Precios (presente en bases de clientes) rechaza un UPDATE que toque
//    COSTO/PRECIOS de mas de 50 filas a la vez.
//  - Con DesdeTrigger = 0, FMT_V_MA_ARTICULOS aplica la politica de precios y pasa los precios a las
//    listas igual que cuando se graba desde el editor (Ma_art.frm).
public sealed partial class ArticulosService
{
    public const int MaxArticulosLote = 5000;
    public static readonly IReadOnlyList<decimal> PasosRedondeoLote = [1m, 10m, 100m];

    public Task<IReadOnlyList<ArticuloLookupOptionDto>> GetFamiliasAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetFamilias", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.V_TA_FAMILIAS', N'U') IS NULL THEN 0 ELSE 1 END;", cancellationToken: token));
            if (existe == 0)
                return (IReadOnlyList<ArticuloLookupOptionDto>)[];
            var rows = await cn.QueryAsync<ArticuloLookupOptionDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IdFamilia)) AS Codigo, ISNULL(LTRIM(RTRIM(Descripcion)), '') AS Descripcion
                FROM dbo.V_TA_FAMILIAS
                WHERE LTRIM(RTRIM(ISNULL(IdFamilia, ''))) <> ''
                ORDER BY LTRIM(RTRIM(IdFamilia));
                """, cancellationToken: token));
            return (IReadOnlyList<ArticuloLookupOptionDto>)rows.ToList();
        }, "No se pudieron cargar las familias de articulos.", ct);

    public Task<ArticuloLoteResultado> AplicarLoteAsync(ArticuloLoteRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("AplicarLote", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var codigos = request.Codigos
                .Select(c => (c ?? string.Empty).Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (codigos.Count == 0)
                throw new InvalidOperationException("Selecciona al menos un articulo.");
            if (codigos.Count > MaxArticulosLote)
                throw new InvalidOperationException($"Se pueden procesar hasta {MaxArticulosLote} articulos por vez.");
            if (request.Redondeo != 0 && !PasosRedondeoLote.Contains(request.Redondeo))
                throw new InvalidOperationException("Elegi un redondeo de la lista.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var cfg = await ReadConfigMapAsync(cn, ["RETAIL", "MAESTROARTICULOCONIVA", "PIVA", "ClasePrecioVenta"], token);
            var retail = ParseBool(Get(cfg, "RETAIL"));

            var accion = (request.Accion ?? string.Empty).Trim().ToLowerInvariant();
            // Validaciones antes de abrir la transaccion.
            string? codigoAsignar = null;
            switch (accion)
            {
                case ArticuloLoteAcciones.Aumento:
                    if (retail)
                        throw new InvalidOperationException("En modo Retail no se aumentan precios por porcentaje: los precios de venta salen de las listas de precios. Usa Listas de precios.");
                    if (request.Porcentaje == 0)
                        throw new InvalidOperationException("Ingresa el porcentaje de aumento.");
                    if (request.Porcentaje <= -100 || request.Porcentaje > 1000)
                        throw new InvalidOperationException("El porcentaje tiene que estar entre -99,99 y 1000.");
                    if (request.AplicarA is not (ArticuloLoteAumentoDestino.CostoYPrecios or ArticuloLoteAumentoDestino.Precios or ArticuloLoteAumentoDestino.Costo))
                        throw new InvalidOperationException("Elegi si el aumento va al costo, a los precios o a ambos.");
                    break;
                case ArticuloLoteAcciones.Utilidad:
                    if (request.Utilidad < 0 || request.Utilidad > 10000)
                        throw new InvalidOperationException("La utilidad tiene que estar entre 0 y 10000 %.");
                    break;
                case ArticuloLoteAcciones.Rubro:
                    codigoAsignar = await ValidarCodigoLookupAsync(cn, "v_ta_rubros", "IdRubro", request.Codigo, "rubro", token);
                    break;
                case ArticuloLoteAcciones.Marca:
                    codigoAsignar = await ValidarCodigoLookupAsync(cn, "v_ta_tipoArticulo", "IdTipo", request.Codigo, "marca", token);
                    break;
                case ArticuloLoteAcciones.Familia:
                    var familia = (request.Codigo ?? string.Empty).Trim();
                    codigoAsignar = familia.Length == 0 ? null : await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                        "SELECT TOP (1) IdFamilia FROM dbo.V_TA_FAMILIAS WHERE LTRIM(RTRIM(IdFamilia)) = @Codigo;",
                        new { Codigo = familia }, cancellationToken: token));
                    if (codigoAsignar is null)
                        throw new InvalidOperationException("Elegi una familia existente.");
                    break;
                default:
                    throw new InvalidOperationException("Elegi la accion a aplicar.");
            }

            var conIva = ParseBool(Get(cfg, "MAESTROARTICULOCONIVA"));
            var ivaDefault = ParseDecimal(Get(cfg, "PIVA"));
            var claseVenta = int.TryParse(Get(cfg, "ClasePrecioVenta"), out var cv) && cv is >= 1 and <= 8 ? cv : 1;
            var resultado = new ArticuloLoteResultado();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            foreach (var codigo in codigos)
            {
                var art = await cn.QuerySingleOrDefaultAsync<ArticuloLoteFila>(new CommandDefinition("""
                    SELECT TOP (1) IDARTICULO AS IdArticulo, ISNULL(COSTO, 0) AS Costo,
                        ISNULL(PRECIO1, 0) AS P1, ISNULL(PRECIO2, 0) AS P2, ISNULL(PRECIO3, 0) AS P3, ISNULL(PRECIO4, 0) AS P4,
                        ISNULL(PRECIO5, 0) AS P5, ISNULL(PRECIO6, 0) AS P6, ISNULL(PRECIO7, 0) AS P7, ISNULL(PRECIO8, 0) AS P8,
                        CAST(ISNULL(TasaIVA, 0) AS decimal(9, 4)) AS TasaIva, ISNULL(EXENTO, 0) AS Exento
                    FROM dbo.V_MA_ARTICULOS WITH (UPDLOCK)
                    WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;
                    """, new { Codigo = codigo }, tx, cancellationToken: token));
                if (art is null)
                {
                    resultado.Observaciones.Add($"{codigo}: ya no existe en la base.");
                    continue;
                }

                switch (accion)
                {
                    case ArticuloLoteAcciones.Aumento:
                    {
                        var p = request.Porcentaje;
                        var tocaCosto = request.AplicarA != ArticuloLoteAumentoDestino.Precios;
                        var tocaPrecios = request.AplicarA != ArticuloLoteAumentoDestino.Costo;
                        decimal Ajustar(decimal v, bool aplica) => aplica ? ArticuloLoteCalculo.Aumentar(v, p, request.Redondeo) : v;
                        await cn.ExecuteAsync(new CommandDefinition("""
                            UPDATE dbo.V_MA_ARTICULOS
                            SET COSTO = @Costo, PRECIO1 = @P1, PRECIO2 = @P2, PRECIO3 = @P3, PRECIO4 = @P4,
                                PRECIO5 = @P5, PRECIO6 = @P6, PRECIO7 = @P7, PRECIO8 = @P8,
                                FhUltimoCosto = CASE WHEN @TocaCosto = 1 THEN GETDATE() ELSE FhUltimoCosto END,
                                DesdeTrigger = 0
                            WHERE IDARTICULO = @IdArticulo;
                            """, new
                        {
                            art.IdArticulo,
                            TocaCosto = tocaCosto,
                            // El costo no se redondea al paso de los precios (puede ser un valor chico).
                            Costo = tocaCosto ? ArticuloLoteCalculo.Aumentar(art.Costo, p, 0) : art.Costo,
                            P1 = Ajustar(art.P1, tocaPrecios), P2 = Ajustar(art.P2, tocaPrecios), P3 = Ajustar(art.P3, tocaPrecios),
                            P4 = Ajustar(art.P4, tocaPrecios), P5 = Ajustar(art.P5, tocaPrecios), P6 = Ajustar(art.P6, tocaPrecios),
                            P7 = Ajustar(art.P7, tocaPrecios), P8 = Ajustar(art.P8, tocaPrecios)
                        }, tx, cancellationToken: token));
                        break;
                    }
                    case ArticuloLoteAcciones.Utilidad:
                    {
                        if (art.Costo <= 0)
                        {
                            // Sin costo, el precio daria 0: se guarda la utilidad y el precio no se toca.
                            await cn.ExecuteAsync(new CommandDefinition(
                                "UPDATE dbo.V_MA_ARTICULOS SET UTILIDAD = @Utilidad WHERE IDARTICULO = @IdArticulo;",
                                new { art.IdArticulo, Utilidad = (double)request.Utilidad }, tx, cancellationToken: token));
                            resultado.Observaciones.Add($"{codigo.Trim()}: sin costo cargado, no se recalculo el precio.");
                            break;
                        }
                        var alicuota = art.TasaIva > 0 ? art.TasaIva : ivaDefault;
                        var precio = ArticuloLoteCalculo.PrecioDesdeUtilidad(art.Costo, request.Utilidad, conIva && !art.Exento ? alicuota : 0, request.Redondeo);
                        await cn.ExecuteAsync(new CommandDefinition($"""
                            UPDATE dbo.V_MA_ARTICULOS
                            SET UTILIDAD = @Utilidad, PRECIO{claseVenta} = @Precio, DesdeTrigger = 0
                            WHERE IDARTICULO = @IdArticulo;
                            """, new { art.IdArticulo, Utilidad = (double)request.Utilidad, Precio = precio }, tx, cancellationToken: token));
                        break;
                    }
                    case ArticuloLoteAcciones.Rubro:
                        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.V_MA_ARTICULOS SET IDRUBRO = @Valor WHERE IDARTICULO = @IdArticulo;",
                            new { art.IdArticulo, Valor = codigoAsignar }, tx, cancellationToken: token));
                        break;
                    case ArticuloLoteAcciones.Marca:
                        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.V_MA_ARTICULOS SET IDTIPO = @Valor WHERE IDARTICULO = @IdArticulo;",
                            new { art.IdArticulo, Valor = codigoAsignar }, tx, cancellationToken: token));
                        break;
                    case ArticuloLoteAcciones.Familia:
                        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.V_MA_ARTICULOS SET IdFamilia = @Valor WHERE IDARTICULO = @IdArticulo;",
                            new { art.IdArticulo, Valor = codigoAsignar }, tx, cancellationToken: token));
                        break;
                }
                resultado.Actualizados++;
            }
            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(ModuleName, "AccionLote", "V_MA_ARTICULOS", accion,
                $"Accion por lote '{accion}' sobre {resultado.Actualizados} articulo(s).",
                new { accion, request.Porcentaje, request.AplicarA, request.Redondeo, request.Codigo, request.Utilidad, Cantidad = codigos.Count },
                CancellationToken.None);
            return resultado;
        }, "No se pudo aplicar la accion a los articulos seleccionados.", ct);

    private static async Task<string> ValidarCodigoLookupAsync(SqlConnection cn, string tabla, string columna, string? codigo, string nombre, CancellationToken ct)
    {
        var limpio = (codigo ?? string.Empty).Trim();
        var real = limpio.Length == 0 ? null : await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            $"SELECT TOP (1) {columna} FROM dbo.{tabla} WHERE LTRIM(RTRIM({columna})) = @Codigo;",
            new { Codigo = limpio }, cancellationToken: ct));
        return real ?? throw new InvalidOperationException($"Elegi un/a {nombre} existente.");
    }

    private sealed class ArticuloLoteFila
    {
        public string IdArticulo { get; set; } = string.Empty;
        public decimal Costo { get; set; }
        public decimal P1 { get; set; }
        public decimal P2 { get; set; }
        public decimal P3 { get; set; }
        public decimal P4 { get; set; }
        public decimal P5 { get; set; }
        public decimal P6 { get; set; }
        public decimal P7 { get; set; }
        public decimal P8 { get; set; }
        public decimal TasaIva { get; set; }
        public bool Exento { get; set; }
    }
}

/// <summary>Calculos de la accion por lote (sin base de datos, para poder testearlos).</summary>
public static class ArticuloLoteCalculo
{
    /// <summary>Aplica el porcentaje; los valores en 0 quedan en 0 (clases sin precio).</summary>
    public static decimal Aumentar(decimal valor, decimal porcentaje, decimal pasoRedondeo)
    {
        if (valor == 0)
            return 0;
        return Redondear(valor * (1 + porcentaje / 100m), pasoRedondeo);
    }

    /// <summary>Mismo calculo que el editor (CalcularUtlPrecio de FrmArtAlta.frm): costo + utilidad y,
    /// si el maestro es con IVA, se suma la alicuota.</summary>
    public static decimal PrecioDesdeUtilidad(decimal costo, decimal utilidad, decimal alicuotaIva, decimal pasoRedondeo)
    {
        var precio = costo + (utilidad * costo / 100m);
        if (alicuotaIva > 0)
            precio *= 1 + alicuotaIva / 100m;
        return Redondear(precio, pasoRedondeo);
    }

    private static decimal Redondear(decimal valor, decimal paso)
        => paso > 0
            ? Math.Round(valor / paso, 0, MidpointRounding.AwayFromZero) * paso
            : Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
