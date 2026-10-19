using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace AlfaCore.Services;

/// <summary>Prueba de autorización WSFE en homologación. No consume el origen ni registra una FC comercial.</summary>
public sealed class CargaComprobantesElectronicaEmisor(IArcaConfigService config,
    IWsaaClient wsaa, IWsfev1Client wsfe, IAppEventService events)
{
    public async Task<CargaComprobantesElectronicaPrueba> EmitirAsync(SqlConnection cn,
        CargaComprobantesContexto contexto, CargaComprobantesOrigen origen,
        IReadOnlyList<CargaComprobantesCantidad> cantidades, Guid operacion, CancellationToken ct, DateTime? fecha = null, string? cuitEsperado = null,
        CargaComprobantesPlanGrabacion? plan = null)
    {
        if (operacion == Guid.Empty) throw new CargaComprobantesReglaException("Falta la identidad de la operación de prueba.");
        var emisor = await config.ResolveEmisorAsync(cn, origen.UnidadNegocio, ct)
            ?? throw new CargaComprobantesReglaException("Falta el emisor electrónico.");
        CargaComprobantesElectronicaVerificador.ValidarHomologacion(emisor, contexto.PuntoVentaElectronico);
        if (emisor.CondicionIvaPropiaCodigo.Trim() != "1" || (cuitEsperado is not null && emisor.Cuit != cuitEsperado))
            throw new CargaComprobantesReglaException("El emisor cambió o requiere un circuito fiscal distinto; no se envió la autorización.");
        var previo = await cn.QuerySingleOrDefaultAsync<CargaComprobantesElectronicaPrueba>(new CommandDefinition("""
            SELECT Operacion,OrigenId,Estado,PuntoVenta,TipoComprobante,Numero,Importe,Cae,Vencimiento
            FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA WHERE Operacion=@Operacion;
            """, new { Operacion = operacion }, cancellationToken: ct));
        if (previo is not null)
        {
            if (previo.OrigenId != origen.Id) throw new CargaComprobantesReglaException("La operación pertenece a otro origen.");
            return previo; // Nunca reenviar un CAE por doble clic o respuesta incierta.
        }
        var cliente = await cn.QuerySingleOrDefaultAsync<ClienteFiscal>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(c.IVA)) CondicionCodigo,ISNULL(iva.DESCRIPCION,'') CondicionDescripcion,
                ISNULL(doc.DESCRIPCION,'') DocumentoDescripcion,LTRIM(RTRIM(c.NUMERO_DOCUMENTO)) DocumentoNumero,
                CONVERT(bit,ISNULL(c.Dada_De_Baja,0)) Baja,CONVERT(bit,ISNULL(c.BLOQUEO,0)) Bloqueado
            FROM dbo.VT_CLIENTES c
            LEFT JOIN dbo.TA_CONDIVA iva ON LTRIM(RTRIM(iva.CODIGO))=LTRIM(RTRIM(c.IVA))
            LEFT JOIN dbo.TA_TIPODOCUMENTO doc ON LTRIM(RTRIM(doc.CODIGO))=LTRIM(RTRIM(c.DOCUMENTO_TIPO))
            WHERE LTRIM(RTRIM(c.CODIGO))=@Cuenta;
            """, new { origen.Cuenta }, cancellationToken: ct))
            ?? throw new CargaComprobantesReglaException("El cliente del origen no está disponible.");
        if (cliente.Baja || cliente.Bloqueado) throw new CargaComprobantesReglaException("El cliente está dado de baja o bloqueado.");
        var condicion = ArcaCodigosAfip.ResolverCondicionIvaReceptor(cliente.CondicionDescripcion, cliente.CondicionCodigo);
        var letra = ArcaCodigosAfip.RequiereFacturaA(condicion) ? "A" : "B";
        if (!ArcaCodigosAfip.EsCondicionValidaParaLetra(condicion, letra))
            throw new CargaComprobantesReglaException("La condición fiscal del cliente no está admitida en esta prueba.");
        var (docTipo, docNumero) = ArcaCodigosAfip.ResolverDocumento(cliente.DocumentoDescripcion, cliente.DocumentoNumero);
        if (plan is { EsElectronico: false }) throw new CargaComprobantesReglaException("La proforma no solicita CAE.");
        var liquidacion = plan?.Liquidacion ?? CargaComprobantesCalculador.Calcular(origen, contexto, cantidades);
        if (liquidacion.NetoNoGravado != 0 || liquidacion.ImpuestosInternos != 0)
            throw new CargaComprobantesReglaException("La primera prueba electrónica admite artículos gravados sin impuestos internos.");
        var tipo = plan?.TipoArca ?? (letra == "A" ? 1 : 6);
        var recurso = $"CargaComprobantes.Homo.{emisor.Cuit}.{emisor.PuntoVentaElectronico}.{tipo}";
        var bloqueo = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@Recurso,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0;
            SELECT @r;
            """, new { Recurso = recurso }, cancellationToken: ct));
        if (bloqueo < 0) throw new CargaComprobantesReglaException("Hay otra prueba electrónica en curso para ese punto de venta. Esperá su resultado.");
        try
        {
            // Una respuesta perdida se conserva como INCIERTA; el número no se reutiliza automáticamente.
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA
                WHERE Cuit=@Cuit AND PuntoVenta=@PuntoVenta AND TipoComprobante=@Tipo AND Estado IN ('ENVIANDO','INCIERTA');
                """, new { emisor.Cuit, PuntoVenta = emisor.PuntoVentaElectronico, Tipo = tipo }, cancellationToken: ct)) > 0)
                throw new CargaComprobantesReglaException("Hay una autorización de resultado incierto. Se debe consultar su número en ARCA antes de emitir otra prueba.");
            var ticket = await wsaa.ObtenerTicketAsync(cn, emisor, ct);
            var numero = 1 + await wsfe.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, tipo, emisor.Ambiente, emisor.WsfeUrl, ct);
            var solicitud = CrearSolicitud(emisor, liquidacion, tipo, condicion, docTipo, docNumero, numero, fecha ?? DateTime.Today);
            if (plan?.Asociado is { } asociado)
            {
                // Confirmar el asociado contra ARCA, también al primer envío de la nota.
                var original = await wsfe.ConsultarComprobanteAsync(ticket, emisor.Cuit, asociado.PuntoVenta, asociado.Tipo, asociado.Numero,
                    emisor.Ambiente, emisor.WsfeUrl, ct);
                if (original.Solicitud.DocTipo != docTipo || original.Solicitud.DocNro.TrimStart('0') != docNumero.TrimStart('0') ||
                    original.Solicitud.CbteFch != asociado.Fecha || asociado.Cuit != emisor.Cuit)
                    throw new CargaComprobantesReglaException("La factura asociada consultada en ARCA no coincide con el cliente, fecha o emisor de la nota.");
                solicitud = solicitud with { ComprobantesAsociados = [asociado] };
            }
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.ALFACORE_VENTAS_HOMO_PRUEBA
                (Operacion,OrigenId,Usuario,UnidadNegocio,Ambiente,Cuit,PuntoVenta,TipoComprobante,Numero,Estado,Importe,Solicitud)
                VALUES(@Operacion,@OrigenId,@Usuario,@UnidadNegocio,'HOMOLOGACION',@Cuit,@PuntoVenta,@Tipo,@Numero,'ENVIANDO',@Importe,@Solicitud);
                """, new { Operacion = operacion, OrigenId = origen.Id, contexto.Usuario, UnidadNegocio = origen.UnidadNegocio,
                    emisor.Cuit, PuntoVenta = emisor.PuntoVentaElectronico, Tipo = tipo, Numero = numero, Importe = solicitud.ImpTotal,
                    Solicitud = JsonSerializer.Serialize(solicitud) }, cancellationToken: ct));
            ArcaCaeResultadoDto resultado;
            var errorRegistrado = false;
            try { resultado = await wsfe.SolicitarCaeAsync(ticket, solicitud, emisor.Ambiente, emisor.WsfeUrl, ct); }
            catch (Exception ex)
            {
                await events.LogErrorAsync("CargaComprobantes", "PruebaCae", ex, "No se pudo confirmar la autorización de homologación.", new { Operacion = operacion }, ct: CancellationToken.None);
                errorRegistrado = true;
                resultado = ArcaCaeResultadoDto.Fallo("La respuesta de autorización no pudo confirmarse.", numero, numero);
            }
            var estado = resultado.Aprobado ? "APROBADA" : resultado.ExitoTecnico ? "RECHAZADA" : "INCIERTA";
            if (!resultado.ExitoTecnico && !errorRegistrado)
                await events.LogErrorAsync("CargaComprobantes", "PruebaCae", new InvalidOperationException(resultado.ErrorTecnico ?? "Respuesta de ARCA no confirmada."),
                    "No se pudo confirmar la autorización de homologación.", new { Operacion = operacion }, ct: CancellationToken.None);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.ALFACORE_VENTAS_HOMO_PRUEBA SET Estado=@Estado,Respuesta=@Respuesta,Cae=@Cae,
                    Vencimiento=@Vencimiento,FechaActualizacion=SYSUTCDATETIME() WHERE Operacion=@Operacion;
                """, new { Estado = estado, Respuesta = JsonSerializer.Serialize(resultado), resultado.Cae,
                    Vencimiento = resultado.CaeVto, Operacion = operacion }, cancellationToken: CancellationToken.None));
            await events.LogAuditAsync("CargaComprobantes", "PruebaElectronicaHomologacion", "ALFACORE_VENTAS_HOMO_PRUEBA", operacion.ToString(),
                "Se registró una prueba de autorización electrónica en homologación.", new { Estado = estado, PuntoVenta = emisor.PuntoVentaElectronico, Tipo = tipo, Numero = numero }, CancellationToken.None);
            return new() { Operacion = operacion, OrigenId = origen.Id, Estado = estado, PuntoVenta = emisor.PuntoVentaElectronico,
                TipoComprobante = tipo, Numero = numero, Importe = solicitud.ImpTotal, Cae = resultado.Cae, Vencimiento = resultado.CaeVto };
        }
        finally
        {
            await cn.ExecuteAsync(new CommandDefinition("EXEC sys.sp_releaseapplock @Resource=@Recurso,@LockOwner='Session';",
                new { Recurso = recurso }, cancellationToken: CancellationToken.None));
        }
    }

    public static ArcaCaeSolicitudDto CrearSolicitud(ArcaEmisorConfig emisor, CargaComprobantesLiquidacion liquidacion,
        int tipo, int condicion, int documentoTipo, string documentoNumero, long numero, DateTime fecha)
    {
        decimal Moneda(decimal x) => decimal.Round(x, 2, MidpointRounding.ToEven);
        var ivas = liquidacion.Alicuotas.Select(x => new ArcaIvaAlicuotaDto(ArcaCodigosAfip.ResolverIdAlicuotaIva(x.Alicuota), Moneda(x.Neto), Moneda(x.Iva))).ToList();
        var total = Moneda(liquidacion.Total);
        var diferencia = total - ivas.Sum(x => x.BaseImponible + x.Importe);
        if (ivas.Count == 0 || Math.Abs(diferencia) > .02m || total <= 0)
            throw new CargaComprobantesReglaException("El desglose de IVA requiere revisión antes de solicitar CAE.");
        if (diferencia != 0) ivas[0] = ivas[0] with { Importe = ivas[0].Importe + diferencia };
        return new(emisor.Cuit, emisor.PuntoVentaElectronico, tipo, 1, documentoTipo, documentoNumero,
            fecha.Date, numero, numero, total, 0, ivas.Sum(x => x.BaseImponible), 0, 0, ivas.Sum(x => x.Importe), condicion, "PES", 1, ivas);
    }

    public async Task<CargaComprobantesElectronicaPrueba> RecuperarAsync(SqlConnection cn, CargaComprobantesContexto contexto,
        Guid operacion, CancellationToken ct)
    {
        var row = await cn.QuerySingleOrDefaultAsync<PruebaRecuperacion>(new CommandDefinition("""
            SELECT Operacion,OrigenId,UnidadNegocio,Cuit,PuntoVenta,TipoComprobante,Numero,Estado,Solicitud
            FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA WHERE Operacion=@Operacion;
            """, new { Operacion = operacion }, cancellationToken: ct))
            ?? throw new CargaComprobantesReglaException("No se encontró la operación electrónica.");
        if (!contexto.AdminCentral && row.UnidadNegocio.Trim() != contexto.UnidadNegocio.Trim())
            throw new CargaComprobantesReglaException("La operación pertenece a otra unidad de negocio.");
        var emisor = await config.ResolveEmisorAsync(cn, row.UnidadNegocio, ct)
            ?? throw new CargaComprobantesReglaException("Falta el emisor de la operación.");
        CargaComprobantesElectronicaVerificador.ValidarHomologacion(emisor, contexto.PuntoVentaElectronico);
        if (emisor.Cuit != row.Cuit || emisor.PuntoVentaElectronico != row.PuntoVenta)
            throw new CargaComprobantesReglaException("El emisor cambió respecto de la solicitud guardada.");
        var solicitud = JsonSerializer.Deserialize<ArcaCaeSolicitudDto>(row.Solicitud)
            ?? throw new CargaComprobantesReglaException("Falta la solicitud original para conciliar.");
        var recurso = $"CargaComprobantes.Homo.{row.Cuit}.{row.PuntoVenta}.{row.TipoComprobante}";
        var bloqueo = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@Recurso,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @r;
            """, new { Recurso = recurso }, cancellationToken: ct));
        if (bloqueo < 0) throw new CargaComprobantesReglaException("Hay otra operación en curso en ese punto de venta.");
        try
        {
            var ticket = await wsaa.ObtenerTicketAsync(cn, emisor, ct);
            var encontrado = await wsfe.ConsultarComprobanteAsync(ticket, row.Cuit, row.PuntoVenta, row.TipoComprobante,
                row.Numero, emisor.Ambiente, emisor.WsfeUrl, ct);
            ValidarConciliacion(solicitud, encontrado.Solicitud);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.ALFACORE_VENTAS_HOMO_PRUEBA SET Estado='APROBADA',Cae=@Cae,Vencimiento=@Vencimiento,
                    Respuesta=@Respuesta,FechaActualizacion=SYSUTCDATETIME() WHERE Operacion=@Operacion;
                """, new { Operacion = operacion, encontrado.Cae, encontrado.Vencimiento, Respuesta = JsonSerializer.Serialize(encontrado) }, cancellationToken: ct));
            return new() { Operacion = operacion, OrigenId = row.OrigenId, Estado = "APROBADA", PuntoVenta = row.PuntoVenta,
                TipoComprobante = row.TipoComprobante, Numero = row.Numero, Importe = solicitud.ImpTotal,
                Cae = encontrado.Cae, Vencimiento = encontrado.Vencimiento };
        }
        finally
        {
            await cn.ExecuteAsync(new CommandDefinition("EXEC sys.sp_releaseapplock @Resource=@Recurso,@LockOwner='Session';",
                new { Recurso = recurso }, cancellationToken: CancellationToken.None));
        }
    }

    public static void ValidarConciliacion(ArcaCaeSolicitudDto original, ArcaCaeSolicitudDto consultada)
    {
        static string Documento(string x) => x.TrimStart('0') is { Length: > 0 } s ? s : "0";
        if (original.Cuit != consultada.Cuit || original.PtoVta != consultada.PtoVta || original.CbteTipo != consultada.CbteTipo ||
            original.CbteDesde != consultada.CbteDesde || original.CbteHasta != consultada.CbteHasta ||
            original.CbteFch != consultada.CbteFch || original.DocTipo != consultada.DocTipo || Documento(original.DocNro) != Documento(consultada.DocNro) ||
            original.Concepto != consultada.Concepto || original.ImpTotal != consultada.ImpTotal || original.ImpNeto != consultada.ImpNeto ||
            original.ImpIva != consultada.ImpIva || original.ImpTotConc != consultada.ImpTotConc || original.ImpOpEx != consultada.ImpOpEx ||
            original.ImpTrib != consultada.ImpTrib || original.MonId != consultada.MonId || original.MonCotiz != consultada.MonCotiz ||
            original.CondicionIvaReceptorId != consultada.CondicionIvaReceptorId ||
            !original.Ivas.OrderBy(x => x.Id).SequenceEqual(consultada.Ivas.OrderBy(x => x.Id)) ||
            !original.ComprobantesAsociados.OrderBy(x => x.Tipo).ThenBy(x => x.PuntoVenta).ThenBy(x => x.Numero)
                .SequenceEqual(consultada.ComprobantesAsociados.OrderBy(x => x.Tipo).ThenBy(x => x.PuntoVenta).ThenBy(x => x.Numero)))
            throw new CargaComprobantesReglaException("El comprobante encontrado en ARCA no coincide con la solicitud guardada. No se atribuyó ese CAE ni se emitió otro comprobante.");
    }

    private sealed class PruebaRecuperacion
    {
        public Guid Operacion { get; set; }
        public int OrigenId { get; set; }
        public string UnidadNegocio { get; set; } = "";
        public string Cuit { get; set; } = "";
        public int PuntoVenta { get; set; }
        public int TipoComprobante { get; set; }
        public long Numero { get; set; }
        public string Estado { get; set; } = "";
        public string Solicitud { get; set; } = "";
    }

    private sealed class ClienteFiscal
    {
        public string CondicionCodigo { get; set; } = "";
        public string CondicionDescripcion { get; set; } = "";
        public string DocumentoDescripcion { get; set; } = "";
        public string DocumentoNumero { get; set; } = "";
        public bool Baja { get; set; }
        public bool Bloqueado { get; set; }
    }
}
