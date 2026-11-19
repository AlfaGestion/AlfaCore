using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed record InformeTrabajoResumenDto(int IdInforme, string Etiqueta);

public interface IInformeTrabajoDocumentService
{
    /// <summary>HTML del informe con la plantilla "Informe de trabajo" del Diseñador de comprobantes. idInforme 0 = datos de ejemplo.</summary>
    Task<DocumentRenderResult> RenderAsync(int idInforme, string? uNegocio, DocumentTemplateDto? previewTemplate = null, CancellationToken ct = default);
    Task<byte[]> GeneratePdfAsync(int idInforme, string? uNegocio, DocumentTemplateDto? previewTemplate = null, CancellationToken ct = default);
    /// <summary>Últimos informes de tickets, para probar la plantilla en el diseñador. Vacío si la base no tiene informes.</summary>
    Task<IReadOnlyList<InformeTrabajoResumenDto>> RecientesAsync(int top = 20, CancellationToken ct = default);
}

/// <summary>
/// Dibuja los informes al cliente (TICK_INFORMES) con el motor de documentos, tipo INFORME_TRABAJO: la plantilla define la hoja
/// (logo, empresa, número de ticket, cliente, firma, pie) y el texto del informe va en el bloque Propuesta. Solo lee.
/// </summary>
public sealed class InformeTrabajoDocumentService(
    IConfiguration configuration,
    IDocumentTemplateService templates,
    IDocumentRenderer renderer,
    IDocumentPdfService pdfService,
    IUsuariosService usuariosService,
    IAppEventService appEvents,
    ISessionService sessionService) : IInformeTrabajoDocumentService
{
    private const string Tipo = TiposDocumentoCore.InformeTrabajo;

    /// <summary>Estilos del contenido del informe (títulos, tablas y avisos del editor de tickets) dentro del bloque Propuesta.</summary>
    internal const string CssContenido =
        ".proposal{display:block}.proposal .informe-titulo{font-size:15pt;margin:0 0 3mm}.proposal h1,.proposal h2{font-size:13pt;margin:5mm 0 2mm}"
        + ".proposal h3{font-size:11pt;margin:4mm 0 1.5mm}.proposal p{margin:0 0 2.5mm}.proposal ul,.proposal ol{margin:0 0 2.5mm;padding-left:6mm}.proposal li{margin:.6mm 0}"
        + ".proposal table{width:100%;border-collapse:collapse;font-size:9pt;margin:2mm 0 4mm}.proposal tr{break-inside:avoid}"
        + ".proposal th,.proposal td{border-bottom:1px solid #dce3e9;padding:1.6mm 2mm;text-align:left;vertical-align:top}.proposal th{color:#596579;font-size:8pt;text-transform:uppercase}"
        + ".proposal blockquote{border-left:3px solid #8aa0b8;margin:0 0 3mm;padding:1mm 3mm}.proposal hr{border:0;border-top:1px solid #dce3e9;margin:4mm 0}"
        + ".proposal .ticket-editor-banner{padding:2.5mm 3.5mm;border-radius:1.5mm;background:#e8f0fb;margin:0 0 3mm;break-inside:avoid}"
        + ".proposal .ticket-editor-banner--success{background:#e7f4ec}.proposal .ticket-editor-banner--warning{background:#fdf1dd}.proposal .ticket-editor-banner--danger{background:#fbe7e7}"
        + ".proposal .ticket-editor-banner__icon{display:none}.proposal .ticket-editor-banner p:last-child{margin:0}";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<DocumentRenderResult> RenderAsync(int idInforme, string? uNegocio, DocumentTemplateDto? previewTemplate = null, CancellationToken ct = default)
    {
        try
        {
            var template = previewTemplate ?? await templates.ResolveAsync(Tipo, uNegocio, ct);
            if (!string.Equals(template.TipoDocumento, Tipo, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("La plantilla no corresponde al informe de trabajo.");
            var definition = templates.DeserializeAndValidate(template.TemplateJson, Tipo);
            var data = idInforme > 0 ? await BuildDataAsync(idInforme, ct) : Ejemplo();
            var theme = await templates.GetGeneralThemeAsync(ct);
            var titulo = string.IsNullOrWhiteSpace(template.Nombre) || template.Nombre.Contains("(fallback)", StringComparison.OrdinalIgnoreCase)
                ? TiposDocumentoCore.NombrePredeterminado(Tipo) : template.Nombre;
            var html = Render(renderer, definition, data, template.CssCustom, theme, titulo);
            if (definition.Paper.Size == "Ticket80") html = await pdfService.PrepareHtmlAsync(html, ct);
            return new DocumentRenderResult
            {
                Html = html, IdTemplate = template.IdTemplate, TipoDocumento = template.TipoDocumento, UNegocio = template.UNegocio,
                Footer = Pie(definition, data.Empresa.Nombre)
            };
        }
        catch (AppUserFacingException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (ArgumentException) { throw; }
        catch (Exception ex)
        {
            var incidente = await appEvents.LogErrorAsync("Documentos", "RenderInformeTrabajo", ex, "No se pudo generar el informe de trabajo.", new { IdInforme = idInforme, UNegocio = uNegocio }, ct: ct);
            throw new AppUserFacingException("No se pudo generar el informe de trabajo.", incidente, ex);
        }
    }

    public async Task<byte[]> GeneratePdfAsync(int idInforme, string? uNegocio, DocumentTemplateDto? previewTemplate = null, CancellationToken ct = default)
    {
        var r = await RenderAsync(idInforme, uNegocio, previewTemplate, ct);
        return await pdfService.GenerateAsync(r.Html, r.Footer, ct);
    }

    public async Task<IReadOnlyList<InformeTrabajoResumenDto>> RecientesAsync(int top = 20, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var filas = await cn.QueryAsync<(int IdInforme, int Numero, string Titulo)>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.TICK_INFORMES', N'U') IS NULL
                SELECT TOP (0) 0 AS IdInforme, 0 AS Numero, N'' AS Titulo;
            ELSE
                SELECT TOP (@Top) i.IdInforme, t.Numero, i.Titulo
                FROM dbo.TICK_INFORMES i INNER JOIN dbo.TICK_TICKETS t ON t.IdTicket = i.IdTicket
                WHERE i.Baja = 0
                ORDER BY ISNULL(i.FechaHoraModificacion, i.FechaHoraAlta) DESC, i.IdInforme DESC;
            """, new { Top = Math.Clamp(top, 1, 100) }, cancellationToken: ct));
        return filas.Select(x => new InformeTrabajoResumenDto(x.IdInforme, $"Ticket #{x.Numero} - {x.Titulo}")).ToList();
    }

    /// <summary>Arma el HTML: el título del informe encabeza el contenido y los estilos del contenido van antes del CSS propio de la plantilla.</summary>
    internal static string Render(IDocumentRenderer renderer, DocumentTemplateDefinition definition, CotizacionDocumentData data, string? cssPlantilla, string? theme, string titulo)
        => renderer.RenderCotizacion(definition, data, CssContenido + (cssPlantilla ?? string.Empty), theme, titulo);

    private async Task<CotizacionDocumentData> BuildDataAsync(int idInforme, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var i = await cn.QueryFirstOrDefaultAsync<Fila>(new CommandDefinition("""
            SELECT i.Titulo, ISNULL(i.Html, N'') AS Html, t.Numero, ISNULL(i.FechaHoraModificacion, ISNULL(i.FechaHoraPublicacion, i.FechaHoraAlta)) AS Fecha,
                   ISNULL(NULLIF(LTRIM(RTRIM(i.UsuarioModificacion)), N''), ISNULL(i.UsuarioAlta, N'')) AS Usuario,
                   LTRIM(RTRIM(ISNULL(t.ClienteCodigo, N''))) AS ClienteCodigo, ISNULL(cli.RAZON_SOCIAL, N'') AS ClienteNombre,
                   ISNULL(LTRIM(RTRIM(cli.NUMERO_DOCUMENTO)), N'') AS ClienteCuit,
                   LTRIM(RTRIM(ISNULL(cli.CALLE, N'') + CASE WHEN NULLIF(LTRIM(RTRIM(cli.LOCALIDAD)), N'') IS NULL THEN N'' ELSE N', ' + LTRIM(RTRIM(cli.LOCALIDAD)) END)) AS ClienteDomicilio,
                   COALESCE(NULLIF(LTRIM(RTRIM(co.Celular)), N''), NULLIF(LTRIM(RTRIM(co.Telefono)), N''), NULLIF(LTRIM(RTRIM(cli.TELEFONO)), N''), N'') AS ClienteTelefono,
                   COALESCE(NULLIF(LTRIM(RTRIM(co.Email)), N''), NULLIF(LTRIM(RTRIM(cli.MAIL)), N''), N'') AS ClienteEmail
            FROM dbo.TICK_INFORMES i
            INNER JOIN dbo.TICK_TICKETS t ON t.IdTicket = i.IdTicket
            LEFT JOIN dbo.MA_CONTACTOS co ON co.id = t.IdContacto
            OUTER APPLY (SELECT TOP (1) c.RAZON_SOCIAL, c.NUMERO_DOCUMENTO, c.CALLE, c.LOCALIDAD, c.TELEFONO, c.MAIL FROM dbo.VT_CLIENTES c WHERE LTRIM(RTRIM(c.CODIGO)) = LTRIM(RTRIM(t.ClienteCodigo))) cli
            WHERE i.IdInforme = @IdInforme AND i.Baja = 0;
            """, new { IdInforme = idInforme }, cancellationToken: ct))
            ?? throw new InvalidOperationException("El informe no existe.");

        var empresa = await EmpresaAsync(cn, ct);
        byte[]? firma = null;
        var firmante = i.Usuario.Trim();
        if (firmante.Length > 0)
        {
            var dto = await usuariosService.GetSignatureAsync(firmante, ct);
            firma = dto?.Imagen;
            if (!string.IsNullOrWhiteSpace(dto?.NombreMostrar)) firmante = dto.NombreMostrar.Trim();
        }
        return Datos(empresa, i.Numero, i.Fecha, i.Titulo, i.Html, firma, firmante,
            new ClienteDocumentData { Codigo = i.ClienteCodigo, RazonSocial = i.ClienteNombre, Cuit = i.ClienteCuit, Domicilio = i.ClienteDomicilio, Telefono = i.ClienteTelefono, Email = i.ClienteEmail });
    }

    internal static CotizacionDocumentData Datos(EmpresaDocumentData empresa, int numeroTicket, DateTime fecha, string titulo, string html, byte[]? firma, string firmante, ClienteDocumentData cliente)
        => new()
        {
            Empresa = empresa,
            Comprobante = new ComprobanteDocumentData { Numero = $"#{numeroTicket}", Fecha = fecha, Vendedor = firmante },
            Cliente = cliente,
            PropuestaHtml = $"<h2 class=\"informe-titulo\">{InformeHtmlSanitizer.Codificar(titulo)}</h2>" + InformeHtmlSanitizer.Limpiar(html),
            FirmaBytes = firma,
            FirmanteNombre = firmante,
            IncluyePortada = false
        };

    /// <summary>Datos de ejemplo para el diseñador cuando la base no tiene informes.</summary>
    internal static CotizacionDocumentData Ejemplo()
        => Datos(new EmpresaDocumentData { Nombre = "Su empresa", Cuit = "30-00000000-0", Domicilio = "Domicilio de la empresa", Telefono = "(011) 0000-0000", Email = "soporte@empresa.com" },
            123, DateTime.Today, "Revisión del sistema de facturación",
            """
            <div class="ticket-editor-banner ticket-editor-banner--success"><div class="ticket-editor-banner__body"><p><strong>Situación resuelta.</strong> El inconveniente quedó corregido y verificado.</p></div></div>
            <h2>Qué pasó</h2><p>Descripción breve del problema informado por el cliente.</p>
            <h2>Qué se hizo</h2><ul><li>Se identificó la causa.</li><li>Se aplicó la corrección y se controló el resultado.</li></ul>
            <table><thead><tr><th>Comprobante</th><th>Fecha</th><th>Estado</th></tr></thead><tbody><tr><td>Factura A 0001-00000123</td><td>18/11</td><td>Corregida</td></tr></tbody></table>
            <h2>Próximos pasos</h2><p>Sin pendientes.</p>
            """, null, "Soporte", new ClienteDocumentData { RazonSocial = "Cliente de ejemplo S.A.", Telefono = "11 1234-5678", Email = "cliente@ejemplo.com" });

    private static async Task<EmpresaDocumentData> EmpresaAsync(SqlConnection cn, CancellationToken ct)
    {
        var config = (await cn.QueryAsync<(string Clave, string? Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) AS Clave,
                   CASE WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR)) ELSE ISNULL(CAST(ValorAux AS nvarchar(max)), '') END AS Valor
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'NOMBRE', N'CUIT', N'DOMICILIO', N'DIRECCION', N'TELEFONO', N'TEL', N'EMAIL', N'MAIL');
            """, cancellationToken: ct))).GroupBy(x => x.Clave, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Valor ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        string V(params string[] claves) => claves.Select(k => config.TryGetValue(k, out var v) ? v : string.Empty).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        byte[]? logo = null;
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.TA_LOGOS') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 1)
            logo = await cn.QueryFirstOrDefaultAsync<byte[]>(new CommandDefinition("SELECT TOP (1) IMAGEN FROM dbo.TA_LOGOS WHERE IDLOGO = 'LOGOEMPRESA';", cancellationToken: ct));
        return new EmpresaDocumentData { Nombre = V("NOMBRE"), Cuit = V("CUIT"), Domicilio = V("DOMICILIO", "DIRECCION"), Telefono = V("TELEFONO", "TEL"), Email = V("EMAIL", "MAIL"), Logo = logo };
    }

    private static DocumentPdfFooterOptions? Pie(DocumentTemplateDefinition definition, string empresa)
    {
        var pie = definition.Blocks.FirstOrDefault(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Pie, StringComparison.OrdinalIgnoreCase));
        if (pie is null) return null;
        var campos = pie.VisibleFields;
        return new DocumentPdfFooterOptions(campos is null || campos.Contains("NumeroPagina", StringComparer.OrdinalIgnoreCase),
            campos is null || campos.Contains("NombreEmpresa", StringComparer.OrdinalIgnoreCase), empresa);
    }

    private sealed class Fila
    {
        public string Titulo { get; set; } = string.Empty;
        public string Html { get; set; } = string.Empty;
        public int Numero { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string ClienteCodigo { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;
        public string ClienteCuit { get; set; } = string.Empty;
        public string ClienteDomicilio { get; set; } = string.Empty;
        public string ClienteTelefono { get; set; } = string.Empty;
        public string ClienteEmail { get; set; } = string.Empty;
    }
}
