using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Configuración de precios de venta por lista (V_mv_vta / clsTotales.ImporteLista).
/// RETAIL=SI: los precios del maestro son costos (proveedor, flete, seguro...), la venta sale siempre de la lista
/// y con la clase de venta fija (ClaseDePrecioDefault, si no ClasePrecioVenta, si no 4).
/// </summary>
public sealed class CargaComprobantesConfigPrecios
{
    public bool Retail { get; init; }
    /// <summary>Clase fija de venta en Retail.</summary>
    public int ClaseVenta { get; init; } = 4;
    /// <summary>Clase de oferta por fecha (ClasePrecioOferta); 0 = sin ofertas.</summary>
    public int ClaseOferta { get; init; }
    /// <summary>OfertaPorDescuento=SI: PRECIO9/PRECIO10 son % de descuento desde CantidadOf2/CantidadOf3.</summary>
    public bool OfertaPorDescuento { get; init; }
    /// <summary>OfertaCantidadPrecioUnitario=SI: PRECIO9/PRECIO10 son el precio unitario desde CantidadOf2/CantidadOf3.</summary>
    public bool OfertaPrecioUnitario { get; init; }
    /// <summary>Lista por defecto para clientes sin lista (GOUR_LISTAPR).</summary>
    public string ListaDefault { get; init; } = "";
    public bool PreciosFinales { get; init; }
    public IReadOnlyDictionary<int, string> NombresClase { get; init; } = new Dictionary<int, string>();

    public string NombreClase(int clase) => NombresClase.TryGetValue(clase, out var n) && n.Length > 0 ? n : $"Clase {clase}";

