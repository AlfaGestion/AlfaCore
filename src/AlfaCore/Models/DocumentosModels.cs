namespace AlfaCore.Models;

public static class TiposDocumentoCore
{
    public const string Cotizacion = "COTIZACION";
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

    public static readonly IReadOnlySet<string> Permitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Logo, Empresa, Comprobante, Cliente, Items, Totales, Propuesta, Firma, Portada
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
        [TiposBloqueDocumento.Cliente] = [new("Codigo", "Código"), new("RazonSocial", "Razón social"), new("Cuit", "CUIT"), new("Domicilio", "Domicilio"), new("Telefono", "Teléfono"), new("Email", "Email")],
        [TiposBloqueDocumento.Totales] = [new("Neto", "Neto"), new("Descuento", "Descuento"), new("Impuestos", "Impuestos"), new("Total", "Total")]
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
            new() { Id = "signature", Type = TiposBloqueDocumento.Firma, Visible = true }
        ]
    };
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

public sealed class DocumentRenderResult { public string Html { get; init; } = string.Empty; public int IdTemplate { get; init; } public string TipoDocumento { get; init; } = string.Empty; public string? UNegocio { get; init; } }

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
