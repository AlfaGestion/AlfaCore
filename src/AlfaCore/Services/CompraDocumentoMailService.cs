using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Envío por mail de lo que se le manda al proveedor, como la impresión diferida del VB6 (FrmRp_Diferida): la orden de compra,
/// el pago / orden de pago y el certificado de cada retención (Ganancias, IVA, Ingresos Brutos, SUSS) de un pago.
/// Cada envío se registra en V_MA_EMAILMOV con el mismo formato que Alfa Gestión, así se ve cuándo se mandó y se puede reenviar.
/// </summary>
public sealed class CompraDocumentoMailService(ISessionService sesiones, IAppUserSessionService usuarios, IPermissionService permisos,
    IAppEventService eventos, CompraCargaService compras, IComprobanteViewerService visor, IDocumentPdfService pdf, CorreoSalienteService correo)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");
    private static readonly string[] TiposCompra = ["OC", "RMC"];
    private static readonly string[] TiposPago = ["PG", "OPG", "PGFP"];
    private const string ClaveMailPrueba = "ALFACORE_MAIL_PRUEBA";

    // Textos configurables por documento (TA_CONFIGURACION, grupo COMPRAS); si no están cargados se usa el de abajo.
    private static string ClaveAsunto(string clase) => $"COMPRAS-MAIL-ASUNTO-{clase}";
    private static string ClaveMensaje(string clase) => $"COMPRAS-MAIL-MENSAJE-{clase}";

    private static readonly Dictionary<string, (string Asunto, string Mensaje)> TextosPorDefecto = new()
    {
        ["OC"] = ("Orden de compra <COMPROBANTE>", "Estimados <NOMBRE>:\n\nAdjuntamos la orden de compra <COMPROBANTE> del <FECHA>.\n\nSaludos."),
        ["PAGO"] = ("Pago <COMPROBANTE>", "Estimados <NOMBRE>:\n\nAdjuntamos el comprobante de pago <COMPROBANTE> del <FECHA> por <IMPORTE>.\n\nSaludos."),
        ["RETENCION"] = ("Certificado de retención de <IMPUESTO> - pago <COMPROBANTE>",
            "Estimados <NOMBRE>:\n\nAdjuntamos el certificado de retención de <IMPUESTO> correspondiente al pago <COMPROBANTE> del <FECHA>.\n\nSaludos.")
    };

    public static string ClaseTexto(string tc, string certificado)
        => certificado.Length > 0 ? "RETENCION" : TiposCompra.Contains(tc) ? "OC" : "PAGO";

    // ---- Listado ----------------------------------------------------------------------------------------------------

    public Task<IReadOnlyList<CompraPagoFila>> ListarPagosAsync(DateTime desde, DateTime hasta, string texto, string certificado, CancellationToken ct = default)
        => EjecutarAsync("ListarPagos", async (cn, unidades, _) =>
        {
            texto = (texto ?? string.Empty).Trim();
            certificado = (certificado ?? string.Empty).Trim().ToUpperInvariant();
            if (desde.Year < 1900 || hasta < desde || hasta.Year > 9998 || texto.Length > 100 || (certificado.Length > 0 && !CompraCertificado.Todos.Contains(certificado)))
                throw new InvalidOperationException("Revisá las fechas y los filtros de pagos.");
            var filas = (await cn.QueryAsync<CompraPagoFila>(new CommandDefinition("""
                SELECT TOP(501) c.ID Id, LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante, c.FECHA Fecha,
                  LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta, LTRIM(RTRIM(ISNULL(c.NOMBRE,''))) Nombre, LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) Unidad,
                  ISNULL(c.IMPORTE,0) Importe, LTRIM(RTRIM(ISNULL(p.MAIL,''))) Mail,
                  ISNULL(c.RETGAN_Importe,0) RetGanancias, ISNULL(c.RETIVA_Importe,0) RetIva,
                  ISNULL(c.RETIBR_Importe,0) RetIibb, ISNULL(c.RETSUSS_Importe,0) RetSuss
                FROM dbo.V_MV_CPTE c
                LEFT JOIN dbo.VT_PROVEEDORES p ON LTRIM(RTRIM(p.CODIGO)) = LTRIM(RTRIM(c.CUENTA))
                WHERE LTRIM(RTRIM(c.TC)) IN ('PG','OPG','PGFP') AND ISNULL(c.ANULADA,0) = 0 AND ISNULL(c.IDCOMPLEMENTO,0) = 0
                  AND c.FECHA >= @desde AND c.FECHA < @fin AND LTRIM(RTRIM(c.UNEGOCIO)) IN @unidades
                  AND (@texto = '' OR c.NOMBRE LIKE @patron OR c.CUENTA LIKE @patron OR c.IDCOMPROBANTE LIKE @patron)
                  AND (@cert = ''
                    OR @cert = 'GAN' AND ISNULL(c.RETGAN_Importe,0) > 0
                    OR @cert = 'IVA' AND ISNULL(c.RETIVA_Importe,0) > 0
                    OR @cert = 'IBR' AND ISNULL(c.RETIBR_Importe,0) > 0
                    OR @cert = 'SUSS' AND ISNULL(c.RETSUSS_Importe,0) > 0)
                ORDER BY c.FECHA DESC, c.ID DESC;
                """, new { desde = desde.Date, fin = hasta.Date.AddDays(1), unidades, texto, patron = "%" + texto + "%", cert = certificado }, cancellationToken: ct))).AsList();
            // El «último envío» es el del documento que se está mirando: el pago, o el certificado si se filtró por impuesto.
            var tcEnvio = certificado;
            foreach (var f in filas)
                f.UltimoEnvio = await UltimoEnvioAsync(cn, tcEnvio.Length > 0 ? tcEnvio : f.Tc, f.Comprobante, ct);
            return (IReadOnlyList<CompraPagoFila>)filas;
        }, ct);

    // ---- PDF --------------------------------------------------------------------------------------------------------

    public Task<byte[]> GenerarPdfAsync(string tc, string comprobante, string certificado = "", CancellationToken ct = default)
        => EjecutarAsync("GenerarPdf", async (cn, unidades, _) =>
        {
            var ficha = await FichaAsync(cn, unidades, tc, comprobante, certificado, ct);
            return await ArmarPdfAsync(cn, ficha, certificado, ct);
        }, ct);

    /// <summary>
    /// Comprobantes cancelados (MV_APLICACION, por la orden/pago que los imprime: incluye lo cancelado con créditos) y medios de pago (asiento)
    /// del pago. Los pagos están en V_MV_CPTE aunque su sistema sea Compras, por eso no se piden al visor de comprobantes.
    /// </summary>
    internal static async Task<ComprobanteViewerDto> DetallePagoAsync(SqlConnection cn, IComprobanteViewerService visor, Ficha ficha, CancellationToken ct)
    {
        var aplica = (await cn.QueryAsync<ComprobanteAplicacionDto>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(ap.TCO_ORIGEN)) TcRelacionado, ap.IDComprobante_Origen IdComprobanteRelacionado,
                MIN(f.FECHA) FechaRelacionado, SUM(ap.IMPORTE) ImporteAplicado
            FROM dbo.MV_APLICACION ap
            LEFT JOIN dbo.C_MV_CPTE f ON f.TC = ap.TCO_ORIGEN AND f.IDCOMPROBANTE = ap.IDComprobante_Origen
            WHERE (LTRIM(RTRIM(ap.TC_PRINT)) = @tc AND ap.IDCOMPROBANTE_PRINT = @id) OR (LTRIM(RTRIM(ap.TC)) = @tc AND ap.IDCOMPROBANTE = @id)
            GROUP BY LTRIM(RTRIM(ap.TCO_ORIGEN)), ap.IDComprobante_Origen
            ORDER BY MIN(f.FECHA), ap.IDComprobante_Origen;
            """, new { tc = ficha.Tc, id = ficha.Comprobante }, cancellationToken: ct))).ToList();
        var asientos = await visor.GetAsientosAsync(ficha.Tc, ficha.Comprobante, ct);
        return new ComprobanteViewerDto { AplicaA = aplica, Asientos = asientos };
    }

    private async Task<byte[]> ArmarPdfAsync(SqlConnection cn, Ficha ficha, string certificado, CancellationToken ct)
    {
        var (empresa, _, _, _, _) = await FacturaDocumentService.BuildEmpresaAsync(cn, null, ficha.Unidad.Length == 0 ? null : ficha.Unidad, ct);
        string html;
        if (certificado.Length > 0)
            html = HtmlCertificado(empresa, await LeerRetencionAsync(cn, ficha, certificado, ct));
        else if (TiposPago.Contains(ficha.Tc))
        {
            var dto = await DetallePagoAsync(cn, visor, ficha, ct);
            html = HtmlPago(empresa, ficha, await LeerRetencionesAsync(cn, ficha, ct), dto);
        }
        else
        {
            var dto = await visor.GetAsync(ficha.Tc, ficha.Comprobante, 0, ct)
                ?? throw new InvalidOperationException("No se encontró el comprobante.");
            html = HtmlOrdenCompra(empresa, ficha, dto);
        }
        return await pdf.GenerateAsync(html, null, ct);
    }

    // ---- Mail -------------------------------------------------------------------------------------------------------

    public Task<CompraMailBorrador> PrepararMailAsync(string tc, string comprobante, string certificado = "", CancellationToken ct = default)
        => EjecutarAsync("PrepararMail", async (cn, unidades, _) =>
        {
            var ficha = await FichaAsync(cn, unidades, tc, comprobante, certificado, ct);
            var (asunto, mensaje) = await TextosAsync(cn, ficha, certificado, ct);
            var prueba = (await CorreoSalienteService.LeerConfigAsync(cn, ClaveMailPrueba, ct)).Trim();
            return new CompraMailBorrador
            {
                Tc = ficha.Tc, Comprobante = ficha.Comprobante, Certificado = certificado, Proveedor = ficha.Nombre, Para = ficha.Mail,
                Asunto = asunto, Mensaje = mensaje, MailPrueba = prueba.Length > 0 ? prueba : null, Archivo = NombreArchivo(ficha, certificado),
                UltimoEnvio = await UltimoEnvioAsync(cn, certificado.Length > 0 ? certificado : ficha.Tc, ficha.Comprobante, ct)
            };
        }, ct);

    /// <summary>Arma y manda el PDF. Nunca lanza por un problema del envío: lo devuelve en el resultado y lo deja registrado.</summary>
    public Task<CompraMailResultado> EnviarMailAsync(string tc, string comprobante, string certificado = "", string? para = null,
        string? asunto = null, string? mensaje = null, CancellationToken ct = default)
        => EjecutarAsync("EnviarMail", async (cn, unidades, usuario) =>
        {
            var ficha = await FichaAsync(cn, unidades, tc, comprobante, certificado, ct);
            var (validos, invalidos) = CorreoSalienteService.SepararMails(para ?? ficha.Mail);
            if (validos.Count == 0)
                return new CompraMailResultado { Mensaje = invalidos.Count > 0
                    ? $"El mail del proveedor no es válido ({string.Join(", ", invalidos)}). Corregilo en su ficha o escribilo en «Para»."
                    : "El proveedor no tiene mail cargado. Agregalo en su ficha (podés poner varios separados por «;») o escribilo en «Para»." };
            var (asuntoBase, mensajeBase) = await TextosAsync(cn, ficha, certificado, ct);
            var asuntoFinal = string.IsNullOrWhiteSpace(asunto) ? asuntoBase : asunto.Trim();
            var mensajeFinal = string.IsNullOrWhiteSpace(mensaje) ? mensajeBase : mensaje;
            IReadOnlyList<string> destinos = validos;
            var prueba = (await CorreoSalienteService.LeerConfigAsync(cn, ClaveMailPrueba, ct)).Trim();
            if (prueba.Length > 0)
            {
                // Modo prueba: no se le escribe al proveedor; se avisa a quién hubiera ido.
                asuntoFinal = $"[PRUEBA · para {string.Join(", ", validos)}] {asuntoFinal}";
                destinos = [prueba];
            }
            var archivo = NombreArchivo(ficha, certificado);
            var tcRegistro = certificado.Length > 0 ? certificado : ficha.Tc;
            CorreoSalienteService.CuentaCorreo? cuenta = null;
            try
            {
                var bytes = await ArmarPdfAsync(cn, ficha, certificado, ct);
                cuenta = await correo.ResolverAsync(cn, usuario, ct);
                var html = "<div style=\"font-family:Arial,sans-serif;font-size:14px\">" +
                           WebUtility.HtmlEncode(mensajeFinal).Replace("\r\n", "\n").Replace("\n", "<br />") + "</div>";
                await correo.EnviarAsync(cn, cuenta, destinos, asuntoFinal, html, [new(archivo, bytes)], ct);
                await RegistrarAsync(cn, cuenta, usuario, ficha, tcRegistro, string.Join(";", destinos), asuntoFinal, mensajeFinal, archivo, "OK");
                var texto = prueba.Length > 0 ? $"Enviado en modo prueba a {prueba} (iba a {string.Join(", ", validos)})." : $"Enviado a {string.Join(", ", destinos)}.";
                if (invalidos.Count > 0) texto += $" Se omitieron mails inválidos: {string.Join(", ", invalidos)}.";
                return new CompraMailResultado { Enviado = true, Para = string.Join(", ", destinos), Mensaje = texto };
            }
            catch (Exception ex) when (ex is InvalidOperationException or SmtpException or AppUserFacingException or FormatException)
            {
                var motivo = MotivoEnvio(ex);
                await RegistrarAsync(cn, cuenta, usuario, ficha, tcRegistro, string.Join(";", destinos), asuntoFinal, mensajeFinal, archivo, $"ERROR: {motivo}");
                if (ex is SmtpException)
                    await eventos.LogErrorAsync("CompraMail", "EnviarMail", ex, motivo, new { ficha.Tc, ficha.Comprobante, certificado }, ct: CancellationToken.None);
                return new CompraMailResultado { Para = string.Join(", ", destinos), Mensaje = motivo };
            }
        }, ct);

    private async Task RegistrarAsync(SqlConnection cn, CorreoSalienteService.CuentaCorreo? cuenta, string usuario, Ficha f, string tcRegistro,
        string para, string asunto, string mensaje, string archivo, string estado)
    {
        try { await CorreoSalienteService.RegistrarEnvioAsync(cn, cuenta, usuario, f.Cuenta, tcRegistro, f.Comprobante, para, asunto, mensaje, archivo, estado); }
        catch (SqlException ex)
        {
            await eventos.LogErrorAsync("CompraMail", "RegistrarMail", ex, "No se pudo registrar el envío del mail.", new { f.Tc, f.Comprobante }, ct: CancellationToken.None);
        }
    }

    private static string MotivoEnvio(Exception ex)
    {
        if (ex is not SmtpException smtp) return ex.Message;
        var texto = smtp.InnerException?.Message ?? smtp.Message;
        if (smtp.StatusCode is SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed)
            return "El servidor de correo rechazó el destinatario. Revisá el mail del proveedor.";
        if (texto.Contains("auth", StringComparison.OrdinalIgnoreCase) || texto.Contains("5.7", StringComparison.OrdinalIgnoreCase))
            return "El servidor de correo no aceptó el usuario o la clave de la cuenta de envío. Revisá la cuenta de correo de tu usuario.";
        return $"No se pudo enviar el mail: {texto} Revisá la cuenta de correo (servidor, puerto, usuario y clave).";
    }

    private static string NombreArchivo(Ficha f, string certificado)
        => certificado.Length > 0 ? $"RET{certificado}_{f.Tc}_{f.Comprobante}.pdf" : $"{f.Tc}_{f.Comprobante}.pdf";

    /// <summary>Marcas como las del mail de facturación: &lt;NOMBRE&gt;, &lt;COMPROBANTE&gt;, &lt;FECHA&gt;, &lt;IMPORTE&gt; y &lt;IMPUESTO&gt;.</summary>
    internal static string ArmarTexto(string plantilla, string nombre, string comprobante, DateTime fecha, decimal importe, string certificado)
        => plantilla.Replace("<NOMBRE>", nombre.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("<COMPROBANTE>", FormatoComprobante(comprobante), StringComparison.OrdinalIgnoreCase)
            .Replace("<FECHA>", fecha.ToString("dd/MM/yyyy", Es), StringComparison.OrdinalIgnoreCase)
            .Replace("<IMPORTE>", importe.ToString("C2", Es), StringComparison.OrdinalIgnoreCase)
            .Replace("<IMPUESTO>", certificado.Length > 0 ? CompraCertificado.Nombre(certificado) : string.Empty, StringComparison.OrdinalIgnoreCase);

    private static async Task<(string Asunto, string Mensaje)> TextosAsync(SqlConnection cn, Ficha f, string certificado, CancellationToken ct)
    {
        var clase = ClaseTexto(f.Tc, certificado);
        var porDefecto = TextosPorDefecto[clase];
        var asunto = await CorreoSalienteService.LeerConfigAsync(cn, ClaveAsunto(clase), ct);
        var mensaje = await CorreoSalienteService.LeerConfigAsync(cn, ClaveMensaje(clase), ct);
        var importe = certificado.Length > 0 ? f.Retencion(certificado) : f.Importe;
        return (ArmarTexto(asunto.Length > 0 ? asunto : porDefecto.Asunto, f.Nombre, f.Comprobante, f.Fecha, importe, certificado),
                ArmarTexto(mensaje.Length > 0 ? mensaje : porDefecto.Mensaje, f.Nombre, f.Comprobante, f.Fecha, importe, certificado));
    }

    private static async Task<string?> UltimoEnvioAsync(SqlConnection cn, string tc, string comprobante, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.V_MA_EMAILMOV', N'U') IS NOT NULL
                SELECT TOP (1) CONVERT(nvarchar(16), FechaHora, 103) + ' ' + CONVERT(nvarchar(5), FechaHora, 108) + ' · ' + ISNULL(Para,'') + ' · ' + ISNULL(Status,'')
                FROM dbo.V_MA_EMAILMOV WHERE LTRIM(RTRIM(TC)) = @Tc AND LTRIM(RTRIM(IdComprobante)) = @Id AND ISNULL(Tipo,'E') = 'E' ORDER BY FechaHora DESC;
            ELSE SELECT CAST(NULL AS nvarchar(10));
            """, new { Tc = tc, Id = comprobante }, cancellationToken: ct));

    // ---- Datos ------------------------------------------------------------------------------------------------------

    internal sealed class Ficha
    {
        public string Tc { get; set; } = "";
        public string Comprobante { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string Cuenta { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Cuit { get; set; } = "";
        public string Domicilio { get; set; } = "";
        public decimal Importe { get; set; }
        public string Unidad { get; set; } = "";
        public string Mail { get; set; } = "";
        public decimal RetGanancias { get; set; }
        public decimal RetIva { get; set; }
        public decimal RetIibb { get; set; }
        public decimal RetSuss { get; set; }

        public decimal Retencion(string certificado) => certificado switch
        {
            CompraCertificado.Ganancias => RetGanancias,
            CompraCertificado.Iva => RetIva,
            CompraCertificado.IngresosBrutos => RetIibb,
            CompraCertificado.Suss => RetSuss,
            _ => 0m
        };
    }

    private static async Task<Ficha> FichaAsync(SqlConnection cn, IReadOnlyList<string> unidades, string tc, string comprobante, string certificado, CancellationToken ct)
    {
        tc = (tc ?? string.Empty).Trim().ToUpperInvariant();
        comprobante = (comprobante ?? string.Empty).Trim();
        certificado = (certificado ?? string.Empty).Trim().ToUpperInvariant();
        var esPago = TiposPago.Contains(tc);
        if (!esPago && !TiposCompra.Contains(tc))
            throw new InvalidOperationException("Solo se envían por mail las órdenes de compra, los pagos y sus certificados de retención.");
        if (certificado.Length > 0 && (!esPago || !CompraCertificado.Todos.Contains(certificado)))
            throw new InvalidOperationException("Los certificados de retención se emiten desde un pago u orden de pago.");
        var sql = esPago
            ? """
              SELECT TOP (1) LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante, c.FECHA Fecha, LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta,
                LTRIM(RTRIM(ISNULL(c.NOMBRE,''))) Nombre, LTRIM(RTRIM(ISNULL(c.DOCUMENTONUMERO,''))) Cuit,
                LTRIM(RTRIM(ISNULL(c.DOMICILIO,''))) Domicilio, ISNULL(c.IMPORTE,0) Importe, LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) Unidad,
                LTRIM(RTRIM(ISNULL(p.MAIL,''))) Mail, ISNULL(c.RETGAN_Importe,0) RetGanancias, ISNULL(c.RETIVA_Importe,0) RetIva,
                ISNULL(c.RETIBR_Importe,0) RetIibb, ISNULL(c.RETSUSS_Importe,0) RetSuss
              FROM dbo.V_MV_CPTE c LEFT JOIN dbo.VT_PROVEEDORES p ON LTRIM(RTRIM(p.CODIGO)) = LTRIM(RTRIM(c.CUENTA))
              WHERE LTRIM(RTRIM(c.TC)) = @tc AND LTRIM(RTRIM(c.IDCOMPROBANTE)) = @comprobante AND ISNULL(c.IDCOMPLEMENTO,0) = 0
                AND ISNULL(c.ANULADA,0) = 0 AND LTRIM(RTRIM(c.UNEGOCIO)) IN @unidades ORDER BY c.ID DESC;
              """
            : """
              SELECT TOP (1) LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante, c.FECHA Fecha, LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta,
                LTRIM(RTRIM(ISNULL(c.NOMBRE,''))) Nombre, ISNULL(c.IMPORTE,0) Importe, LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) Unidad,
                LTRIM(RTRIM(ISNULL(p.MAIL,''))) Mail
              FROM dbo.C_MV_CPTE c LEFT JOIN dbo.VT_PROVEEDORES p ON LTRIM(RTRIM(p.CODIGO)) = LTRIM(RTRIM(c.CUENTA))
              WHERE LTRIM(RTRIM(c.TC)) = @tc AND LTRIM(RTRIM(c.IDCOMPROBANTE)) = @comprobante
                AND ISNULL(c.ANULADA,0) = 0 AND LTRIM(RTRIM(c.UNEGOCIO)) IN @unidades ORDER BY c.ID DESC;
              """;
        var ficha = await cn.QuerySingleOrDefaultAsync<Ficha>(new CommandDefinition(sql, new { tc, comprobante, unidades }, cancellationToken: ct))
            ?? throw new InvalidOperationException("No se encontró el comprobante en las unidades autorizadas, o está anulado.");
        if (certificado.Length > 0 && ficha.Retencion(certificado) <= 0)
            throw new InvalidOperationException($"Ese pago no tiene retención de {CompraCertificado.Nombre(certificado)}.");
        return ficha;
    }

    internal sealed class Retencion
    {
        public string Certificado { get; set; } = "";
        public string Numero { get; set; } = "";
        public string Regimen { get; set; } = "";
        public decimal Base { get; set; }
        public decimal Alicuota { get; set; }
        public decimal Importe { get; set; }
        public Ficha Pago { get; set; } = new();
    }

    private sealed class RegimenFila
    {
        public string Descripcion { get; set; } = "";
        public decimal Porcentaje { get; set; }
    }

    private static async Task<IReadOnlyList<Retencion>> LeerRetencionesAsync(SqlConnection cn, Ficha pago, CancellationToken ct)
    {
        var lista = new List<Retencion>();
        foreach (var codigo in CompraCertificado.Todos)
            if (pago.Retencion(codigo) > 0) lista.Add(await LeerRetencionAsync(cn, pago, codigo, ct));
        return lista;
    }

    private static async Task<Retencion> LeerRetencionAsync(SqlConnection cn, Ficha pago, string certificado, CancellationToken ct)
    {
        var p = CompraCertificado.Prefijo(certificado); // lista cerrada: el prefijo nunca viene del usuario
        var r = await cn.QuerySingleOrDefaultAsync<Retencion>(new CommandDefinition($"""
            SELECT TOP (1) ISNULL(CONVERT(nvarchar(30), c.{p}_NRO), '') Numero, LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(10), c.{p}_IdRetencion), ''))) Regimen,
                CONVERT(decimal(18,4), ISNULL(c.{p}_BaseImponible, 0)) Base, CONVERT(decimal(18,4), ISNULL(c.{p}_ALICUOTA, 0)) Alicuota, CONVERT(decimal(18,4), ISNULL(c.{p}_Importe, 0)) Importe
            FROM dbo.V_MV_CPTE c WHERE LTRIM(RTRIM(c.TC)) = @tc AND LTRIM(RTRIM(c.IDCOMPROBANTE)) = @comprobante AND ISNULL(c.IDCOMPLEMENTO,0) = 0
            ORDER BY c.ID DESC;
            """, new { tc = pago.Tc, comprobante = pago.Comprobante }, cancellationToken: ct)) ?? new Retencion();
        r.Certificado = certificado;
        r.Pago = pago;
        if (r.Regimen.Length > 0)
        {
            // Los pagos no guardan la alícuota (queda en 0): se toma la del régimen, o se deduce de lo retenido sobre la base.
            var regimen = await cn.QuerySingleOrDefaultAsync<RegimenFila>(new CommandDefinition(
                "SELECT TOP (1) LTRIM(RTRIM(Descripcion)) Descripcion, CONVERT(decimal(18,4), PORCENTAJE) Porcentaje FROM dbo.V_TA_RETENCIONES WHERE LTRIM(RTRIM(IdRetencion)) = @Id;",
                new { Id = r.Regimen }, cancellationToken: ct));
            if (regimen is { Descripcion.Length: > 0 }) r.Regimen = $"{r.Regimen} · {regimen.Descripcion}";
            if (r.Alicuota == 0 && regimen is { Porcentaje: > 0 }) r.Alicuota = regimen.Porcentaje;
        }
        if (r.Alicuota == 0 && r.Base > 0) r.Alicuota = Math.Round(r.Importe * 100m / r.Base, 4);
        return r;
    }

    // ---- Ejecución con las mismas reglas que la carga de compras ------------------------------------------------------

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, IReadOnlyList<string>, string, Task<T>> trabajo, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones, usuarios, out var sesion))
            throw new InvalidOperationException("Iniciá sesión en la base donde querés enviar los comprobantes.");
        var usuario = usuarios.GetCurrentUserName();
        try
        {
            if (!await permisos.HasAccessAsync(CompraCargaService.MenuKey, ct))
                throw new InvalidOperationException("No tenés permiso para enviar comprobantes de compras.");
            var unidades = (await compras.ContextoAsync(null, ct)).Unidades.Select(x => x.Codigo).ToArray();
            await using var cn = new SqlConnection(sesiones.GetConnectionString());
            await cn.OpenAsync(ct);
            var resultado = await trabajo(cn, unidades, usuario);
            ct.ThrowIfCancellationRequested();
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones, usuarios, out var actual) || actual != sesion)
                throw new OperationCanceledException("La sesión o base activa cambió.");
            return resultado;
        }
        catch (OperationCanceledException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            const string mensaje = "No se pudo completar la operación de mail de compras. Reintentá.";
            var codigo = await eventos.LogErrorAsync("CompraMail", accion, ex, mensaje, ct: CancellationToken.None);
            throw new AppUserFacingException(mensaje, codigo, ex);
        }
    }

    // ---- HTML de los PDF ----------------------------------------------------------------------------------------------

    private static string E(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
    private static string M(decimal valor) => valor.ToString("N2", Es);

    /// <summary>0001 00000040 X → 0001-00000040-X, como se lee en el papel.</summary>
    internal static string FormatoComprobante(string id)
        => id.Length == 13 ? $"{id[..4]}-{id.Substring(4, 8)}-{id[12]}" : id;

    private const string Estilo = """
        <style>
        body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#111;margin:0}
        .doc{padding:8px 4px}
        .cab{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #222;padding-bottom:8px;margin-bottom:12px}
        .cab h1{font-size:20px;margin:0 0 4px}.cab .emp{font-size:11px;color:#444;line-height:1.5}
        .cab .num{text-align:right;font-size:13px;line-height:1.6}.cab img{max-height:56px;margin-bottom:6px}
        .caja{border:1px solid #bbb;border-radius:4px;padding:8px 10px;margin-bottom:12px;line-height:1.6}
        table{width:100%;border-collapse:collapse;margin-bottom:12px}
        th{background:#eee;text-align:left;font-size:11px;padding:6px;border:1px solid #ccc}
        td{padding:6px;border:1px solid #ddd}.n{text-align:right;white-space:nowrap}
        h2{font-size:13px;margin:14px 0 6px}.tot{width:45%;margin-left:55%}.tot td{border:none;padding:3px 6px}
        .tot tr:last-child td{border-top:2px solid #222;font-weight:bold;font-size:14px}
        .firma{margin-top:48px;text-align:center;width:40%;margin-left:60%;border-top:1px solid #222;padding-top:4px;font-size:11px}
        .nota{font-size:10px;color:#555;margin-top:16px}
        </style>
        """;

    private static string Encabezado(EmpresaDocumentData empresa, string titulo, string numero, DateTime fecha)
    {
        var sb = new StringBuilder("<div class=\"cab\"><div>");
        if (empresa.Logo is { Length: > 0 }) sb.Append("<img alt=\"\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(empresa.Logo)).Append("\" /><br />");
        sb.Append("<h1>").Append(E(titulo)).Append("</h1><div class=\"emp\"><strong>").Append(E(empresa.Nombre)).Append("</strong>");
        if (empresa.Cuit.Length > 0) sb.Append("<br />CUIT ").Append(E(empresa.Cuit));
        if (empresa.Domicilio.Length > 0) sb.Append("<br />").Append(E(empresa.Domicilio));
        if (empresa.Telefono.Length > 0) sb.Append("<br />Tel. ").Append(E(empresa.Telefono));
        sb.Append("</div></div><div class=\"num\"><strong>N° ").Append(E(FormatoComprobante(numero))).Append("</strong><br />Fecha ")
          .Append(fecha.ToString("dd/MM/yyyy", Es)).Append("</div></div>");
        return sb.ToString();
    }

    private static string Documento(string cuerpo) => $"<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\" />{Estilo}</head><body><div class=\"doc\">{cuerpo}</div></body></html>";

    private static string CajaProveedor(Ficha f)
    {
        var sb = new StringBuilder("<div class=\"caja\"><strong>Proveedor:</strong> ").Append(E(f.Nombre)).Append(" (").Append(E(f.Cuenta)).Append(')');
        if (f.Cuit.Length > 0) sb.Append("<br /><strong>CUIT:</strong> ").Append(E(f.Cuit));
        if (f.Domicilio.Length > 0) sb.Append("<br /><strong>Domicilio:</strong> ").Append(E(f.Domicilio));
        return sb.Append("</div>").ToString();
    }

    internal static string HtmlOrdenCompra(EmpresaDocumentData empresa, Ficha f, ComprobanteViewerDto dto)
    {
        var cab = dto.Cabecera;
        var titulo = f.Tc == "OC" ? "ORDEN DE COMPRA" : "REMITO DE COMPRA";
        var sb = new StringBuilder(Encabezado(empresa, titulo, f.Comprobante, f.Fecha));
        sb.Append(CajaProveedor(f));
        sb.Append("<table><thead><tr><th>Código</th><th>Descripción</th><th>Un.</th><th class=\"n\">Cantidad</th><th class=\"n\">Precio</th><th class=\"n\">Total</th></tr></thead><tbody>");
        foreach (var i in dto.Insumos)
            sb.Append("<tr><td>").Append(E(i.CodigoArticulo)).Append("</td><td>").Append(E(i.Descripcion)).Append("</td><td>").Append(E(i.Unidad))
              .Append("</td><td class=\"n\">").Append(i.Cantidad.ToString("0.####", Es)).Append("</td><td class=\"n\">").Append(M(i.PrecioImporte))
              .Append("</td><td class=\"n\">").Append(M(i.Total)).Append("</td></tr>");
        sb.Append("</tbody></table>");
        var total = cab?.ImporteTotal is > 0 ? cab.ImporteTotal : f.Importe;
        sb.Append("<table class=\"tot\">");
        if (cab is { ImporteSinIva: > 0 }) sb.Append("<tr><td>Importe sin IVA</td><td class=\"n\">").Append(M(cab.ImporteSinIva)).Append("</td></tr>");
        if (cab is { Iva: > 0 }) sb.Append("<tr><td>IVA</td><td class=\"n\">").Append(M(cab.Iva)).Append("</td></tr>");
        sb.Append("<tr><td>Total</td><td class=\"n\">").Append(M(total)).Append("</td></tr></table>");
        var observaciones = string.Join(" ", new[] { cab?.ObservacionesGenerales, cab?.Comentarios }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (observaciones.Length > 0) sb.Append("<div class=\"caja\"><strong>Observaciones:</strong> ").Append(E(observaciones)).Append("</div>");
        return Documento(sb.ToString());
    }

    internal static string HtmlPago(EmpresaDocumentData empresa, Ficha f, IReadOnlyList<Retencion> retenciones, ComprobanteViewerDto? dto)
    {
        var titulo = f.Tc switch { "OPG" => "ORDEN DE PAGO", "PGFP" => "PAGO PROFORMA", _ => "COMPROBANTE DE PAGO" };
        var sb = new StringBuilder(Encabezado(empresa, titulo, f.Comprobante, f.Fecha));
        sb.Append(CajaProveedor(f));
        if (dto?.AplicaA is { Count: > 0 } aplica)
        {
            sb.Append("<h2>Comprobantes cancelados</h2><table><thead><tr><th>Fecha</th><th>Comprobante</th><th class=\"n\">Importe aplicado</th></tr></thead><tbody>");
            foreach (var a in aplica)
                sb.Append("<tr><td>").Append(a.FechaRelacionado?.ToString("dd/MM/yyyy", Es)).Append("</td><td>").Append(E(a.TcRelacionado)).Append(' ')
                  .Append(E(FormatoComprobante(a.IdComprobanteRelacionado))).Append("</td><td class=\"n\">").Append(M(a.ImporteAplicado)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        if (dto?.Asientos is { Count: > 0 } asientos && asientos.Any(a => a.Haber > 0))
        {
            sb.Append("<h2>Medios de pago y retenciones (cuentas acreditadas)</h2><table><thead><tr><th>Cuenta</th><th>Detalle</th><th class=\"n\">Importe</th></tr></thead><tbody>");
            foreach (var a in asientos.Where(a => a.Haber > 0))
                sb.Append("<tr><td>").Append(E(a.DescripcionCuenta.Length > 0 ? a.DescripcionCuenta : a.Cuenta)).Append("</td><td>").Append(E(a.Detalle)).Append(E(a.Cheque.Length > 0 ? " · Cheque " + a.Cheque : ""))
                  .Append("</td><td class=\"n\">").Append(M(a.Haber)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        if (retenciones.Count > 0)
        {
            sb.Append("<h2>Retenciones practicadas</h2><table><thead><tr><th>Impuesto</th><th>Certificado</th><th>Régimen</th><th class=\"n\">Base</th><th class=\"n\">Alícuota %</th><th class=\"n\">Retenido</th></tr></thead><tbody>");
            foreach (var r in retenciones)
                sb.Append("<tr><td>").Append(E(CompraCertificado.Nombre(r.Certificado))).Append("</td><td>").Append(E(r.Numero)).Append("</td><td>").Append(E(r.Regimen))
                  .Append("</td><td class=\"n\">").Append(M(r.Base)).Append("</td><td class=\"n\">").Append(r.Alicuota.ToString("0.####", Es))
                  .Append("</td><td class=\"n\">").Append(M(r.Importe)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        // Lo cancelado por encima de lo pagado y retenido se canceló con créditos (notas de crédito, anticipos) aplicados a esta orden.
        var conCreditos = (dto?.AplicaA.Sum(a => a.ImporteAplicado) ?? 0m) - f.Importe - retenciones.Sum(r => r.Importe);
        sb.Append("<table class=\"tot\">");
        if (conCreditos > 0.005m) sb.Append("<tr><td>Cancelado con créditos y anticipos aplicados</td><td class=\"n\">").Append(M(conCreditos)).Append("</td></tr>");
        sb.Append("<tr><td>Total del pago</td><td class=\"n\">").Append(M(f.Importe)).Append("</td></tr></table>");
        return Documento(sb.ToString());
    }

    internal static string HtmlCertificado(EmpresaDocumentData empresa, Retencion r)
    {
        var f = r.Pago;
        var sb = new StringBuilder(Encabezado(empresa, "CERTIFICADO DE RETENCIÓN", r.Numero.Length > 0 ? r.Numero : f.Comprobante, f.Fecha));
        sb.Append("<div class=\"caja\"><strong>Impuesto:</strong> ").Append(E(CompraCertificado.Nombre(r.Certificado)))
          .Append("<br /><strong>Agente de retención:</strong> ").Append(E(empresa.Nombre));
        if (empresa.Cuit.Length > 0) sb.Append(" · CUIT ").Append(E(empresa.Cuit));
        sb.Append("</div><div class=\"caja\"><strong>Sujeto retenido:</strong> ").Append(E(f.Nombre)).Append(" (").Append(E(f.Cuenta)).Append(')');
        if (f.Cuit.Length > 0) sb.Append("<br /><strong>CUIT:</strong> ").Append(E(f.Cuit));
        if (f.Domicilio.Length > 0) sb.Append("<br /><strong>Domicilio:</strong> ").Append(E(f.Domicilio));
        sb.Append("</div><table><thead><tr><th>Comprobante que originó la retención</th>");
        if (r.Regimen.Length > 0) sb.Append("<th>Régimen</th>");
        sb.Append("<th class=\"n\">Base imponible</th><th class=\"n\">Alícuota %</th><th class=\"n\">Importe retenido</th></tr></thead><tbody><tr><td>")
          .Append(E(f.Tc)).Append(' ').Append(E(FormatoComprobante(f.Comprobante))).Append(" del ").Append(f.Fecha.ToString("dd/MM/yyyy", Es)).Append("</td>");
        if (r.Regimen.Length > 0) sb.Append("<td>").Append(E(r.Regimen)).Append("</td>");
        sb.Append("<td class=\"n\">").Append(M(r.Base)).Append("</td><td class=\"n\">").Append(r.Alicuota.ToString("0.####", Es))
          .Append("</td><td class=\"n\">").Append(M(r.Importe)).Append("</td></tr></tbody></table>");
        sb.Append("<div class=\"firma\">Firma y aclaración del agente de retención</div>");
        return Documento(sb.ToString());
    }
}
