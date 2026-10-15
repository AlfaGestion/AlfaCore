using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Escribe todos los efectos locales en la transacción del coordinador; nunca hace commit ni llama a ARCA.</summary>
public sealed class CargaComprobantesGrabador(CargaComprobantesPlanificador planificador,
    CargaComprobantesActualizadorOrigen actualizador)
{
    public async Task<CargaComprobantesGrabacionResultado> GrabarAsync(SqlConnection cn, SqlTransaction tx,
        CargaComprobantesContexto contexto, CargaComprobantesPlanGrabacion plan, Guid operacion,
        long numero, string cae, DateTime vencimientoCae, CancellationToken ct)
    {
        if (tx.Connection != cn) throw new ArgumentException("Se requiere la transacción activa de esta conexión.");
        if (numero is <= 0 or > 99999999) throw new CargaComprobantesReglaException("Número fiscal fuera del formato legacy.");
        if (plan.Directa is null) await cn.ExecuteAsync(new CommandDefinition("""
            SELECT ID FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE ID=@Id;
            SELECT ID FROM dbo.V_MV_CPTEINSUMOS WITH(UPDLOCK,HOLDLOCK) WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante;
            """, new { Id = plan.OrigenId, plan.Origen.Tc, plan.Origen.Comprobante }, tx, cancellationToken: ct));
        var actual = await planificador.CrearAsync(cn, contexto, plan.OrigenId, plan.Talonario, plan.Cantidades, plan.Fecha, ct, tx, plan.Directa);
        if (actual.Huella != (plan.HuellaDatos.Length > 0 ? plan.HuellaDatos : plan.Huella)) throw new CargaComprobantesReglaException("Cambió el origen, cliente o configuración después de preparar la factura. No se grabó ningún movimiento local.");
        var sucursal = plan.PuntoVenta.ToString("0000");
        var nro = numero.ToString("00000000");
        var comprobante = sucursal + nro + plan.Letra;
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC))=@TipoDestino AND IDCOMPROBANTE=@Comprobante;
            """, new { Comprobante = comprobante, TipoDestino = plan.Tc }, tx, cancellationToken: ct)) != 0)
            throw new CargaComprobantesReglaException("Ese número de factura ya existe. No se sobreescribirá el comprobante.");
        var liquidacion = plan.Liquidacion;
        decimal Red(decimal x) => decimal.Round(x, 2, MidpointRounding.ToEven);
        var neto = Red(liquidacion.NetoGravado + liquidacion.NetoNoGravado);
        var total = Red(liquidacion.Total);
        var iva = total - neto;
        var solicitudJson = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT Solicitud FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA WHERE Operacion=@Operacion;",
            new { Operacion = operacion }, tx, cancellationToken: ct));
        var solicitud = solicitudJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<ArcaCaeSolicitudDto>(solicitudJson);
        var p = new DynamicParameters(new
        {
            Id = plan.OrigenId, Comprobante = comprobante, Sucursal = sucursal, Numero = nro, plan.Letra,
            // CUENTA pertenece al plan contable: conserva la excepción de alineación a la izquierda.
            Fecha = plan.Fecha, plan.Vencimiento, Cuenta = plan.Origen.Cuenta.PadRight(15),
            CondicionIva = CodigoPk.Format(plan.CondicionIva, 4), CondicionVenta = CodigoPk.Format(plan.CondicionVenta, 4),
            Motivo = CodigoPk.Format(plan.Motivo, 4), Unidad = CodigoPk.Format(plan.UnidadNegocio, 4),
            Usuario = CodigoPk.Format(plan.Usuario, 50), Caja = plan.Caja.Length == 0 ? null : CodigoPk.Format(plan.Caja, 4),
            Tc = CodigoPk.Format(plan.Tc, 4), TipoDestino = plan.Tc, TcCont = CodigoPk.Format(plan.TcContable, 4),
            Subdiario = CodigoPk.Format(plan.Subdiario, 4), Periodo = plan.Periodo,
            Mes = plan.Fecha.Month, Moneda = CodigoPk.Format("1", 4), Neto = neto, Iva = iva, Total = total,
            NetoGravado = Red(liquidacion.NetoGravado), NetoNoGravado = Red(liquidacion.NetoNoGravado),
            Alicuota = liquidacion.Alicuotas.SingleOrDefault()?.Alicuota ?? 0, Cae = plan.EsElectronico ? cae : null,
            VtoCae = plan.EsElectronico ? (DateTime?)vencimientoCae : null,
            TcOrigen = plan.Asociado is not null ? CodigoPk.Format("FC", 4) : plan.Directa is null ? CodigoPk.Format(plan.Origen.Tc, 4) : null,
            CpteOrigen = plan.AsociadoComprobante ?? (plan.Directa is null ? plan.Origen.Comprobante : null),
            MotivoStock = CodigoPk.Format(plan.MotivoStock, 4), Operacion = operacion.ToString(),
            TipoAfip = plan.EsElectronico ? (int?)plan.TipoArca : null, Punto = plan.PuntoVenta, NumeroFiscal = numero,
            SignoStock = plan.SignoStock == 0 ? -1 : plan.SignoStock, AfectaIva = plan.EsElectronico,
            SignoLibro = plan.SentidoContable == "H" ? -1 : 1,
            ExentoCabecera = plan.Origen.ExentoIvaArticulos || (plan.Tc == "FP" && liquidacion.Iva == 0),
            DocTipo = solicitud?.DocTipo, DocNro = solicitud?.DocNro
        });
        p.Add("ClaseDirecta", plan.ClasePrecio);
        p.Add("ObservacionesDirecta", plan.Origen.Observaciones);
        p.Add("DepositoDirecta", CodigoPk.Format(plan.Origen.Deposito, 4));
        p.Add("ListaDirecta", string.IsNullOrWhiteSpace(plan.Origen.Lista) ? null : CodigoPk.Format(plan.Origen.Lista, 4));
        var fuenteCabecera = plan.Directa is null
            ? "dbo.V_MV_CPTE c JOIN dbo.VT_CLIENTES cl ON LTRIM(RTRIM(cl.CODIGO))=LTRIM(RTRIM(c.CUENTA)) WHERE c.ID=@Id"
            : """
              dbo.VT_CLIENTES cl CROSS APPLY
                (SELECT @ClaseDirecta CLASEPRECIO,@ObservacionesDirecta OBSERVACIONES,cl.IdVendedor,
                  @DepositoDirecta IdDeposito,@ListaDirecta IdLista,0 SucursalCuenta) c
              WHERE LTRIM(RTRIM(cl.CODIGO))=LTRIM(RTRIM(@Cuenta))
              """;
        var id = await cn.ExecuteScalarAsync<int>(new CommandDefinition($"""
            DECLARE @Insertado TABLE(ID int);
            INSERT INTO dbo.V_MV_CPTE
            (TC,IDCOMPROBANTE,IDCOMPLEMENTO,FECHA,FECHAESTFIN,CUENTA,NOMBRE,DOMICILIO,TELEFONO,LOCALIDAD,IDPROVINCIA,CODIGOPOSTAL,
             DOCUMENTOTIPO,DOCUMENTONUMERO,CONDICIONIVA,IDCOND_CPRA_VTA,CLASEPRECIO,OBSERVACIONES,
             TCORIGEN,COMPROBANTEORIGEN,COMPLEMENTOORIGEN,IMPORTE,IMPORTE_S_IVA,IDMOTIVOCPRAVTA,IdVendedor,
             ImporteInsumos,ImporteServicios,ImporteOtrosConceptos,ImporteIva,NetoGravado,NetoNoGravado,IdDeposito,
             IdMotivoStock,IdLista,AlicIva,Moneda,Cotizacion,SucursalCuenta,SUCURSAL,NUMERO,LETRA,UNEGOCIO,Usuario,
             UNEGOCIO_DESTINO,FechaHora_Grabacion,CodCpteAfip,Recepcion,ExentoIVAArticulos)
            OUTPUT inserted.ID INTO @Insertado
            SELECT @Tc,@Comprobante,0,@Fecha,@Vencimiento,@Cuenta,cl.RAZON_SOCIAL,
                LEFT(LTRIM(RTRIM(ISNULL(cl.CALLE,'')))+' '+LTRIM(RTRIM(ISNULL(cl.NUMERO,''))),50),
                cl.TELEFONO,cl.LOCALIDAD,cl.PROVINCIA,cl.CPOSTAL,cl.DOCUMENTO_TIPO,LEFT(cl.NUMERO_DOCUMENTO,13),
                @CondicionIva,@CondicionVenta,c.CLASEPRECIO,c.OBSERVACIONES,
                @TcOrigen,@CpteOrigen,0,@Total,@Neto,@Motivo,c.IdVendedor,@Neto,0,0,@Iva,@NetoGravado,@NetoNoGravado,c.IdDeposito,
                @MotivoStock,c.IdLista,@Alicuota,@Moneda,1,ISNULL(c.SucursalCuenta,0),@Sucursal,@Numero,@Letra,
                @Unidad,@Usuario,@Unidad,GETDATE(),CONVERT(nvarchar(4),@TipoAfip),-999,@ExentoCabecera
            FROM {fuenteCabecera};
            SELECT ID FROM @Insertado;
            """, p, tx, cancellationToken: ct));
        // AuxCoeficiente es un coeficiente en el detalle comercial (VB6 12431), no la cantidad base de stock.
        var secuencia = 0;
        // Misma escritura de detalle/stock: solo difiere la fuente de los datos de entrada.
        var fuenteDetalle = plan.Directa is null ? "dbo.V_MV_CPTEINSUMOS i WHERE i.ID=@Linea" : """
            (SELECT a.ID,a.IDARTICULO,a.DESCRIPCION,a.IDUNIDAD,a.IDUNIDAD IDUNIDADBASE,1 CANTIDADUD,@EquivalenciaReferencia EQUIV_UDBASE,
                COALESCE(@CostoReferencia,a.COSTO) COSTO,
                @PrecioDirecta IMPORTE,@NetoDirecta IMPORTE_S_IVA,@ListaDirecta IdLista,@ClaseDirecta CLASEPRECIO,
                @DescuentoDirecta PorcDto,@DepositoDirecta IdDeposito,@CantidadReferencia CANTIDAD,@BultosReferencia CANT_BL,
                @KilosReferencia CANT_KG,@MetrosCubicosReferencia CANT_M3
             FROM dbo.V_MA_ARTICULOS a WHERE LTRIM(RTRIM(a.IDARTICULO))=@ArticuloDirecta) i
            """;
        foreach (var linea in liquidacion.Lineas)
        {
            var original = plan.Origen.Lineas.Single(x => x.Id == linea.LineaId);
            p.Add("Linea", linea.LineaId); p.Add("Secuencia", ++secuencia); p.Add("Cantidad", linea.Cantidad);
            p.Add("TotalLinea", linea.Total); p.Add("NetoLinea", linea.Neto);
            p.Add("DtoUnitario", decimal.Round(original.PrecioSinIva * original.Descuento / 100, 4, MidpointRounding.ToEven));
            p.Add("ArticuloDirecta", original.Articulo);
            p.Add("PrecioDirecta", plan.Tc == "FP" && linea.Iva == 0 ? original.PrecioSinIva : original.Precio);
            p.Add("NetoDirecta", original.PrecioSinIva); p.Add("DescuentoDirecta", original.Descuento);
            p.Add("ExentoLinea", linea.Exento); p.Add("AlicuotaLinea", linea.Alicuota);
            p.Add("CantidadReferencia", original.CantidadReferencia);
            p.Add("EquivalenciaReferencia", original.EquivalenciaReferencia == 0 ? 1 : original.EquivalenciaReferencia);
            p.Add("CostoReferencia", original.CostoReferencia, System.Data.DbType.Decimal);
            p.Add("BultosReferencia", original.BultosReferencia); p.Add("KilosReferencia", original.KilosReferencia);
            p.Add("MetrosCubicosReferencia", original.MetrosCubicosReferencia);
            p.Add("ReferenciaOrigenId", original.ReferenciaOrigenId == 0 ? (int?)null : original.ReferenciaOrigenId);
            await cn.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.V_MV_CPTEINSUMOS
                (TC,IDCOMPROBANTE,IDCOMPLEMENTO,SECUENCIA,IDARTICULO,DESCRIPCION,IDUNIDAD,IDUNIDADBASE,CANTIDAD,CANTIDADUD,
                 EQUIV_UDBASE,COSTO,IMPORTE,IMPORTE_S_IVA,IMPUESTOS,TOTAL,TotalFinal,EXENTO,IdLista,CLASEPRECIO,
                 TipoLista,PorcDto,ImporteDto,AlicIVA,IDCAJA,IdDeposito,CANTIDAD_ORIGEN,Recepcion,
                 CANTIDADPREPARADA,CANTIDADPREPARADAUD,CANTIDADAFACTURAR,CANTIDADAFACTURARUD,CANT_BL,CANT_KG,CANT_M3)
                SELECT @Tc,@Comprobante,0,@Secuencia,i.IDARTICULO,i.DESCRIPCION,i.IDUNIDAD,i.IDUNIDADBASE,@Cantidad,i.CANTIDADUD,
                    i.EQUIV_UDBASE,i.COSTO,i.IMPORTE,i.IMPORTE_S_IVA,0,@TotalLinea,@TotalLinea,@ExentoLinea,i.IdLista,i.CLASEPRECIO,
                    'V',i.PorcDto,@DtoUnitario,@AlicuotaLinea,@Caja,i.IdDeposito,
                    i.CANTIDAD,-999,0,0,0,0,COALESCE(i.CANT_BL*@Cantidad/NULLIF(i.CANTIDAD,0),0),
                    COALESCE(i.CANT_KG*@Cantidad/NULLIF(i.CANTIDAD,0),0),COALESCE(i.CANT_M3*@Cantidad/NULLIF(i.CANTIDAD,0),0)
                FROM {fuenteDetalle};
                """, p, tx, cancellationToken: ct));
            if (plan.DescargaStock)
                await cn.ExecuteAsync(new CommandDefinition($"""
                    INSERT INTO dbo.V_MV_STOCK
                    (TC,IDCOMPROBANTE,IDCOMPLEMENTO,SECUENCIA,FECHA,IDArticulo,Descripcion,IDUnidad,IdUnidadBase,
                     Cantidad,CantidadUD,EQUIV_UDBASE,Costo,PrecioVenta,IMPORTE_S_IVA,IVARI,IVARNI,IMPUESTOS,
                     IdLista,ClasePrecio,CuentaProveedor,IdDeposito,IdMotivoStock,UNEGOCIO,TC_Origen,
                     IdComprobante_Origen,IdComplemento_Origen,Id_Origen,ArtMoneda,ArtCotizacion,IDCAJA,DtoItem,CANT_BL,CANT_KG,CANT_M3)
                    SELECT @Tc,@Comprobante,0,@Secuencia,@Fecha,i.IDARTICULO,i.DESCRIPCION,i.IDUNIDAD,i.IDUNIDADBASE,
                        @SignoStock*@Cantidad,@SignoStock*@Cantidad,1,i.COSTO,i.IMPORTE,i.IMPORTE_S_IVA,@AlicuotaLinea,0,0,i.IdLista,i.CLASEPRECIO,
                        @Cuenta,i.IdDeposito,@MotivoStock,@Unidad,@TcOrigen,@CpteOrigen,0,{(plan.Directa is null ? "i.ID" : "@ReferenciaOrigenId")},@Moneda,1,@Caja,i.PorcDto,
                        COALESCE(i.CANT_BL*@Cantidad/NULLIF(i.CANTIDAD,0),0),COALESCE(i.CANT_KG*@Cantidad/NULLIF(i.CANTIDAD,0),0),
                        COALESCE(i.CANT_M3*@Cantidad/NULLIF(i.CANTIDAD,0),0)
                    FROM {fuenteDetalle};
                    """, p, tx, cancellationToken: ct));
        }
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.V_MV_CPTE SET BL=(SELECT SUM(CANT_BL) FROM dbo.V_MV_CPTEINSUMOS WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante),
                KG=(SELECT SUM(CANT_KG) FROM dbo.V_MV_CPTEINSUMOS WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante),
                M3=(SELECT SUM(CANT_M3) FROM dbo.V_MV_CPTEINSUMOS WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante) WHERE ID=@FacturaId;
            """, Agregar(p, "FacturaId", id), tx, cancellationToken: ct));
        if (plan.Directa is null) await actualizador.ActualizarAsync(cn, tx, plan.Origen, plan.Cantidades, ct);
        // Numeración de asiento serializada dentro del ejercicio/mes/subdiario.
        var asiento = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT ISNULL(MAX([NUMERO ASIENTO]),0)+1 FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK)
            WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND TIPO_REG=@Subdiario;
            """, p, tx, cancellationToken: ct));
        p.Add("Asiento", asiento);
        var asientoSec = 0;
        var debeCliente = plan.SentidoContable != "H";
        foreach (var renglon in new[] { (plan.CuentaCliente, debeCliente ? "D" : "H", total),
            (plan.CuentaIva, debeCliente ? "H" : "D", iva), (plan.CuentaVentas, debeCliente ? "H" : "D", neto) }.Where(x => x.Item3 != 0))
        {
            p.Add("CuentaAsiento", renglon.Item1.PadRight(15));
            p.Add("Dh", renglon.Item2); p.Add("ImpAsiento", renglon.Item3); p.Add("SecAsiento", ++asientoSec);
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.MV_ASIENTOS
                (CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,FechaSubdiario,DETALLE,TC,SUCURSAL,NUMERO,LETRA,
                 [DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,VENCIMIENTO,TIPO_REG,USUARIO_LOGEADO,UNEGOCIO,
                 IdCajas,IdVendedor,IDMOTIVO,Tc_Origen,Cpte_Origen,CABCUENTA,CABNOMBRE,CABCUIT,CABCONDIVA,
                 LIVA_TIPO,LIVA_ImpNetoGrav,LIVA_ImpNetoNGrav,LIVA_AlicIVA,LIVA_ImpIVA,LIVA_TOTAL,NroCAI,FhVtoCAI,FechaHora_Grabacion)
                SELECT @CuentaAsiento,@SecAsiento,@Mes,@Asiento,@Fecha,@Fecha,LEFT(c.NOMBRE,200),@TcCont,@Sucursal,@Numero,@Letra,
                    @Dh,@ImpAsiento,@Moneda,1,@Periodo,@Total,@Vencimiento,@Subdiario,@Usuario,@Unidad,@Caja,c.IdVendedor,@Motivo,
                    @TcOrigen,@CpteOrigen,CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN c.CUENTA END,
                    CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN c.NOMBRE END,CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN c.DOCUMENTONUMERO END,
                    CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN @CondicionIva END,CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN 'Ventas' END,
                    CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN @SignoLibro*@NetoGravado END,
                    CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN @SignoLibro*@NetoNoGravado END,
                    CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN @Alicuota END,
                    CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN @SignoLibro*@Iva END,
                    CASE WHEN @SecAsiento=1 AND @AfectaIva=1 THEN @SignoLibro*@Total END,@Cae,@VtoCae,GETDATE()
                FROM dbo.V_MV_CPTE c WHERE c.ID=@FacturaId;
                """, Agregar(p, "FacturaId", id), tx, cancellationToken: ct));
        }
        var balance = await cn.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS
            WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND TIPO_REG=@Subdiario AND [NUMERO ASIENTO]=@Asiento;
            """, p, tx, cancellationToken: ct));
        if (balance != 0) throw new CargaComprobantesReglaException("El asiento no balancea; se revierte toda la grabación.");
        await cn.ExecuteAsync(new CommandDefinition("""
            IF @TcOrigen IS NOT NULL
            INSERT INTO dbo.MV_APLICACION
            (CUENTA,TC,SUCURSAL,NUMERO,LETRA,TCO_ORIGEN,SUCURSAL_ORIGEN,NUMERO_ORIGEN,LETRA_ORIGEN,
             IMPORTE,IDCOMPROBANTE,IDCOMPROBANTE_ORIGEN,IDCOMPLEMENTO_ORIGEN,UNEGOCIO)
            VALUES(@Cuenta,@Tc,@Sucursal,@Numero,@Letra,@TcOrigen,LEFT(@CpteOrigen,4),SUBSTRING(@CpteOrigen,5,8),RIGHT(@CpteOrigen,1),
                CASE WHEN @TipoDestino IN ('NC','ND') THEN @Total ELSE @Neto END,@Comprobante,@CpteOrigen,0,@Unidad);
            IF @AfectaIva=1
            INSERT INTO dbo.V_MV_CPTE_ELECTRONICOS
            (IdRequerimiento,CantReg,Presta_Serv,TC,IdComprobante,Tipo_Doc,Nro_Doc,Tipo_Cpte,Punto_Vta,Cpte_Desde,Cpte_Hasta,
             Imp_Total,Imp_NoGrav,Imp_NetoGrav,Imp_ImpuestoLiquidado,Imp_OPExentas,Fecha_Cpte,Resultado,CAE,VtoCAE,Archivado)
            VALUES(@Operacion,1,0,@Tc,@Comprobante,@DocTipo,@DocNro,@TipoAfip,@Punto,@Numero,@Numero,@Total,0,@Neto,@Iva,0,@Fecha,'A',@Cae,@VtoCae,0);
            """, p, tx, cancellationToken: ct));
        // ARCA determina el número; GrabaUltimoCpte legacy conserva el siguiente (número + 1).
        await cn.ExecuteAsync(new CommandDefinition(plan.Letra == "X" ? """
            UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO=CASE WHEN ISNULL(X_ULTIMO_NRO,0)<@NumeroFiscal+1 THEN @NumeroFiscal+1 ELSE X_ULTIMO_NRO END
            WHERE LTRIM(RTRIM(CODIGO))=@TipoDestino AND Talonario=@Talonario;
            """ : plan.Letra == "A" ? """
            UPDATE dbo.V_TA_CPTE SET A_ULTIMO_NRO=CASE WHEN ISNULL(A_ULTIMO_NRO,0)<@NumeroFiscal+1 THEN @NumeroFiscal+1 ELSE A_ULTIMO_NRO END
            WHERE LTRIM(RTRIM(CODIGO))=@TipoDestino AND Talonario=@Talonario;
            """ : """
            UPDATE dbo.V_TA_CPTE SET B_ULTIMO_NRO=CASE WHEN ISNULL(B_ULTIMO_NRO,0)<@NumeroFiscal+1 THEN @NumeroFiscal+1 ELSE B_ULTIMO_NRO END
            WHERE LTRIM(RTRIM(CODIGO))=@TipoDestino AND Talonario=@Talonario;
            """, Agregar(p, "Talonario", plan.Talonario), tx, cancellationToken: ct));
        return new() { Operacion = operacion, OrigenId = plan.OrigenId, Borrador = plan.Directa?.Borrador, UnidadNegocio = plan.UnidadNegocio,
            Tc = plan.Tc, FacturaAsociadaId = plan.Directa?.FacturaAsociadaId,
            Estado = "GRABADA", FacturaId = id, Comprobante = comprobante, NumeroAsiento = asiento,
            Cae = plan.EsElectronico ? cae : null, Vencimiento = plan.EsElectronico ? vencimientoCae : null };
    }

    private static DynamicParameters Agregar(DynamicParameters p, string key, object value) { p.Add(key, value); return p; }
}