    public static async Task<CargaComprobantesConfigPrecios> LeerAsync(SqlConnection cn, CancellationToken ct, SqlTransaction? tx = null)
    {
        var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))),COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(200),VALORAUX),'')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('RETAIL','CLASEDEPRECIODEFAULT','CLASEPRECIOVENTA','CLASEPRECIOOFERTA',
                'OFERTAPORDESCUENTO','OFERTACANTIDADPRECIOUNITARIO','GOUR_LISTAPR','PRECIOSFINALES')
                OR UPPER(LTRIM(RTRIM(CLAVE))) LIKE 'NOM_PCLASE%';
            """, transaction: tx, cancellationToken: ct)))
            .GroupBy(x => x.Clave).ToDictionary(x => x.Key, x => x.First().Valor.Trim(), StringComparer.OrdinalIgnoreCase);
        static bool Si(string? v) => v?.Trim().ToUpperInvariant() is "SI" or "SÍ" or "S" or "1" or "TRUE";
        static int Clase(string? v) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 8 ? n : 0;
        var claseVenta = Clase(cfg.GetValueOrDefault("CLASEDEPRECIODEFAULT"));
        if (claseVenta == 0) claseVenta = Clase(cfg.GetValueOrDefault("CLASEPRECIOVENTA"));
        var nombres = new Dictionary<int, string>();
        for (var n = 1; n <= 8; n++)
            if (cfg.TryGetValue($"NOM_PCLASE{n}", out var nombre)) nombres[n] = nombre;
        return new()
        {
            Retail = Si(cfg.GetValueOrDefault("RETAIL")),
            ClaseVenta = claseVenta == 0 ? 4 : claseVenta,
            ClaseOferta = Clase(cfg.GetValueOrDefault("CLASEPRECIOOFERTA")),
            OfertaPorDescuento = Si(cfg.GetValueOrDefault("OFERTAPORDESCUENTO")),
            OfertaPrecioUnitario = Si(cfg.GetValueOrDefault("OFERTACANTIDADPRECIOUNITARIO")),
            ListaDefault = cfg.GetValueOrDefault("GOUR_LISTAPR") ?? "",
            PreciosFinales = Si(cfg.GetValueOrDefault("PRECIOSFINALES")),
            NombresClase = nombres
        };
    }
}

/// <summary>Lista y clase del comprobante con las filas de lista de los artículos cargados (para la grilla).</summary>
public sealed class CargaComprobantesPreciosInfo
{
    public string Lista { get; set; } = "";
    public string ListaNombre { get; set; } = "";
    public int Clase { get; set; }
    public CargaComprobantesConfigPrecios Config { get; set; } = new();
    public Dictionary<string, CargaComprobantesPrecioListaFila> Filas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Fila de V_MA_PRECIOS (lista de venta) de un artículo.</summary>
public sealed class CargaComprobantesPrecioListaFila
{
    public string Articulo { get; set; } = "";
    public bool ConIva { get; set; }
    public decimal Precio1 { get; set; }
    public decimal Precio2 { get; set; }
    public decimal Precio3 { get; set; }
    public decimal Precio4 { get; set; }
    public decimal Precio5 { get; set; }
    public decimal Precio6 { get; set; }
    public decimal Precio7 { get; set; }
    public decimal Precio8 { get; set; }
    public decimal Precio9 { get; set; }
    public decimal Precio10 { get; set; }
    public decimal Costo { get; set; }
    public DateTime? OfertaDesde { get; set; }
    public DateTime? OfertaHasta { get; set; }
    public decimal CantidadOf2 { get; set; }
    public decimal CantidadOf3 { get; set; }

    public decimal Precio(int clase) => clase switch
    {
        1 => Precio1, 2 => Precio2, 3 => Precio3, 4 => Precio4, 5 => Precio5,
        6 => Precio6, 7 => Precio7, 8 => Precio8, 9 => Precio9, 10 => Precio10, _ => 0
    };
}

/// <summary>Precio de lista resuelto para un renglón: bruto de lista (con o sin IVA según la fila) y descuento automático.</summary>
public sealed record CargaComprobantesPrecioListaResuelto(decimal Bruto, decimal Descuento, bool Oferta, string Etiqueta);

/// <summary>Precio unitario resuelto: neto sin IVA ni internos, final con IVA e internos, descuento automático de la lista.</summary>
public sealed record CargaComprobantesPrecioVenta(decimal Neto, decimal Final, decimal Descuento, bool Oferta, string Etiqueta,
    bool DeLista, decimal? Costo);

public static class CargaComprobantesPreciosLista
{
    /// <summary>
    /// Igual que clsTotales.ImporteLista para ventas con lista:
    /// 1) oferta por fecha (FhOfertaDesde ≤ fecha ≤ FhOfertaHasta; Hasta vacío = abierta): toma la clase de oferta;
    /// 2) si no hay oferta, ofertas por cantidad con CantidadOf2/CantidadOf3 y PRECIO9/PRECIO10
    ///    (descuento %, precio unitario o precio por pack según la configuración).
    /// Los Dto1..9/Rec1..3 de la lista son de compras: en ventas no se aplican.
    /// </summary>
    public static CargaComprobantesPrecioListaResuelto Resolver(CargaComprobantesPrecioListaFila fila, int clase,
        decimal cantidad, DateTime fecha, CargaComprobantesConfigPrecios cfg)
    {
        var dia = fecha.Date;
        if (cfg.ClaseOferta > 0 && fila.OfertaDesde is { } desde && desde.Date <= dia
            && (fila.OfertaHasta is null || fila.OfertaHasta.Value.Date >= dia) && fila.Precio(cfg.ClaseOferta) > 0)
            return new(fila.Precio(cfg.ClaseOferta), 0, true,
                fila.OfertaHasta is { } hasta ? $"Oferta hasta {hasta:dd/MM/yyyy}" : "Oferta");
        var bruto = fila.Precio(clase);
        var descuento = 0m;
        var tramos = Tramos(fila, cfg);
        if (cantidad > 0)
        {
            foreach (var (desdeCantidad, valor) in new[] { (fila.CantidadOf2, fila.Precio9), (fila.CantidadOf3, fila.Precio10) })
            {
                if (desdeCantidad <= 0 || valor <= 0) continue;
                if (cfg.OfertaPrecioUnitario) { if (cantidad >= desdeCantidad) bruto = valor; }
                else if (cfg.OfertaPorDescuento) { if (cantidad >= desdeCantidad && valor < 100) descuento = valor; }
                else if (cantidad % desdeCantidad == 0) bruto = decimal.Round(valor / desdeCantidad, 4, MidpointRounding.ToEven);
            }
        }
        return new(bruto, descuento, false, tramos);
    }

    /// <summary>
    /// Precio unitario de venta de un renglón, igual para la grabación, el buscador y la grilla:
    /// lista (con oferta) si el artículo tiene precio en ella, si no el maestro; el interno (PreciosFinales)
    /// se separa del precio final antes del IVA y se vuelve a sumar al final.
    /// </summary>
    public static CargaComprobantesPrecioVenta Calcular(CargaComprobantesPrecioListaFila? fila, decimal precioMaestro, int clase,
        decimal cantidad, DateTime fecha, CargaComprobantesConfigPrecios cfg, decimal tasa, bool preciosConIva, decimal interno)
    {
        var bruto = precioMaestro;
        var conIva = preciosConIva;
        var descuento = 0m;
        var oferta = false;
        var etiqueta = "";
        var deLista = fila is not null && fila.Precio(clase) > 0;
        if (deLista)
        {
            var resuelto = Resolver(fila!, clase, cantidad, fecha, cfg);
            bruto = resuelto.Bruto; descuento = resuelto.Descuento; oferta = resuelto.Oferta; etiqueta = resuelto.Etiqueta;
            if (cfg.Retail) conIva = fila!.ConIva;
        }
        var (neto, final) = ArticuloPrecioResolverService.SplitPrice(bruto - interno, tasa, conIva);
        return new(neto, final + interno, descuento, oferta, etiqueta, deLista, deLista && fila!.Costo > 0 ? fila.Costo : null);
    }

    /// <summary>Texto de las ofertas por cantidad, para mostrar en la grilla.</summary>
    public static string Tramos(CargaComprobantesPrecioListaFila fila, CargaComprobantesConfigPrecios cfg)
    {
        var partes = new List<string>();
        foreach (var (desde, valor) in new[] { (fila.CantidadOf2, fila.Precio9), (fila.CantidadOf3, fila.Precio10) })
        {
            if (desde <= 0 || valor <= 0) continue;
            var cant = desde.ToString("0.###", CultureInfo.GetCultureInfo("es-AR"));
            var importe = valor.ToString("N2", CultureInfo.GetCultureInfo("es-AR"));
            partes.Add(cfg.OfertaPrecioUnitario ? $"${importe} desde {cant}"
                : cfg.OfertaPorDescuento ? $"{valor.ToString("0.##", CultureInfo.GetCultureInfo("es-AR"))}% desde {cant}"
                : $"{cant} x ${importe}");
        }
        return string.Join(" · ", partes);
    }

    public static async Task<Dictionary<string, CargaComprobantesPrecioListaFila>> LeerAsync(SqlConnection cn, string lista,
        IEnumerable<string> articulos, CancellationToken ct, SqlTransaction? tx = null)
    {
        var resultado = new Dictionary<string, CargaComprobantesPrecioListaFila>(StringComparer.OrdinalIgnoreCase);
        var ids = articulos.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0 || string.IsNullOrWhiteSpace(lista)) return resultado;
        foreach (var lote in ids.Chunk(500))
            foreach (var fila in await cn.QueryAsync<CargaComprobantesPrecioListaFila>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(p.IdArticulo)) Articulo,CONVERT(bit,ISNULL(p.ConIVA,0)) ConIva,
                    ISNULL(p.Precio1,0) Precio1,ISNULL(p.Precio2,0) Precio2,ISNULL(p.Precio3,0) Precio3,ISNULL(p.Precio4,0) Precio4,
                    ISNULL(p.Precio5,0) Precio5,ISNULL(p.PRECIO6,0) Precio6,ISNULL(p.PRECIO7,0) Precio7,ISNULL(p.PRECIO8,0) Precio8,
                    ISNULL(p.PRECIO9,0) Precio9,ISNULL(p.PRECIO10,0) Precio10,ISNULL(p.COSTO,0) Costo,
                    p.FhOfertaDesde OfertaDesde,p.FhOfertaHasta OfertaHasta,
                    CONVERT(decimal(18,4),ISNULL(p.CantidadOf2,0)) CantidadOf2,CONVERT(decimal(18,4),ISNULL(p.CantidadOf3,0)) CantidadOf3
                FROM dbo.V_MA_PRECIOS p
                WHERE LTRIM(RTRIM(p.IdLista))=@Lista AND p.TipoLista='V' AND LTRIM(RTRIM(p.IdArticulo)) IN @Ids;
                """, new { Lista = lista.Trim(), Ids = lote }, tx, cancellationToken: ct)))
                resultado.TryAdd(fila.Articulo, fila);
        return resultado;
    }
}
