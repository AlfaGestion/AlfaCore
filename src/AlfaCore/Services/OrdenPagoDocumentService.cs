using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Arma los datos de una orden de pago grabada (V_MV_CPTE + MV_APLICACION + MV_ASIENTOS) y los dibuja con el motor de documentos
/// (plantilla del Diseñador de comprobantes, tipos ORDEN_PAGO y PAGO_PROFORMA). Solo lee.
/// </summary>
public sealed class OrdenPagoDocumentService(
    IConfiguration configuration,
    IDocumentTemplateService templates,
    IDocumentRenderer renderer,
    IDocumentPdfService pdfService,
    IAppEventService appEvents,
    ILogger<OrdenPagoDocumentService> logger,
    ISessionService sessionService) : IOrdenPagoDocumentService
{
    private const string ModuleName = "Documentos";
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<DocumentRenderResult> RenderAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null)
    {
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            var armado = await BuildDataAsync(cn, (tc ?? string.Empty).Trim(), (idComprobante ?? string.Empty).Trim(), ct)
                ?? throw new InvalidOperationException("La orden de pago no existe o está anulada.");
            var data = armado.Datos;
            var tipo = data.Comprobante.TipoDocumento;
            var template = previewTemplate ?? await templates.ResolveAsync(tipo, uNegocio ?? armado.UNegocio, ct);
            if (!string.Equals(template.TipoDocumento, tipo, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("La plantilla no corresponde al tipo de comprobante seleccionado.");
            var definition = templates.DeserializeAndValidate(template.TemplateJson, tipo);
            var theme = await templates.GetGeneralThemeAsync(ct);
            var html = renderer.RenderFactura(definition, data, template.CssCustom, theme, TiposDocumentoCore.NombrePredeterminado(tipo));
            if (definition.TotalesAlPiePagina || definition.Paper.Size == "Ticket80") html = await pdfService.PrepareHtmlAsync(html, ct);
            logger.LogInformation("Documentos: HTML de orden de pago {Tc}/{IdComprobante}, plantilla {IdTemplate}.", tc, idComprobante, template.IdTemplate);
            return new DocumentRenderResult
            {
                Html = html, IdTemplate = template.IdTemplate, TipoDocumento = template.TipoDocumento, UNegocio = template.UNegocio,
                Footer = FacturaDocumentService.BuildFooterOptions(definition, data.Empresa.Nombre)
            };
        }
        catch (AppUserFacingException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (ArgumentException) { throw; }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, "RenderOrdenPago", ex, "No se pudo generar la vista previa de la orden de pago.", new { Tc = tc, IdComprobante = idComprobante }, ct: ct);
            throw new AppUserFacingException("No se pudo generar la vista previa de la orden de pago.", incidentId, ex);
        }
    }

    public async Task<byte[]> GeneratePdfAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null)
    {
        var result = await RenderAsync(tc, idComprobante, uNegocio, ct, previewTemplate);
        return await pdfService.GenerateAsync(result.Html, result.Footer, ct);
    }

    public async Task<IReadOnlyList<FacturaResumenDto>> SearchRecientesAsync(string tipoDocumento, int top = 20, CancellationToken ct = default)
    {
        var tc = string.Equals(tipoDocumento, TiposDocumentoCore.PagoProforma, StringComparison.OrdinalIgnoreCase) ? "PGFP"
            : string.Equals(tipoDocumento, TiposDocumentoCore.OrdenPago, StringComparison.OrdinalIgnoreCase) ? "OPG" : string.Empty;
        if (tc.Length == 0) return [];
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        return (await cn.QueryAsync<FacturaResumenDto>(new CommandDefinition("""
            SELECT TOP (@Top) LTRIM(RTRIM(TC)) AS Tc, LTRIM(RTRIM(IDCOMPROBANTE)) AS IdComprobante,
                LTRIM(RTRIM(SUCURSAL)) + '-' + LTRIM(RTRIM(NUMERO)) AS Numero, FECHA AS Fecha, ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS Cliente
            FROM dbo.V_MV_CPTE
            WHERE UPPER(LTRIM(RTRIM(TC))) = @Tc AND ISNULL(ANULADA, 0) = 0 AND ISNULL(IDCOMPLEMENTO, 0) = 0 AND ISNULL(FINALIZADA, 0) = 1
            ORDER BY FECHA DESC, ID DESC;
            """, new { Top = Math.Clamp(top, 1, 200), Tc = tc }, cancellationToken: ct))).AsList();
    }

    internal sealed record Armado(FacturaDocumentData Datos, string UNegocio);

    private sealed class CabeceraRow
    {
        public string Tc { get; set; } = ""; public string Sucursal { get; set; } = ""; public string Numero { get; set; } = ""; public string Letra { get; set; } = "";
        public DateTime Fecha { get; set; } public string UNegocio { get; set; } = ""; public string Codigo { get; set; } = ""; public string RazonSocial { get; set; } = "";
        public string Domicilio { get; set; } = ""; public string Localidad { get; set; } = ""; public string Telefono { get; set; } = "";
        public string DocumentoNumero { get; set; } = ""; public string DocumentoTipo { get; set; } = ""; public string CondicionIva { get; set; } = "";
        public string Email { get; set; } = ""; public decimal Total { get; set; } public string Moneda { get; set; } = "";
        public decimal Retencion { get; set; } public string RetencionNro { get; set; } = ""; public string Comentarios { get; set; } = "";
    }

    /// <summary>Datos de la orden para el motor de documentos. Null si no existe, está anulada o no es una OPG/PGFP.</summary>
    internal static async Task<Armado?> BuildDataAsync(SqlConnection cn, string tc, string idComprobante, CancellationToken ct, SqlTransaction? tx = null)
    {
        var h = await cn.QueryFirstOrDefaultAsync<CabeceraRow>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(v.TC)) Tc, ISNULL(LTRIM(RTRIM(v.SUCURSAL)),'') Sucursal, ISNULL(LTRIM(RTRIM(v.NUMERO)),'') Numero, ISNULL(LTRIM(RTRIM(v.LETRA)),'') Letra,
                v.FECHA Fecha, ISNULL(LTRIM(RTRIM(v.UNEGOCIO)),'') UNegocio, ISNULL(LTRIM(RTRIM(v.CUENTA)),'') Codigo, ISNULL(LTRIM(RTRIM(v.NOMBRE)),'') RazonSocial,
                ISNULL(LTRIM(RTRIM(v.DOMICILIO)),'') Domicilio, ISNULL(LTRIM(RTRIM(v.LOCALIDAD)),'') Localidad, ISNULL(LTRIM(RTRIM(v.TELEFONO)),'') Telefono,
                ISNULL(LTRIM(RTRIM(v.DOCUMENTONUMERO)),'') DocumentoNumero, ISNULL(LTRIM(RTRIM(td.DESCRIPCION)),'') DocumentoTipo,
                ISNULL(LTRIM(RTRIM(v.CONDICIONIVA)),'') CondicionIva, ISNULL(LTRIM(RTRIM(p.MAIL)),'') Email,
                CONVERT(decimal(15,2), ISNULL(v.IMPORTE,0)) Total, ISNULL(LTRIM(RTRIM(v.MONEDA)),'') Moneda,
                CONVERT(decimal(15,2), ISNULL(v.RETGAN_Importe,0)) Retencion, ISNULL(LTRIM(RTRIM(v.RETGAN_NRO)),'') RetencionNro, ISNULL(LTRIM(RTRIM(v.COMENTARIOS)),'') Comentarios
            FROM dbo.V_MV_CPTE v
            LEFT JOIN dbo.TA_TIPODOCUMENTO td ON UPPER(LTRIM(RTRIM(td.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(v.DOCUMENTOTIPO,''))))
            LEFT JOIN dbo.Vt_Proveedores p ON LTRIM(RTRIM(p.CODIGO)) = LTRIM(RTRIM(v.CUENTA))
            WHERE v.TC = @Tc AND v.IDCOMPROBANTE = @Id AND ISNULL(v.IDCOMPLEMENTO,0) = 0 AND ISNULL(v.ANULADA,0) = 0;
            """, new { Tc = tc, Id = idComprobante }, tx, cancellationToken: ct));
        if (h is null || !TiposDocumentoCore.EsOrdenPago(TiposDocumentoCore.TipoParaComprobante(h.Tc, h.Letra))) return null;

        var condIva = h.CondicionIva.Length == 0 ? "" : await cn.ExecuteScalarAsync<string>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.TA_CONDIVA', N'U') IS NULL SELECT N'' ELSE
            SELECT ISNULL((SELECT TOP (1) LTRIM(RTRIM(DESCRIPCION)) FROM dbo.TA_CONDIVA WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(@C)), N'');
            """, new { C = h.CondicionIva }, tx, cancellationToken: ct)) ?? "";

        // Comprobantes cancelados por la orden (propios y con créditos aplicados), uno por comprobante.
        var cancelados = (await cn.QueryAsync<(string Tc, string Cpte, DateTime? Fecha, decimal Importe)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(ap.TCO_ORIGEN)), ap.IDComprobante_Origen, MIN(f.FECHA), SUM(ap.IMPORTE)
            FROM dbo.MV_APLICACION ap
            LEFT JOIN dbo.C_MV_CPTE f ON f.TC = ap.TCO_ORIGEN AND f.IDCOMPROBANTE = ap.IDComprobante_Origen
            WHERE (LTRIM(RTRIM(ap.TC_PRINT)) = @Tc AND ap.IDCOMPROBANTE_PRINT = @Id) OR (LTRIM(RTRIM(ap.TC)) = @Tc AND ap.IDCOMPROBANTE = @Id)
            GROUP BY LTRIM(RTRIM(ap.TCO_ORIGEN)), ap.IDComprobante_Origen
            ORDER BY MIN(f.FECHA), ap.IDComprobante_Origen;
            """, new { Tc = h.Tc, Id = idComprobante }, tx, cancellationToken: ct))).ToList();

        var creditos = (await cn.QueryAsync<(string Tc, string Cpte, decimal Importe)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(ap.TC)), ap.IDCOMPROBANTE, SUM(ap.IMPORTE)
            FROM dbo.MV_APLICACION ap
            WHERE LTRIM(RTRIM(ap.TC_PRINT)) = @Tc AND ap.IDCOMPROBANTE_PRINT = @Id AND NOT (LTRIM(RTRIM(ap.TC)) = @Tc AND ap.IDCOMPROBANTE = @Id)
            GROUP BY LTRIM(RTRIM(ap.TC)), ap.IDCOMPROBANTE
            ORDER BY ap.IDCOMPROBANTE;
            """, new { Tc = h.Tc, Id = idComprobante }, tx, cancellationToken: ct))).ToList();

        var medios = (await cn.QueryAsync<(string Cuenta, decimal Importe, string Cheque, DateTime? Vencimiento, bool EsRetencion)>(new CommandDefinition($"""
            SELECT ISNULL(NULLIF(LTRIM(RTRIM(c.DESCRIPCION)),''), LTRIM(RTRIM(a.CUENTA))), a.IMPORTE, ISNULL(LTRIM(RTRIM(a.NroComprobanteBancario)),''), a.VENCIMIENTO,
                CONVERT(bit, {OrdenPagoService.SqlLineaEsRetencion})
            FROM dbo.MV_ASIENTOS a LEFT JOIN dbo.MA_CUENTAS c ON c.CODIGO = a.CUENTA
            WHERE a.TC = @Tc AND a.SUCURSAL = @S AND a.NUMERO = @N AND a.LETRA = @L AND a.[DEBE-HABER] = 'H'
            ORDER BY a.[NUMERO ASIENTO], a.SECUENCIA;
            """, new { Tc = h.Tc, S = h.Sucursal, N = h.Numero, L = h.Letra, ret = h.Retencion }, tx, cancellationToken: ct))).ToList();

        // Lo que la orden aplicó por sí misma; si el total la supera, la diferencia quedó como anticipo a favor del proveedor.
        var aplicadoPorLaOrden = await cn.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT CONVERT(decimal(18,2), ISNULL(SUM(IMPORTE),0)) FROM dbo.MV_APLICACION
            WHERE LTRIM(RTRIM(TC)) = @Tc AND IDCOMPROBANTE = @Id AND LTRIM(RTRIM(ISNULL(TC_PRINT,''))) = @Tc AND IDCOMPROBANTE_PRINT = @Id;
            """, new { Tc = h.Tc, Id = idComprobante }, tx, cancellationToken: ct));

        var (empresa, condicionEmisor, iibb, inicio, _) = await FacturaDocumentService.BuildEmpresaAsync(cn, tx, h.UNegocio, ct);
        var tipo = TiposDocumentoCore.TipoParaComprobante(h.Tc, h.Letra);
        static string Formato(string id) => CompraDocumentoMailService.FormatoComprobante(id);
        var moneda = h.Moneda is "" or "1" ? "" : h.Moneda;
        var datos = new FacturaDocumentData
        {
            Empresa = empresa, CondicionIvaEmisor = condicionEmisor, IngresosBrutosEmisor = iibb, InicioActividadesEmisor = inicio,
            EtiquetaTercero = "Proveedor", Concepto = h.Comentarios, AnticipoGenerado = Math.Max(0, h.Total - aplicadoPorLaOrden),
            Comprobante = new FacturaComprobanteDocumentData
            {
                TipoDocumento = tipo, Tc = h.Tc, Letra = "X", PuntoVenta = h.Sucursal, Numero = h.Numero, Fecha = h.Fecha
            },
            Cliente = new FacturaClienteDocumentData
            {
                Codigo = h.Codigo, RazonSocial = h.RazonSocial, DocumentoTipoDescripcion = h.DocumentoTipo, DocumentoNumero = h.DocumentoNumero,
                CondicionIvaDescripcion = condIva, Domicilio = h.Domicilio, Localidad = h.Localidad, Telefono = h.Telefono, Email = h.Email
            },
            Items = [.. cancelados.Select(c => new FacturaDocumentItemData
            {
                Codigo = c.Tc, Descripcion = Formato(c.Cpte) + (c.Fecha is { } f ? " · " + f.ToString("dd/MM/yyyy", Ar) : ""),
                Cantidad = 1, PrecioUnitario = c.Importe, Subtotal = c.Importe
            })],
            Totales = new FacturaTotalesDocumentData { Total = h.Total, Moneda = moneda },
            MediosPago = [.. medios.Select(m => new FacturaPagoDocumentData(
                m.EsRetencion ? "Retención de Ganancias" + (h.RetencionNro.Length > 0 ? " · Certificado N° " + h.RetencionNro : "")
                : m.Cuenta + (m.Cheque.Length > 0 ? " · Cheque " + m.Cheque + (m.Vencimiento is { } v ? " al " + v.ToString("dd/MM/yyyy", Ar) : "") : ""), m.Importe))],
            CreditosAplicados = [.. creditos.Select(c => new FacturaPagoDocumentData(c.Tc + " " + Formato(c.Cpte), c.Importe))]
        };
        return new Armado(datos, h.UNegocio);
    }
}
