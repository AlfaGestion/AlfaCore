using AlfaCore.Services;

namespace AlfaCore.Models;

public static class TiposDocumentoCore
{
    public const string Cotizacion = "COTIZACION";
    public const string FacturaA = "FACTURA_A";
    public const string FacturaB = "FACTURA_B";
    public const string FacturaC = "FACTURA_C";

    public static readonly IReadOnlySet<string> Todos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Cotizacion, FacturaA, FacturaB, FacturaC
    };

    private static readonly IReadOnlyDictionary<string, string> LetraPorTipo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [FacturaA] = "A",
        [FacturaB] = "B",
        [FacturaC] = "C"
    };

    public static bool EsFactura(string tipoDocumento)
        => LetraPorTipo.ContainsKey(tipoDocumento ?? string.Empty);

    public static string? LetraDe(string tipoDocumento)
        => LetraPorTipo.TryGetValue(tipoDocumento ?? string.Empty, out var letra) ? letra : null;

    public static string ParaLetra(string letra) => letra?.Trim().ToUpperInvariant() switch
    {
        "A" => FacturaA,
        "B" => FacturaB,
        "C" => FacturaC,
        _ => throw new InvalidOperationException($"Letra de comprobante no soportada: '{letra}'.")
    };
}

public static class TiposBloqueDocumento
{
    public const string Logo = "Logo";
    public const string Empresa = "Empresa";
    public const string Comprobante = "Comprobante";
    public const string Cliente = "Cliente";
    public const string Items = "Items";
    public const string Totales = "Totales";
    public const string Propuesta = "Propuesta";
    public const string Firma = "Firma";
    public const string Portada = "Portada";
    public const string Pie = "Pie";
    public const string RecuadroTipo = "RecuadroTipo";
    public const string Cae = "Cae";
    public const string QrAfip = "QrAfip";

    public static readonly IReadOnlySet<string> Permitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Logo, Empresa, Comprobante, Cliente, Items, Totales, Propuesta, Firma, Portada, Pie,
        RecuadroTipo, Cae, QrAfip
    };
}

/// <summary>Campos seleccionables por tipo de bloque (para el checklist del diseñador y para
/// validar que VisibleFields no traiga nombres inventados).</summary>
public sealed record DocumentFieldOption(string Field, string Label);

public static class DocumentBlockFields
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<DocumentFieldOption>> PorTipo = new Dictionary<string, IReadOnlyList<DocumentFieldOption>>(StringComparer.OrdinalIgnoreCase)
    {
        [TiposBloqueDocumento.Empresa] = [new("Nombre", "Nombre"), new("Cuit", "CUIT"), new("Domicilio", "Domicilio"), new("Telefono", "Teléfono"), new("Email", "Email")],
        [TiposBloqueDocumento.Comprobante] = [new("Numero", "Número"), new("Fecha", "Fecha"), new("Vencimiento", "Vencimiento"), new("Moneda", "Moneda")],
        [TiposBloqueDocumento.Cliente] = [new("Codigo", "Código"), new("RazonSocial", "Razón social"), new("Cuit", "CUIT"), new("CondicionIva", "Condición IVA"), new("Domicilio", "Domicilio"), new("Telefono", "Teléfono"), new("Email", "Email")],
        [TiposBloqueDocumento.Totales] = [new("Neto", "Neto"), new("Descuento", "Descuento"), new("Impuestos", "Impuestos"), new("Total", "Total")],
        [TiposBloqueDocumento.Pie] = [new("NumeroPagina", "Número de página"), new("NombreEmpresa", "Nombre de la empresa")],
        [TiposBloqueDocumento.RecuadroTipo] = [new("Letra", "Letra"), new("CodigoAfip", "Código AFIP")],
        [TiposBloqueDocumento.Cae] = [new("Cae", "CAE"), new("Vencimiento", "Vencimiento"), new("CodigoBarra", "Código de barras")]
    };

    public static IReadOnlyList<DocumentFieldOption> For(string tipo)
        => PorTipo.TryGetValue(tipo, out var options) ? options : [];
}

