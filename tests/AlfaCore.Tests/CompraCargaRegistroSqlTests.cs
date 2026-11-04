using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraCargaRegistroSqlTests
{
    [CompraCargaRegistroSqlFact]
    public async Task RegistraAYCConLibroYReintentoSinDuplicarYRevierteElEnsayo()
    {
        var cadena=Environment.GetEnvironmentVariable("COMPRA_REGISTRO_TEST_SQL")!;var config=new SqlConnectionStringBuilder(cadena);
        Assert.Equal("ALFANET",config.InitialCatalog,StringComparer.OrdinalIgnoreCase);Assert.Equal("SERVER-ALFAVB6",config.DataSource,StringComparer.OrdinalIgnoreCase);
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> f) where T:class
        {var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=f;return p;}
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var servicio=new CompraCargaService(Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>cadena,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Administrador",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var solicitudes=new List<CompraCargaRegistroSolicitud>();
        foreach(var(letra,proveedor) in new[]{("A","211010002"),("C","211010174")})
        {
            var d=new CompraCargaBorrador{Proveedor=proveedor,Unidad="1",Motivo="99",Letra=letra,Sucursal="9999",Numero=letra=="A"?"90000001":"90000002",
                Fecha=new(2026,8,25),FechaContable=new(2026,8,26),Vencimiento=new(2026,9,9),TotalDocumento=letra=="A"?12.1m:10m,
                Lineas=[new(){Descripcion="Ensayo transaccional de servicio",Neto=10,Tasa=letra=="A"?21:0,Cuenta="21209"}]};
            var vista=await servicio.PrepararAsync(d);Assert.Empty(vista.Errores);
            solicitudes.Add(new(Guid.NewGuid(),sesion.Id,d,vista.Huella));
        }
        var contexto=await servicio.ContextoAsync();var proveedorA=await servicio.ProveedorAsync("211010002");
        await using var cn=new SqlConnection(cadena);await cn.OpenAsync();
        const string foto="SELECT CONVERT(varchar(20),(SELECT COUNT(*) FROM C_MV_CPTE))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM MV_ASIENTOS))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM V_MV_STOCK))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM MV_APLICACION))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM V_MV_CPTEACCIONES))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM IA_Compras_CAB))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM INT_COMPROBANTE_RECIBIDO))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM INT_COMPROBANTE_RECIBIDO_HIST))";
        var antes=await cn.ExecuteScalarAsync<string>(foto);
        using(var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
                var script=File.ReadAllText(Path.Combine(root,"src/AlfaCore/App_Data/updates/2026-11-03-006__compras_registro_operaciones.sql"));
                await cn.ExecuteAsync(script,transaction:tx);
                await cn.ExecuteAsync(File.ReadAllText(Path.Combine(root,"src/AlfaCore/App_Data/updates/2026-11-03-019__compras_recepcion_registrada.sql")),transaction:tx);
                await cn.ExecuteAsync(File.ReadAllText(Path.Combine(root,"src/AlfaCore/App_Data/updates/2026-11-03-015__compras_borradores_anulacion.sql")),transaction:tx);
                var borradorId=Guid.NewGuid();var jsonEdicion="{\"neto\":\"12.\",\"numero\":\"\"}";
                Assert.Equal(1,await servicio.GuardarBorradorEnTransaccionAsync(cn,tx,borradorId,0,"Borrador incompleto",jsonEdicion,default));
                Assert.Equal(jsonEdicion,await cn.ExecuteScalarAsync<string>("SELECT Json FROM ALFACORE_COMPRA_BORRADOR WHERE Id=@borradorId",new{borradorId},tx));
                Assert.Equal(2,await servicio.GuardarBorradorEnTransaccionAsync(cn,tx,borradorId,1,"Borrador corregido",jsonEdicion,default));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.GuardarBorradorEnTransaccionAsync(cn,tx,borradorId,1,"Versión vieja",jsonEdicion,default));
                var registrados=new List<CompraCargaRegistrada>();
                var marca="ENSAYO-COMPRA-"+Guid.NewGuid().ToString("N");
                var documento=await cn.ExecuteScalarAsync<long>("""
                    INSERT INT_COMPROBANTE_RECIBIDO(UsuarioAlta,PcAlta,IdEstado,IdTipoDocumento,Observacion)
                    SELECT 'Administrador','TEST',e.IdEstado,t.IdTipoDocumento,@marca FROM INT_ESTADO e CROSS JOIN INT_TIPO_DOCUMENTO t
                    WHERE e.Codigo='A_PROCESAR' AND t.Codigo='COMPROBANTE_COMPRA'; SELECT CONVERT(bigint,SCOPE_IDENTITY());
                    """,new{marca},tx);
                var lectura=await cn.ExecuteScalarAsync<int>("""
                    INSERT IA_Compras_CAB(Estado,Archivo_RutaOriginal,Archivo_NombreOriginal,IdComprobanteRecibido,JsonResultado)
                    VALUES('PROCESADO','ensayo','ensayo.pdf',@documento,'{}'); SELECT CONVERT(int,SCOPE_IDENTITY());
                    """,new{documento},tx);
                var primera=solicitudes[0];primera.Borrador.DocumentoRecibido=documento;primera.Borrador.LecturaCompraId=lectura;primera.Borrador.LecturaCompraHuella=CompraCargaOrigenLectura.Huella("{}");
                solicitudes[0]=primera with{Huella=CompraCargaService.HuellaRevision(primera.Borrador,proveedorA,contexto,CompraCargaPlanificador.Preparar(primera.Borrador,proveedorA,contexto))};
                async Task<int> Contar(string registro,int? estado=null)
                {
                    using var resultados=await cn.QueryMultipleAsync(InterfacesService.ListadoSql,new{Desde=(DateTime?)null,Hasta=(DateTime?)null,IdEstado=estado,IdTipoDocumento=(int?)null,Registracion=registro,TextoLike="%"+marca+"%",Skip=0,PageSize=1},tx);
                    var filas=(await resultados.ReadAsync<dynamic>()).ToList();var cantidad=await resultados.ReadSingleAsync<int>();Assert.Equal(cantidad,filas.Count);return cantidad;
                }
                Assert.Equal(1,await Contar("PENDIENTES"));Assert.Equal(0,await Contar("REGISTRADAS"));
                // Un rechazo no aprueba la recepción: la factura duplicada existente se controla antes de escribir.
                var duplicada=System.Text.Json.JsonSerializer.Deserialize<CompraCargaBorrador>(System.Text.Json.JsonSerializer.Serialize(primera.Borrador))!;
                duplicada.Sucursal="1";duplicada.Numero="2";
                var revisionDuplicada=CompraCargaPlanificador.Preparar(duplicada,proveedorA,contexto);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,duplicada,CompraCargaService.HuellaRevision(duplicada,proveedorA,contexto,revisionDuplicada))));
                Assert.Equal("PROCESADO",await cn.ExecuteScalarAsync<string>("SELECT Estado FROM IA_Compras_CAB WHERE ID=@lectura",new{lectura},tx));
                Assert.Equal(1,await Contar("PENDIENTES"));
                foreach(var solicitud in solicitudes)
                {
                    var r=await servicio.RegistrarEnTransaccionAsync(cn,tx,solicitud);registrados.Add(r);
                    Assert.Equal(r,await servicio.RegistrarEnTransaccionAsync(cn,tx,solicitud));
                    if(solicitud.Borrador.DocumentoRecibido is not null)
                    {
                        Assert.Equal("REGISTRADO",await cn.ExecuteScalarAsync<string>("SELECT e.Codigo FROM INT_COMPROBANTE_RECIBIDO c JOIN INT_ESTADO e ON e.IdEstado=c.IdEstado WHERE c.IdComprobanteRecibido=@documento",new{documento},tx));
                        Assert.Equal("APROBADO",await cn.ExecuteScalarAsync<string>("SELECT Estado FROM IA_Compras_CAB WHERE ID=@lectura",new{lectura},tx));
                        Assert.Equal(0,await Contar("PENDIENTES"));Assert.Equal(1,await Contar("REGISTRADAS"));
                        var estadoRegistro=await cn.ExecuteScalarAsync<int>("SELECT IdEstado FROM INT_ESTADO WHERE Codigo='REGISTRADO'",transaction:tx);
                        // Simula vínculos guardados antes de incorporar el estado documental: el filtro usa el estado efectivo mostrado.
                        await cn.ExecuteAsync("UPDATE INT_COMPROBANTE_RECIBIDO SET IdEstado=(SELECT IdEstado FROM INT_ESTADO WHERE Codigo='A_PROCESAR') WHERE IdComprobanteRecibido=@documento",new{documento},tx);
                        Assert.Equal(1,await Contar("REGISTRADAS",estadoRegistro));
                        await cn.ExecuteAsync("UPDATE INT_COMPROBANTE_RECIBIDO SET IdEstado=@estadoRegistro WHERE IdComprobanteRecibido=@documento",new{documento,estadoRegistro},tx);

                        var consulta=await servicio.ConsultarDocumentoEnConexionAsync(cn,documento,default,tx);
                        Assert.Equal(r.Comprobante,consulta.Comprobante.Cabecera!.IdComprobante);Assert.Equal(3,consulta.Asientos.Count);
                        Assert.Contains(consulta.Asientos,a=>a.Cuenta.Trim()=="211010002"&&a.Haber==12.1m);
                        Assert.Single(consulta.Comprobante.Observaciones);
                    }

                    Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM C_MV_CPTE WHERE ID=@CompraId;",r,tx));
                    var a=(await cn.QueryAsync<dynamic>("SELECT * FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro",r,tx)).ToList();
                    Assert.Equal(solicitud.Borrador.Letra=="A"?3:2,a.Count);
                    Assert.Single(a.Where(x=>(int)x.SECUENCIA==1&&(string)x.LIVA_TIPO=="COMPRAS"));
                    var fiscal=a.Single(x=>(int)x.SECUENCIA==1);Assert.Equal(r.Total,(decimal)fiscal.LIVA_TOTAL);
                    Assert.Equal(10m,(decimal)fiscal.LIVA_ImpNetoGrav);Assert.Equal(solicitud.Borrador.Letra=="A"?2.1m:0m,(decimal)fiscal.LIVA_ImpIVA);
                    Assert.Equal(0m,a.Sum(x=>(string)((IDictionary<string,object>)x)["DEBE-HABER"]=="D"?(decimal)x.IMPORTE:-(decimal)x.IMPORTE));
                    Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE SUCURSAL='9999' AND NUMERO=@numero AND (DATALENGTH(TC)<>6 OR DATALENGTH(CUENTA)<>DATALENGTH(LTRIM(RTRIM(CUENTA))))",new{numero=r.Comprobante.Substring(4,8)},tx));
                    await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.RegistrarEnTransaccionAsync(cn,tx,solicitud with{Operacion=Guid.NewGuid()}));
                }
                // Crédito con aplicación: usa el saldo oficial y conserva el signo negativo del libro.
                var nota=System.Text.Json.JsonSerializer.Deserialize<CompraCargaBorrador>(System.Text.Json.JsonSerializer.Serialize(solicitudes[0].Borrador))!;
                nota.DocumentoRecibido=null;nota.LecturaCompraId=null;nota.LecturaCompraHuella="";nota.Tipo="NCC";nota.Numero="90000003";nota.NotaFacturaId=registrados[0].CompraId;nota.NotaAplicar=5;
                var vNota=CompraCargaPlanificador.Preparar(nota,proveedorA,contexto);
                var rNota=await servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,nota,CompraCargaService.HuellaRevision(nota,proveedorA,contexto,vNota)));
                Assert.Equal(-12.1m,await cn.ExecuteScalarAsync<decimal>("SELECT LIVA_TOTAL FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro AND SECUENCIA=1",rNota,tx));
                Assert.Equal(5m,await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE FROM MV_APLICACION WHERE TC='NCC' AND IDCOMPROBANTE=@Comprobante",rNota,tx));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.AnularEnTransaccionAsync(cn,tx,rNota.CompraId,"ensayo aplicación protegida",sesion.Id,default));
                // Anulación sin aplicaciones: conserva la cabecera y la auditoría, retira asiento y libro.
                var anulada=registrados[1];await servicio.AnularEnTransaccionAsync(cn,tx,anulada.CompraId,"ensayo transaccional",sesion.Id,default);
                Assert.True(await cn.ExecuteScalarAsync<bool>("SELECT ANULADA FROM C_MV_CPTE WHERE ID=@CompraId",anulada,tx));
                Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro",anulada,tx));
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ALFACORE_COMPRA_ANULACION WHERE CompraId=@CompraId",anulada,tx));
                var vistaAnulada=await servicio.ConsultarCompraEnConexionAsync(cn,anulada.CompraId,default,tx);Assert.True(vistaAnulada.Comprobante.Cabecera!.Anulada);Assert.Empty(vistaAnulada.Asientos);
                // Centros de costos: el maestro se cambia únicamente dentro del ensayo y se revierte al finalizar.
                await cn.ExecuteAsync("UPDATE MA_CUENTAS SET usaCCostos=1 WHERE LTRIM(RTRIM(CODIGO))='21209';",transaction:tx);
                contexto.Cuentas.Single(c=>c.Codigo=="21209").RequiereCentroCosto=true;
                var centro=System.Text.Json.JsonSerializer.Deserialize<CompraCargaBorrador>(System.Text.Json.JsonSerializer.Serialize(nota))!;
                centro.Tipo="FCC";centro.Numero="90000004";centro.NotaFacturaId=null;centro.NotaAplicar=0;centro.CentrosCosto=[new("21209","AC",60),new("21209","AP",40)];
                var vCentro=CompraCargaPlanificador.Preparar(centro,proveedorA,contexto);
                var rCentro=await servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,centro,CompraCargaService.HuellaRevision(centro,proveedorA,contexto,vCentro)));
                Assert.Equal(10m,await cn.ExecuteScalarAsync<decimal>("SELECT SUM(Importe) FROM MV_ASIENTOSCCOSTO WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(Tipo_Reg))=@TipoRegistro",rCentro,tx));
                // Facturación parcial: el remito ya ingresó stock; la OC todavía no. Todo se revierte.
                foreach(var tipoOrigen in new[]{"RMC","OC"})
                {
                    var origenCpte=tipoOrigen=="RMC"?"999990000005R":"999990000007X";
                    var origen=await cn.ExecuteScalarAsync<int>("""
                        INSERT C_MV_CPTE(TC,IDCOMPROBANTE,CUENTA,NOMBRE,FECHA,SUCURSAL,NUMERO,LETRA,UNEGOCIO,IDMOTIVOCPRAVTA,IdDeposito,MONEDA,COTIZACION,IMPORTE,IMPORTE_S_IVA,ImporteInsumos)
                        VALUES(@tc,@id,'211010002','Ensayo mercadería','20260825','9999',SUBSTRING(@id,5,8),RIGHT(@id,1),'   1','  99','   1','   1',1,20,20,20);
                        SELECT CONVERT(int,SCOPE_IDENTITY());
                        """,new{tc=tipoOrigen,id=origenCpte},tx);
                    await cn.ExecuteAsync("""
                        INSERT C_MV_CPTEINSUMOS(TC,IDCOMPROBANTE,CUENTA,IDARTICULO,DESCRIPCION,IDUNIDAD,IDUNIDADBASE,CANTIDAD,CANTIDADUD,EQUIV_UDBASE,COSTO,IMPORTE,IMPORTE_S_IVA,TOTAL,EXENTO,AlicIva,Secuencia)
                        VALUES(@tc,@id,'211010002',RIGHT(SPACE(25)+'1',25),'Artículo de ensayo','   1','   1',2,2,1,10,10,10,20,0,21,1);
                        """,new{tc=tipoOrigen,id=origenCpte},tx);
                    if(tipoOrigen=="RMC")await cn.ExecuteAsync("""
                        INSERT V_MV_STOCK(TC,IDCOMPROBANTE,IDArticulo,Descripcion,CuentaProveedor,Cantidad,CantidadUD,IdDeposito,UNEGOCIO,FECHA)
                        VALUES('RMC',@id,RIGHT(SPACE(25)+'1',25),'Artículo de ensayo','211010002',2,2,'   1','   1','20260825');
                        """,new{id=origenCpte},tx);
                    if(tipoOrigen=="OC")
                    {
                        tx.Save("reglaIr");
                        await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='SI' WHERE CLAVE='IR_Oblig';",transaction:tx);
                        var errorIr=await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.PrepararDesdeOrigenEnConexionAsync(cn,origen,default,tx));
                        Assert.Contains("IR_Oblig",errorIr.Message);tx.Rollback("reglaIr");
                    }
                    var stockAntes=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK",transaction:tx);
                    var mercaderia=await servicio.PrepararDesdeOrigenEnConexionAsync(cn,origen,default,tx);
                    Assert.Equal(20,mercaderia.Lineas.Single().Neto);mercaderia.Sucursal="9999";mercaderia.Numero=tipoOrigen=="RMC"?"90000006":"90000008";
                    mercaderia.Lineas[0].Cantidad=1;mercaderia.Lineas[0].Neto=10;mercaderia.TotalDocumento=12.1m;
                    mercaderia.Fecha=mercaderia.FechaContable=mercaderia.Vencimiento=new(2026,8,25);mercaderia.Lineas[0].Cuenta="21209";mercaderia.CentrosCosto=[new("21209","AC",100)];
                    var vMercaderia=CompraCargaPlanificador.Preparar(mercaderia,proveedorA,contexto);Assert.Empty(vMercaderia.Errores);
                    var rMercaderia=await servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,mercaderia,CompraCargaService.HuellaRevision(mercaderia,proveedorA,contexto,vMercaderia)));
                    Assert.Equal(stockAntes+(tipoOrigen=="OC"?1:0),await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK",transaction:tx));
                    Assert.Equal(10m,await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE_S_IVA FROM C_MV_CPTEINSUMOS WHERE TC='FCC' AND IDCOMPROBANTE=@Comprobante AND CUENTA='211010002'",rMercaderia,tx));
                    Assert.False(await cn.ExecuteScalarAsync<bool>("SELECT Finalizado FROM C_MV_CPTE WHERE ID=@origen",new{origen},tx));
                    var saldo=await servicio.PrepararDesdeOrigenEnConexionAsync(cn,origen,default,tx);
                    Assert.Equal(1,saldo.Lineas.Single().Cantidad);Assert.Equal(10,saldo.Lineas.Single().Neto);Assert.Equal(10,saldo.Lineas.Single().CostoUnitario);
                    Assert.NotEqual(mercaderia.CompraOrigenHuella,saldo.CompraOrigenHuella);
                    saldo.Sucursal="9999";saldo.Numero=tipoOrigen=="RMC"?"90000015":"90000016";
                    saldo.Fecha=saldo.FechaContable=saldo.Vencimiento=new(2026,8,25);saldo.Lineas[0].Cuenta="21209";saldo.CentrosCosto=[new("21209","AC",100)];
                    // Un intento de exceder el saldo debe revertirse completo, incluida la cabecera y el contador.
                    tx.Save("excesoOrigen");saldo.Lineas[0].Cantidad=1.1m;saldo.Lineas[0].Neto=11;saldo.TotalDocumento=13.31m;
                    var exceso=CompraCargaPlanificador.Preparar(saldo,proveedorA,contexto);
                    await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,saldo,CompraCargaService.HuellaRevision(saldo,proveedorA,contexto,exceso))));
                    tx.Rollback("excesoOrigen");
                    // Cierre voluntario del saldo (pregunta del legacy): solo OC; un remito lo rechaza sin dejar rastros.
                    Assert.Equal(tipoOrigen,saldo.CompraOrigenTipo);
                    if(tipoOrigen=="RMC")
                    {
                        tx.Save("cierreRemito");saldo.CerrarSaldoOrigen=true;saldo.Lineas[0].Cantidad=0.5m;saldo.Lineas[0].Neto=5;saldo.TotalDocumento=6.05m;
                        var cierreRemito=CompraCargaPlanificador.Preparar(saldo,proveedorA,contexto);
                        await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,saldo,CompraCargaService.HuellaRevision(saldo,proveedorA,contexto,cierreRemito))));
                        tx.Rollback("cierreRemito");saldo.CerrarSaldoOrigen=false;
                    }
                    else
                    {
                        saldo.CerrarSaldoOrigen=true;saldo.Lineas[0].Cantidad=0.5m;saldo.Lineas[0].Neto=5;saldo.TotalDocumento=6.05m;
                    }
                    if(tipoOrigen=="RMC"){saldo.Lineas[0].Cantidad=1;saldo.Lineas[0].Neto=10;saldo.TotalDocumento=12.1m;}
                    var vSaldo=CompraCargaPlanificador.Preparar(saldo,proveedorA,contexto);Assert.Empty(vSaldo.Errores);
                    await servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,saldo,CompraCargaService.HuellaRevision(saldo,proveedorA,contexto,vSaldo)));
                    Assert.Equal(stockAntes+(tipoOrigen=="OC"?2:0),await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK",transaction:tx));
                    Assert.True(await cn.ExecuteScalarAsync<bool>("SELECT Finalizado FROM C_MV_CPTE WHERE ID=@origen",new{origen},tx));
                    if(tipoOrigen=="OC")Assert.Equal(0.5m,await cn.ExecuteScalarAsync<decimal>("SELECT SUM(Cantidad) FROM V_MV_STOCK WHERE TC='FCC' AND IdComprobante_Origen=@id AND Cantidad<1",new{id=origenCpte},tx));
                    await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.PrepararDesdeOrigenEnConexionAsync(cn,origen,default,tx));
                }
                // Carga directa equivalente a la grilla de ventas: artículo + concepto, con costo de compra.
                var articulo=(await CompraCargaService.LeerArticulosAsync(cn,"","1","1",["1"],default,tx,contexto.TasasFiscales.FirstOrDefault())).Single();
                var directa=new CompraCargaBorrador{Tipo="FCC",Proveedor="211010002",Unidad="1",Motivo="99",Deposito="1",Letra="A",Sucursal="9999",Numero="90000009",
                    Fecha=new(2026,8,25),FechaContable=new(2026,8,25),Vencimiento=new(2026,8,25),TotalDocumento=30.25m,CentrosCosto=[new("21209","AC",100)],
                    Lineas=[new(){Articulo=articulo.Codigo,ArticuloHuella=articulo.Huella,UnidadArticulo=articulo.Unidad,Descripcion=articulo.Descripcion,Cantidad=2,CostoUnitario=10,Neto=20,Tasa=21,Cuenta="21209"},
                        new(){Descripcion="Servicio adicional",Neto=5,Tasa=21,Cuenta="21209"}]};
                async Task<CompraCargaRegistrada> GrabarDirecta(CompraCargaBorrador d)
                {
                    var revision=CompraCargaPlanificador.Preparar(d,proveedorA,contexto);Assert.Empty(revision.Errores);
                    return await servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,d,CompraCargaService.HuellaRevision(d,proveedorA,contexto,revision)));
                }
                var directaGrabada=await GrabarDirecta(directa);
                Assert.Equal(20m,await cn.ExecuteScalarAsync<decimal>("SELECT ImporteInsumos FROM C_MV_CPTE WHERE ID=@CompraId",directaGrabada,tx));
                Assert.Equal(5m,await cn.ExecuteScalarAsync<decimal>("SELECT ImporteOtrosConceptos FROM C_MV_CPTE WHERE ID=@CompraId",directaGrabada,tx));
                Assert.Equal(2d,await cn.ExecuteScalarAsync<double>("SELECT Cantidad FROM V_MV_STOCK WHERE TC='FCC' AND IDCOMPROBANTE=@Comprobante",directaGrabada,tx));
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM C_MV_CPTE_OBSERV WHERE TC='FCC' AND IDCOMPROBANTE=@Comprobante",directaGrabada,tx));
                directa.Numero="90000010";directa.Lineas[0].ArticuloHuella=new string('0',64);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>GrabarDirecta(directa));
                Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM C_MV_CPTE WHERE TC='FCC' AND SUCURSAL='9999' AND NUMERO='90000010'",transaction:tx));
                directa.Lineas[0].ArticuloHuella=articulo.Huella;directa.Lineas.RemoveAt(1);directa.TotalDocumento=24.2m;directa.Tipo="NCC";directa.Numero="90000011";
                var creditoArticulo=await GrabarDirecta(directa);
                Assert.Equal(-2d,await cn.ExecuteScalarAsync<double>("SELECT Cantidad FROM V_MV_STOCK WHERE TC='NCC' AND IDCOMPROBANTE=@Comprobante",creditoArticulo,tx));
                // OC y remito directos: se graban como la factura de artículos pero sin asiento ni libro; la OC no mueve stock y el remito sí.
                await cn.ExecuteAsync("UPDATE VT_PROVEEDORES SET FCC_Sin_OC=1 WHERE LTRIM(RTRIM(CODIGO))='211010002'",transaction:tx);proveedorA.SinOc=true;
                var asientosAntes=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS",transaction:tx);
                var libroAntes=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE LTRIM(RTRIM(TC)) IN ('OC','RMC')",transaction:tx);
                CompraCargaBorrador Comercial(string tipo,string letra,string sucursal,string numero)=>new(){Tipo=tipo,Proveedor="211010002",Unidad="1",Motivo="99",Deposito="1",Letra=letra,Sucursal=sucursal,Numero=numero,
                    Fecha=new(2026,8,25),FechaContable=new(2026,8,25),Vencimiento=new(2026,9,25),TotalDocumento=24.2m,
                    Lineas=[new(){Articulo=articulo.Codigo,ArticuloHuella=articulo.Huella,UnidadArticulo=articulo.Unidad,Descripcion=articulo.Descripcion,Cantidad=2,CostoUnitario=10,Neto=20,Tasa=21}]};
                async Task<CompraCargaRegistrada> GrabarComercial(CompraCargaBorrador d)
                {
                    var revision=CompraCargaPlanificador.Preparar(d,proveedorA,contexto);Assert.Empty(revision.Errores);Assert.Empty(revision.Asiento);
                    return await servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,d,CompraCargaService.HuellaRevision(d,proveedorA,contexto,revision)));
                }
                var stockComercial=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK",transaction:tx);
                var ordenGrabada=await GrabarComercial(Comercial("OC","X","0001","00000001"));
                Assert.Equal("000100000001X",ordenGrabada.Comprobante);
                Assert.True(await cn.ExecuteScalarAsync<bool>("SELECT Aprobado FROM C_MV_CPTE WHERE ID=@CompraId",ordenGrabada,tx));
                Assert.False(await cn.ExecuteScalarAsync<bool>("SELECT Finalizado FROM C_MV_CPTE WHERE ID=@CompraId",ordenGrabada,tx));
                Assert.Equal(24.2m,await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE FROM C_MV_CPTE WHERE ID=@CompraId",ordenGrabada,tx));
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM C_MV_CPTEINSUMOS WHERE TC='OC' AND IDCOMPROBANTE=@Comprobante",ordenGrabada,tx));
                Assert.Equal(stockComercial,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK",transaction:tx));
                Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT A_ULTIMO_NRO FROM V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='OC' AND ISNULL(Talonario,0)=0 AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))='1'",transaction:tx));
                var remitoGrabado=await GrabarComercial(Comercial("RMC","R","9999","90000020"));
                Assert.Equal(2d,await cn.ExecuteScalarAsync<double>("SELECT Cantidad FROM V_MV_STOCK WHERE TC='RMC' AND IDCOMPROBANTE=@Comprobante",remitoGrabado,tx));
                Assert.False(await cn.ExecuteScalarAsync<bool>("SELECT Aprobado FROM C_MV_CPTE WHERE ID=@CompraId",remitoGrabado,tx));
                Assert.Equal(asientosAntes,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS",transaction:tx));
                Assert.Equal(libroAntes,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE LTRIM(RTRIM(TC)) IN ('OC','RMC')",transaction:tx));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>GrabarComercial(Comercial("RMC","R","9999","90000020")));
                // Lo grabado queda disponible para facturarse con el circuito existente (sin repetir el stock del remito).
                foreach(var creado in new[]{ordenGrabada,remitoGrabado})
                    Assert.Equal(2,(await servicio.PrepararDesdeOrigenEnConexionAsync(cn,creado.CompraId,default,tx)).Lineas.Single().Cantidad);
                await cn.ExecuteAsync("UPDATE V_MA_ARTICULOS SET NO_CONTROLA_STOCK=1 WHERE LTRIM(RTRIM(IDARTICULO))='1'",transaction:tx);
                var sinStock=(await CompraCargaService.LeerArticulosAsync(cn,"","1","1",["1"],default,tx,contexto.TasasFiscales.FirstOrDefault())).Single();
                directa.Tipo="FCC";directa.Numero="90000012";directa.Lineas[0].ArticuloHuella=sinStock.Huella;
                var articuloSinStock=await GrabarDirecta(directa);
                Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK WHERE TC='FCC' AND IDCOMPROBANTE=@Comprobante",articuloSinStock,tx));
            }
            finally {if(tx.Connection is not null)tx.Rollback();}
        }
        Assert.Equal(antes,await cn.ExecuteScalarAsync<string>(foto));
    }
}
public sealed class CompraCargaRegistroSqlFactAttribute:FactAttribute
{
    public CompraCargaRegistroSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPRA_REGISTRO_TEST_SQL")))Skip="Ensayo autorizado en ALFANET/SERVER-ALFAVB6 con ROLLBACK obligatorio; no habilitar en producción.";}
}
