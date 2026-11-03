using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using Dapper;

namespace AlfaCore.Services;

public sealed partial class CargaComprobantesService(
    ISessionService sessionService, IAppUserSessionService appUserSession,
    IPermissionService permissions, IAppEventService events,
    CargaComprobantesRepository repository, CargaComprobantesElectronicaVerificador electronica,
    CargaComprobantesElectronicaEmisor emisorPrueba, CargaComprobantesEmisionService emision,
    IFacturaDocumentService documents, IDocumentPdfService pdf, CorreoSalienteService? correo = null) : ICargaComprobantesService
{
    public const string MenuKey = "D60CCW";

    public Task<IReadOnlyList<CargaComprobantesOpcionFiltro>> BuscarFacturasAsociadasAsync(string cliente, string unidad, string texto, CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<CargaComprobantesOpcionFiltro>>("FacturasAsociadas", async (cn, ctx, token) =>
        {
            if (!ctx.AdminCentral && unidad.Trim() != ctx.UnidadNegocio)
                throw new CargaComprobantesReglaException("La unidad de negocio no pertenece al usuario actual.");
            var esPrueba = ArcaModoEmision.EsBasePrueba(cn.Database, ArcaModoEmision.NombreSesion(sessionService));
            // Las de AlfaCore y, en una base real, también las de Alfa Gestión con CAE (últimos 18 meses).
            return (await cn.QueryAsync<CargaComprobantesOpcionFiltro>(new CommandDefinition($"""
                SELECT TOP(30) CONVERT(nvarchar(15),f.Id) Codigo,
                    LTRIM(RTRIM(f.Comprobante))+' · '+CONVERT(nvarchar(10),f.Fecha,103)+' · $ '+CONVERT(nvarchar(30),f.Total)
                    + CASE WHEN f.Acreditado<>0 THEN ' · saldo $ '+CONVERT(nvarchar(30),f.Total-f.Acreditado) ELSE '' END Descripcion
                FROM ({CargaComprobantesAsociadoPreparador.SqlFactura}
                    AND LTRIM(RTRIM(c.CUENTA))=@Cliente AND LTRIM(RTRIM(c.UNEGOCIO))=@Unidad
                    AND c.FECHA >= DATEADD(month,-18,GETDATE()) AND c.IDCOMPROBANTE LIKE @Texto) f
                ORDER BY f.Fecha DESC, f.Id DESC;
                """, new { Cliente = cliente.Trim(), Unidad = unidad.Trim(), Texto = "%" + texto.Trim() + "%", EsPrueba = esPrueba,
                    Ambiente = esPrueba ? ArcaModoEmision.Homologacion : ArcaModoEmision.Produccion }, cancellationToken: token))).ToList();
        }, ct);

    /// <summary>Otros conceptos (grilla «Otros», TIPO_OBS 'OC') de una factura, para armar su nota de crédito.</summary>
    public Task<IReadOnlyList<CargaComprobantesDirectaLinea>> GetConceptosFacturaAsync(int facturaId, CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<CargaComprobantesDirectaLinea>>("ConceptosFactura", async (cn, ctx, token) =>
            (await cn.QueryAsync<(string Detalle, decimal Importe)>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(500),o.OBSERVACION),''))), ISNULL(o.IMPORTE_S_IVA, o.IMPORTE)
                FROM dbo.V_MV_CPTE c JOIN dbo.V_MV_CPTE_OBSERV o ON o.TC=c.TC AND o.IDCOMPROBANTE=c.IDCOMPROBANTE AND o.IDCOMPLEMENTO=0
                WHERE c.ID=@Id AND LTRIM(RTRIM(o.TIPO_OBS))='OC' AND (@AdminCentral=1 OR LTRIM(RTRIM(c.UNEGOCIO))=@Unidad)
                ORDER BY o.SECUENCIA;
                """, new { Id = facturaId, ctx.AdminCentral, Unidad = ctx.UnidadNegocio.Trim() }, cancellationToken: token)))
            .Where(x => x.Detalle.Length > 0)
            .Select(x => new CargaComprobantesDirectaLinea { Concepto = x.Detalle.Length > 250 ? x.Detalle[..250] : x.Detalle,
                Cantidad = 1, PrecioUnitarioNeto = decimal.Round(x.Importe, 4) }).ToList(), ct);

    public Task<IReadOnlyList<CargaComprobantesOpcionFiltro>> BuscarClientesDirectaAsync(string texto, CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<CargaComprobantesOpcionFiltro>>("ClientesDirecta", async (cn, ctx, token) =>
        {
            var busqueda = texto.Trim();
            if (busqueda.Length < 1) return []; // como en Alfa, con una letra ya busca (2026-10-22)
            static string Literal(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
            var documento = new string(busqueda.Where(char.IsAsciiDigit).ToArray());
            return (await cn.QueryAsync<CargaComprobantesOpcionFiltro>(new CommandDefinition("""
                SELECT TOP(30) LTRIM(RTRIM(CODIGO)) Codigo,RAZON_SOCIAL Descripcion FROM dbo.VT_CLIENTES
                WHERE ISNULL(BLOQUEO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 AND
                    (LTRIM(RTRIM(CODIGO)) LIKE @Texto ESCAPE '\' OR RAZON_SOCIAL LIKE @Texto ESCAPE '\'
                     OR NUMERO_DOCUMENTO LIKE @Texto ESCAPE '\'
                     OR (@EsDocumento=1 AND REPLACE(REPLACE(REPLACE(NUMERO_DOCUMENTO,'-',''),'.',''),' ','') LIKE @Documento))
                ORDER BY CASE WHEN LTRIM(RTRIM(CODIGO))=@Exacto OR
                    (@EsDocumento=1 AND REPLACE(REPLACE(REPLACE(NUMERO_DOCUMENTO,'-',''),'.',''),' ','')=@DocumentoExacto) THEN 0
                    WHEN RAZON_SOCIAL LIKE @Inicio ESCAPE '\' OR LTRIM(RTRIM(CODIGO)) LIKE @Inicio ESCAPE '\' THEN 1 ELSE 2 END,
                    RAZON_SOCIAL,CODIGO;
                """, new { Texto = "%" + Literal(busqueda) + "%", Inicio = Literal(busqueda) + "%", Exacto = busqueda,
                    EsDocumento = documento.Length >= 2 && busqueda.All(c => char.IsAsciiDigit(c) || c is '-' or '.' or ' '),
                    Documento = "%" + documento + "%", DocumentoExacto = documento }, cancellationToken: token))).ToList();
        }, ct);

    public Task<IReadOnlyList<CrmCotizacionArticuloDto>> BuscarArticulosDirectaAsync(string cliente, string texto, CancellationToken ct = default)
        => BuscarArticulosDirectaAsync(new CargaComprobantesDirecta { Cliente = cliente }, texto, ct);

    public Task<IReadOnlyList<CrmCotizacionArticuloDto>> BuscarArticulosDirectaAsync(CargaComprobantesDirecta directa, string texto, CancellationToken ct = default)
        => ExecuteAsync("ArticulosDirecta", async (cn, ctx, token) =>
        {
            if (string.IsNullOrWhiteSpace(directa.Cliente)) throw new CargaComprobantesReglaException("Primero elegí el cliente para resolver sus precios.");
            var precios = new ArticuloPrecioResolverService();
            var pricing = await new CargaComprobantesSeleccionPrecios().ResolverAsync(cn, ctx.Usuario, directa, token);
            return await precios.SearchArticulosAsync(cn, pricing, texto, 30, token);
        }, ct);

    public Task<IReadOnlyDictionary<string, decimal>> GetStockArticulosAsync(IReadOnlyList<string> articulos, string? deposito, CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyDictionary<string, decimal>>("StockArticulosDirecta", async (cn, _, token) =>
        {
            var ids = articulos.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
            if (ids.Length == 0) return new Dictionary<string, decimal>();
            var filas = await cn.QueryAsync<(string Articulo, decimal Stock)>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(s.IDArticulo)) Articulo, SUM(ISNULL(s.Cantidad,0)) Stock
                FROM dbo.V_MV_STOCK s
                WHERE LTRIM(RTRIM(s.IDArticulo)) IN @Ids AND ISNULL(s.Anulado,0)=0
                    AND (@Deposito IS NULL OR LTRIM(RTRIM(s.IdDeposito))=@Deposito)
                GROUP BY LTRIM(RTRIM(s.IDArticulo));
                """, new { Ids = ids, Deposito = string.IsNullOrWhiteSpace(deposito) ? null : deposito.Trim() }, cancellationToken: token));
            return filas.ToDictionary(x => x.Articulo, x => x.Stock, StringComparer.OrdinalIgnoreCase);
        }, ct);

    public Task<CargaComprobantesPlanGrabacion> ValidarDirectaAsync(CargaComprobantesDirecta directa, int talonario, CancellationToken ct = default)
        => ExecuteAsync("ValidarDirecta", (cn, ctx, token) => emision.ValidarAsync(cn, ctx, 0, talonario, [], token, directa), ct);

    public Task<CargaComprobantesGrabacionResultado?> ConsultarDirectaAsync(Guid borrador, Guid operacion, CancellationToken ct = default)
        => ExecuteAsync("ConsultarDirecta", (cn, ctx, token) => emision.ConsultarDirectaAsync(cn, ctx, borrador, operacion, token), ct);

    public Task<CargaComprobantesGrabacionResultado> GrabarDirectaAsync(CargaComprobantesDirecta directa, int talonario,
        Guid operacion, string huellaEsperada, CancellationToken ct = default)
        => ExecuteAsync("GrabarDirecta", (cn, ctx, token) => emision.EmitirAsync(cn, ctx, 0, talonario, [], operacion,
            huellaEsperada, () => EnsureContext(ctx.SesionId, ctx.Usuario, ctx.Sistema), token, directa), ct);

    public Task<byte[]> GetPdfFacturaAsync(Guid operacion, CancellationToken ct = default)
        => ExecuteAsync("PdfFacturaHomologacion", async (cn, ctx, token) =>
        {
            var resultado = await emision.GetAsync(cn, ctx, operacion, token);
            if (resultado?.Estado != "GRABADA" || string.IsNullOrWhiteSpace(resultado.Comprobante))
                throw new CargaComprobantesReglaException("La factura todavía no tiene grabación comercial completa.");
            var documento = await documents.RenderAsync(resultado.Tc, resultado.Comprobante, resultado.UnidadNegocio, token);
            if (!ArcaModoEmision.EsBasePrueba(cn.Database, ArcaModoEmision.NombreSesion(sessionService)))
                return await pdf.GenerateAsync(documento.Html, documento.Footer, token);
            var html = documento.Html.Replace("</head>", "<style>.ventas-homologacion{position:fixed;top:2px;left:0;right:0;text-align:center;font-size:10px;font-weight:bold;color:#a00;z-index:9999}</style></head>", StringComparison.OrdinalIgnoreCase)
                .Replace("</body>", "<div class='ventas-homologacion'>HOMOLOGACIÓN · COMPROBANTE DE PRUEBA · SIN VALIDEZ FISCAL</div></body>", StringComparison.OrdinalIgnoreCase);
            return await pdf.GenerateAsync(html, documento.Footer, token);
        }, ct);

    public Task<CargaComprobantesPlanGrabacion> ValidarGrabacionAsync(int origenId, int talonario,
        IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct = default)
        => ExecuteAsync("ValidarGrabacion", (cn, ctx, token) => emision.ValidarAsync(cn, ctx, origenId, talonario, cantidades, token), ct);

    public Task<CargaComprobantesGrabacionResultado> GrabarFacturaAsync(int origenId, int talonario,
        IReadOnlyList<CargaComprobantesCantidad> cantidades, Guid operacion, string huellaEsperada, CancellationToken ct = default)
        => ExecuteAsync("GrabarFacturaHomologacion", (cn, ctx, token) => emision.EmitirAsync(cn, ctx, origenId, talonario, cantidades,
            operacion, huellaEsperada, () => EnsureContext(ctx.SesionId, ctx.Usuario, ctx.Sistema), token), ct);

    public Task<CargaComprobantesGrabacionResultado> RecuperarGrabacionAsync(Guid operacion, CancellationToken ct = default)
        => ExecuteAsync("RecuperarFacturaHomologacion", (cn, ctx, token) => emision.RecuperarAsync(cn, ctx, operacion,
            () => EnsureContext(ctx.SesionId, ctx.Usuario, ctx.Sistema), token), ct);

    public Task<CargaComprobantesElectronicaPrueba> RecuperarPruebaElectronicaAsync(Guid operacion, CancellationToken ct = default)
        => ExecuteAsync("ConciliarPruebaHomologacion", (cn, ctx, token) => emisorPrueba.RecuperarAsync(cn, ctx, operacion, token), ct);

    public Task<CargaComprobantesGrabacionResultado?> GetUltimaGrabacionAsync(int origenId, CancellationToken ct = default)
        => ExecuteAsync("UltimaGrabacion", async (cn, ctx, token) =>
        {
            var operacion = await cn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT TOP(1) Operacion FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE OrigenId=@Id
                    AND (@AdminCentral=1 OR LTRIM(RTRIM(UnidadNegocio))=@UnidadNegocio) ORDER BY FechaCreacion DESC;
                """, new { Id = origenId, ctx.AdminCentral, ctx.UnidadNegocio }, cancellationToken: token));
            return operacion.HasValue ? await emision.GetAsync(cn, ctx, operacion.Value, token) : null;
        }, ct);

    public Task<IReadOnlyList<CargaComprobantesGrabacionResultado>> GetGrabacionesRecientesAsync(CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<CargaComprobantesGrabacionResultado>>("GrabacionesRecientes", async (cn, ctx, token) =>
            (await cn.QueryAsync<CargaComprobantesGrabacionResultado>(new CommandDefinition("""
                SELECT TOP(25) Operacion,OrigenId,Borrador,Tc,FacturaAsociadaId,UnidadNegocio,Estado,FacturaId,Comprobante,NumeroAsiento,Cae,Vencimiento,ISNULL(Mensaje,'') Mensaje
                FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE @AdminCentral=1 OR LTRIM(RTRIM(UnidadNegocio))=@UnidadNegocio
                ORDER BY FechaCreacion DESC;
                """, new { ctx.AdminCentral, ctx.UnidadNegocio }, cancellationToken: token))).ToList(), ct);

    public Task<CargaComprobantesContexto> GetContextoAsync(CancellationToken ct = default)
        => ExecuteAsync("Contexto", async (cn, contexto, token) =>
        {
            (contexto.ModoEmision, contexto.ModoEmisionMotivo) = await ArcaModoEmision.LeerAsync(cn, ArcaModoEmision.NombreSesion(sessionService), token);
            return contexto;
        }, ct);

    public Task<CargaComprobantesElectronicaPrueba?> GetUltimaPruebaElectronicaAsync(int origenId, CancellationToken ct = default)
        => ExecuteAsync("ConsultarPruebaHomologacion", async (cn, contexto, token) =>
        {
            await repository.GetOrigenAsync(cn, contexto, origenId, token);
            return await cn.QuerySingleOrDefaultAsync<CargaComprobantesElectronicaPrueba>(new CommandDefinition("""
                SELECT TOP(1) Operacion,OrigenId,Estado,PuntoVenta,TipoComprobante,Numero,Importe,Cae,Vencimiento
                FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA WHERE OrigenId=@OrigenId ORDER BY FechaCreacion DESC;
                """, new { OrigenId = origenId }, cancellationToken: token));
        }, ct);

    public Task<CargaComprobantesElectronicaEstado> VerificarElectronicaAsync(int? origenId = null, CancellationToken ct = default)
        => ExecuteAsync("VerificarElectronicaHomologacion", async (cn, contexto, token) =>
        {
            var unidad = origenId.HasValue
                ? (await repository.GetOrigenAsync(cn, contexto, origenId.Value, token)).UnidadNegocio
                : contexto.UnidadNegocio;
            return await electronica.VerificarAsync(cn, unidad, contexto.PuntoVentaElectronico, token);
        }, ct);

    public Task<CargaComprobantesElectronicaPrueba> EmitirPruebaElectronicaAsync(int origenId, int talonario,
        IReadOnlyList<CargaComprobantesCantidad> cantidades, Guid operacion, CancellationToken ct = default)
        => ExecuteAsync("EmitirPruebaHomologacion", async (cn, contexto, token) =>
        {
            if (CargaComprobantesConsolidacion.Identidades(origenId,cantidades).Length>1 ||
                cantidades.Any(x=>x.PrecioUnitarioNeto.HasValue || x.Descuento.HasValue))
                throw new CargaComprobantesReglaException("Para varios orígenes o precios editados usá Validar grabación y Grabar factura de prueba.");
            var origen = await repository.GetOrigenAsync(cn, contexto, origenId, token);
            if (!contexto.Talonarios.Any(x => x.Talonario == talonario && (x.UnidadNegocio.Length == 0 || x.UnidadNegocio == origen.UnidadNegocio)))
                throw new CargaComprobantesReglaException("Elegí un talonario compatible con el origen.");
            return await emisorPrueba.EmitirAsync(cn, contexto, origen, cantidades, operacion, token);
        }, ct);

    public Task<PagedResult<CargaComprobantesOrigen>> BuscarOrigenesAsync(CargaComprobantesFiltro filtro, CancellationToken ct = default)
        => ExecuteAsync("Buscar", (cn, contexto, token) =>
        {
            if (filtro.TipoOrigen is not ("NP" or "RM"))
                throw new CargaComprobantesReglaException("Elegí pedidos o remitos como origen.");
            return repository.BuscarAsync(cn, contexto, filtro, token);
        }, ct);

    public Task<IReadOnlyList<CargaComprobantesFacturacionAutomaticaFila>> BuscarFacturacionAutomaticaAsync(CargaComprobantesFiltro filtro, CancellationToken ct = default)
        => ExecuteAsync("BuscarFacturacionAutomatica", (cn, contexto, token) =>
            CargaComprobantesFacturacionAutomatica.BuscarAsync(cn, contexto, filtro, token), ct);

    public Task<CargaComprobantesFacturacionAutomaticaResultado> PrevisualizarFacturacionAutomaticaAsync(string cuenta, DateTime fechaFacturacion, CancellationToken ct = default)
        => ResultadoFacturacionAutomaticaAsync(cuenta, "PrevisualizarFacturacionAutomatica", async (cn, ctx, token) =>
        {
            var (borrador, talonario, fila) = await CargaComprobantesFacturacionAutomatica.PrepararAsync(cn, ctx, cuenta, fechaFacturacion.Date, token);
            // Con una fecha emitible, la vista previa la valida igual que la emisión (incluido el último comprobante).
            var hoy = await cn.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT CONVERT(date,GETDATE());", cancellationToken: token));
            if (Math.Abs((fechaFacturacion.Date - hoy.Date).TotalDays) <= CargaComprobantesFacturacionAutomatica.DiasFechaEmision)
                borrador.Fecha = fechaFacturacion.Date;
            var plan = await emision.ValidarAsync(cn, ctx, 0, talonario, [], token, borrador);
            return Resumen(plan, fila, talonario, "VALIDADA",
                "Validada: se ensayó la grabación completa y se revirtió. No se pidió CAE ni se modificó la facturación automática.");
        }, ct);

    public Task<CargaComprobantesFacturacionAutomaticaResultado> EmitirFacturacionAutomaticaAsync(string cuenta, DateTime fechaFacturacion, CancellationToken ct = default, bool enviarMail = false, bool comoProforma = false)
        => ResultadoFacturacionAutomaticaAsync(cuenta, "EmitirFacturacionAutomatica", async (cn, ctx, token) =>
        {
            var hoy = await cn.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT CONVERT(date,GETDATE());", cancellationToken: token));
            // La factura sale con la fecha a facturar (ARCA acepta hasta 5 días antes o después de hoy y no
            // anterior al último comprobante del punto de venta): la fecha de última factura y el período
            // del concepto quedan coherentes con esa fecha.
            var fecha = fechaFacturacion.Date;
            if (Math.Abs((fecha - hoy.Date).TotalDays) > CargaComprobantesFacturacionAutomatica.DiasFechaEmision)
                throw new CargaComprobantesReglaException($"La fecha a facturar puede ser hasta {CargaComprobantesFacturacionAutomatica.DiasFechaEmision} días antes o después de hoy ({hoy:dd/MM/yyyy}).");
            var (borrador, talonario, fila) = await CargaComprobantesFacturacionAutomatica.PrepararAsync(cn, ctx, cuenta, fecha, token, comoProforma ? "FP" : null);
            borrador.Fecha = fecha;
            var plan = await emision.ValidarAsync(cn, ctx, 0, talonario, [], token, borrador);
            var resultado = await emision.EmitirAsync(cn, ctx, 0, talonario, [], Guid.NewGuid(), plan.Huella,
                () => EnsureContext(ctx.SesionId, ctx.Usuario, ctx.Sistema), token, borrador);
            // Rechazada: nada grabado, se corrige y se vuelve a emitir. Pendiente: hay que recuperarla antes.
            var resumen = Resumen(plan, fila, talonario, resultado.Estado switch { "GRABADA" => "GRABADA", "RECHAZADA" => "RECHAZADA", _ => "PENDIENTE" },
                resultado.Estado switch
                {
                    "GRABADA" when resultado.Tc == "FP" => "Proforma grabada (sin CAE, no va a ARCA); se actualizó la última factura y las cuotas. Más adelante se podrá convertir en factura electrónica.",
                    "GRABADA" => comoProforma ? "El cliente ya tenía una factura emitida hoy: no se generó la proforma." : (resultado.Cae is { Length: > 0 } && !ArcaModoEmision.EsBasePrueba(cn.Database, ArcaModoEmision.NombreSesion(sessionService))
                        ? "Factura emitida con CAE; se actualizó la última factura y las cuotas."
                        : "Factura grabada en homologación; se actualizó la última factura y las cuotas."),
                    "RECHAZADA" => resultado.Mensaje is { Length: > 0 } m ? m : $"ARCA rechazó el comprobante. {CargaComprobantesRechazos.NadaGrabado}",
                    _ => comoProforma && resultado.Tc != "FP" ? "El cliente tiene una factura electrónica a medio emitir: recuperala antes de hacer una proforma (no se generó nada nuevo)." : resultado.Mensaje is { Length: > 0 } p ? p : CargaComprobantesRechazos.Incierta
                });
            resumen.Tc = resultado.Tc; resumen.Comprobante = resultado.Comprobante; resumen.Cae = resultado.Cae; resumen.Operacion = resultado.Operacion;
            // Mail de la factura (como el VB6 con «enviar mail automático»): un problema de envío no anula la emisión.
            if (enviarMail && resultado.Estado == "GRABADA" && resultado.Comprobante is { Length: > 0 } comprobanteEmitido)
            {
                try
                {
                    var envio = await EnviarMailAsync(cn, ctx, resultado.Tc, comprobanteEmitido, null, null, null, token);
                    resumen.MailEstado = envio.Enviado ? "ENVIADO" : "ERROR"; resumen.MailMensaje = envio.Mensaje;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    resumen.MailEstado = "ERROR";
                    resumen.MailMensaje = ex is CargaComprobantesReglaException ? ex.Message : "No se pudo enviar el mail; la factura quedó emitida. Reenviala desde Ver comprobantes.";
                }
            }
            return resumen;
        }, ct);

    public Task<CargaComprobantesClienteResumen?> GetClienteDirectaAsync(string cliente, CancellationToken ct = default)
        => ExecuteAsync("ClienteDirecta", async (cn, ctx, token) =>
        {
            if (string.IsNullOrWhiteSpace(cliente)) return null;
            var resumen = await cn.QuerySingleOrDefaultAsync<CargaComprobantesClienteResumen>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(cl.CODIGO)) Codigo, ISNULL(cl.RAZON_SOCIAL,'') RazonSocial,
                    ISNULL(LTRIM(RTRIM(td.DESCRIPCION)),'') DocumentoTipo, LTRIM(RTRIM(ISNULL(cl.NUMERO_DOCUMENTO,''))) Documento,
                    ISNULL(LTRIM(RTRIM(iva.DESCRIPCION)),'') CondicionIva,
                    LTRIM(RTRIM(LTRIM(RTRIM(ISNULL(cl.CALLE,''))) + ' ' + LTRIM(RTRIM(ISNULL(cl.NUMERO,''))))) Domicilio,
                    LTRIM(RTRIM(ISNULL(cl.LOCALIDAD,''))) Localidad, ISNULL(LTRIM(RTRIM(pr.DESCRIPCION)),'') Provincia,
                    LTRIM(RTRIM(ISNULL(cl.TELEFONO,''))) Telefono, LTRIM(RTRIM(ISNULL(cl.MAIL,''))) Mail,
                    LTRIM(RTRIM(ISNULL(cl.IdVendedor,''))) VendedorCodigo, ISNULL(LTRIM(RTRIM(vd.Nombre)),'') VendedorNombre,
                    ISNULL(LTRIM(RTRIM(cv.Descripcion)),'') CondicionVenta, ISNULL(cv.CantidadDias,0) DiasVencimiento,
                    ISNULL(cl.Limite_Credito,0) LimiteCredito, CONVERT(bit, ISNULL(cl.BLOQUEO,0) | ISNULL(cl.Dada_De_Baja,0)) Bloqueado
                FROM dbo.VT_CLIENTES cl
                LEFT JOIN dbo.TA_TIPODOCUMENTO td ON LTRIM(RTRIM(td.CODIGO)) = LTRIM(RTRIM(cl.DOCUMENTO_TIPO))
                LEFT JOIN dbo.TA_CONDIVA iva ON LTRIM(RTRIM(iva.CODIGO)) = LTRIM(RTRIM(cl.IVA))
                LEFT JOIN dbo.TA_ESTADOS pr ON LTRIM(RTRIM(pr.CODIGO)) = LTRIM(RTRIM(cl.PROVINCIA))
                LEFT JOIN dbo.V_TA_VENDEDORES vd ON LTRIM(RTRIM(vd.IdVendedor)) = LTRIM(RTRIM(cl.IdVendedor))
                LEFT JOIN dbo.V_TA_Cpra_Vta cv ON LTRIM(RTRIM(cv.IDCond_Cpra_Vta)) = LTRIM(RTRIM(cl.idCond_Cpra_Vta))
                WHERE LTRIM(RTRIM(cl.CODIGO)) = @Cliente;
                """, new { Cliente = cliente.Trim() }, cancellationToken: token));
            if (resumen is null) return null;
            // Lista y clase que le corresponden (las mismas que usa la carga cuando queda "Del cliente").
            var pricing = await new ArticuloPrecioResolverService().ResolveContextAsync(cn, cliente.Trim(), token);
            resumen.ListaCodigo = pricing.IdLista?.Trim() ?? "";
            resumen.Clase = pricing.ClasePrecio;
            resumen.ListaNombre = resumen.ListaCodigo.Length == 0 ? "" : await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT TOP (1) LTRIM(RTRIM(Nombre)) FROM dbo.V_MA_PRECIOSCAB WHERE LTRIM(RTRIM(IdLista))=@Lista AND LTRIM(RTRIM(TipoLista))='V';",
                new { Lista = resumen.ListaCodigo }, cancellationToken: token)) ?? "";
            resumen.ClaseNombre = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
                SELECT TOP (1) COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''), CONVERT(nvarchar(200), VALORAUX), '')
                FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """, new { Clave = $"NOM_PCLASE{resumen.Clase}" }, cancellationToken: token)) ?? "";
            return resumen;
        }, ct);

    public Task<ComprobantesVisorResultado> BuscarComprobantesAsync(ComprobantesVisorFiltro filtro, CancellationToken ct = default)
        => ExecuteAsync("VisorComprobantes", async (cn, ctx, token) =>
        {
            var hasta = (filtro.FechaHasta ?? DateTime.Today).Date;
            var desde = (filtro.FechaDesde ?? hasta.AddDays(-30)).Date;
            if (desde > hasta) (desde, hasta) = (hasta, desde);
            if ((hasta - desde).TotalDays > 3700) throw new CargaComprobantesReglaException("Elegí un período de hasta 10 años.");
            var pageSize = Math.Clamp(filtro.PageSize, 10, 200);
            var page = Math.Max(1, filtro.PageNumber);
            var texto = (filtro.Busqueda ?? string.Empty).Trim();
            static string Literal(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
            // Números escritos como en pantalla (0010-00000519) se buscan sin guiones ni espacios.
            var textoNumero = new string(texto.Where(char.IsLetterOrDigit).ToArray());
            var p = new
            {
                Tc = (filtro.Tc ?? string.Empty).Trim(), Desde = desde, Hasta = hasta,
                ctx.AdminCentral, UnidadUsuario = ctx.UnidadNegocio.Trim(), Unidad = (filtro.UnidadNegocio ?? string.Empty).Trim(),
                filtro.IncluirAnulados, Texto = texto, Like = "%" + Literal(texto) + "%", LikeNumero = "%" + Literal(textoNumero) + "%",
                Desde_ = (page - 1) * pageSize + 1, Hasta_ = page * pageSize
            };
            // Orden por columna (lista cerrada: nunca se arma SQL con texto del usuario).
            var columnaOrden = (filtro.Orden ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "tipo" => "LTRIM(RTRIM(c.TC))", "numero" => "c.IDCOMPROBANTE", "cliente" => "c.NOMBRE", "importe" => "c.IMPORTE",
                "unidad" => "c.UNEGOCIO", "usuario" => "c.Usuario", _ => "c.FECHA"
            };
            var direccion = filtro.Descendente ? "DESC" : "ASC";
            // SQL Server 2008: paginado con ROW_NUMBER y total con COUNT(*) OVER ().
            // Último envío por mail (V_MA_EMAILMOV, lo comparte con el VB6) si la tabla existe en la base.
            var hayMails = await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.V_MA_EMAILMOV',N'U') IS NULL THEN 0 ELSE 1 END;", cancellationToken: token)) == 1;
            var mailSelect = hayMails ? ", m.UltimoMail" : ", CAST(NULL AS nvarchar(400)) UltimoMail";
            var mailApply = hayMails ? """
                OUTER APPLY (SELECT TOP (1) CONVERT(nvarchar(10), FechaHora, 103) + ' ' + CONVERT(nvarchar(5), FechaHora, 108) + ' · ' + ISNULL(Para,'') + ' · ' + ISNULL(Status,'') UltimoMail
                             FROM dbo.V_MA_EMAILMOV WHERE TC = q.Tc AND IdComprobante = q.IdComprobante AND ISNULL(Tipo,'E') = 'E' ORDER BY FechaHora DESC) m
                """ : string.Empty;
            var filas = (await cn.QueryAsync<ComprobantesVisorFilaTotal>(new CommandDefinition($"""
                WITH q AS (
                    SELECT c.ID Id, LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) IdComprobante, ISNULL(c.IDCOMPLEMENTO,0) IdComplemento,
                        c.FECHA Fecha, LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta, ISNULL(c.NOMBRE,'') Cliente, ISNULL(c.IMPORTE,0) Importe,
                        CONVERT(bit,ISNULL(c.ANULADA,0)) Anulada, LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) UnidadNegocio,
                        LTRIM(RTRIM(ISNULL(c.Usuario,''))) Usuario,
                        ROW_NUMBER() OVER (ORDER BY {columnaOrden} {direccion}{(columnaOrden == "c.FECHA" ? "" : $", c.FECHA {direccion}")}, c.ID {direccion}) Fila,
                        COUNT(*) OVER () Total, SUM(CASE WHEN ISNULL(c.ANULADA,0)=0 THEN ISNULL(c.IMPORTE,0) ELSE 0 END) OVER () ImporteTotal
                    FROM dbo.V_MV_CPTE c
                    WHERE c.FECHA >= @Desde AND c.FECHA < DATEADD(day, 1, @Hasta) AND ISNULL(c.IDCOMPLEMENTO,0) = 0
                      AND (@Tc = '' OR LTRIM(RTRIM(c.TC)) = @Tc)
                      AND (@IncluirAnulados = 1 OR ISNULL(c.ANULADA,0) = 0)
                      AND (@AdminCentral = 1 OR LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) IN ('', @UnidadUsuario))
                      AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) = @Unidad)
                      AND (@Texto = '' OR c.NOMBRE LIKE @Like ESCAPE '\' OR LTRIM(RTRIM(c.CUENTA)) LIKE @Like ESCAPE '\'
                           OR c.IDCOMPROBANTE LIKE @LikeNumero ESCAPE '\')
                )
                SELECT q.*, ISNULL(LTRIM(RTRIM(t.DESCRIPCION)),'') TcDescripcion, e.CAE Cae{mailSelect}
                FROM q
                OUTER APPLY (SELECT TOP (1) DESCRIPCION FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO)) = q.Tc ORDER BY Talonario) t
                OUTER APPLY (SELECT TOP (1) NULLIF(LTRIM(RTRIM(CAE)),'') CAE FROM dbo.V_MV_CPTE_ELECTRONICOS
                             WHERE TC = q.Tc AND IdComprobante = q.IdComprobante AND ISNULL(Archivado,0) = 0) e
                {mailApply}
                WHERE q.Fila BETWEEN @Desde_ AND @Hasta_
                ORDER BY q.Fila;
                """, p, commandTimeout: 60, cancellationToken: token))).ToList();
            var tipos = (await cn.QueryAsync<CargaComprobantesOpcionFiltro>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(CODIGO)) Codigo, MIN(LTRIM(RTRIM(ISNULL(DESCRIPCION,'')))) Descripcion
                FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(SISTEMA)) = 'Ventas' GROUP BY LTRIM(RTRIM(CODIGO)) ORDER BY 1;
                """, cancellationToken: token))).ToList();
            return new ComprobantesVisorResultado
            {
                Items = filas.Select(x => (ComprobantesVisorFila)x).ToList(), Total = filas.FirstOrDefault()?.Total ?? 0, ImporteTotal = filas.FirstOrDefault()?.ImporteTotal ?? 0,
                PageNumber = page, PageSize = pageSize, Tipos = tipos
            };
        }, ct);

    public Task<byte[]> GetPdfComprobanteAsync(string tc, string idComprobante, CancellationToken ct = default)
        => ExecuteAsync("PdfComprobante", async (cn, ctx, token) =>
        {
            var unidad = await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("""
                SELECT TOP (1) LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) FROM dbo.V_MV_CPTE WHERE LTRIM(RTRIM(TC))=@Tc AND IDCOMPROBANTE=@Id;
                """, new { Tc = tc.Trim(), Id = idComprobante.Trim() }, cancellationToken: token))
                ?? throw new CargaComprobantesReglaException("No se encontró el comprobante.");
            if (!ctx.AdminCentral && unidad.Length > 0 && unidad != ctx.UnidadNegocio.Trim())
                throw new CargaComprobantesReglaException("El comprobante pertenece a otra unidad de negocio.");
            try { return await documents.GeneratePdfAsync(tc.Trim(), idComprobante.Trim(), unidad.Length == 0 ? null : unidad, token); }
            catch (InvalidOperationException ex) when (ex.GetType().Name == "ComprobanteNoSoportadoException")
            { throw new CargaComprobantesReglaException($"Todavía no hay un formato de impresión para comprobantes {tc.Trim()}."); }
        }, ct);

    private sealed class ComprobantesVisorFilaTotal
    {
        public int Id { get; set; }
        public string Tc { get; set; } = string.Empty;
        public string TcDescripcion { get; set; } = string.Empty;
        public string IdComprobante { get; set; } = string.Empty;
        public int IdComplemento { get; set; }
        public DateTime? Fecha { get; set; }
        public string Cuenta { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public decimal Importe { get; set; }
        public bool Anulada { get; set; }
        public string UnidadNegocio { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string? Cae { get; set; }
        public string? UltimoMail { get; set; }
        public int Total { get; set; }
        public decimal ImporteTotal { get; set; }
        public static implicit operator ComprobantesVisorFila(ComprobantesVisorFilaTotal f) => new()
        {
            Id = f.Id, Tc = f.Tc, TcDescripcion = f.TcDescripcion, IdComprobante = f.IdComprobante, IdComplemento = f.IdComplemento,
            Fecha = f.Fecha, Cuenta = f.Cuenta, Cliente = f.Cliente, Importe = f.Importe, Anulada = f.Anulada,
            UnidadNegocio = f.UnidadNegocio, Usuario = f.Usuario, Cae = f.Cae, UltimoMail = f.UltimoMail
        };
    }

    public Task<byte[]> PrevisualizarPdfFacturacionAutomaticaAsync(string cuenta, DateTime fechaFacturacion, CancellationToken ct = default)
        => ExecuteAsync("PdfVistaPreviaFacturacionAutomatica", async (cn, ctx, token) =>
        {
            var (borrador, talonario, _) = await CargaComprobantesFacturacionAutomatica.PrepararAsync(cn, ctx, cuenta, fechaFacturacion.Date, token);
            var (_, documento) = await emision.PrevisualizarAsync(cn, ctx, 0, talonario, [], token, borrador,
                (c, tx, tc, comprobante, unidad) => documents.RenderEnTransaccionAsync(c, tx, tc, comprobante, unidad, token));
            return await pdf.GenerateAsync(MarcarVistaPrevia(documento.Html), documento.Footer, token);
        }, ct);

    /// <summary>Marca de agua en todas las páginas y número ficticio tapado: la vista previa no es un comprobante.</summary>
    internal static string MarcarVistaPrevia(string html)
    {
        const string marca = """
            <style>
            .alfa-vista-previa{position:fixed;inset:0;display:flex;align-items:center;justify-content:center;pointer-events:none;z-index:99999}
            .alfa-vista-previa span{transform:rotate(-32deg);font:700 54px Arial,sans-serif;letter-spacing:4px;white-space:nowrap;
              color:rgba(190,20,20,.20);border:6px solid rgba(190,20,20,.20);border-radius:12px;padding:10px 36px}
            .alfa-vista-previa-banda{position:fixed;top:0;left:0;right:0;text-align:center;font:700 10px Arial,sans-serif;color:#b00;z-index:99999}
            </style>
            <div class="alfa-vista-previa"><span>VISTA PREVIA · SIN VALIDEZ</span></div>
            <div class="alfa-vista-previa-banda">VISTA PREVIA · NO ES UN COMPROBANTE · NÚMERO Y CAE SE ASIGNAN AL EMITIR</div>
            """;
        var conNumero = html.Replace(CargaComprobantesEmisionService.NumeroVistaPrevia.ToString("00000000"), "XXXXXXXX", StringComparison.Ordinal);
        return conNumero.Contains("</body>", StringComparison.OrdinalIgnoreCase)
            ? conNumero.Replace("</body>", marca + "</body>", StringComparison.OrdinalIgnoreCase)
            : conNumero + marca;
    }

    private static CargaComprobantesFacturacionAutomaticaResultado Resumen(CargaComprobantesPlanGrabacion plan,
        CargaComprobantesFacturacionAutomaticaFila fila, int talonario, string estado, string mensaje)
    {
        var fiscal = CargaComprobantesDesgloseFiscal.Crear(plan.Liquidacion);
        return new()
        {
            Cuenta = fila.Cuenta, Estado = estado, Tc = plan.Tc, Letra = plan.Letra, PuntoVenta = plan.PuntoVenta, Talonario = talonario,
            Total = fiscal.Total, Iva = fiscal.Iva, Concepto = plan.Origen.Observaciones, Mensaje = mensaje,
            Lineas = plan.Liquidacion.Lineas.Select(l => new CargaComprobantesFacturacionAutomaticaLinea(
                plan.Origen.Lineas.Single(x => x.Id == l.LineaId).Descripcion, l.Total)).ToList()
        };
    }

    // Un error de un cliente no corta el lote: se devuelve como resultado ERROR con su motivo.
    private async Task<CargaComprobantesFacturacionAutomaticaResultado> ResultadoFacturacionAutomaticaAsync(string cuenta, string accion,
        Func<SqlConnection, CargaComprobantesContexto, CancellationToken, Task<CargaComprobantesFacturacionAutomaticaResultado>> operacion, CancellationToken ct)
    {
        try { return await ExecuteAsync(accion, operacion, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (CargaComprobantesReglaException ex) { return new() { Cuenta = cuenta, Estado = "ERROR", Mensaje = ex.Message }; }
        catch (AppUserFacingException ex) { return new() { Cuenta = cuenta, Estado = "ERROR", Mensaje = ex.Message }; }
    }

    public Task<CargaComprobantesOrigen> GetOrigenAsync(int id, CancellationToken ct = default)
        => ExecuteAsync("CargarOrigen", (cn, contexto, token) => repository.GetOrigenAsync(cn, contexto, id, token), ct);

    public Task<CargaComprobantesSimulacion> SimularAsync(int origenId, int talonario,
        IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct = default)
        => ExecuteAsync("Simular", async (cn, contexto, token) =>
        {
            if (CargaComprobantesConsolidacion.Identidades(origenId, cantidades).Length > 1 ||
                cantidades.Any(x => x.PrecioUnitarioNeto.HasValue || x.Descuento.HasValue))
            {
                var plan = await new CargaComprobantesPlanificador(repository).CrearAsync(cn, contexto, origenId, talonario, cantidades,
                    await cn.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT CONVERT(date,GETDATE());", cancellationToken: token)), token);
                return new CargaComprobantesSimulacion { Liquidacion = plan.Liquidacion, ImporteReferencia = plan.Liquidacion.Total,
                    LineasSeleccionadas = plan.Liquidacion.Lineas.Count,
                    EfectoStock = plan.DescargaStock ? "Generará salida de stock al emitir." : "Los remitos ya descargaron stock.",
                    Avisos = [$"{plan.Partes.Count} documentos de origen. La simulación no reserva cantidades ni emite comprobantes."] };
            }
            var origen = await repository.GetOrigenAsync(cn, contexto, origenId, token);
            if (!contexto.Talonarios.Any(x => x.Talonario == talonario &&
                (x.UnidadNegocio.Length == 0 || x.UnidadNegocio == origen.UnidadNegocio)))
                throw new CargaComprobantesReglaException("Elegí un talonario de factura compatible con la unidad de negocio del origen.");
            return CargaComprobantesSimulador.Simular(origen, contexto, cantidades);
        }, ct);

    private async Task<T> ExecuteAsync<T>(string action,
        Func<SqlConnection, CargaComprobantesContexto, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessionService, appUserSession, out var sessionId))
            throw new CargaComprobantesReglaException("Ingresá con un usuario autorizado en la base activa.");
        var user = appUserSession.GetCurrentUserName().Trim();
        var system = appUserSession.CurrentUser?.SystemCode ?? string.Empty;
        var connectionString = sessionService.GetConnectionString();
        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);
            if (!await permissions.HasAccessAsync(MenuKey, ct))
            {
                // El servicio compartido devuelve false también para un conjunto vacío.
                // Confirmar ausencia real de restricciones: nunca interpretar un fallo
                // de lectura de permisos como acceso irrestricto.
                var restricted = await Dapper.SqlMapper.ExecuteScalarAsync<int>(cn, new Dapper.CommandDefinition("""
                    SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TA_TAREAS
                        WHERE UPPER(LTRIM(RTRIM(USUARIO)))=UPPER(@Usuario)
                          AND UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema) AND ISNULL(TAREA,'')<>'')
                        OR EXISTS(SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB
                        WHERE UPPER(LTRIM(RTRIM(USUARIO)))=UPPER(@Usuario)
                          AND UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema) AND ISNULL(Clave,'')<>'')
                        THEN 1 ELSE 0 END;
                    """, new { Usuario = user, Sistema = system },
                    commandTimeout: 5, cancellationToken: ct));
                if (restricted != 0)
                    throw new CargaComprobantesReglaException("Tu usuario no tiene acceso a Carga de Comprobantes.");
            }
            EnsureContext(sessionId, user, system);
            var contexto = await repository.GetContextoAsync(cn, user, ct, incluirCatalogos: action == "Contexto");
            contexto.SesionId = sessionId;
            contexto.Sistema = system;
            var result = await operation(cn, contexto, ct);
            EnsureContext(sessionId, user, system);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (CargaComprobantesReglaException) { throw; }
        // Ya trae un mensaje para el usuario y su código (ej. falta Chromium para el PDF): no taparlo con el genérico.
        catch (AppUserFacingException) { EnsureContext(sessionId, user, system); throw; }
        catch (Exception ex)
        {
            EnsureContext(sessionId, user, system);
            const string message = "No se pudo completar la operación de comprobantes. Revisá el resultado y recuperá la operación antes de intentar emitir nuevamente.";
            var code = await events.LogErrorAsync("CargaComprobantes", action, ex, message, ct: ct);
            throw new AppUserFacingException(message, code, ex);
        }
    }

    private void EnsureContext(Guid sessionId, string user, string system)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessionService, appUserSession, out var current) || current != sessionId ||
            !string.Equals(user, appUserSession.GetCurrentUserName().Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(system, appUserSession.CurrentUser?.SystemCode ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            throw new CargaComprobantesReglaException("Cambió la base o el usuario. Volvé a cargar el módulo en la sesión actual.");
    }
}