/// <summary>Fuente de verdad versionable del diseño; no contiene HTML ni código ejecutable.</summary>
public sealed class DocumentTemplateDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public DocumentPaperDefinition Paper { get; set; } = new();
    public List<DocumentBlockDefinition> Blocks { get; set; } = [];

    public static DocumentTemplateDefinition CrearCotizacionEstandar() => new()
    {
        Paper = new DocumentPaperDefinition(),
        Blocks =
        [
            new() { Id = "cover", Type = TiposBloqueDocumento.Portada, Visible = true },
            new() { Id = "header-logo", Type = TiposBloqueDocumento.Logo, Visible = true, Align = "right", Width = 120 },
            new() { Id = "company", Type = TiposBloqueDocumento.Empresa, Visible = true },
            new() { Id = "document", Type = TiposBloqueDocumento.Comprobante, Visible = true },
            new() { Id = "customer", Type = TiposBloqueDocumento.Cliente, Visible = true },
            new() { Id = "proposal", Type = TiposBloqueDocumento.Propuesta, Visible = true },
            new()
            {
                Id = "items", Type = TiposBloqueDocumento.Items, Visible = true,
                Columns =
                [
                    new() { Field = "Codigo", Title = "Código", Visible = true, WidthPercent = 14, Align = "left" },
                    new() { Field = "Descripcion", Title = "Descripción", Visible = true, WidthPercent = 42, Align = "left" },
                    new() { Field = "Cantidad", Title = "Cant.", Visible = true, WidthPercent = 11, Align = "right" },
                    new() { Field = "Precio", Title = "Precio", Visible = true, WidthPercent = 16, Align = "right" },
                    new() { Field = "Total", Title = "Total", Visible = true, WidthPercent = 17, Align = "right" }
                ]
            },
            new() { Id = "totals", Type = TiposBloqueDocumento.Totales, Visible = true },
            new() { Id = "signature", Type = TiposBloqueDocumento.Firma, Visible = true },
            new() { Id = "footer", Type = TiposBloqueDocumento.Pie, Visible = false }
        ]
    };

    /// <summary>Plantilla base para Factura A/B/C -- la letra sólo cambia el default de la columna
    /// "Iva" (A discrimina IVA por línea en el cuerpo, B/C no) y el bloque RecuadroTipo (letra +
    /// código AFIP). El resto del layout es igual para las tres; cada letra queda como una plantilla
    /// de sistema independiente y editable por separado (ver TiposDocumentoCore).</summary>
    public static DocumentTemplateDefinition CrearFacturaEstandar(string letra)
    {
        // La columna "Iva" por línea queda oculta por default en las 3 letras: las líneas de
        // "Otros conceptos" (texto libre, ej. servicios facturados manualmente) no tienen alícuota
        // propia confiable en el origen de datos -- el desglose de IVA legalmente válido es el de
        // la cabecera (bloque Totales), no por línea. El usuario puede tildarla igual si su base
        // sí carga alícuota por artículo/tarea y la quiere ver.
        return new DocumentTemplateDefinition
        {
            Paper = new DocumentPaperDefinition(),
            Blocks =
            [
                new() { Id = "header-logo", Type = TiposBloqueDocumento.Logo, Visible = true, Align = "left", Width = 100, CombineWithCompany = true },
                new() { Id = "company", Type = TiposBloqueDocumento.Empresa, Visible = true },
                new() { Id = "recuadro-tipo", Type = TiposBloqueDocumento.RecuadroTipo, Visible = true },
                new() { Id = "document", Type = TiposBloqueDocumento.Comprobante, Visible = true },
                new() { Id = "customer", Type = TiposBloqueDocumento.Cliente, Visible = true },
                new()
                {
                    Id = "items", Type = TiposBloqueDocumento.Items, Visible = true,
                    Columns =
                    [
                        new() { Field = "Codigo", Title = "Código", Visible = true, WidthPercent = 10, Align = "left" },
                        new() { Field = "Descripcion", Title = "Descripción", Visible = true, WidthPercent = 36, Align = "left" },
                        new() { Field = "Cantidad", Title = "Cantidad", Visible = true, WidthPercent = 10, Align = "right" },
                        new() { Field = "Precio", Title = "P.Unitario", Visible = true, WidthPercent = 14, Align = "right" },
                        new() { Field = "Descuento", Title = "Bonif.", Visible = false, WidthPercent = 8, Align = "right" },
                        new() { Field = "Iva", Title = "Alíc. IVA", Visible = false, WidthPercent = 10, Align = "right" },
                        new() { Field = "Total", Title = "Total", Visible = true, WidthPercent = 12, Align = "right" }
                    ]
                },
                new() { Id = "totals", Type = TiposBloqueDocumento.Totales, Visible = true },
                new() { Id = "cae", Type = TiposBloqueDocumento.Cae, Visible = true },
                new() { Id = "qr", Type = TiposBloqueDocumento.QrAfip, Visible = true, Align = "left", Width = 30 },
                new() { Id = "footer", Type = TiposBloqueDocumento.Pie, Visible = false }
            ]
        };
    }
}

