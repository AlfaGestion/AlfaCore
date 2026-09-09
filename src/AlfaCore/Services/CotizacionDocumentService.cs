using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Adapta la versión de Cotización al contrato de documentos sin exponer tablas legacy al renderer.</summary>
public sealed class CotizacionDocumentService(
    IConfiguration configuration,
    ICotizacionesService cotizacionesService,
    IDocumentTemplateService templates,
    IDocumentRenderer renderer,
    IDocumentPdfService pdfService,
    IUsuariosService usuariosService,
    IAppEventService appEvents,
    ILogger<CotizacionDocumentService> logger) : ICotizacionDocumentService
{
    private string ConnectionString => configuration.GetConnectionString("AlfaGestion")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<DocumentRenderResult> RenderAsync(long idVersion, string? uNegocio, CancellationToken ct = default)
    {
        try
        {
            var detail = await cotizacionesService.GetVersionDetailAsync(idVersion, ct)
                ?? throw new InvalidOperationException("La cotización indicada no existe.");
            var template = await templates.ResolveAsync(TiposDocumentoCore.Cotizacion, uNegocio, ct);
            var definition = templates.DeserializeAndValidate(template.TemplateJson);
            var data = await BuildDataAsync(detail, ct);
            data.IncluyePortada = detail.IncluyePortada;
            data.PortadaBytes = template.TienePortada ? await templates.GetPortadaImageBytesAsync(template.IdTemplate, ct) : null;
            var theme = await templates.GetGeneralThemeAsync(ct);
            var html = renderer.RenderCotizacion(definition, data, template.CssCustom, theme);
            logger.LogInformation("Documentos: HTML de cotización {IdVersion}, plantilla {IdTemplate}, UNegocio {UNegocio}.", idVersion, template.IdTemplate, uNegocio ?? "GLOBAL");
            return new DocumentRenderResult { Html = html, IdTemplate = template.IdTemplate, TipoDocumento = template.TipoDocumento, UNegocio = template.UNegocio };
        }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync("Documentos", "RenderCotizacion", ex, "No se pudo generar la vista previa de la cotización.", new { IdVersion = idVersion, UNegocio = uNegocio }, ct: ct);
            throw new AppUserFacingException("No se pudo generar la vista previa de la cotización.", incidentId, ex);
        }
    }

    public async Task<byte[]> GeneratePdfAsync(long idVersion, string? uNegocio, CancellationToken ct = default)
    {
        var result = await RenderAsync(idVersion, uNegocio, ct);
        var pdf = await pdfService.GenerateAsync(result.Html, ct);
        logger.LogInformation("Documentos: PDF beta de cotización {IdVersion}, plantilla {IdTemplate}.", idVersion, result.IdTemplate);
        return pdf;
    }

    private async Task<CotizacionDocumentData> BuildDataAsync(CotizacionVersionDetailDto detail, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var config = (await cn.QueryAsync<ConfiguracionEmpresaRow>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) AS Clave,
                   CASE WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR))
                        ELSE ISNULL(CAST(ValorAux AS nvarchar(max)), '') END AS Valor
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'NOMBRE', N'CUIT', N'DOMICILIO', N'DIRECCION', N'TELEFONO', N'TEL', N'EMAIL', N'MAIL');
            """, cancellationToken: ct))).ToDictionary(x => x.Clave, x => x.Valor ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        byte[]? logo = null;
        if (await ExistsAsync(cn, "dbo.TA_LOGOS", ct))
            logo = await cn.QueryFirstOrDefaultAsync<byte[]>(new CommandDefinition("SELECT TOP (1) IMAGEN FROM dbo.TA_LOGOS WHERE IDLOGO = 'LOGOEMPRESA';", cancellationToken: ct));

        byte[]? firma = null;
        var firmante = (detail.UsuarioAlta ?? string.Empty).Trim();
        if (firmante.Length > 0)
            firma = await usuariosService.GetSignatureBytesAsync(firmante, ct);

        return new CotizacionDocumentData
        {
            Empresa = new EmpresaDocumentData
            {
                Nombre = Value(config, "NOMBRE"), Cuit = Value(config, "CUIT"),
                Domicilio = First(config, "DOMICILIO", "DIRECCION"), Telefono = First(config, "TELEFONO", "TEL"),
                Email = First(config, "EMAIL", "MAIL"), Logo = logo
            },
            Comprobante = new ComprobanteDocumentData
            {
                Numero = detail.CodigoVisible, Fecha = detail.Fecha, FechaVencimiento = detail.FechaVencimiento,
                Vendedor = detail.UsuarioAlta ?? string.Empty, Moneda = detail.CodigoMoneda
            },
            Cliente = new ClienteDocumentData
            {
                Codigo = detail.CodigoCliente ?? string.Empty, RazonSocial = detail.EmpresaProspecto,
                Cuit = detail.DocumentoFiscal, Telefono = detail.ContactoTelefono, Email = detail.ContactoEmail
            },
            Items = detail.Lineas.OrderBy(x => x.Orden).Select(x => new CotizacionDocumentItemData
            {
                Codigo = x.CodigoRef ?? string.Empty, Descripcion = x.Descripcion, Cantidad = x.Cantidad,
                Precio = x.PrecioUnitario, Descuento = x.PorcentajeDescuento, Total = x.Subtotal, ImpactaTotal = x.ImpactaTotal
            }).ToList(),
            Totales = new TotalesDocumentData { Neto = detail.Subtotal, Descuento = detail.TotalDescuento, Total = detail.Total },
            PropuestaHtml = detail.CuerpoPropuesta,
            FirmaBytes = firma,
            FirmanteNombre = firmante
        };
    }

    private static string Value(IReadOnlyDictionary<string, string> source, string key) => source.TryGetValue(key, out var value) ? value : string.Empty;
    private static string First(IReadOnlyDictionary<string, string> source, params string[] keys) => keys.Select(x => Value(source, x)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
    private static async Task<bool> ExistsAsync(SqlConnection cn, string name, CancellationToken ct) => await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(@Name) IS NULL THEN 0 ELSE 1 END;", new { Name = name }, cancellationToken: ct)) == 1;
    private sealed class ConfiguracionEmpresaRow { public string Clave { get; set; } = string.Empty; public string? Valor { get; set; } }
}
