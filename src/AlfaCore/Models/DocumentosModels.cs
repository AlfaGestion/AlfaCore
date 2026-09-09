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
    public const string Observaciones = "Observaciones";

    public static readonly IReadOnlySet<string> Permitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Logo, Empresa, Comprobante, Cliente, Items, Totales, Observaciones
    };
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
            new() { Id = "header-logo", Type = TiposBloqueDocumento.Logo, Visible = true, Align = "right", Width = 120 },
            new() { Id = "company", Type = TiposBloqueDocumento.Empresa, Visible = true },
            new() { Id = "document", Type = TiposBloqueDocumento.Comprobante, Visible = true },
            new() { Id = "customer", Type = TiposBloqueDocumento.Cliente, Visible = true },
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
            new() { Id = "observations", Type = TiposBloqueDocumento.Observaciones, Visible = true }
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
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class EmpresaDocumentData { public string Nombre { get; set; } = string.Empty; public string Cuit { get; set; } = string.Empty; public string Domicilio { get; set; } = string.Empty; public string Telefono { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; public byte[]? Logo { get; set; } }
public sealed class ComprobanteDocumentData { public string Numero { get; set; } = string.Empty; public DateTime Fecha { get; set; } public DateTime? FechaVencimiento { get; set; } public string Vendedor { get; set; } = string.Empty; public string CondicionVenta { get; set; } = string.Empty; public string Moneda { get; set; } = string.Empty; }
public sealed class ClienteDocumentData { public string Codigo { get; set; } = string.Empty; public string RazonSocial { get; set; } = string.Empty; public string Cuit { get; set; } = string.Empty; public string Domicilio { get; set; } = string.Empty; public string Telefono { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; }
public sealed class CotizacionDocumentItemData { public string Codigo { get; set; } = string.Empty; public string Descripcion { get; set; } = string.Empty; public decimal Cantidad { get; set; } public decimal Precio { get; set; } public decimal Descuento { get; set; } public decimal Total { get; set; } public bool ImpactaTotal { get; set; } }
public sealed class TotalesDocumentData { public decimal Neto { get; set; } public decimal Descuento { get; set; } public decimal Impuestos { get; set; } public decimal Total { get; set; } }

public sealed class DocumentRenderResult { public string Html { get; init; } = string.Empty; public int IdTemplate { get; init; } public string TipoDocumento { get; init; } = string.Empty; public string? UNegocio { get; init; } }
