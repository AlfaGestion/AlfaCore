using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using System.Net;
using System.Text.Json;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesHomologacionDocumentoTests
{
    [CargaComprobantesDocumentoFact]
    public async Task ExportaAutorizacionExistenteSinSolicitarOtroCae()
    {
        var operacion = Guid.Parse(Environment.GetEnvironmentVariable("ALFACORE_ARCA_DOCUMENTO_OP")!);
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        var dato = await cn.QuerySingleAsync<Documento>("""
            SELECT p.Solicitud,p.Cae,p.Vencimiento,LTRIM(RTRIM(c.IDCOMPROBANTE)) Origen,
                c.NOMBRE Cliente,p.Estado FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA p
            JOIN dbo.V_MV_CPTE c ON c.ID=p.OrigenId WHERE p.Operacion=@Operacion;
            """, new { Operacion = operacion });
        Assert.Equal("APROBADA", dato.Estado);
        var req = JsonSerializer.Deserialize<ArcaCaeSolicitudDto>(dato.Solicitud)!;
        Assert.Equal(1007, req.PtoVta);
        string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        string Importe(decimal d) => d.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("es-AR"));
        var letra = req.CbteTipo == 1 ? "A" : "B";
        var numero = $"{req.PtoVta:D5}-{req.CbteDesde:D8}";
        var iva = string.Join("", req.Ivas.Select(x => $"<tr><td>IVA · código ARCA {x.Id}</td><td>{Importe(x.BaseImponible)}</td><td>{Importe(x.Importe)}</td></tr>"));
        var html = $$"""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><title>Factura {{letra}} {{numero}} — homologación</title>
            <style>body{font-family:Arial,sans-serif;color:#253044;margin:48px;font-size:14px}h1{font-size:26px;margin:0 0 10px}.aviso{border:2px solid #64748b;padding:14px;margin-bottom:28px;font-weight:bold}.cabecera{display:flex;justify-content:space-between;gap:24px}.letra{font-size:36px;border:1px solid #cbd5e1;padding:16px 28px}table{width:100%;border-collapse:collapse;margin:30px 0}th,td{padding:12px;text-align:right;border-bottom:1px solid #e2e8f0}th:first-child,td:first-child{text-align:left}.total{font-size:24px;font-weight:bold;text-align:right}.pie{margin-top:40px;border-top:1px solid #cbd5e1;padding-top:20px;font-size:12px;line-height:1.7}</style></head><body>
            <div class="aviso">HOMOLOGACIÓN · PRUEBA DE AUTORIZACIÓN ELECTRÓNICA</div>
            <div class="cabecera"><div><h1>Factura {{letra}}</h1><p>Número {{numero}}</p><p>Fecha: {{req.CbteFch:dd/MM/yyyy}}</p><p>CUIT emisor: {{H(req.Cuit)}}</p></div><div class="letra">{{letra}}</div></div>
            <p><strong>Cliente:</strong> {{H(dato.Cliente)}}</p><p>Documento receptor: tipo {{req.DocTipo}} · {{H(req.DocNro)}}</p>
            <p>Origen de los datos: NP {{H(dato.Origen)}} · Concepto productos</p>
            <table><thead><tr><th>Componente fiscal</th><th>Base imponible $</th><th>IVA $</th></tr></thead><tbody>{{iva}}</tbody></table>
            <p class="total">Total $ {{Importe(req.ImpTotal)}}</p>
            <div class="pie"><strong>CAE: {{H(dato.Cae)}}</strong><br>Vencimiento: {{dato.Vencimiento:dd/MM/yyyy}}<br>
            Operación de prueba: {{operacion}}<br>Autorización obtenida en el ambiente de homologación de ARCA.
            El registro comercial de factura, stock, contabilidad y aplicaciones sigue en desarrollo; el pedido conserva sus pendientes.</div>
            </body></html>
            """;
        var root = EncontrarRepo();
        var carpeta = Path.Combine(root, "artifacts", "ventas", "homologacion");
        Directory.CreateDirectory(carpeta);
        var baseName = Path.Combine(carpeta, $"factura-{letra}-{numero}");
        await File.WriteAllTextAsync(baseName + ".html", html);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = CargaComprobantesBrowserFactAttribute.BrowserPath });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(html);
        await page.PdfAsync(new() { Path = baseName + ".pdf", Format = "A4", PrintBackground = true });
        Console.WriteLine("Comprobante de homologación: " + baseName + ".pdf");
        Assert.True(new FileInfo(baseName + ".pdf").Length > 1000);
    }

    private static string EncontrarRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "AlfaCore", "AlfaCore.csproj"))) return d.FullName;
        throw new InvalidOperationException("No se encontró el repositorio.");
    }
    private sealed class Documento
    {
        public string Solicitud { get; set; } = "";
        public string Cae { get; set; } = "";
        public DateTime Vencimiento { get; set; }
        public string Origen { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Estado { get; set; } = "";
    }
}

public sealed class CargaComprobantesDocumentoFactAttribute : FactAttribute
{
    public CargaComprobantesDocumentoFactAttribute()
    {
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("ALFACORE_ARCA_DOCUMENTO_OP"), out _) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")) || CargaComprobantesBrowserFactAttribute.BrowserPath is null)
            Skip = "Exportación opcional de una autorización existente; requiere GUID, SQL y Chromium. No solicita CAE.";
    }
}
