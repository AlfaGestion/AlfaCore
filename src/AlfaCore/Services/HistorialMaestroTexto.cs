using System.Globalization;
using System.Text.RegularExpressions;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Nombres y valores legibles del registro de cambios del maestro (ALFACORE_ARTICULOS_AUDITORIA).</summary>
public static partial class HistorialMaestroTexto
{
    public const string DatoAlta = "ALTA";
    public const string DatoBaja = "BAJA";
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    private static readonly Dictionary<string, string> Nombres = new(StringComparer.OrdinalIgnoreCase)
    {
        [DatoAlta] = "Alta del artículo", [DatoBaja] = "Baja del artículo",
        ["IDARTICULO"] = "Código", ["CODIGOBARRA"] = "Código de barras", ["CODIGOBARRA1"] = "Código de barras 2", ["CODIGOBARRA2"] = "Código de barras 3",
        ["CODIGOBARRA3"] = "Código de barras 4", ["CODIGOBARRA4"] = "Código de barras 5", ["CodigoBarraDun"] = "Código DUN",
        ["UMCB1"] = "Unidad del código de barras 2", ["UMCB2"] = "Unidad del código de barras 3", ["UMCB3"] = "Unidad del código de barras 4", ["UMCB4"] = "Unidad del código de barras 5",
        ["DESCRIPCION"] = "Descripción", ["DescripAbrev"] = "Descripción abreviada", ["Presentacion"] = "Presentación",
        ["CUENTAPROVEEDOR"] = "Proveedor", ["CodigoArtProveedor"] = "Código en el proveedor",
        ["IDRUBRO"] = "Rubro", ["IDTIPO"] = "Marca", ["IdFamilia"] = "Familia",
        ["IDUNIDAD"] = "Unidad", ["UD_CPRA"] = "Unidad de compra", ["UD_STOCK"] = "Unidad de stock", ["UD_TTE"] = "Unidad de transporte",
        ["EXENTO"] = "Exento de IVA", ["TasaIVA"] = "Tasa de IVA", ["IdPercepcion"] = "Percepción", ["ITC"] = "Impuesto interno (ITC)",
        ["PoliticaPrecios"] = "Política de precios", ["UTILIDAD"] = "Utilidad", ["Moneda"] = "Moneda", ["ActualizaCosto"] = "Actualiza costo",
        ["SUSPENDIDO"] = "Suspendido", ["SuspendidoV"] = "Suspendido en ventas", ["SuspendidoC"] = "Suspendido en compras",
        ["NOTAS"] = "Notas", ["Observaciones"] = "Observaciones", ["InsertaObserv"] = "Agrega observaciones al vender",
        ["PUNTOPEDIDO"] = "Punto de pedido", ["PUNTOMAXIMO"] = "Stock máximo", ["NO_CONTROLA_STOCK"] = "No controla stock", ["CantidadPropuesta"] = "Cantidad propuesta",
        ["USASERIE"] = "Usa número de serie", ["USALOTE"] = "Usa lote", ["Perecedero"] = "Perecedero", ["Pesable"] = "Pesable",
        ["CuentaContable"] = "Cuenta contable de ventas", ["CuentaContableC"] = "Cuenta contable de compras",
        ["KILOS"] = "Kilos", ["M3"] = "Metros cúbicos", ["Espesor"] = "Espesor", ["Ancho"] = "Ancho", ["Largo"] = "Largo o días de vencimiento",
        ["PideMedidas"] = "Pide medidas", ["PideEquivalencia"] = "Pide equivalencia", ["PideDescripcionAdicional"] = "Pide descripción adicional",
        ["Dto1"] = "Descuento 1", ["Dto2"] = "Descuento 2", ["Dto3"] = "Descuento 3", ["Dto4"] = "Descuento 4", ["Dto5"] = "Descuento 5",
        ["Dto6"] = "Descuento 6", ["Dto7"] = "Descuento 7", ["Dto8"] = "Descuento 8", ["Dto9"] = "Descuento 9",
        ["Rec1"] = "Recargo 1", ["Rec2"] = "Recargo 2", ["Rec3"] = "Recargo 3", ["FhDtoDesde"] = "Descuento desde", ["FhDtoHasta"] = "Descuento hasta",
        ["IdTarifaFlete"] = "Tarifa de flete", ["PorcSeguro"] = "Seguro (%)",
        ["EnOferta"] = "En oferta", ["StockOferta"] = "Stock en oferta", ["EnComodato"] = "En comodato",
        ["UsaTalle"] = "Usa talle", ["TalleDefault"] = "Talle por defecto", ["ColorDefault"] = "Color por defecto", ["SexoDefault"] = "Sexo por defecto",
        ["FHALTA"] = "Fecha de alta", ["RutaImagen"] = "Imagen", ["URL1"] = "Enlace 1", ["URL2"] = "Enlace 2", ["URL3"] = "Enlace 3",
        ["Ubicacion_Habitual"] = "Ubicación habitual", ["Procedencia"] = "Procedencia",
        ["InsumosPorPorcentaje"] = "Insumos por porcentaje", ["CostoInsumoIndependiente"] = "Costo de insumos independiente"
    };

