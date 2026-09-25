using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Arma el PDF de una factura o nota de crédito/débito A/B/C YA EMITIDA y aprobada por AFIP -- el sistema legacy ya
/// hizo el WSFE y guardó el CAE en V_MV_CPTE_ELECTRONICOS. Genera el QR al solicitar el reporte;
/// solo lee datos fiscales, nunca llama a AFIP. Espejo de CotizacionDocumentService, pero la fuente de
/// datos es completamente distinta (tablas transaccionales de venta, no un módulo propio con
/// estado editable).</summary>
public sealed class FacturaDocumentService(
    IConfiguration configuration,
    IDocumentTemplateService templates,
    IDocumentRenderer renderer,
    IDocumentPdfService pdfService,
    IArcaQrService arcaQr,
    IAppEventService appEvents,
    ILogger<FacturaDocumentService> logger,
    ISessionService sessionService) : IFacturaDocumentService
{
    private const string ModuleName = "Documentos";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<DocumentRenderResult> RenderAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null)
        => RenderCoreAsync(tc, idComprobante, uNegocio, null, ct, previewTemplate);

    public Task<DocumentRenderResult> RenderAsync(string tc, string idComprobante, string? uNegocio, string tipoDocumento, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null)
        => RenderCoreAsync(tc, idComprobante, uNegocio, tipoDocumento, ct, previewTemplate);

    private async Task<DocumentRenderResult> RenderCoreAsync(string tc, string idComprobante, string? uNegocio, string? tipoSolicitado, CancellationToken ct, DocumentTemplateDto? previewTemplate)
    {
        try
        {
            var data = await BuildDataAsync(tc, idComprobante, ct)
                ?? throw new ComprobanteNoSoportadoException();
            var tipoDocumento = tipoSolicitado ?? data.Comprobante.TipoDocumento;
            if (!string.Equals(TiposDocumentoCore.NormalizarFiscal(tipoDocumento), TiposDocumentoCore.NormalizarFiscal(data.Comprobante.TipoDocumento), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("El tipo seleccionado no corresponde al comprobante.");
            var template = previewTemplate ?? await templates.ResolveAsync(tipoDocumento, uNegocio, ct);
            if (!string.Equals(TiposDocumentoCore.NormalizarFiscal(template.TipoDocumento), TiposDocumentoCore.NormalizarFiscal(tipoDocumento), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("La plantilla no corresponde al tipo de comprobante seleccionado.");
            var definition = templates.DeserializeAndValidate(template.TemplateJson, tipoDocumento);
            var theme = await templates.GetGeneralThemeAsync(ct);
            var html = renderer.RenderFactura(definition, data, template.CssCustom, theme, TiposDocumentoCore.EsFiscal(tipoDocumento) ? null : TiposDocumentoCore.NombrePredeterminado(tipoDocumento));
            if (definition.TotalesAlPiePagina || definition.Paper.Size == "Ticket80") html = await pdfService.PrepareHtmlAsync(html, ct);
            var footer = BuildFooterOptions(definition, data.Empresa.Nombre);
            logger.LogInformation("Documentos: HTML de factura {Tc}/{IdComprobante}, plantilla {IdTemplate}, UNegocio {UNegocio}.", tc, idComprobante, template.IdTemplate, uNegocio ?? "GLOBAL");
            return new DocumentRenderResult { Html = html, IdTemplate = template.IdTemplate, TipoDocumento = template.TipoDocumento, UNegocio = template.UNegocio, Footer = footer };
        }
        catch (AppUserFacingException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, "RenderFactura", ex, "No se pudo generar la vista previa de la factura.", new { Tc = tc, IdComprobante = idComprobante, UNegocio = uNegocio }, ct: ct);
            throw new AppUserFacingException("No se pudo generar la vista previa de la factura.", incidentId, ex);
        }
    }

    public async Task<byte[]> GeneratePdfAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null)
    {
        var result = await RenderAsync(tc, idComprobante, uNegocio, ct, previewTemplate);
        return await pdfService.GenerateAsync(result.Html, result.Footer, ct);
    }

    public async Task<byte[]> GeneratePdfAsync(string tc, string idComprobante, string? uNegocio, string tipoDocumento, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null)
    {
        var result = await RenderAsync(tc, idComprobante, uNegocio, tipoDocumento, ct, previewTemplate);
        var pdf = await pdfService.GenerateAsync(result.Html, result.Footer, ct);
        logger.LogInformation("Documentos: PDF de factura {Tc}/{IdComprobante}, plantilla {IdTemplate}.", tc, idComprobante, result.IdTemplate);
        return pdf;
    }

    public async Task<byte[]?> GeneratePdfParaClienteAsync(string codigoCliente, int idComprobante, CancellationToken ct = default)
    {
        var result = await RenderParaClienteAsync(codigoCliente, idComprobante, ct);
        return result is null ? null : await pdfService.GenerateAsync(result.Html, result.Footer, ct);
    }

    public async Task<DocumentRenderResult?> RenderParaClienteAsync(string codigoCliente, int idComprobante, CancellationToken ct = default)
    {
        try
        {
            return await RenderParaClienteCoreAsync(codigoCliente, idComprobante, ct);
        }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, "RenderComprobantePortal", ex,
                "No se pudo generar la vista previa del comprobante.", new { IdComprobante = idComprobante }, ct: ct);
            throw new AppUserFacingException("No se pudo generar la vista previa del comprobante.", incidentId, ex);
        }
    }

    private async Task<DocumentRenderResult?> RenderParaClienteCoreAsync(string codigoCliente, int idComprobante, CancellationToken ct)
    {
        var codigo = (codigoCliente ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(codigo) || idComprobante <= 0)
            return null;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        // Resuelve Tc/IdComprobante desde el ID interno y valida pertenencia ANTES de armar nada --
        // mismo chequeo que PortalClienteService.GetComprobanteClienteDetalleAsync (nunca distingue
        // "no existe" de "es de otro cliente", ambos casos devuelven null).
        var row = await cn.QuerySingleOrDefaultAsync<ComprobanteClienteRow>(new CommandDefinition(
            "SELECT ISNULL(LTRIM(RTRIM(TC)), '') AS Tc, ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto, ISNULL(LTRIM(RTRIM(CUENTA)), '') AS Cuenta, LTRIM(RTRIM(UNEGOCIO)) AS UNegocio FROM dbo.V_MV_Cpte WHERE ID = @Id;",
            new { Id = idComprobante }, cancellationToken: ct));

        if (row is null || row.Tc.Length == 0 || !string.Equals(row.Cuenta, codigo, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            return await RenderAsync(row.Tc, row.IdComprobanteTexto, row.UNegocio, ct);
        }
        catch (ComprobanteNoSoportadoException)
        {
            // No es un comprobante fiscal soportado (ej. recibo) -- Portal Cliente no tiene nada
            // más que ofrecer para este comprobante, se degrada a "no disponible" en vez de error.
            return null;
        }
    }

    private sealed class ComprobanteClienteRow
    {
        public string Tc { get; set; } = string.Empty;
        public string IdComprobanteTexto { get; set; } = string.Empty;
        public string Cuenta { get; set; } = string.Empty;
        public string? UNegocio { get; set; }
    }

    private sealed class ComprobanteNoSoportadoException() : InvalidOperationException(
        "El comprobante indicado no existe o no es una factura, nota de crédito o débito A/B/C.");

    public async Task<IReadOnlyList<FacturaResumenDto>> SearchRecientesAsync(string tipoDocumento, int top = 20, CancellationToken ct = default)
    {
        var tipoCpte = TiposDocumentoCore.Fiscal(tipoDocumento)?.CodigoArca ?? 0;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        if (tipoCpte == 0)
        {
            var tc = tipoDocumento switch
            {
                TiposDocumentoCore.RemitoR or TiposDocumentoCore.RemitoX => "RM",
                TiposDocumentoCore.CobranzaContado => "CBCT",
                TiposDocumentoCore.CobranzaA or TiposDocumentoCore.CobranzaB or TiposDocumentoCore.CobranzaC or TiposDocumentoCore.CobranzaProforma => "CB",
                _ => string.Empty
            };
            if (tc.Length == 0) return [];
            var otros = await cn.QueryAsync<FacturaResumenDto>(new CommandDefinition("""
                SELECT TOP (@Top) LTRIM(RTRIM(TC)) AS Tc, LTRIM(RTRIM(IDCOMPROBANTE)) AS IdComprobante,
                    LTRIM(RTRIM(SUCURSAL)) + '-' + LTRIM(RTRIM(NUMERO)) AS Numero,
                    FECHA AS Fecha, ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS Cliente
                FROM dbo.V_MV_Cpte
                WHERE UPPER(LTRIM(RTRIM(TC))) = @Tc
                  AND (@Tc = 'CBCT' OR UPPER(LTRIM(RTRIM(LETRA))) = @Letra)
                ORDER BY FECHA DESC, ID DESC;
                """, new { Top = Math.Clamp(top, 1, 200), Tc = tc, Letra = TiposDocumentoCore.LetraDe(tipoDocumento) }, cancellationToken: ct));
            return otros.AsList();
        }
        if (!await ExistsAsync(cn, "dbo.V_MV_CPTE_ELECTRONICOS", ct))
            return [];

        var rows = await cn.QueryAsync<FacturaResumenDto>(new CommandDefinition("""
            SELECT TOP (@Top)
                e.TC AS Tc, e.IdComprobante AS IdComprobante,
                LTRIM(RTRIM(v.SUCURSAL)) + '-' + LTRIM(RTRIM(v.NUMERO)) AS Numero,
                v.FECHA AS Fecha, ISNULL(LTRIM(RTRIM(v.NOMBRE)), '') AS Cliente
            FROM dbo.V_MV_CPTE_ELECTRONICOS e
            JOIN dbo.V_MV_Cpte v ON v.TC = e.TC AND v.IDCOMPROBANTE = e.IdComprobante
            WHERE e.Tipo_Cpte = @TipoCpte
              AND ISNULL(e.Archivado, 0) = 0
              AND UPPER(LTRIM(RTRIM(ISNULL(e.Resultado, '')))) = 'A'
              AND NULLIF(LTRIM(RTRIM(ISNULL(e.CAE, ''))), '') IS NOT NULL
            ORDER BY e.Fecha_Cpte DESC;
            """, new { Top = Math.Clamp(top, 1, 200), TipoCpte = tipoCpte }, cancellationToken: ct));
        return rows.AsList();
    }

    private async Task<FacturaDocumentData?> BuildDataAsync(string tc, string idComprobante, CancellationToken ct)
    {
        var tcTrim = (tc ?? string.Empty).Trim();
        var idTrim = (idComprobante ?? string.Empty).Trim();
        if (tcTrim.Length == 0 || idTrim.Length == 0)
            return null;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var tieneCondIva = await ExistsAsync(cn, "dbo.TA_CONDIVA", ct);
        var condIvaJoin = tieneCondIva ? "LEFT JOIN dbo.TA_CONDIVA ci ON UPPER(LTRIM(RTRIM(ci.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(v.CONDICIONIVA, ''))))" : string.Empty;
        var condIvaSelect = tieneCondIva ? "ISNULL(ci.DESCRIPCION, '')" : "''";

        var tieneElectronico = await ExistsAsync(cn, "dbo.V_MV_CPTE_ELECTRONICOS", ct);
        var electronicoJoin = tieneElectronico ? "LEFT JOIN dbo.V_MV_CPTE_ELECTRONICOS e ON e.TC = v.TC AND e.IdComprobante = v.IDCOMPROBANTE AND ISNULL(e.Archivado, 0) = 0" : string.Empty;
        var electronicoSelect = tieneElectronico
            ? "e.Tipo_Cpte AS TipoCpte, e.CAE AS Cae, e.VtoCAE AS VtoCae, e.CodigoBarraCAE AS CodigoBarraCae, e.Resultado AS Resultado, ISNULL(CAST(e.Motivo AS nvarchar(500)), '') AS Motivo, e.Fecha_Cpte AS FechaElectronica, e.Punto_Vta AS PuntoVentaElectronico, e.Cpte_Desde AS NumeroElectronico, CAST(e.Imp_Total AS decimal(15,2)) AS TotalElectronico, e.Tipo_Doc AS TipoDocElectronico, e.Nro_Doc AS NumeroDocElectronico"
            : "CAST(NULL AS int) AS TipoCpte, CAST(NULL AS nvarchar(20)) AS Cae, CAST(NULL AS datetime) AS VtoCae, CAST(NULL AS nvarchar(60)) AS CodigoBarraCae, CAST(NULL AS nvarchar(4)) AS Resultado, '' AS Motivo";

        var tienePercepcion = await ExistsAsync(cn, "dbo.V_TA_PERCEPCION", ct);
        var percepcionJoin = tienePercepcion
            ? """
              LEFT JOIN dbo.V_TA_PERCEPCION pIbr ON UPPER(LTRIM(RTRIM(pIbr.idPercepcion))) = UPPER(LTRIM(RTRIM(ISNULL(v.RETIBR_IdRetencion, ''))))
              LEFT JOIN dbo.V_TA_PERCEPCION pIva ON UPPER(LTRIM(RTRIM(pIva.idPercepcion))) = UPPER(LTRIM(RTRIM(ISNULL(v.RETIVA_IdRetencion, ''))))
              """
            : string.Empty;
        var percepcionSelect = tienePercepcion
            ? "ISNULL(pIbr.Descripcion, 'Percepción IIBB') AS RetIbrDescripcion, ISNULL(pIva.Descripcion, 'Retención IVA') AS RetIvaDescripcion"
            : "'Percepción IIBB' AS RetIbrDescripcion, 'Retención IVA' AS RetIvaDescripcion";

        var header = await cn.QuerySingleOrDefaultAsync<FacturaCabeceraRow>(new CommandDefinition(
            $"""
            SELECT
                ISNULL(LTRIM(RTRIM(v.TC)), '') AS Tc, ISNULL(LTRIM(RTRIM(v.LETRA)), '') AS Letra,
                ISNULL(LTRIM(RTRIM(v.SUCURSAL)), '') AS Sucursal, ISNULL(LTRIM(RTRIM(v.NUMERO)), '') AS Numero,
                v.FECHA AS Fecha,
                LTRIM(RTRIM(v.Moneda)) AS Moneda, LTRIM(RTRIM(v.UNEGOCIO)) AS UNegocio,
                ISNULL(LTRIM(RTRIM(v.CUENTA)), '') AS CodigoCliente, ISNULL(LTRIM(RTRIM(v.NOMBRE)), '') AS RazonSocial,
                ISNULL(LTRIM(RTRIM(v.DOMICILIO)), '') AS Domicilio, ISNULL(LTRIM(RTRIM(v.LOCALIDAD)), '') AS Localidad,
                ISNULL(LTRIM(RTRIM(v.TELEFONO)), '') AS Telefono,
                ISNULL(LTRIM(RTRIM(v.USUARIO)), '') AS Usuario,
                ISNULL(LTRIM(RTRIM(v.IdVendedor)), '') AS IdVendedor,
                ISNULL(LTRIM(RTRIM(vd.Nombre)), '') AS VendedorNombre,
                ISNULL(LTRIM(RTRIM(v.DOCUMENTONUMERO)), '') AS DocumentoNumero, ISNULL(td.DESCRIPCION, '') AS DocumentoTipoDescripcion,
                {condIvaSelect} AS CondicionIvaDescripcion,
                ISNULL(cv.Descripcion, '') AS CondicionVenta,
                ISNULL(CONVERT(decimal(15,2), v.IMPORTE), 0) AS Total,
                ISNULL(CONVERT(decimal(15,2), v.NetoGravado), 0) AS NetoGravado,
                ISNULL(CONVERT(decimal(15,2), v.NetoNoGravado), 0) AS NetoNoGravado,
                ISNULL(CONVERT(decimal(15,2), v.ImporteImpuestosInternos), 0) AS ImporteImpuestosInternos,
                ISNULL(CONVERT(decimal(9,4), v.AlicIva), 0) AS AlicIva1, ISNULL(CONVERT(decimal(15,2), v.ImporteIva), 0) AS ImporteIva1,
                ISNULL(CONVERT(decimal(9,4), v.AlicIva2), 0) AS AlicIva2, ISNULL(CONVERT(decimal(15,2), v.ImporteIva2), 0) AS ImporteIva2,
                ISNULL(CONVERT(decimal(9,4), v.AlicIVA3), 0) AS AlicIva3, ISNULL(CONVERT(decimal(15,2), v.ImpIVA3), 0) AS ImporteIva3,
                ISNULL(CONVERT(decimal(9,4), v.AlicIVA4), 0) AS AlicIva4, ISNULL(CONVERT(decimal(15,2), v.ImpIVA4), 0) AS ImporteIva4,
                ISNULL(CONVERT(decimal(9,4), v.AlicIvaRec), 0) AS AlicIvaRec, ISNULL(CONVERT(decimal(15,2), v.ImporteIvaRec), 0) AS ImporteIvaRec,
                ISNULL(CONVERT(decimal(9,4), v.PorcDescuento1), 0) AS PorcDescuento1, ISNULL(CONVERT(decimal(15,2), v.ImpDescuento1), 0) AS ImpDescuento1,
                ISNULL(CONVERT(decimal(9,4), v.PorcDescuento2), 0) AS PorcDescuento2, ISNULL(CONVERT(decimal(15,2), v.ImpDescuento2), 0) AS ImpDescuento2,
                ISNULL(CONVERT(decimal(9,4), v.PorcDescuento3), 0) AS PorcDescuento3, ISNULL(CONVERT(decimal(15,2), v.ImpDescuento3), 0) AS ImpDescuento3,
                ISNULL(CONVERT(decimal(9,4), v.PorcDescuento4), 0) AS PorcDescuento4, ISNULL(CONVERT(decimal(15,2), v.ImpDescuento4), 0) AS ImpDescuento4,
                ISNULL(CONVERT(decimal(15,2), v.RETIBR_BaseImponible), 0) AS RetIbrBase, ISNULL(CONVERT(decimal(9,4), v.RETIBR_ALICUOTA), 0) AS RetIbrAlicuota, ISNULL(CONVERT(decimal(15,2), v.RETIBR_Importe), 0) AS RetIbrImporte,
                ISNULL(CONVERT(decimal(15,2), v.RETIVA_BaseImponible), 0) AS RetIvaBase, ISNULL(CONVERT(decimal(9,4), v.RETIVA_ALICUOTA), 0) AS RetIvaAlicuota, ISNULL(CONVERT(decimal(15,2), v.RETIVA_Importe), 0) AS RetIvaImporte,
                ISNULL(CONVERT(decimal(15,2), v.RETGAN_Importe), 0) AS RetGanImporte, ISNULL(CONVERT(decimal(15,2), v.RETSUSS_Importe), 0) AS RetSussImporte,
                {percepcionSelect},
                {electronicoSelect}
            FROM dbo.V_MV_Cpte v
            LEFT JOIN dbo.TA_TIPODOCUMENTO td ON UPPER(LTRIM(RTRIM(td.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(v.DOCUMENTOTIPO, ''))))
            LEFT JOIN dbo.V_TA_Cpra_Vta cv ON UPPER(LTRIM(RTRIM(cv.IDCond_Cpra_Vta))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDCOND_CPRA_VTA, ''))))
            LEFT JOIN dbo.V_TA_VENDEDORES vd ON UPPER(LTRIM(RTRIM(vd.IdVendedor))) = UPPER(LTRIM(RTRIM(ISNULL(v.IdVendedor, ''))))
            {condIvaJoin}
            {percepcionJoin}
            {electronicoJoin}
            WHERE v.TC = @Tc AND v.IDCOMPROBANTE = @IdComprobante;
            """,
            new { Tc = tcTrim, IdComprobante = idTrim },
            cancellationToken: ct));

        var pagos = await cn.QueryAsync<FacturaPagoRow>(new CommandDefinition(
            """
            SELECT
                ISNULL(LTRIM(RTRIM(c.DESCRIPCION)), ISNULL(LTRIM(RTRIM(b.CUENTA)), '')) AS MedioPago,
                ISNULL(CONVERT(decimal(15,2), b.IMPORTE), 0) AS Importe
            FROM dbo.MV_APLICACION a
            INNER JOIN dbo.MV_ASIENTOS b
                ON a.TC = b.TC
               AND a.SUCURSAL = b.SUCURSAL
               AND a.NUMERO = b.NUMERO
               AND a.LETRA = b.LETRA
            LEFT JOIN dbo.MA_CUENTAS c ON b.CUENTA = c.CODIGO
            WHERE a.TCO_ORIGEN = @Tc
              AND a.IDComprobante_ORIGEN = @IdComprobante
              AND b.[DEBE-HABER] = 'D'
            ORDER BY ISNULL(b.[NUMERO ASIENTO], 0), ISNULL(b.SECUENCIA, 0), b.CUENTA;
            """,
            new { Tc = tcTrim, IdComprobante = idTrim },
            cancellationToken: ct));

        if (header is null)
            return null;

        var (letra, codigoAfip) = ResolveLetraYCodigo(header.TipoCpte, header.Letra);
        var tipoCabecera = TiposDocumentoCore.TipoParaComprobante(header.Tc, header.Letra);
        if (!header.TipoCpte.HasValue && header.Tc.Trim().ToUpperInvariant() is "RM" or "CB" or "CBCT")
        {
            letra = TiposDocumentoCore.LetraDe(tipoCabecera);
            codigoAfip = string.Empty;
        }
        if (letra is null)
            return null;

        var lineas = await cn.QueryAsync<FacturaDetalleRow>(new CommandDefinition(
            """
            SELECT
                ISNULL(LTRIM(RTRIM(codigo)), '') AS Codigo, ISNULL(LTRIM(RTRIM(Descripcion)), '') AS Descripcion,
                ISNULL(LTRIM(RTRIM(IdUnidad)), '') AS Unidad,
                ISNULL(CONVERT(decimal(15,4), cantidad), 0) AS Cantidad,
                ISNULL(CONVERT(decimal(15,4), IMPORTE), 0) AS PrecioUnitario,
                ISNULL(CONVERT(decimal(15,2), total), 0) AS Subtotal
            FROM dbo.V_MV_CPTE_DETALLE
            WHERE TC = @Tc AND IDCOMPROBANTE = @IdComprobante
            ORDER BY SECUENCIA;
            """,
            new { Tc = tcTrim, IdComprobante = idTrim },
            cancellationToken: ct));

        var empresa = await BuildEmpresaAsync(cn, header.UNegocio, ct);

        return new FacturaDocumentData
        {
            Empresa = empresa.Empresa,
            CondicionIvaEmisor = empresa.CondicionIva,
            IngresosBrutosEmisor = empresa.IngresosBrutos,
            InicioActividadesEmisor = empresa.InicioActividades,
            Comprobante = new FacturaComprobanteDocumentData
            {
                Tc = header.Tc, Letra = letra, CodigoAfip = codigoAfip,
                TipoDocumento = header.TipoCpte.HasValue ? TiposDocumentoCore.Fiscal(header.TipoCpte.Value)!.Tipo : tipoCabecera,
                PuntoVenta = header.Sucursal, Numero = header.Numero, Fecha = header.Fecha,
                CondicionVenta = header.CondicionVenta,
                Vendedor = string.IsNullOrWhiteSpace(header.VendedorNombre) ? header.IdVendedor : header.VendedorNombre
            },
            Cliente = new FacturaClienteDocumentData
            {
                Codigo = header.CodigoCliente, RazonSocial = header.RazonSocial,
                DocumentoTipoDescripcion = header.DocumentoTipoDescripcion, DocumentoNumero = header.DocumentoNumero,
                CondicionIvaDescripcion = header.CondicionIvaDescripcion,
                Domicilio = header.Domicilio, Localidad = header.Localidad, Telefono = header.Telefono
            },
            Items = lineas.Select(x => new FacturaDocumentItemData
            {
                Codigo = x.Codigo, Descripcion = x.Descripcion, Unidad = x.Unidad,
                Cantidad = x.Cantidad, PrecioUnitario = x.PrecioUnitario, Subtotal = x.Subtotal
            }).ToList(),
            Totales = BuildTotales(header),
            DatosTicket = new FacturaTicketExtrasData
            {
                Pagos = pagos.Select(x => new FacturaPagoDocumentData(x.MedioPago, x.Importe)).ToList(),
                TotalUnidades = lineas.Sum(x => x.Cantidad),
                CantidadProductos = lineas.Count(),
                Cajero = header.Usuario,
                Vendedor = string.IsNullOrWhiteSpace(header.VendedorNombre) ? header.IdVendedor : header.VendedorNombre
            },
            Cae = header.Cae is { Length: > 0 }
                ? new FacturaCaeDocumentData
                {
                    Cae = header.Cae, VencimientoCae = header.VtoCae ?? default,
                    CodigoBarra = header.CodigoBarraCae, Resultado = header.Resultado ?? string.Empty,
                    Motivo = header.Motivo
                }
                : null,
            QrBytes = await GenerateQrAsync(cn, header, empresa.CuitQr, ct)
        };
    }

    private async Task<byte[]?> GenerateQrAsync(SqlConnection cn, FacturaCabeceraRow header, string cuit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(header.Cae)) return null;
        if (!string.Equals(header.Resultado?.Trim(), "A", StringComparison.OrdinalIgnoreCase)) return null;

        var moneda = ResolveMonedaQr(header.Moneda);
        decimal cotizacion = 1;
        if (moneda != "PES")
        {
            // La rutina de impresión VB6 toma la última cotización del día del comprobante.
            var rate = await cn.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
                SELECT TOP (1) CASE WHEN @Moneda = 'DOL' THEN MONEDA2 ELSE MONEDA3 END
                FROM dbo.TA_COTIZACION
                WHERE FECHA_HORA >= @Fecha AND FECHA_HORA < DATEADD(day, 1, @Fecha)
                ORDER BY ID DESC;
                """, new { Moneda = moneda, Fecha = header.Fecha.Date }, cancellationToken: ct));
            cotizacion = rate ?? throw new ArgumentException("No existe cotización histórica para generar el QR del comprobante.");
        }
        if (header.FechaElectronica is null || header.PuntoVentaElectronico is null || header.TipoCpte is null
            || header.TotalElectronico is null || header.TipoDocElectronico is null
            || !long.TryParse(header.NumeroElectronico?.Trim(), out var numero))
            throw new ArgumentException("Faltan datos de autorización electrónica para generar el QR del comprobante.");

        var result = arcaQr.GeneratePng(new ArcaQrData(header.FechaElectronica.Value, cuit ?? "",
            header.PuntoVentaElectronico.Value, header.TipoCpte.Value, numero, header.TotalElectronico.Value,
            moneda, cotizacion, header.TipoDocElectronico, header.NumeroDocElectronico, "E", header.Cae.Trim()));
        logger.LogInformation("Documentos: QR fiscal generado en memoria para {Tc}/{Numero}, tipo {TipoCpte}.", header.Tc, header.NumeroElectronico, header.TipoCpte);
        return result;
    }

    internal static string ResolveMonedaQr(string? codigo) => codigo?.Trim().ToUpperInvariant() switch
    {
        null or "" or "0" or "1" or "PES" => "PES",
        "2" or "DOL" => "DOL",
        "3" or "EUR" => "EUR",
        _ => throw new ArgumentException("La moneda del comprobante no tiene equivalencia ARCA soportada.")
    };

    internal static (string? Letra, string Codigo) ResolveLetraYCodigo(int? tipoCpte, string letraCabecera)
    {
        // Un tipo electrónico informado es autoritativo: cada documento conserva su
        // denominación fiscal aunque comparta la letra de la cabecera.
        if (tipoCpte.HasValue)
            return TiposDocumentoCore.Fiscal(tipoCpte.Value) is { } mapped
                ? (mapped.Letra, mapped.CodigoArca.ToString("D3"))
                : (null, string.Empty);

        // Sin fila en V_MV_CPTE_ELECTRONICOS (comprobante viejo o sin electrónica todavía): se cae
        // a la letra directa de la cabecera, mapeando el código AFIP de 3 dígitos estándar.
        var letra = letraCabecera.Trim().ToUpperInvariant();
        return letra switch
        {
            "A" => ("A", "001"),
            "B" => ("B", "006"),
            "C" => ("C", "011"),
            "X" => ("X", string.Empty),
            _ => (null, string.Empty)
        };
    }

    private static FacturaTotalesDocumentData BuildTotales(FacturaCabeceraRow header)
    {
        var lineasIva = new List<FacturaIvaLineaData>();
        void AddIva(decimal alicuota, decimal importe, bool esRecargo = false)
        {
            if (importe != 0) lineasIva.Add(new FacturaIvaLineaData(alicuota, importe, esRecargo));
        }
        AddIva(header.AlicIva1, header.ImporteIva1);
        AddIva(header.AlicIva2, header.ImporteIva2);
        AddIva(header.AlicIva3, header.ImporteIva3);
        AddIva(header.AlicIva4, header.ImporteIva4);
        AddIva(header.AlicIvaRec, header.ImporteIvaRec, esRecargo: true);

        var lineasDescuento = new List<FacturaDescuentoLineaData>();
        void AddDescuento(int numero, decimal porcentaje, decimal importe)
        {
            if (importe != 0) lineasDescuento.Add(new FacturaDescuentoLineaData(numero, porcentaje, importe));
        }
        AddDescuento(1, header.PorcDescuento1, header.ImpDescuento1);
        AddDescuento(2, header.PorcDescuento2, header.ImpDescuento2);
        AddDescuento(3, header.PorcDescuento3, header.ImpDescuento3);
        AddDescuento(4, header.PorcDescuento4, header.ImpDescuento4);

        var lineasPercepcion = new List<FacturaPercepcionLineaData>();
        if (header.RetIbrImporte != 0) lineasPercepcion.Add(new FacturaPercepcionLineaData(header.RetIbrDescripcion, header.RetIbrBase, header.RetIbrAlicuota, header.RetIbrImporte));
        if (header.RetIvaImporte != 0) lineasPercepcion.Add(new FacturaPercepcionLineaData(header.RetIvaDescripcion, header.RetIvaBase, header.RetIvaAlicuota, header.RetIvaImporte));
        if (header.RetGanImporte != 0) lineasPercepcion.Add(new FacturaPercepcionLineaData("Retención Ganancias", 0, 0, header.RetGanImporte));
        if (header.RetSussImporte != 0) lineasPercepcion.Add(new FacturaPercepcionLineaData("Retención SUSS", 0, 0, header.RetSussImporte));

        return new FacturaTotalesDocumentData
        {
            NetoGravado = header.NetoGravado, NetoNoGravado = header.NetoNoGravado,
            ImporteImpuestosInternos = header.ImporteImpuestosInternos,
            LineasIva = lineasIva, LineasDescuento = lineasDescuento, LineasPercepcion = lineasPercepcion,
            Total = header.Total, Moneda = string.Empty
        };
    }

    private static async Task<(EmpresaDocumentData Empresa, string CondicionIva, string IngresosBrutos, DateTime? InicioActividades, string CuitQr)> BuildEmpresaAsync(SqlConnection cn, string? uNegocio, CancellationToken ct)
    {
        var config = (await cn.QueryAsync<ConfiguracionRow>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) AS Clave,
                   CASE WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR))
                        ELSE ISNULL(CAST(ValorAux AS nvarchar(max)), '') END AS Valor
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'NOMBRE', N'RAZONSOCIAL', N'RAZON_SOCIAL', N'NOMBREEMPRESA', N'CUIT', N'WSFE_CUIT', N'DOMICILIO', N'DIRECCION', N'CALLE', N'NUMERO', N'PISO', N'DEPARTAMENTO', N'CPOSTAL', N'LOCALIDAD', N'PROVINCIA', N'TELEFONO', N'TEL', N'EMAIL', N'MAIL', N'CONDIVAEMPRESA', N'NROINGRESOSBRUTOS', N'INICIOACTIVIDADES');
            """, cancellationToken: ct))).ToDictionary(x => x.Clave, x => x.Valor ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var unidad = string.IsNullOrWhiteSpace(uNegocio) ? null
            : await cn.QuerySingleOrDefaultAsync<UnidadEmisorRow>(new CommandDefinition("""
                SELECT ISNULL(USAEFC, 0) AS UsaEfc, LTRIM(RTRIM(RAZON_SOCIAL)) AS RazonSocial,
                       LTRIM(RTRIM(CUIT)) AS Cuit
                FROM dbo.V_TA_UnidadNegocio
                WHERE LTRIM(RTRIM(Codigo)) = @UNegocio;
                """, new { UNegocio = uNegocio.Trim() }, cancellationToken: ct));
        var nombreGeneral = First(config, "NOMBRE", "RAZONSOCIAL", "RAZON_SOCIAL", "NOMBREEMPRESA");
        var cuitGeneral = First(config, "CUIT", "WSFE_CUIT");
        var emisor = ResolveEmisor(unidad?.UsaEfc == true, unidad?.RazonSocial, unidad?.Cuit,
            nombreGeneral, cuitGeneral, Value(config, "WSFE_CUIT"));

        byte[]? logo = null;
        if (await ExistsAsync(cn, "dbo.TA_LOGOS", ct))
            logo = await cn.QueryFirstOrDefaultAsync<byte[]>(new CommandDefinition("SELECT TOP (1) IMAGEN FROM dbo.TA_LOGOS WHERE IDLOGO = 'LOGOEMPRESA';", cancellationToken: ct));

        var condivaCodigo = Value(config, "CONDIVAEMPRESA");
        var condicionIva = string.Empty;
        if (condivaCodigo.Length > 0 && await ExistsAsync(cn, "dbo.TA_CONDIVA", ct))
            condicionIva = await cn.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                "SELECT TOP (1) ISNULL(DESCRIPCION, '') FROM dbo.TA_CONDIVA WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));",
                new { Codigo = condivaCodigo }, cancellationToken: ct)) ?? string.Empty;

        var inicioActividadesTexto = Value(config, "INICIOACTIVIDADES");
        DateTime? inicioActividades = DateTime.TryParseExact(inicioActividadesTexto, "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : null;

        var domicilio = First(config, "DOMICILIO", "DIRECCION");
        if (string.IsNullOrWhiteSpace(domicilio))
        {
            var calle = string.Join(" ", new[]
            {
                Value(config, "CALLE"), Value(config, "NUMERO"), Value(config, "PISO"), Value(config, "DEPARTAMENTO")
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var localidad = string.Join(" · ", new[]
            {
                Value(config, "LOCALIDAD"), Value(config, "PROVINCIA"), Value(config, "CPOSTAL")
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            domicilio = string.Join(" · ", new[] { calle, localidad }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        var empresa = new EmpresaDocumentData
        {
            Nombre = emisor.Nombre, Cuit = emisor.Cuit,
            Domicilio = domicilio, Telefono = First(config, "TELEFONO", "TEL"),
            Email = First(config, "EMAIL", "MAIL"), Logo = logo
        };
        return (empresa, condicionIva, Value(config, "NROINGRESOSBRUTOS"), inicioActividades, emisor.CuitQr);
    }

    // Una sola decisión para encabezado/pie y QR; la unidad proviene de V_MV_Cpte.
    internal static (string Nombre, string Cuit, string CuitQr) ResolveEmisor(
        bool usaEfc, string? razonSocialUnidad, string? cuitUnidad,
        string nombreGeneral, string cuitGeneral, string cuitWsfe)
    {
        if (usaEfc)
        {
            if (string.IsNullOrWhiteSpace(razonSocialUnidad) || string.IsNullOrWhiteSpace(cuitUnidad))
                throw new ArgumentException("La unidad de negocio usa factura electrónica pero le falta razón social o CUIT.");
            return (razonSocialUnidad.Trim(), cuitUnidad.Trim(), cuitUnidad.Trim());
        }

        // En bases antiguas la unidad puede contener los datos reales aunque
        // USAEFC todavía esté en 0. Se usa como respaldo antes de mostrar un
        // encabezado vacío o genérico.
        if (string.IsNullOrWhiteSpace(nombreGeneral) && !string.IsNullOrWhiteSpace(razonSocialUnidad))
            nombreGeneral = razonSocialUnidad;
        if (string.IsNullOrWhiteSpace(cuitGeneral) && !string.IsNullOrWhiteSpace(cuitUnidad))
            cuitGeneral = cuitUnidad;

        return (nombreGeneral.Trim(), cuitGeneral.Trim(), string.IsNullOrWhiteSpace(cuitWsfe) ? cuitGeneral.Trim() : cuitWsfe.Trim());
    }

    private sealed class UnidadEmisorRow
    {
        public bool UsaEfc { get; set; }
        public string? RazonSocial { get; set; }
        public string? Cuit { get; set; }
    }

    private static DocumentPdfFooterOptions? BuildFooterOptions(DocumentTemplateDefinition definition, string empresaNombre)
    {
        var pie = definition.Blocks.FirstOrDefault(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Pie, StringComparison.OrdinalIgnoreCase));
        if (pie is null) return null;
        var campos = pie.VisibleFields;
        var mostrarPagina = campos is null || campos.Contains("NumeroPagina", StringComparer.OrdinalIgnoreCase);
        var mostrarEmpresa = campos is null || campos.Contains("NombreEmpresa", StringComparer.OrdinalIgnoreCase);
        return new DocumentPdfFooterOptions(mostrarPagina, mostrarEmpresa, empresaNombre);
    }

    private static string Value(IReadOnlyDictionary<string, string> source, string key) => source.TryGetValue(key, out var value) ? value : string.Empty;
    private static string First(IReadOnlyDictionary<string, string> source, params string[] keys) => keys.Select(x => Value(source, x)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
    private static async Task<bool> ExistsAsync(SqlConnection cn, string name, CancellationToken ct) => await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(@Name) IS NULL THEN 0 ELSE 1 END;", new { Name = name }, cancellationToken: ct)) == 1;

    private sealed class ConfiguracionRow { public string Clave { get; set; } = string.Empty; public string? Valor { get; set; } }

    private sealed class FacturaDetalleRow
    {
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Subtotal { get; set; }
    }

    private sealed class FacturaPagoRow
    {
        public string MedioPago { get; set; } = string.Empty;
        public decimal Importe { get; set; }
    }

    private sealed class FacturaCabeceraRow
    {
        public string Tc { get; set; } = string.Empty;
        public string Letra { get; set; } = string.Empty;
        public string Sucursal { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string CodigoCliente { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Domicilio { get; set; } = string.Empty;
        public string Localidad { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string IdVendedor { get; set; } = string.Empty;
        public string VendedorNombre { get; set; } = string.Empty;
        public string DocumentoNumero { get; set; } = string.Empty;
        public string DocumentoTipoDescripcion { get; set; } = string.Empty;
        public string CondicionIvaDescripcion { get; set; } = string.Empty;
        public string CondicionVenta { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal NetoGravado { get; set; }
        public decimal NetoNoGravado { get; set; }
        public decimal ImporteImpuestosInternos { get; set; }
        public decimal AlicIva1 { get; set; }
        public decimal ImporteIva1 { get; set; }
        public decimal AlicIva2 { get; set; }
        public decimal ImporteIva2 { get; set; }
        public decimal AlicIva3 { get; set; }
        public decimal ImporteIva3 { get; set; }
        public decimal AlicIva4 { get; set; }
        public decimal ImporteIva4 { get; set; }
        public decimal AlicIvaRec { get; set; }
        public decimal ImporteIvaRec { get; set; }
        public decimal PorcDescuento1 { get; set; }
        public decimal ImpDescuento1 { get; set; }
        public decimal PorcDescuento2 { get; set; }
        public decimal ImpDescuento2 { get; set; }
        public decimal PorcDescuento3 { get; set; }
        public decimal ImpDescuento3 { get; set; }
        public decimal PorcDescuento4 { get; set; }
        public decimal ImpDescuento4 { get; set; }
        public decimal RetIbrBase { get; set; }
        public decimal RetIbrAlicuota { get; set; }
        public decimal RetIbrImporte { get; set; }
        public decimal RetIvaBase { get; set; }
        public decimal RetIvaAlicuota { get; set; }
        public decimal RetIvaImporte { get; set; }
        public decimal RetGanImporte { get; set; }
        public decimal RetSussImporte { get; set; }
        public string RetIbrDescripcion { get; set; } = string.Empty;
        public string RetIvaDescripcion { get; set; } = string.Empty;
        public int? TipoCpte { get; set; }
        public string? Cae { get; set; }
        public DateTime? VtoCae { get; set; }
        public string? CodigoBarraCae { get; set; }
        public string? Resultado { get; set; }
        public string? Motivo { get; set; }
        public string? Moneda { get; set; }
        public string? UNegocio { get; set; }
        public DateTime? FechaElectronica { get; set; }
        public int? PuntoVentaElectronico { get; set; }
        public string? NumeroElectronico { get; set; }
        public decimal? TotalElectronico { get; set; }
        public int? TipoDocElectronico { get; set; }
        public string? NumeroDocElectronico { get; set; }
    }
}