public sealed class DocumentPaperDefinition
{
    public string Size { get; set; } = "A4";
    public string Orientation { get; set; } = "Portrait";
    public decimal MarginTopMm { get; set; } = 10;
    public decimal MarginBottomMm { get; set; } = 10;
    public decimal MarginLeftMm { get; set; } = 10;
    public decimal MarginRightMm { get; set; } = 10;
}

public sealed class DocumentBlockDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public string Align { get; set; } = "left";
    public decimal? Width { get; set; }
    /// <summary>Tamaño de letra del bloque en puntos. Null = usa el tamaño por defecto del tema.</summary>
    public decimal? FontSizePt { get; set; }
    /// <summary>Campos a mostrar (ver DocumentBlockFields.PorTipo). Null = todos los campos del bloque.</summary>
    public List<string>? VisibleFields { get; set; }
    /// <summary>Solo aplica al bloque Logo: en vez de mostrarse arriba en su propia línea, se
    /// combina en una sola cabecera con recuadro junto a los datos de la empresa. El lado lo decide
    /// Align (left/right) del propio bloque Logo.</summary>
    public bool CombineWithCompany { get; set; }
    public List<DocumentItemColumnDefinition> Columns { get; set; } = [];
}

public sealed class DocumentItemColumnDefinition
{
    public string Field { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public decimal WidthPercent { get; set; }
    public string Align { get; set; } = "left";
}

public sealed class DocumentTemplateDto
{
    public int IdTemplate { get; set; }
    public string? UNegocio { get; set; }
    public string TipoDocumento { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string TemplateJson { get; set; } = string.Empty;
    public string? CssCustom { get; set; }
    public bool EsSistema { get; set; }
    public bool EsPredeterminado { get; set; }
    public bool Activo { get; set; }
    public bool TienePortada { get; set; }
    public DateTime FechaAlta { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public string? UsuarioModificacion { get; set; }
}

public sealed class DocumentTemplateSaveRequest
{
    public int? IdTemplate { get; set; }
    public string? UNegocio { get; set; }
    public string TipoDocumento { get; set; } = TiposDocumentoCore.Cotizacion;
    public string Nombre { get; set; } = string.Empty;
    public DocumentTemplateDefinition Definition { get; set; } = DocumentTemplateDefinition.CrearCotizacionEstandar();
    public string? CssCustom { get; set; }
    public bool EsPredeterminado { get; set; }
    public bool Activo { get; set; } = true;
    public string? Usuario { get; set; }
}

public sealed class CotizacionDocumentData
{
    public EmpresaDocumentData Empresa { get; set; } = new();
    public ComprobanteDocumentData Comprobante { get; set; } = new();
    public ClienteDocumentData Cliente { get; set; } = new();
    public List<CotizacionDocumentItemData> Items { get; set; } = [];
    public TotalesDocumentData Totales { get; set; } = new();
    /// <summary>HTML ya saneado de la solapa "Propuesta" (pensado para el cliente). Nunca las
    /// "Observaciones internas" -- esas son notas de uso interno y no deben imprimirse.</summary>
    public string PropuestaHtml { get; set; } = string.Empty;
    public byte[]? FirmaBytes { get; set; }
    public string FirmanteNombre { get; set; } = string.Empty;
    public byte[]? PortadaBytes { get; set; }
    /// <summary>Tilde por versión de la cotización (COT_VERSION.IncluyePortada) -- independiente de
    /// que la plantilla tenga o no configurado el bloque Portada. Deben darse las dos cosas.</summary>
    public bool IncluyePortada { get; set; } = true;
}

public sealed class EmpresaDocumentData { public string Nombre { get; set; } = string.Empty; public string Cuit { get; set; } = string.Empty; public string Domicilio { get; set; } = string.Empty; public string Telefono { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; public byte[]? Logo { get; set; } }
public sealed class ComprobanteDocumentData { public string Numero { get; set; } = string.Empty; public DateTime Fecha { get; set; } public DateTime? FechaVencimiento { get; set; } public string Vendedor { get; set; } = string.Empty; public string CondicionVenta { get; set; } = string.Empty; public string Moneda { get; set; } = string.Empty; }
public sealed class ClienteDocumentData { public string Codigo { get; set; } = string.Empty; public string RazonSocial { get; set; } = string.Empty; public string Cuit { get; set; } = string.Empty; public string Domicilio { get; set; } = string.Empty; public string Telefono { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; }
public sealed class CotizacionDocumentItemData { public string Codigo { get; set; } = string.Empty; public string Descripcion { get; set; } = string.Empty; public decimal Cantidad { get; set; } public decimal Precio { get; set; } public decimal Descuento { get; set; } public decimal Total { get; set; } public bool ImpactaTotal { get; set; } }
public sealed class TotalesDocumentData { public decimal Neto { get; set; } public decimal Descuento { get; set; } public decimal Impuestos { get; set; } public decimal Total { get; set; } }

/// <summary>Datos resueltos de una Factura A/B/C ya emitida (comprobante real, no un borrador) para
/// el Diseñador de comprobantes. A diferencia de CotizacionDocumentData, viene de tablas
/// transaccionales de venta (V_MV_Cpte/V_MV_CpteInsumos/V_MV_CPTE_ELECTRONICOS/Aux_MV_CpteQR), no de
/// un módulo propio con estado editable.</summary>
public sealed class FacturaDocumentData
{
    public EmpresaDocumentData Empresa { get; set; } = new();
    /// <summary>Condición de IVA del EMISOR (Alfa Gestión), no del cliente -- ej. "Responsable
    /// Monotributo". Sale de TA_CONFIGURACION, no de la cabecera del comprobante.</summary>
    public string CondicionIvaEmisor { get; set; } = string.Empty;
    public string IngresosBrutosEmisor { get; set; } = string.Empty;
    public DateTime? InicioActividadesEmisor { get; set; }

    public FacturaComprobanteDocumentData Comprobante { get; set; } = new();
    public FacturaClienteDocumentData Cliente { get; set; } = new();
    public List<FacturaDocumentItemData> Items { get; set; } = [];
    public FacturaTotalesDocumentData Totales { get; set; } = new();
    /// <summary>Null = todavía no hay CAE (comprobante pre-electrónico o pendiente de AFIP) -- el
    /// renderer omite el bloque en vez de mostrarlo vacío.</summary>
    public FacturaCaeDocumentData? Cae { get; set; }
    /// <summary>Bytes del QR de AFIP ya generado (Aux_MV_CpteQR.QR_AFIP). Null si todavía no existe.</summary>
    public byte[]? QrBytes { get; set; }
}

public sealed class FacturaComprobanteDocumentData
{
    public string Tc { get; set; } = string.Empty;
    /// <summary>"A" | "B" | "C" -- determina si Totales discrimina IVA por alícuota en el cuerpo.</summary>
    public string Letra { get; set; } = string.Empty;
    /// <summary>Código AFIP de 3 dígitos ("001"/"006"/"011"), para el recuadro de tipo.</summary>
    public string CodigoAfip { get; set; } = string.Empty;
    public string PuntoVenta { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string CondicionVenta { get; set; } = string.Empty;
    public string Vendedor { get; set; } = string.Empty;
}

public sealed class FacturaClienteDocumentData
{
    public string Codigo { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    /// <summary>"CUIT"/"DNI"/etc, según DOCUMENTOTIPO del receptor.</summary>
    public string DocumentoTipoDescripcion { get; set; } = string.Empty;
    public string DocumentoNumero { get; set; } = string.Empty;
    /// <summary>Condición de IVA del RECEPTOR (distinta de FacturaDocumentData.CondicionIvaEmisor).</summary>
    public string CondicionIvaDescripcion { get; set; } = string.Empty;
    public string Domicilio { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class FacturaDocumentItemData
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Bonificacion { get; set; }
    /// <summary>Se guarda siempre aunque la letra no la imprima en el cuerpo (B/C).</summary>
    public decimal AlicuotaIva { get; set; }
    public decimal Subtotal { get; set; }
}

public sealed class FacturaTotalesDocumentData
{
    public decimal NetoGravado { get; set; }
    public decimal NetoNoGravado { get; set; }
    public decimal ImporteExento { get; set; }
    public decimal ImporteImpuestosInternos { get; set; }
    /// <summary>Armada desde AlicIva/ImporteIva .. AlicIVA4/ImpIVA4 + AlicIvaRec/ImporteIvaRec de la
    /// cabecera real (V_MV_Cpte) -- nunca recalculada agrupando el detalle. Viaja siempre completa;
    /// es el renderer quien decide no imprimirla si Letra != "A".</summary>
    public List<FacturaIvaLineaData> LineasIva { get; set; } = [];
    public List<FacturaDescuentoLineaData> LineasDescuento { get; set; } = [];
    /// <summary>IIBB (RETIBR_*), IVA (RETIVA_*), Ganancias (RETGAN_Importe), SUSS (RETSUSS_Importe).</summary>
    public List<FacturaPercepcionLineaData> LineasPercepcion { get; set; } = [];
    public decimal Total { get; set; }
    public string Moneda { get; set; } = string.Empty;
}

public sealed record FacturaIvaLineaData(decimal Alicuota, decimal Importe);
public sealed record FacturaDescuentoLineaData(decimal Porcentaje, decimal Importe);
public sealed record FacturaPercepcionLineaData(string Descripcion, decimal BaseImponible, decimal Alicuota, decimal Importe);

/// <summary>Fila liviana para el combo de preview del Diseñador (buscar un comprobante real de una
/// letra puntual) -- no trae el detalle completo, solo lo necesario para identificarlo en una lista.</summary>
public sealed record FacturaResumenDto(string Tc, string IdComprobante, string Numero, DateTime Fecha, string Cliente);

public sealed class FacturaCaeDocumentData
{
    public string Cae { get; set; } = string.Empty;
    public DateTime VencimientoCae { get; set; }
    public string? CodigoBarra { get; set; }
    /// <summary>'A' = aprobado por AFIP. Distinto de 'A' -- se muestra un aviso, no se bloquea el PDF.</summary>
    public string Resultado { get; set; } = string.Empty;
    public string? Motivo { get; set; }
}

public sealed class DocumentRenderResult
{
    public string Html { get; init; } = string.Empty;
    public int IdTemplate { get; init; }
    public string TipoDocumento { get; init; } = string.Empty;
    public string? UNegocio { get; init; }
    public DocumentPdfFooterOptions? Footer { get; init; }
}

/// <summary>Tema visual (paleta de colores) aplicado a TODOS los documentos, independiente de la
/// plantilla elegida -- es una preferencia general de la base, no por plantilla.</summary>
public sealed record DocumentThemePreset(string Key, string Nombre, string ColorPrimario, string ColorSecundario, string ColorTexto, string ColorFondoSuave);

public static class DocumentThemePresets
{
    public const string Default = "clasico";

    public static readonly IReadOnlyList<DocumentThemePreset> Todos =
    [
        new("clasico", "Clásico", "#123a63", "#168da0", "#172033", "#f8fafc"),
        new("moderno", "Moderno", "#0f766e", "#14b8a6", "#0f172a", "#f0fdfa"),
        new("minimalista", "Minimalista", "#374151", "#6b7280", "#111827", "#f9fafb"),
        new("corporativo", "Corporativo", "#1c1917", "#b45309", "#1c1917", "#fafaf9"),
        new("calido", "Cálido", "#9a3412", "#ea580c", "#1c1917", "#fff7ed")
    ];

    public static DocumentThemePreset Resolve(string? key)
        => Todos.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Todos[0];
}