    /// <summary>Columnas bit conocidas: se muestran como Sí / No.</summary>
    private static readonly HashSet<string> SiNo = new(StringComparer.OrdinalIgnoreCase)
    {
        "USASERIE", "USALOTE", "EXENTO", "SUSPENDIDO", "SuspendidoV", "SuspendidoC", "ActualizaCosto", "Perecedero", "Pesable", "UsaTalle",
        "InsumosPorPorcentaje", "EnOferta", "PideMedidas", "EnComodato", "PideEquivalencia", "InsertaObserv", "NO_CONTROLA_STOCK",
        "PideDescripcionAdicional", "CostoInsumoIndependiente"
    };

    /// <summary>Clave del dato: ALTA, BAJA o la columna.</summary>
    public static string Dato(HistorialMaestroCambioDto x) => x.Accion switch
    {
        HistorialMaestroAcciones.Alta => DatoAlta,
        HistorialMaestroAcciones.Baja => DatoBaja,
        _ => x.Campo
    };

    public static string NombreDato(string? dato)
        => string.IsNullOrWhiteSpace(dato) ? "" : Nombres.TryGetValue(dato.Trim(), out var n) ? n : dato.Trim();

    /// <summary>Valor legible: Sí/No, fecha, número con coma, o el código seguido del nombre del rubro, marca o proveedor.</summary>
    public static string Valor(string? campo, string? valor, string? nombre = null)
    {
        var v = (valor ?? string.Empty).Trim();
        if (v.Length == 0) return "(vacío)";
        if (campo is not null && SiNo.Contains(campo) && v is "0" or "1") return v == "1" ? "Sí" : "No";
        if (Fecha().IsMatch(v) && DateTime.TryParseExact(v, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString(d.TimeOfDay == TimeSpan.Zero ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm", Ar);
        if (Decimal().IsMatch(v) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
            return n.ToString("#,##0.######", Ar);
        var texto = (nombre ?? string.Empty).Trim();
        return texto.Length > 0 && !string.Equals(texto, v, StringComparison.OrdinalIgnoreCase) ? $"{v} · {texto}" : v;
    }

    /// <summary>Quién hizo el cambio: el usuario de Alfa si la grabación lo informó; si no, el login de SQL.</summary>
    public static string Quien(HistorialMaestroCambioDto x)
        => x.Usuario.Trim().Length > 0 ? x.Usuario.Trim() : x.LoginSql.Trim().Length > 0 ? $"Sin usuario ({x.LoginSql.Trim()})" : "Sin usuario";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$")]
    private static partial Regex Fecha();

    [GeneratedRegex(@"^-?\d+\.\d+$")]
    private static partial Regex Decimal();
}
