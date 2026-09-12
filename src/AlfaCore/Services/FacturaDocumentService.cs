using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Arma el PDF de una Factura A/B/C YA EMITIDA y aprobada por AFIP -- el sistema legacy ya
/// hizo el WSFE y guardó CAE/QR en la base (V_MV_CPTE_ELECTRONICOS/Aux_MV_CpteQR); este servicio
/// solo LEE esos datos, nunca llama a AFIP. Espejo de CotizacionDocumentService, pero la fuente de
/// datos es completamente distinta (tablas transaccionales de venta, no un módulo propio con
/// estado editable).</summary>
public sealed class FacturaDocumentService(
    IConfiguration configuration,
    IDocumentTemplateService templates,
    IDocumentRenderer renderer,
    IDocumentPdfService pdfService,
    IAppEventService appEvents,
    ILogger<FacturaDocumentService> logger) : IFacturaDocumentService
{
    private const string ModuleName = "Documentos";

    // Solo Factura -- Tipo_Cpte de Nota de Crédito/Débito (2/3/7/8/12/13...) se rechaza.
    private static readonly IReadOnlyDictionary<int, (string Letra, string Codigo)> CodigoPorTipoCpte = new Dictionary<int, (string, string)>
    {
        [1] = ("A", "001"),
        [6] = ("B", "006"),
        [11] = ("C", "011")
    };

    private string ConnectionString => configuration.GetConnectionString("AlfaGestion")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<DocumentRenderResult> RenderAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default)
    {
        try
        {
            var data = await BuildDataAsync(tc, idComprobante, ct)
                ?? throw new InvalidOperationException("El comprobante indicado no existe o no es una Factura A/B/C.");
            var tipoDocumento = TiposDocumentoCore.ParaLetra(data.Comprobante.Letra);
            var template = await templates.ResolveAsync(tipoDocumento, uNegocio, ct);
            var definition = templates.DeserializeAndValidate(template.TemplateJson, tipoDocumento);
            var theme = await templates.GetGeneralThemeAsync(ct);
            var html = renderer.RenderFactura(definition, data, template.CssCustom, theme);
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

    public async Task<byte[]> GeneratePdfAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default)
    {
        var result = await RenderAsync(tc, idComprobante, uNegocio, ct);
        var pdf = await pdfService.GenerateAsync(result.Html, result.Footer, ct);
        logger.LogInformation("Documentos: PDF de factura {Tc}/{IdComprobante}, plantilla {IdTemplate}.", tc, idComprobante, result.IdTemplate);
        return pdf;
    }

    public async Task<byte[]?> GeneratePdfParaClienteAsync(string codigoCliente, int idComprobante, CancellationToken ct = default)
    {
        var codigo = (codigoCliente ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(codigo) || idComprobante <= 0)
            return null;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        // Resuelve Tc/IdComprobante desde el ID interno y valida pertenencia ANTES de armar nada --
        // mismo chequeo que PortalClienteService.GetComprobanteClienteDetalleAsync (nunca distingue
        // "no existe" de "es de otro cliente", ambos casos devuelven null).
        var row = await cn.QuerySingleOrDefaultAsync<(string Tc, string IdComprobanteTexto, string Cuenta)>(new CommandDefinition(
            "SELECT ISNULL(LTRIM(RTRIM(TC)), '') AS Tc, ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto, ISNULL(LTRIM(RTRIM(CUENTA)), '') AS Cuenta FROM dbo.V_MV_Cpte WHERE ID = @Id;",
            new { Id = idComprobante }, cancellationToken: ct));

        if (row.Tc.Length == 0 || !string.Equals(row.Cuenta, codigo, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            return await GeneratePdfAsync(row.Tc, row.IdComprobanteTexto, null, ct);
        }
        catch (InvalidOperationException)
        {
            // No es Factura A/B/C (ej. nota de crédito, recibo) -- Portal Cliente no tiene nada
            // más que ofrecer para este comprobante, se degrada a "no disponible" en vez de error.
            return null;
        }
    }

    public async Task<IReadOnlyList<FacturaResumenDto>> SearchRecientesAsync(string letra, int top = 20, CancellationToken ct = default)
    {
        var tipoCpte = CodigoPorTipoCpte.FirstOrDefault(x => string.Equals(x.Value.Letra, letra, StringComparison.OrdinalIgnoreCase)).Key;
        if (tipoCpte == 0)
            return [];

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
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
            ORDER BY e.Fecha_Cpte DESC;
            """, new { Top = top, TipoCpte = tipoCpte }, cancellationToken: ct));
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
        var electronicoJoin = tieneElectronico ? "LEFT JOIN dbo.V_MV_CPTE_ELECTRONICOS e ON e.TC = v.TC AND e.IdComprobante = v.IDCOMPROBANTE" : string.Empty;
        var electronicoSelect = tieneElectronico
            ? "e.Tipo_Cpte AS TipoCpte, e.CAE AS Cae, e.VtoCAE AS VtoCae, e.CodigoBarraCAE AS CodigoBarraCae, e.Resultado AS Resultado, ISNULL(CAST(e.Motivo AS nvarchar(500)), '') AS Motivo"
            : "CAST(NULL AS int) AS TipoCpte, CAST(NULL AS nvarchar(20)) AS Cae, CAST(NULL AS datetime) AS VtoCae, CAST(NULL AS nvarchar(60)) AS CodigoBarraCae, CAST(NULL AS nvarchar(4)) AS Resultado, '' AS Motivo";

        var tieneQr = await ExistsAsync(cn, "dbo.Aux_MV_CpteQR", ct);
        var qrJoin = tieneQr ? "LEFT JOIN dbo.Aux_MV_CpteQR q ON q.TC = v.TC AND q.IDCOMPROBANTE = v.IDCOMPROBANTE" : string.Empty;
        var qrSelect = tieneQr ? "q.QR_AFIP AS QrAfip" : "CAST(NULL AS varbinary(max)) AS QrAfip";

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
                ISNULL(LTRIM(RTRIM(v.CUENTA)), '') AS CodigoCliente, ISNULL(LTRIM(RTRIM(v.NOMBRE)), '') AS RazonSocial,
                ISNULL(LTRIM(RTRIM(v.DOMICILIO)), '') AS Domicilio, ISNULL(LTRIM(RTRIM(v.LOCALIDAD)), '') AS Localidad,
                ISNULL(LTRIM(RTRIM(v.TELEFONO)), '') AS Telefono,
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
                {electronicoSelect}, {qrSelect}
            FROM dbo.V_MV_Cpte v
            LEFT JOIN dbo.TA_TIPODOCUMENTO td ON UPPER(LTRIM(RTRIM(td.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(v.DOCUMENTOTIPO, ''))))
            LEFT JOIN dbo.V_TA_Cpra_Vta cv ON UPPER(LTRIM(RTRIM(cv.IDCond_Cpra_Vta))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDCOND_CPRA_VTA, ''))))
            {condIvaJoin}
            {percepcionJoin}
            {electronicoJoin}
            {qrJoin}
            WHERE v.TC = @Tc AND v.IDCOMPROBANTE = @IdComprobante;
            """,
            new { Tc = tcTrim, IdComprobante = idTrim },
            cancellationToken: ct));

        if (header is null)
            return null;

        var (letra, codigoAfip) = ResolveLetraYCodigo(header);
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

        var empresa = await BuildEmpresaAsync(cn, ct);

        return new FacturaDocumentData
        {
            Empresa = empresa.Empresa,
            CondicionIvaEmisor = empresa.CondicionIva,
            IngresosBrutosEmisor = empresa.IngresosBrutos,
            InicioActividadesEmisor = empresa.InicioActividades,
            Comprobante = new FacturaComprobanteDocumentData
            {
                Tc = header.Tc, Letra = letra, CodigoAfip = codigoAfip,
                PuntoVenta = header.Sucursal, Numero = header.Numero, Fecha = header.Fecha,
                CondicionVenta = header.CondicionVenta
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
            Cae = header.Cae is { Length: > 0 }
                ? new FacturaCaeDocumentData
                {
                    Cae = header.Cae, VencimientoCae = header.VtoCae ?? default,
                    CodigoBarra = header.CodigoBarraCae, Resultado = header.Resultado ?? string.Empty,
                    Motivo = header.Motivo
                }
                : null,
            QrBytes = header.QrAfip
        };
    }

    private static (string? Letra, string Codigo) ResolveLetraYCodigo(FacturaCabeceraRow header)
    {
        if (header.TipoCpte is { } tipoCpte && CodigoPorTipoCpte.TryGetValue(tipoCpte, out var mapped))
            return (mapped.Letra, mapped.Codigo);

        // Sin fila en V_MV_CPTE_ELECTRONICOS (comprobante viejo o sin electrónica todavía): se cae
        // a la letra directa de la cabecera, mapeando el código AFIP de 3 dígitos estándar.
        var letra = header.Letra.Trim().ToUpperInvariant();
        return letra switch
        {
            "A" => ("A", "001"),
            "B" => ("B", "006"),
            "C" => ("C", "011"),
            _ => (null, string.Empty)
        };
    }

    private static FacturaTotalesDocumentData BuildTotales(FacturaCabeceraRow header)
    {
        var lineasIva = new List<FacturaIvaLineaData>();
        void AddIva(decimal alicuota, decimal importe) { if (importe != 0) lineasIva.Add(new FacturaIvaLineaData(alicuota, importe)); }
        AddIva(header.AlicIva1, header.ImporteIva1);
        AddIva(header.AlicIva2, header.ImporteIva2);
        AddIva(header.AlicIva3, header.ImporteIva3);
        AddIva(header.AlicIva4, header.ImporteIva4);
        AddIva(header.AlicIvaRec, header.ImporteIvaRec);

        var lineasDescuento = new List<FacturaDescuentoLineaData>();
        void AddDescuento(decimal porcentaje, decimal importe) { if (importe != 0) lineasDescuento.Add(new FacturaDescuentoLineaData(porcentaje, importe)); }
        AddDescuento(header.PorcDescuento1, header.ImpDescuento1);
        AddDescuento(header.PorcDescuento2, header.ImpDescuento2);
        AddDescuento(header.PorcDescuento3, header.ImpDescuento3);
        AddDescuento(header.PorcDescuento4, header.ImpDescuento4);

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

    private static async Task<(EmpresaDocumentData Empresa, string CondicionIva, string IngresosBrutos, DateTime? InicioActividades)> BuildEmpresaAsync(SqlConnection cn, CancellationToken ct)
    {
        var config = (await cn.QueryAsync<ConfiguracionRow>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) AS Clave,
                   CASE WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR))
                        ELSE ISNULL(CAST(ValorAux AS nvarchar(max)), '') END AS Valor
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'NOMBRE', N'CUIT', N'DOMICILIO', N'DIRECCION', N'TELEFONO', N'TEL', N'EMAIL', N'MAIL', N'CONDIVAEMPRESA', N'NROINGRESOSBRUTOS', N'INICIOACTIVIDADES');
            """, cancellationToken: ct))).ToDictionary(x => x.Clave, x => x.Valor ?? string.Empty, StringComparer.OrdinalIgnoreCase);

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

        var empresa = new EmpresaDocumentData
        {
            Nombre = Value(config, "NOMBRE"), Cuit = Value(config, "CUIT"),
            Domicilio = First(config, "DOMICILIO", "DIRECCION"), Telefono = First(config, "TELEFONO", "TEL"),
            Email = First(config, "EMAIL", "MAIL"), Logo = logo
        };
        return (empresa, condicionIva, Value(config, "NROINGRESOSBRUTOS"), inicioActividades);
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
        public byte[]? QrAfip { get; set; }
    }
}
