using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Cobranza inmediata de una venta de contado (condición de venta con 0 días), como el POS
/// (sp_web_CreaCobPorFactura, sp_web_creaLineaAsiento y sp_web_CreaAplicacionCobranzaFactura) y la cobranza
/// que genera Alfa Gestión: CBCT (CBFP para proformas) en el punto de la factura con letra X, asiento CB
/// (cliente al haber, medios de pago al debe) y aplicación a la factura.
/// A diferencia de los procedimientos del POS se graba dentro de la transacción de la factura, con importes
/// decimales, la fecha y la unidad de negocio del comprobante: si algo falla no queda una factura sin cobrar.
/// </summary>
public static class CargaComprobantesCobranzaContado
{
    /// <summary>Tipos que se cobran en el momento cuando el cliente es de contado.</summary>
    public static bool Cobra(string tc) => tc is "FC" or "FP" or "ND";

    public static string TcCobranza(string tc) => tc == "FP" ? "CBFP" : "CBCT";

    /// <summary>
    /// Valida los medios de pago contra MA_CUENTAS (MEDIODEPAGO) y que cubran exactamente el total.
    /// Un único medio sin importe se completa con el total de la factura.
    /// </summary>
    public static async Task<List<CargaComprobantesPago>> ValidarAsync(SqlConnection cn, SqlTransaction? tx,
        IReadOnlyList<CargaComprobantesPago> pagos, decimal total, CancellationToken ct)
    {
        if (pagos.Count == 0)
            throw new CargaComprobantesReglaException($"El cliente es de contado: cargá los medios de pago por {total:N2}.");
        if (pagos.Count > 20 || pagos.Any(x => string.IsNullOrWhiteSpace(x.CodigoMedioPago) || x.Importe < 0 || decimal.Round(x.Importe, 2) != x.Importe))
            throw new CargaComprobantesReglaException("Revisá los medios de pago: cada uno necesita una cuenta y un importe positivo con hasta dos decimales.");
        var normalizados = pagos.Count == 1 && pagos[0].Importe == 0
            ? [new CargaComprobantesPago { CodigoMedioPago = pagos[0].CodigoMedioPago.Trim(), Importe = total }]
            : pagos.Select(x => new CargaComprobantesPago { CodigoMedioPago = x.CodigoMedioPago.Trim(), Importe = x.Importe }).ToList();
        if (normalizados.Any(x => x.Importe <= 0) || normalizados.Sum(x => x.Importe) != total)
            throw new CargaComprobantesReglaException($"Los medios de pago suman {normalizados.Sum(x => x.Importe):N2} y el comprobante {total:N2}: tienen que coincidir.");
        var cuentas = (await cn.QueryAsync<(string Codigo, string Descripcion, string Medio, string Moneda, bool Habilitada)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)),ISNULL(LTRIM(RTRIM(DESCRIPCION)),''),UPPER(ISNULL(LTRIM(RTRIM(MEDIODEPAGO)),'')),
                ISNULL(LTRIM(RTRIM(MONEDA)),''),CONVERT(bit,CASE WHEN ISNULL(TITULO,0)=0 AND ISNULL(BLOQUEO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 THEN 1 ELSE 0 END)
            FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO)) IN @Codigos;
            """, new { Codigos = normalizados.Select(x => x.CodigoMedioPago).Distinct().ToArray() }, tx, cancellationToken: ct)))
            .ToDictionary(x => x.Codigo, StringComparer.OrdinalIgnoreCase);
        foreach (var pago in normalizados)
        {
            if (!cuentas.TryGetValue(pago.CodigoMedioPago, out var c) || c.Medio.Length == 0 || !c.Habilitada)
                throw new CargaComprobantesReglaException($"La cuenta {pago.CodigoMedioPago} no es un medio de pago habilitado.");
            // Cheques (necesitan número, banco y fecha) y moneda extranjera (cotización) quedan para una etapa siguiente.
            if (c.Medio == "CH")
                throw new CargaComprobantesReglaException($"{c.Descripcion}: los cheques requieren sus datos; por ahora cobralos desde el sistema de escritorio.");
            if (c.Moneda is not ("" or "1"))
                throw new CargaComprobantesReglaException($"{c.Descripcion}: los medios en moneda extranjera todavía no se admiten en esta carga.");
        }
        return normalizados;
    }

    /// <summary>Graba la cobranza de la factura <paramref name="facturaId"/> dentro de la transacción de la factura.</summary>
    public static async Task<string> GrabarAsync(SqlConnection cn, SqlTransaction tx, CargaComprobantesPlanGrabacion plan,
        int facturaId, string comprobanteFactura, decimal total, CancellationToken ct)
    {
        var pagos = plan.Cobranza ?? [];
        if (pagos.Count == 0 || pagos.Sum(x => x.Importe) != total)
            throw new CargaComprobantesReglaException("Los medios de pago no coinciden con el total de la factura. No se grabó nada.");
        var tcCobranza = TcCobranza(plan.Tc);
        var sucursal = plan.PuntoVenta.ToString("0000");
        // FN_OBTIENE_PROXIMO_NUMERO_CPTE con bloqueo: máximo del punto y letra X más uno.
        var ultimo = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT MAX(IDCOMPROBANTE) FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK)
            WHERE TC=@Tc AND SUBSTRING(IDCOMPROBANTE,1,4)=@Sucursal AND SUBSTRING(IDCOMPROBANTE,13,1)='X';
            """, new { Tc = tcCobranza, Sucursal = sucursal }, tx, cancellationToken: ct));
        var numero = (ultimo is { Length: 13 } && long.TryParse(ultimo.AsSpan(4, 8), out var n) ? n : 0) + 1;
        if (numero > 99999999) throw new CargaComprobantesReglaException("La numeración de cobranzas del punto está agotada.");
        var nro = numero.ToString("00000000");
        var idCobranza = sucursal + nro + "X";
        var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))),LTRIM(RTRIM(CONVERT(nvarchar(50),VALOR))) FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE)))=@Clave;
            """, new { Clave = "EQU_" + tcCobranza }, tx, cancellationToken: ct))).FirstOrDefault();
        var tcContable = string.IsNullOrWhiteSpace(cfg.Valor) ? tcCobranza : cfg.Valor.Trim();
        var subdiario = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) LTRIM(RTRIM(Subdiario)) FROM dbo.MA_SUBDIARIOSCOMPROBANTES WHERE LTRIM(RTRIM(TC))=@Tc;",
            new { Tc = tcContable }, tx, cancellationToken: ct));
        var p = new DynamicParameters(new
        {
            FacturaId = facturaId, Tc = tcCobranza, IdCobranza = idCobranza, Sucursal = sucursal, Numero = nro,
            TcCont = tcContable, Subdiario = CodigoPk.Format(string.IsNullOrWhiteSpace(subdiario) ? "CB" : subdiario, 4),
            plan.Periodo, Mes = plan.Fecha.Month, Fecha = plan.Fecha.Date, Total = total,
            Detalle = $"Cob.{plan.Tc} {comprobanteFactura}", Caja = CodigoPk.Format(plan.Caja.Length == 0 ? "1" : plan.Caja, 4),
            Usuario = CodigoPk.Format(plan.Usuario, 50), Unidad = CodigoPk.Format(plan.UnidadNegocio, 4),
            CuentaCliente = plan.CuentaCliente.Trim()
        });
        var cobranzaId = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @Insertado TABLE(ID int);
            INSERT INTO dbo.V_MV_CPTE
            (TC,IDCOMPROBANTE,IDCOMPLEMENTO,FECHA,FECHAESTFIN,FECHAESTINICIO,CUENTA,NOMBRE,DOMICILIO,TELEFONO,LOCALIDAD,IDPROVINCIA,
             CODIGOPOSTAL,DOCUMENTOTIPO,DOCUMENTONUMERO,CONDICIONIVA,IDCOND_CPRA_VTA,IMPORTE,IMPORTE_S_IVA,ImporteIva,MONEDA,Cotizacion,
             IDVENDEDOR,CLASEPRECIO,UNEGOCIO_DESTINO,IdLista,IDMOTIVOCPRAVTA,IDDEPOSITO,AlicIva,NetoGravado,NetoNoGravado,UNEGOCIO,Usuario,
             SUCURSAL,NUMERO,LETRA,FechaHora_Grabacion)
            OUTPUT inserted.ID INTO @Insertado
            SELECT @Tc,@IdCobranza,0,f.FECHA,f.FECHA,f.FECHA,f.CUENTA,f.NOMBRE,f.DOMICILIO,f.TELEFONO,f.LOCALIDAD,f.IDPROVINCIA,
                f.CODIGOPOSTAL,f.DOCUMENTOTIPO,f.DOCUMENTONUMERO,f.CONDICIONIVA,f.IDCOND_CPRA_VTA,f.IMPORTE,f.IMPORTE_S_IVA,f.ImporteIva,f.MONEDA,1,
                f.IdVendedor,f.CLASEPRECIO,f.UNEGOCIO_DESTINO,f.IdLista,f.IDMOTIVOCPRAVTA,f.IdDeposito,f.AlicIva,0,0,f.UNEGOCIO,f.Usuario,
                @Sucursal,@Numero,'X',GETDATE()
            FROM dbo.V_MV_CPTE f WHERE f.ID=@FacturaId;
            SELECT ID FROM @Insertado;
            """, p, tx, cancellationToken: ct));
        var asiento = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT ISNULL(MAX([NUMERO ASIENTO]),0)+1 FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK)
            WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND TIPO_REG=@Subdiario;
            """, p, tx, cancellationToken: ct));
        p.Add("Asiento", asiento);
        // Cliente al haber por el total; cada medio de pago al debe (sp_web_creaLineaAsiento).
        var renglones = new List<(string Cuenta, string Dh, decimal Importe)> { (plan.CuentaCliente.Trim(), "H", total) };
        renglones.AddRange(pagos.Select(x => (x.CodigoMedioPago.Trim(), "D", x.Importe)));
        var secuencia = 0;
        foreach (var (cuenta, dh, importe) in renglones)
        {
            p.Add("CuentaAsiento", cuenta); p.Add("Dh", dh); p.Add("Importe", importe); p.Add("Secuencia", ++secuencia);
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.MV_ASIENTOS
                (CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,FechaSubdiario,DETALLE,TC,SUCURSAL,NUMERO,LETRA,[DEBE-HABER],IMPORTE,
                 MONEDA,COTIZACION,PERIODO,CABIMPORTE,TIPO_REG,CONTABILIZADO,FECHAHORA_GRABACION,USUARIO_LOGEADO,IDCAJAS,UNEGOCIO)
                VALUES(@CuentaAsiento,@Secuencia,@Mes,@Asiento,@Fecha,@Fecha,@Detalle,@TcCont,@Sucursal,@Numero,'X',@Dh,@Importe,
                 '   1',1,@Periodo,@Total,@Subdiario,0,GETDATE(),@Usuario,@Caja,@Unidad);
                """, p, tx, cancellationToken: ct));
        }
        var balance = await cn.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS
            WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND TIPO_REG=@Subdiario AND [NUMERO ASIENTO]=@Asiento;
            """, p, tx, cancellationToken: ct));
        if (balance != 0) throw new CargaComprobantesReglaException("El asiento de la cobranza no balancea; se revierte toda la grabación.");
        // Aplicación de la cobranza a la factura (sp_web_CreaAplicacionCobranzaFactura).
        await cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.MV_APLICACION
            (CUENTA,TC,SUCURSAL,NUMERO,LETRA,IDCOMPROBANTE,TCO_ORIGEN,SUCURSAL_ORIGEN,NUMERO_ORIGEN,LETRA_ORIGEN,
             IDCOMPROBANTE_ORIGEN,IDCOMPLEMENTO_ORIGEN,IMPORTE,Transmision,Recepcion,UNegocio,Secuencia)
            SELECT f.CUENTA,@Tc,@Sucursal,@Numero,'X',@IdCobranza,f.TC,f.SUCURSAL,f.NUMERO,f.LETRA,
                f.IDCOMPROBANTE,0,@Total,0,0,f.UNEGOCIO,0
            FROM dbo.V_MV_CPTE f WHERE f.ID=@FacturaId;
            """, p, tx, cancellationToken: ct));
        return cobranzaId > 0 ? idCobranza : throw new CargaComprobantesReglaException("No se pudo crear la cobranza.");
    }
}
