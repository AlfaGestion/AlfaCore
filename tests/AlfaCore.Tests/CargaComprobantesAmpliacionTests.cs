using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesAmpliacionTests
{
    private static ArcaEmisorConfig Emisor()=>new("30712345671","Prueba",ArcaAmbiente.Homologacion,1007,"","","1","","");
    [Fact]
    public void ConsolidaSinMezclarRenglonesNiModificarLosOriginales()
    {
        CargaComprobantesOrigen Doc(int id) => new() { Id=id,Tc="NP",Cuenta="1",UnidadNegocio="4",Deposito="1",Moneda="1",
            Lineas=[new() { Id=id*10,Secuencia=1,Articulo="PAPEL",Pendiente=2 }] };
        var a=Doc(1); var b=Doc(2);
        var union=CargaComprobantesConsolidacion.Unir([a,b]);
        Assert.Equal(2,union.Lineas.Count); Assert.Equal([1,2],union.Lineas.Select(x=>x.Secuencia));
        Assert.Equal(1,b.Lineas[0].Secuencia);
        b.Deposito="2";
        Assert.Throws<CargaComprobantesReglaException>(()=>CargaComprobantesConsolidacion.Unir([a,b]));
        b.Deposito="1";b.Cuenta="OTRO";
        Assert.Throws<CargaComprobantesReglaException>(()=>CargaComprobantesConsolidacion.Unir([a,b]));
        Assert.Throws<CargaComprobantesReglaException>(()=>CargaComprobantesConsolidacion.Unir([a,a]));
    }

    [Fact]
    public void SeleccionExigePrincipalYConservaContratoDeCantidadAnterior()
    {
        var q=new CargaComprobantesCantidad { LineaId=12,Cantidad=2 };
        Assert.Equal("{\"LineaId\":12,\"Cantidad\":2}",JsonSerializer.Serialize(q));
        Assert.Equal([1],CargaComprobantesConsolidacion.Identidades(1,[q]));
        q.OrigenId=2;
        Assert.Throws<CargaComprobantesReglaException>(()=>CargaComprobantesConsolidacion.Identidades(1,[q]));
    }

    [Fact]
    public void ConsolidadoClasificaExentosAunqueElPrimerOrigenSeaSoloGravado()
    {
        var partes=new List<CargaComprobantesPlanGrabacion>();
        foreach(var id in new[]{1,2})
            partes.Add(new(){OrigenId=id,Origen=new(){Id=id,Tc="NP",Moneda="1",DatosCalculoDisponibles=true,
                Lineas=[new(){Id=id,Secuencia=1,Articulo="PAPEL",CantidadOriginal=1,Pendiente=1,RenglonesArticulo=1,
                    PrecioSinIva=100,Precio=id==1?121:100,AlicuotaIva=21,Exento=id==2}]}});
        var cantidades=new List<CargaComprobantesCantidad>{new(){OrigenId=1,LineaId=1,Cantidad=1},new(){OrigenId=2,LineaId=2,Cantidad=1}};
        var p=CargaComprobantesConsolidacion.UnirPlanes(partes,new(){PreciosFinales=true,AlicuotaGeneral=21,Decimales=2,ArtExentoANoGrav=true},cantidades);
        Assert.True(p.ArtExentoANoGrav);Assert.Equal(100m,p.Liquidacion.NetoNoGravado);Assert.Equal(221m,p.Liquidacion.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public void DesgloseFiscalConciliaTresTasasYExentos(decimal exento)
    {
        var liquidacion=new CargaComprobantesLiquidacion { NetoGravado=300,NetoNoGravado=exento,Iva=58.5m,Total=358.5m+exento,
            Alicuotas=[new(){ Alicuota=21,Neto=100,Iva=21 },new(){ Alicuota=10.5m,Neto=100,Iva=10.5m },new(){ Alicuota=27,Neto=100,Iva=27 }] };
        var solicitud=CargaComprobantesElectronicaEmisor.CrearSolicitud(Emisor(),
            liquidacion,1,1,80,"30712345671",1,DateTime.Today);
        Assert.Equal(3,solicitud.Ivas.Count);Assert.Equal(exento,solicitud.ImpOpEx);
        Assert.Equal(solicitud.ImpTotal,solicitud.ImpNeto+solicitud.ImpIva+solicitud.ImpOpEx);
    }

    [Fact]
    public void ExentoPuroNoInventaAlicuotaYDescuadreNoSeOculta()
    {
        var l=new CargaComprobantesLiquidacion { NetoNoGravado=100,Total=100 };
        var s=CargaComprobantesElectronicaEmisor.CrearSolicitud(Emisor(),l,1,1,80,"30712345671",1,DateTime.Today);
        Assert.Empty(s.Ivas);Assert.Equal(100,s.ImpOpEx);Assert.Equal(0,s.ImpIva);
        l.Total=100.01m;
        Assert.Throws<CargaComprobantesReglaException>(()=>CargaComprobantesDesgloseFiscal.Crear(l));
    }

    [Fact]
    public void EquivalenciasDeBultosUsanDireccionOriginalYRechazanDuplicados()
    {
        List<CargaComprobantesUnidades.Equivalencia> equivalencias=[new("PAPEL","BL","UD",5)];
        Assert.Equal(10m,CargaComprobantesUnidades.Convertir("PAPEL","BL","UD",2,equivalencias));
        Assert.Equal(2m,CargaComprobantesUnidades.Convertir("PAPEL","UD","BL",10,equivalencias));
        Assert.Null(CargaComprobantesUnidades.Convertir("PAPEL","UD","KG",10,equivalencias));
        equivalencias.Add(equivalencias[0]);
        Assert.Throws<CargaComprobantesReglaException>(()=>CargaComprobantesUnidades.Convertir("PAPEL","BL","UD",2,equivalencias));
    }

    [CargaComprobantesSqlFact]
    public async Task ExentosRespetanClasificacionLegacyYConciliacionElectronicaConRollback()
    {
        await using var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP",cn.Database);Assert.Contains("SERVER-ALFAVB6",cn.DataSource.ToUpperInvariant());
        using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync("UPDATE V_MA_ARTICULOS SET EXENTO=1,TasaIVA=0 WHERE LTRIM(RTRIM(IDARTICULO))='403'",transaction:tx);
            var repo=new CargaComprobantesRepository();var planner=new CargaComprobantesPlanificador(repo);
            var numero=99999998L;
            foreach(var valor in new[]{"NO","SI"})
            {
                await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR=@Valor WHERE LTRIM(RTRIM(CLAVE))='ArtExentoaNoGrav'",new{Valor=valor},tx);
                var ctx=await repo.GetContextoAsync(cn,"Administrador",default,tx,false);
                var draft=new CargaComprobantesDirecta{Cliente="112011541",UnidadNegocio="4",Deposito="1",Lineas=[new(){Articulo="403",Cantidad=2}]};
                var plan=await planner.CrearAsync(cn,ctx,0,10,[],DateTime.Today,default,tx,draft);
                Assert.Empty(plan.Liquidacion.Alicuotas);Assert.Equal(0,plan.Liquidacion.Iva);
                var r=await new CargaComprobantesGrabador(planner,new()).GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),numero--,"00000000000000",DateTime.Today,default);
                var l=await cn.QuerySingleAsync("SELECT LIVA_EXENTO Exento,LIVA_ImpNetoNGrav NoGravado,LIVA_TOTAL Total FROM MV_ASIENTOS WHERE [NUMERO ASIENTO]=@NumeroAsiento AND MES_OPERATIVO=@Mes AND PERIODO=@Periodo AND TIPO_REG=@Subdiario AND LIVA_TIPO='Ventas'",new{r.NumeroAsiento,Mes=DateTime.Today.Month,plan.Periodo,plan.Subdiario},tx);
                Assert.Equal(plan.Liquidacion.Total,(decimal)l.Total);
                Assert.Equal(valor=="SI"?plan.Liquidacion.Total:0,(decimal)l.Exento);
                Assert.Equal(valor=="NO"?plan.Liquidacion.Total:0,(decimal)l.NoGravado);
                Assert.Equal(plan.Liquidacion.Total,await cn.ExecuteScalarAsync<decimal>("SELECT Imp_OPExentas FROM V_MV_CPTE_ELECTRONICOS WHERE TC='FC' AND IdComprobante=@Comprobante",r,tx));
            }
        }
        finally{if(tx.Connection is not null)tx.Rollback();}
    }

    [CargaComprobantesSqlFact]
    public async Task PrecioEditadoDePedidoLlegaALaFacturaYNoModificaElPrecioDelOrigen()
    {
        await using var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP",cn.Database);Assert.Contains("SERVER-ALFAVB6",cn.DataSource.ToUpperInvariant());
        using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            var repo=new CargaComprobantesRepository();var ctx=await repo.GetContextoAsync(cn,"Administrador",default,tx,false);
            var origen=await repo.GetOrigenAsync(cn,ctx,432549,default,tx);
            var seleccion=origen.Lineas.Select(l=>new CargaComprobantesCantidad {LineaId=l.Id,Cantidad=l.Pendiente,
                PrecioUnitarioNeto=decimal.Round(l.PrecioSinIva*1.1m,4)}).ToList();
            var planner=new CargaComprobantesPlanificador(repo);var plan=await planner.CrearAsync(cn,ctx,origen.Id,10,seleccion,DateTime.Today,default,tx);
            var r=await new CargaComprobantesGrabador(planner,new()).GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999998,"00000000000000",DateTime.Today,default);
            var precios=(await cn.QueryAsync<decimal>("SELECT IMPORTE_S_IVA FROM V_MV_CPTEINSUMOS WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante ORDER BY SECUENCIA",r,tx)).ToList();
            Assert.Equal(seleccion.Select(x=>x.PrecioUnitarioNeto!.Value),precios);
            foreach(var linea in origen.Lineas)
                Assert.Equal(linea.PrecioSinIva,await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE_S_IVA FROM V_MV_CPTEINSUMOS WHERE ID=@Id",new{linea.Id},tx));
        }
        finally{if(tx.Connection is not null)tx.Rollback();}
    }

    [CargaComprobantesSqlFact]
    public async Task OrigenSecundarioPendienteOBloqueadoNoPuedeVolverAEmitirse()
    {
        await using var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP",cn.Database);Assert.Contains("SERVER-ALFAVB6",cn.DataSource.ToUpperInvariant());
        using var runtime=new CargaComprobantesArcaRuntime();
        var repo=new CargaComprobantesRepository();var ctx=await repo.GetContextoAsync(cn,"Administrador",default,incluirCatalogos:false);
        var origen=await repo.GetOrigenAsync(cn,ctx,432549,default);
        var cantidades=origen.Lineas.Select(l=>new CargaComprobantesCantidad{LineaId=l.Id,Cantidad=l.Pendiente}).ToList();
        var planner=new CargaComprobantesPlanificador(repo);
        // Integraciones nulas deliberadas: cualquier llegada al envío hace fallar esta prueba sin contactar ARCA.
        var servicio=new CargaComprobantesEmisionService(planner,new(planner,new()),null!,null!,runtime.Events);
        var fixture=Guid.NewGuid();
        var plan=new CargaComprobantesPlanGrabacion{OrigenId=99999988,Origenes=[new(){OrigenId=99999988},new(){OrigenId=origen.Id}]};
        try
        {
            await cn.ExecuteAsync("""
                INSERT dbo.ALFACORE_VENTAS_CARGA_OPERACION(Operacion,OrigenId,UnidadNegocio,Usuario,Ambiente,Estado,PlanJson)
                VALUES(@Id,99999988,'4','Administrador','HOMOLOGACION','PREPARADA',@Plan);
                """,new{Id=fixture,Plan=JsonSerializer.Serialize(plan)});
            var error=await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>servicio.EmitirAsync(cn,ctx,origen.Id,10,cantidades,Guid.NewGuid(),"",()=>{},default));
            Assert.Contains("operación pendiente",error.Message);
        }
        finally{await cn.ExecuteAsync("DELETE dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE Operacion=@Id",new{Id=fixture});}
        await using var otra=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await otra.OpenAsync();
        var recurso=$"CargaComprobantes.Origen.{origen.Id}";
        await otra.ExecuteAsync("EXEC sys.sp_getapplock @Resource=@Recurso,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0",new{Recurso=recurso});
        try
        {
            var bloqueado=await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>servicio.EmitirAsync(cn,ctx,origen.Id,10,cantidades,Guid.NewGuid(),"",()=>{},default));
            Assert.Contains("otra ventana",bloqueado.Message);
        }
        finally {await otra.ExecuteAsync("EXEC sys.sp_releaseapplock @Resource=@Recurso,@LockOwner='Session'",new{Recurso=recurso});}
    }

    [CargaComprobantesSqlFact]
    public async Task BultosDirectosReleenEquivalenciaYGrabanUnidadesStockSinCambiarMaestro()
    {
        await using var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP",cn.Database);Assert.Contains("SERVER-ALFAVB6",cn.DataSource.ToUpperInvariant());
        using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync("""
                DELETE FROM dbo.S_TA_EQUIV WHERE LTRIM(RTRIM(IDARTICULO))='403' AND
                    ((LTRIM(RTRIM(IDUNIDAD))='BL' AND LTRIM(RTRIM(IDUNIDAD_EQUIV))='1') OR
                     (LTRIM(RTRIM(IDUNIDAD))='1' AND LTRIM(RTRIM(IDUNIDAD_EQUIV))='BL'));
                INSERT dbo.S_TA_EQUIV(IDARTICULO,IDUNIDAD,IDUNIDAD_EQUIV,COEFICIENTE,ParaTodos)
                    VALUES(@Articulo,@Bulto,@Unidad,5,0);
                """,new{Articulo=AlfaCore.Common.CodigoPk.Format("403",25),Bulto=AlfaCore.Common.CodigoPk.Format("BL",4),Unidad=AlfaCore.Common.CodigoPk.Format("1",4)},tx);
            var draft=new CargaComprobantesDirecta{Cliente="112011541",UnidadNegocio="4",Deposito="1",
                Lineas=[new(){Articulo="403",Cantidad=1,Bultos=2}]};
            var repo=new CargaComprobantesRepository();var ctx=await repo.GetContextoAsync(cn,"Administrador",default,tx,false);
            var planner=new CargaComprobantesPlanificador(repo);var plan=await planner.CrearAsync(cn,ctx,0,10,[],DateTime.Today,default,tx,draft);
            Assert.Equal(10m,plan.Origen.Lineas[0].CantidadOriginal);Assert.Equal(2m,plan.Origen.Lineas[0].BultosReferencia);
            var r=await new CargaComprobantesGrabador(planner,new()).GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999998,"00000000000000",DateTime.Today,default);
            Assert.Equal(-10m,await cn.ExecuteScalarAsync<decimal>("SELECT Cantidad FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",r,tx));
            Assert.Equal(2m,await cn.ExecuteScalarAsync<decimal>("SELECT CANT_BL FROM V_MV_CPTEINSUMOS WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",r,tx));
            await cn.ExecuteAsync("UPDATE S_TA_EQUIV SET COEFICIENTE=6 WHERE LTRIM(RTRIM(IDARTICULO))='403' AND LTRIM(RTRIM(IDUNIDAD))='BL' AND LTRIM(RTRIM(IDUNIDAD_EQUIV))='1'",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>new CargaComprobantesGrabador(planner,new()).GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999997,"00000000000000",DateTime.Today,default));
        }
        finally{if(tx.Connection is not null)tx.Rollback();}
    }

    [CargaComprobantesSqlFact]
    public async Task DosPedidosCompartenFacturaYAplicanCadaOrigenConRollbackCompleto()
    {
        await using var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP",cn.Database);Assert.Contains("SERVER-ALFAVB6",cn.DataSource.ToUpperInvariant());
        using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            // Copias operativas exclusivamente dentro de la transacción revertida; ningún origen real es consumido.
            const string clon="999999999991X";
            var id=await ClonarAsync(cn,tx,"V_MV_CPTE","ID=432549",clon);
            await ClonarAsync(cn,tx,"V_MV_CPTEINSUMOS","TC='NP' AND IDCOMPROBANTE='000400008674X' AND IDCOMPLEMENTO=0",clon);
            await cn.ExecuteAsync("UPDATE V_MV_CPTEINSUMOS SET AlicIVA=10.5,IMPORTE=ROUND(IMPORTE_S_IVA*1.105,4) WHERE TC='NP' AND IDCOMPROBANTE=@Comprobante",new{Comprobante=clon},tx);
            var repo=new CargaComprobantesRepository(); var ctx=await repo.GetContextoAsync(cn,"Administrador",default,tx,false);
            var uno=await repo.GetOrigenAsync(cn,ctx,432549,default,tx); var dos=await repo.GetOrigenAsync(cn,ctx,id,default,tx);
            var cantidades=new[]{uno,dos}.SelectMany(o=>o.Lineas.Where(l=>l.Pendiente>0).Select(l=>new CargaComprobantesCantidad
                { OrigenId=o.Id,LineaId=l.Id,Cantidad=l.Pendiente/2 })).ToList();
            var planner=new CargaComprobantesPlanificador(repo);
            var plan=await planner.CrearAsync(cn,ctx,uno.Id,10,cantidades,DateTime.Today,default,tx);
            Assert.Equal(2,plan.Partes.Count);
            Assert.Equal(2,plan.Liquidacion.Alicuotas.Count);
            var r=await new CargaComprobantesGrabador(planner,new()).GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999998,"00000000000000",DateTime.Today,default);
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_APLICACION WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",r,tx));
            Assert.Equal(cantidades.Count,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTEINSUMOS WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",r,tx));
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT IdComprobante_Origen) FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",r,tx));
            Assert.True(await cn.ExecuteScalarAsync<decimal>("SELECT ImporteIva2 FROM V_MV_CPTE WHERE ID=@FacturaId",r,tx)>0);
            Assert.True(await cn.ExecuteScalarAsync<decimal>("SELECT SUM(ISNULL(LIVA_ImpIva2,0)) FROM MV_ASIENTOS WHERE TC='FC' AND SUCURSAL='1007' AND NUMERO='99999998'",transaction:tx)>0);
            Assert.Equal(0m,await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE TC='FC' AND SUCURSAL='1007' AND NUMERO='99999998'",transaction:tx));
            foreach(var o in new[]{uno,dos})
            {
                var actualizado=await repo.GetOrigenAsync(cn,ctx,o.Id,default,tx);
                Assert.Equal(o.Lineas.Sum(x=>x.Pendiente)/2,actualizado.Lineas.Sum(x=>x.Pendiente));
            }
            // Cambiar cualquiera de los orígenes invalida toda la operación antes de grabar.
            await cn.ExecuteAsync("UPDATE V_MV_CPTE SET OBSERVACIONES='cambio concurrente' WHERE ID=@Id",new{Id=id},tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>new CargaComprobantesGrabador(planner,new()).GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999997,"00000000000000",DateTime.Today,default));
        }
        finally {if(tx.Connection is not null)tx.Rollback();}
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE WHERE IDCOMPROBANTE IN ('999999999991X','100799999998A')"));
    }

    [CargaComprobantesSqlFact]
    public async Task DosRemitosConStockPrevioConservanPendientesYSinNuevaDescarga()
    {
        await using var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP",cn.Database);Assert.Contains("SERVER-ALFAVB6",cn.DataSource.ToUpperInvariant());
        using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            var ids=new List<int>();
            foreach(var numero in new[]{"999999999992X","999999999993X"})
            {
                ids.Add(await ClonarAsync(cn,tx,"V_MV_CPTE","ID=432549",numero,"RM"));
                await ClonarAsync(cn,tx,"V_MV_CPTEINSUMOS","TC='NP' AND IDCOMPROBANTE='000400008674X' AND IDCOMPLEMENTO=0",numero,"RM");
                await cn.ExecuteAsync("UPDATE V_MV_CPTEINSUMOS SET CANTIDADAFACTURAR=CANTIDAD,CANTIDADAFACTURARUD=CANTIDADUD WHERE TC='RM' AND IDCOMPROBANTE=@Numero",new{Numero=numero},tx);
                await ClonarAsync(cn,tx,"V_MV_STOCK","ID=(SELECT TOP(1) ID FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE='100700000186A' ORDER BY ID)",numero,"RM");
            }
            var repo=new CargaComprobantesRepository();var ctx=await repo.GetContextoAsync(cn,"Administrador",default,tx,false);
            Assert.True(ctx.DescargaStockRemitos);
            var origenes=new List<CargaComprobantesOrigen>();
            foreach(var id in ids)origenes.Add(await repo.GetOrigenAsync(cn,ctx,id,default,tx));
            var cantidades=origenes.SelectMany(o=>o.Lineas.Select(l=>new CargaComprobantesCantidad{OrigenId=o.Id,LineaId=l.Id,Cantidad=l.Pendiente/2})).ToList();
            var planner=new CargaComprobantesPlanificador(repo);
            var plan=await planner.CrearAsync(cn,ctx,ids[0],10,cantidades,DateTime.Today,default,tx);
            Assert.False(plan.DescargaStock);
            var r=await new CargaComprobantesGrabador(planner,new()).GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999998,"00000000000000",DateTime.Today,default);
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",r,tx));
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_APLICACION WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",r,tx));
            foreach(var o in origenes)
            {
                var actual=await repo.GetOrigenAsync(cn,ctx,o.Id,default,tx);
                Assert.Equal(o.Lineas.Sum(x=>x.Pendiente)/2,actual.Lineas.Sum(x=>x.Pendiente));
            }
        }
        finally{if(tx.Connection is not null)tx.Rollback();}
    }

    private static async Task<int> ClonarAsync(SqlConnection cn,SqlTransaction tx,string tabla,string filtro,string comprobante,string? tc=null)
    {
        // Nombres y filtros son constantes de esta prueba. Metadatos excluyen identidad, calculados y timestamp.
        var columnas=(await cn.QueryAsync<string>("SELECT QUOTENAME(name) FROM sys.columns WHERE object_id=OBJECT_ID(@Tabla) AND is_identity=0 AND is_computed=0 AND system_type_id<>189 ORDER BY column_id",new{Tabla="dbo."+tabla},tx)).ToList();
        var valores=columnas.Select(x=>x.Equals("[IDCOMPROBANTE]",StringComparison.OrdinalIgnoreCase)?"@Comprobante":x.Equals("[TC]",StringComparison.OrdinalIgnoreCase)&&tc is not null?"@Tc":x);
        return await cn.ExecuteScalarAsync<int>($"DECLARE @Ids TABLE(Id int); INSERT dbo.{tabla} ({string.Join(',',columnas)}) OUTPUT inserted.ID INTO @Ids SELECT {string.Join(',',valores)} FROM dbo.{tabla} WHERE {filtro}; SELECT TOP(1) Id FROM @Ids;",new{Comprobante=comprobante,Tc=tc},tx);
    }
}
