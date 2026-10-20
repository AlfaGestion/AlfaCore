using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Coordina preparación durable, CAE homologación y commit local recuperable sin una transacción abierta durante HTTP.</summary>
public sealed class CargaComprobantesEmisionService(CargaComprobantesPlanificador planner,
    CargaComprobantesGrabador writer, CargaComprobantesElectronicaEmisor electronica,
    IArcaConfigService config, IAppEventService events)
{
    public async Task<CargaComprobantesPlanGrabacion> ValidarAsync(SqlConnection cn, CargaComprobantesContexto ctx,
        int origenId, int talonario, IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct,
        CargaComprobantesDirecta? directa = null)
    {
        var fecha = await cn.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT CONVERT(date,GETDATE());", cancellationToken: ct));
        var plan = await planner.CrearAsync(cn, ctx, origenId, talonario, cantidades, fecha, ct, directa: directa);
        var emisor = await config.ResolveEmisorAsync(cn, plan.UnidadNegocio, ct)
            ?? throw new CargaComprobantesReglaException("Falta configurar el emisor electrónico.");
        CargaComprobantesElectronicaVerificador.ValidarHomologacion(emisor, plan.EsElectronico ? plan.PuntoVenta : emisor.PuntoVentaElectronico);
        if (emisor.CondicionIvaPropiaCodigo.Trim() != "1")
            throw new CargaComprobantesReglaException("Este circuito inicial requiere un emisor responsable inscripto.");
        if (plan.Asociado is { } asociado && asociado.Cuit != emisor.Cuit)
            throw new CargaComprobantesReglaException("El emisor de la nota no coincide con el emisor de la factura asociada.");
        // Ensayo de efectos reales, siempre revertido ANTES de solicitar CAE.
        // Detecta cierres, cuentas, stock y rechazos de triggers sin consumir el documento.
        using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try { await writer.GrabarAsync(cn, tx, ctx, plan, Guid.NewGuid(), 99999998, "00000000000000", fecha, ct); }
        finally { if (tx.Connection is not null) tx.Rollback(); }
        plan.HuellaDatos = plan.Huella;
        plan.CuitEmisor = emisor.Cuit;
        plan.Huella = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{plan.HuellaDatos}|{emisor.Cuit}|{emisor.PuntoVentaElectronico}|{emisor.CondicionIvaPropiaCodigo}|{emisor.Ambiente}|{emisor.WsaaUrl}|{emisor.WsfeUrl}")));
        return plan;
    }

    public async Task<CargaComprobantesGrabacionResultado> EmitirAsync(SqlConnection cn, CargaComprobantesContexto ctx,
        int origenId, int talonario, IReadOnlyList<CargaComprobantesCantidad> cantidades, Guid operacion,
        string huellaEsperada, Action validarSesion, CancellationToken ct, CargaComprobantesDirecta? directa = null)
    {
        if (operacion == Guid.Empty) throw new CargaComprobantesReglaException("Falta la identidad de la operación.");
        var recurso = directa?.FacturaAsociadaId is { } idAsociado ? $"CargaComprobantes.Nota.{idAsociado}"
            : directa is null ? $"CargaComprobantes.Origen.{origenId}" : $"CargaComprobantes.Directa.{directa.Borrador}";
        var origenes = directa is null ? CargaComprobantesConsolidacion.Identidades(origenId, cantidades) : [];
        await using var bloqueos = await CargaComprobantesBloqueo.TomarAsync(cn,
            directa is null ? origenes.Select(x => $"CargaComprobantes.Origen.{x}") : [recurso], ct);
        var anterior = await GetAsync(cn, ctx, operacion, ct);
        if (anterior is not null)
        {
            if (anterior.OrigenId != origenId || anterior.Borrador != directa?.Borrador)
                throw new CargaComprobantesReglaException("La operación corresponde a otro origen o borrador.");
            return anterior; // Recuperación explícita: nunca modificar ni reenviar una operación ya creada.
        }
        if (directa is not null)
        {
            var previa = await cn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT Operacion FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE Borrador=@Borrador;",
                new { directa.Borrador }, cancellationToken: ct));
            if (previa.HasValue) return (await GetAsync(cn, ctx, previa.Value, ct))!;
        }
        if (directa is null)
        {
            // OPENJSON requiere compatibilidad SQL 130. Las bases legacy conservan niveles inferiores.
            // El journal guarda pocos intentos activos; leer solo esos planes sin cambiar la base.
            var pendientes = await cn.QueryAsync<string>(new CommandDefinition(
                "SELECT PlanJson FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE Estado NOT IN ('GRABADA','RECHAZADA');",
                cancellationToken:ct));
            foreach(var jsonPendiente in pendientes)
            {
                var pendiente=JsonSerializer.Deserialize<CargaComprobantesPlanGrabacion>(jsonPendiente)
                    ?? throw new CargaComprobantesReglaException("Hay una operación cuyo plan necesita revisión antes de emitir.");
                if(pendiente.Directa is null && pendiente.Partes.Any(x=>origenes.Contains(x.OrigenId)))
                    throw new CargaComprobantesReglaException("Un origen tiene una operación pendiente. Recuperala antes de generar otra factura.");
            }
        }
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA WHERE Operacion=@Operacion;",
            new { Operacion = operacion }, cancellationToken: ct)) != 0)
            throw new CargaComprobantesReglaException("La operación es una prueba fiscal anterior sin plan comercial; no puede convertirse en una factura.");
        var plan = await ValidarAsync(cn, ctx, origenId, talonario, cantidades, ct, directa);
        if (plan.Huella != huellaEsperada)
            throw new CargaComprobantesReglaException("Cambió el documento, la fecha, el cliente o la configuración desde la vista validada. Volvé a simular y validar antes de confirmar; no se pidió CAE.");
        validarSesion();
        await cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.ALFACORE_VENTAS_CARGA_OPERACION(Operacion,OrigenId,UnidadNegocio,Usuario,Ambiente,Estado,PlanJson,Borrador,Tc,FacturaAsociadaId)
            VALUES(@Operacion,@OrigenId,@UnidadNegocio,@Usuario,'HOMOLOGACION','PREPARADA',@Plan,@Borrador,@Tc,@FacturaAsociadaId);
            """, new { Operacion = operacion, OrigenId = origenId, plan.UnidadNegocio, plan.Usuario,
                Plan = JsonSerializer.Serialize(plan), Borrador = directa?.Borrador, plan.Tc, FacturaAsociadaId = directa?.FacturaAsociadaId }, cancellationToken: ct));
        try
        {
            if (!plan.EsElectronico) return await CompletarProformaAsync(cn, ctx, plan, operacion, validarSesion, ct);
            var cae = await electronica.EmitirAsync(cn, ctx, plan.Origen, plan.Cantidades, operacion, ct, plan.Fecha, plan.CuitEmisor, plan);
            return await CompletarAsync(cn, ctx, plan, cae, validarSesion, ct);
        }
        catch (Exception ex)
        {
            await MarcarErrorAsync(cn, operacion, ex, CancellationToken.None);
            throw;
        }
    }

    public async Task<CargaComprobantesGrabacionResultado> RecuperarAsync(SqlConnection cn, CargaComprobantesContexto ctx,
        Guid operacion, Action validarSesion, CancellationToken ct)
    {
        var actual = await GetAsync(cn, ctx, operacion, ct) ?? throw new CargaComprobantesReglaException("No se encontró esa operación.");
        if (actual.Estado is "GRABADA" or "RECHAZADA") return actual;
        var recurso = actual.FacturaAsociadaId is { } idAsociado ? $"CargaComprobantes.Nota.{idAsociado}"
            : actual.Borrador is { } borrador ? $"CargaComprobantes.Directa.{borrador}" : $"CargaComprobantes.Origen.{actual.OrigenId}";
        var json = await cn.QuerySingleAsync<string>(new CommandDefinition("SELECT PlanJson FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE Operacion=@Operacion;",
            new { Operacion = operacion }, cancellationToken: ct));
        var plan = JsonSerializer.Deserialize<CargaComprobantesPlanGrabacion>(json)
            ?? throw new CargaComprobantesReglaException("No se pudo recuperar el plan de grabación.");
        await using var bloqueos = await CargaComprobantesBloqueo.TomarAsync(cn,
            plan.Directa is null ? plan.Partes.Select(x => $"CargaComprobantes.Origen.{x.OrigenId}") : [recurso], ct);
        try
        {
            actual = (await GetAsync(cn, ctx, operacion, ct))!;
            if (actual.Estado is "GRABADA" or "RECHAZADA") return actual;
            if (!plan.EsElectronico) return await CompletarProformaAsync(cn, ctx, plan, operacion, validarSesion, ct);
            var prueba = await cn.QuerySingleOrDefaultAsync<CargaComprobantesElectronicaPrueba>(new CommandDefinition("""
                SELECT Operacion,OrigenId,Estado,PuntoVenta,TipoComprobante,Numero,Importe,Cae,Vencimiento
                FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA WHERE Operacion=@Operacion;
                """, new { Operacion = operacion }, cancellationToken: ct));
            if (prueba is null)
            {
                // El journal comercial se escribió antes del de envío: no hubo una solicitud de CAE.
                await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.ALFACORE_VENTAS_CARGA_OPERACION SET Estado='RECHAZADA',Mensaje=N'No se inició el envío a ARCA.' WHERE Operacion=@Operacion;",
                    new { Operacion = operacion }, cancellationToken: ct));
                return (await GetAsync(cn, ctx, operacion, ct))!;
            }
            if (prueba.Estado is "ENVIANDO" or "INCIERTA") prueba = await electronica.RecuperarAsync(cn, ctx, operacion, ct);
            return await CompletarAsync(cn, ctx, plan, prueba, validarSesion, ct);
        }
        catch (Exception ex) { await MarcarErrorAsync(cn, operacion, ex, CancellationToken.None); throw; }
    }

    private async Task<CargaComprobantesGrabacionResultado> CompletarProformaAsync(SqlConnection cn, CargaComprobantesContexto ctx,
        CargaComprobantesPlanGrabacion plan, Guid operacion, Action validarSesion, CancellationToken ct)
    {
        validarSesion();
        var emisor = await config.ResolveEmisorAsync(cn, plan.UnidadNegocio, ct)
            ?? throw new CargaComprobantesReglaException("Falta el emisor de la operación.");
        CargaComprobantesElectronicaVerificador.ValidarHomologacion(emisor, emisor.PuntoVentaElectronico);
        if (emisor.Cuit != plan.CuitEmisor) throw new CargaComprobantesReglaException("Cambió el emisor desde la validación.");
        CargaComprobantesGrabacionResultado resultado;
        using (var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                // El contador legacy guarda el siguiente número. La cabecera se consulta también
                // para no reutilizar numeraciones que hayan sido consumidas por otra terminal.
                var siguiente = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                    SELECT ISNULL(X_ULTIMO_NRO,1) FROM dbo.V_TA_CPTE WITH(UPDLOCK,HOLDLOCK)
                    WHERE LTRIM(RTRIM(CODIGO))='FP' AND Talonario=@Talonario;
                    """, new { plan.Talonario }, tx, cancellationToken: ct));
                var maximo = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                    SELECT ISNULL(MAX(CONVERT(bigint,CASE WHEN NUMERO NOT LIKE '%[^0-9]%' AND NUMERO<>'' THEN NUMERO ELSE '0' END)),0)
                    FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC))='FP'
                        AND SUCURSAL=@Sucursal AND LETRA='X';
                    """, new { Sucursal = plan.PuntoVenta.ToString("0000") }, tx, cancellationToken: ct));
                resultado = await writer.GrabarAsync(cn, tx, ctx, plan, operacion, Math.Max(Math.Max(siguiente, 1), maximo + 1), "", plan.Fecha, ct);
                await GuardarResultadoAsync(cn, tx, resultado, ct);
                validarSesion(); tx.Commit();
            }
            catch { if (tx.Connection is not null) tx.Rollback(); throw; }
        }
        await events.LogAuditAsync("CargaComprobantes", "ProformaGrabada", "V_MV_CPTE", resultado.FacturaId!.Value.ToString(),
            "Proforma grabada con asiento y sin libro de IVA ni CAE.", new { resultado.Operacion, resultado.Comprobante, resultado.NumeroAsiento }, CancellationToken.None);
        return resultado;
    }

    private static Task GuardarResultadoAsync(SqlConnection cn, SqlTransaction tx, CargaComprobantesGrabacionResultado resultado, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ALFACORE_VENTAS_CARGA_OPERACION SET Estado='GRABADA',FacturaId=@FacturaId,Comprobante=@Comprobante,
                NumeroAsiento=@NumeroAsiento,Mensaje=NULL,FechaActualizacion=SYSUTCDATETIME() WHERE Operacion=@Operacion;
            """, resultado, tx, cancellationToken: ct));

    private async Task<CargaComprobantesGrabacionResultado> CompletarAsync(SqlConnection cn, CargaComprobantesContexto ctx,
        CargaComprobantesPlanGrabacion plan, CargaComprobantesElectronicaPrueba cae, Action validarSesion, CancellationToken ct)
    {
        var estado = cae.Estado == "APROBADA" ? "PENDIENTE_GRABACION" : cae.Estado;
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ALFACORE_VENTAS_CARGA_OPERACION SET Estado=@Estado,Cae=@Cae,Vencimiento=@Vencimiento,
                FechaActualizacion=SYSUTCDATETIME() WHERE Operacion=@Operacion;
            """, new { Estado = estado, cae.Cae, cae.Vencimiento, cae.Operacion }, cancellationToken: CancellationToken.None));
        if (cae.Estado != "APROBADA") return (await GetAsync(cn, ctx, cae.Operacion, ct))!;
        if (cae.PuntoVenta != plan.PuntoVenta || cae.TipoComprobante != plan.TipoArca ||
            cae.Importe != decimal.Round(plan.Liquidacion.Total, 2) || string.IsNullOrWhiteSpace(cae.Cae) || cae.Vencimiento is null)
            throw new CargaComprobantesReglaException("La autorización fiscal no coincide con el plan; quedó pendiente de conciliación.");
        var emisorActual = await config.ResolveEmisorAsync(cn, plan.UnidadNegocio, ct)
            ?? throw new CargaComprobantesReglaException("Falta el emisor de la operación aprobada.");
        CargaComprobantesElectronicaVerificador.ValidarHomologacion(emisorActual, plan.PuntoVenta);
        if (emisorActual.Cuit != plan.CuitEmisor)
            throw new CargaComprobantesReglaException("Cambió el emisor después de autorizar; la factura quedó pendiente de conciliación.");
        validarSesion();
        CargaComprobantesGrabacionResultado resultado;
        using (var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                resultado = await writer.GrabarAsync(cn, tx, ctx, plan, cae.Operacion, cae.Numero, cae.Cae, cae.Vencimiento.Value, ct);
                await GuardarResultadoAsync(cn, tx, resultado, ct);
                validarSesion();
                tx.Commit();
            }
            catch { if (tx.Connection is not null) tx.Rollback(); throw; }
        }
        await events.LogAuditAsync("CargaComprobantes", "FacturaHomologacionGrabada", "V_MV_CPTE", resultado.FacturaId!.Value.ToString(),
            "Factura de homologación grabada con efectos comerciales.", new { resultado.Operacion, resultado.Comprobante, resultado.NumeroAsiento }, CancellationToken.None);
        return resultado;
    }

    public async Task<CargaComprobantesGrabacionResultado?> GetAsync(SqlConnection cn, CargaComprobantesContexto ctx, Guid operacion, CancellationToken ct)
    {
        var row = await cn.QuerySingleOrDefaultAsync<CargaComprobantesGrabacionResultado>(new CommandDefinition("""
            SELECT Operacion,OrigenId,Borrador,Tc,FacturaAsociadaId,UnidadNegocio,Estado,FacturaId,Comprobante,NumeroAsiento,Cae,Vencimiento,ISNULL(Mensaje,'') Mensaje
            FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE Operacion=@Operacion;
            """, new { Operacion = operacion }, cancellationToken: ct));
        if (row is not null && !ctx.AdminCentral && row.UnidadNegocio.Trim() != ctx.UnidadNegocio.Trim())
            throw new CargaComprobantesReglaException("La operación pertenece a otra unidad de negocio.");
        return row;
    }

    public async Task<CargaComprobantesGrabacionResultado?> ConsultarDirectaAsync(SqlConnection cn, CargaComprobantesContexto ctx,
        Guid borrador, Guid operacion, CancellationToken ct)
    {
        var encontrada = await cn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT Operacion FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE Borrador=@Borrador;",
            new { Borrador = borrador }, cancellationToken: ct));
        var resultado = await GetAsync(cn, ctx, encontrada ?? operacion, ct);
        if (resultado is not null)
        {
            if (resultado.Borrador != borrador) throw new CargaComprobantesReglaException("La operación pertenece a otro borrador.");
            return resultado;
        }
        // Ausencia verificada antes de desbloquear una carga cuyo envío falló en preparación.
        // Si hay rastro fiscal, nunca interpretar ausencia de journal comercial como ausencia de CAE.
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA WHERE Operacion=@Operacion;",
            new { Operacion = operacion }, cancellationToken: ct)) != 0)
            throw new CargaComprobantesReglaException("Existe una operación fiscal sin plan comercial; requiere revisión antes de emitir otra vez.");
        return null;
    }

    private async Task MarcarErrorAsync(SqlConnection cn, Guid operacion, Exception error, CancellationToken ct)
    {
        var code = await events.LogErrorAsync("CargaComprobantes", "Grabacion", error,
            "La operación quedó guardada para recuperación; no reenviar el CAE.", new { Operacion = operacion }, ct: ct);
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ALFACORE_VENTAS_CARGA_OPERACION SET Mensaje=@Mensaje,FechaActualizacion=SYSUTCDATETIME()
            WHERE Operacion=@Operacion AND Estado<>'GRABADA';
            """, new { Operacion = operacion, Mensaje = $"No se completó la operación. Código: {code}. Usá Recuperar operación; no emitas otra factura." }, cancellationToken: ct));
    }
}
