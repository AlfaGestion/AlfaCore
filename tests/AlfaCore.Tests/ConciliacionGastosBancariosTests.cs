using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConciliacionGastosBancariosTests
{
    private static readonly ConciliacionGastosConfig Config=new(){Cuentas=new(){["Gastos"]="42209",["IvaCredito"]="11203",["IvaPercepcion"]="11208",["Iibb"]="11211",["Idc"]="42225"}};
    private static long _id;
    private static ConciliacionFilaBanco Fila(string detalle,decimal importe,long? conciliacion=null,string registracion="")=>
        new(){Id=++_id,Fecha=new(2026,10,1),Detalle=detalle,Importe=importe,Huella="H"+_id,ConciliacionId=conciliacion,Registracion=registracion};

    /// <summary>Movimientos reales del extracto de Patagonia de octubre/2026 (totales por concepto del PDF), más ruido que no es gasto.</summary>
    private static List<ConciliacionFilaBanco> Octubre()=>
    [
        Fila("EMISION ECHEQ",-210.00m),Fila("COMISION CONSULTA DE CHEQUES",-18192.98m),Fila("COMIS.MANTENIMIENTO CUENTA 10/26",-70000.00m),
        Fila("IVA ALICUOTA GENERAL",-44.10m),Fila("IVA ALICUOTA GENERAL",-3820.53m),Fila("IVA ALICUOTA GENERAL",-14700.00m),
        Fila("IVA PERCEPCION",-545.79m),Fila("IVA PERCEPCION",-2100.00m),
        Fila("RET.IIBB SIRCREB",-334113.10m),
        Fila("IMP.DB/CR BANCARIOS P/DEBITOS",-441794.34m),Fila("IMP.DB/CR BANCARIOS P/CREDITOS",-77793.57m),
        Fila("MASTERCARD COM 9191467 685379",-125658.45m),Fila("TRANSFERENCIA ENTRE CUENTAS",5000000m),Fila("ECHEQ CAMARA COMPENSADORA 103822320",-2280460.54m),
        Fila("DEBITO AUTOMATICO DE MERCANTIL",-78629.81m),Fila("CREDITO POR TRANSFERENCIA",13500000m)
    ];

    [Fact]
    public void OctubreCoincideConElResumenImpositivoDelBanco()
    {
        var c=ConciliacionGastosBancarios.Calcular(Octubre(),Config,1m);
        Assert.Empty(c.Errores);
        Assert.Equal(88402.98m,c.Importe(ConciliacionGastosBancarios.Gastos));
        Assert.Equal(18564.63m,c.Importe(ConciliacionGastosBancarios.IvaCredito));
        Assert.Equal(2645.79m,c.Importe(ConciliacionGastosBancarios.IvaPercepcion));
        Assert.Equal(334113.10m,c.Importe(ConciliacionGastosBancarios.Iibb));
        Assert.Equal(519587.91m,c.Importe(ConciliacionGastosBancarios.Idc));
        Assert.Equal(0m,c.Importe(ConciliacionGastosBancarios.Ganancias));
        Assert.Equal(963314.41m,c.Total);
        Assert.Equal(11,c.Movimientos.Count);
    }

    [Fact]
    public void LibroDeIvaYAsientoSalenDelMismoCalculo()
    {
        var c=ConciliacionGastosBancarios.Calcular(Octubre(),Config,1m);
        var libro=ConciliacionGastosBancarios.ArmarLibroIva(c,Config);
        Assert.Equal(88402.98m,libro.NetoGravado);
        Assert.Equal(519587.91m,libro.NetoNoGravado);
        Assert.Equal(21m,libro.Alicuota);
        Assert.Equal(18564.63m,libro.Iva);
        Assert.Equal(2645.79m,libro.PercepcionIva);
        Assert.Equal(334113.10m,libro.PercepcionIibb);
        Assert.Equal(0m,libro.PercepcionGanancias);
        Assert.Equal(libro.NetoGravado+libro.NetoNoGravado+libro.Iva+libro.PercepcionIva+libro.PercepcionIibb+libro.PercepcionGanancias,libro.Total);
        var debe=ConciliacionGastosBancarios.LineasDebe(c,Config.Cuentas);
        Assert.Equal(["42209","11203","11208","11211","42225"],debe.Select(l=>l.Cuenta));
        Assert.Equal(c.Total,debe.Sum(l=>l.Importe));
    }

    [Fact]
    public void ElIvaQueNoCorrespondeAlPorcentajeDeLosGastosBloquea()
    {
        var filas=Octubre();filas.Add(Fila("COMISION EXTRA",-1000m));
        var c=ConciliacionGastosBancarios.Calcular(filas,Config,1m);
        Assert.Single(c.Errores);
        Assert.Contains("IVA",c.Errores[0]);
    }

    [Fact]
    public void LosYaConciliadosNoSeIncluyenYLasDevolucionesSeDescuentanDelConcepto()
    {
        var filas=Octubre();
        filas.Add(Fila("RET.IIBB SIRCREB",-500m,conciliacion:7));
        filas.Add(Fila("IMP.DB/CR BANCARIOS P/DEBITOS",-9m,registracion:"Registrado"));
        filas.Add(Fila("IMP.DB/CR BANCARIOS P/CREDITOS",25m));
        var c=ConciliacionGastosBancarios.Calcular(filas,Config,1m);
        Assert.Equal(963289.41m,c.Total);
        Assert.Equal(519562.91m,c.Importe(ConciliacionGastosBancarios.Idc));
        Assert.Equal(2,c.Avisos.Count);
    }

    [Fact]
    public void FaltaLaCuentaDeUnConceptoConImporte()
    {
        var c=ConciliacionGastosBancarios.Calcular(Octubre(),Config,1m);
        var sinIdc=Config.Cuentas.Where(x=>x.Key!="Idc").ToDictionary(x=>x.Key,x=>x.Value);
        var ex=Assert.Throws<InvalidOperationException>(()=>ConciliacionGastosBancarios.LineasDebe(c,sinIdc));
        Assert.Contains("débitos y créditos",ex.Message);
    }

    [Fact]
    public void LaHuellaCambiaSiCambiaUnMovimiento()
    {
        var filas=Octubre();
        var a=ConciliacionGastosBancarios.Calcular(filas,Config,1m).Huella;
        filas[3].Huella="OTRA";
        Assert.NotEqual(a,ConciliacionGastosBancarios.Calcular(filas,Config,1m).Huella);
    }

    [Fact]
    public void LasCuentasInicialesSeTomanDelAsientoModeloPorSuDescripcion()
    {
        var modelo=new ConciliacionGastosBancarios.ModeloFila[]
        {
            new("111020001","Banco Patagonia","H",0),new("111020004","Banco Galicia","H",0),
            new("42209","Gastos Bancarios","D",21),new("11203","I.V.A. C.F. Por Compra de Bienes-Merc. Local","D",0),
            new("11208","I.V.A. Retención/Percepción","D",0),new("11210","Retención de Impuestos a las Ganancias","D",0),
            new("11211","Retencion de Impuestos S/Ing. Brutos","D",0),new("42225","IDC Imp. Deb. Cred.","D",0)
        };
        var cuentas=ConciliacionGastosBancarios.SugerirCuentas(modelo);
        Assert.Equal("42209",cuentas["Gastos"]);
        Assert.Equal("11203",cuentas["IvaCredito"]);
        Assert.Equal("11208",cuentas["IvaPercepcion"]);
        Assert.Equal("11211",cuentas["Iibb"]);
        Assert.Equal("11210",cuentas["Ganancias"]);
        Assert.Equal("42225",cuentas["Idc"]);
        Assert.DoesNotContain("111020001",cuentas.Values);
    }

    [Theory]
    [InlineData("RET.IIBB SIRCREB","Iibb")]
    [InlineData("IMP.DB/CR BANCARIOS P/CREDITOS","Idc")]
    [InlineData("IVA PERCEPCION","IvaPercepcion")]
    [InlineData("IVA ALICUOTA GENERAL","IvaCredito")]
    [InlineData("EMISION ECHEQ","Gastos")]
    [InlineData("COMIS.MANTENIMIENTO CUENTA 10/26","Gastos")]
    [InlineData("MASTERCARD COM 9191467 685379",null)]
    [InlineData("TRANSFERENCIA ENTRE CUENTAS",null)]
    [InlineData("ECHEQ CAMARA COMPENSADORA 103822320",null)]
    public void ClasificaPorDescripcionDelExtracto(string descripcion,string? esperado)
        =>Assert.Equal(esperado,ConciliacionGastosBancarios.Clasificar(descripcion,new()));
}

[Collection("Conciliacion SQL")]
public sealed class ConciliacionGastosSqlTests
{
    [ConciliacionSqlFact]
    public async Task GeneraElAsientoMensualConLibroDeIvaYConciliaTodosLosMovimientosConRollback()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Contains(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog,new[]{"ALFANET2007_PRUEBAS","ELBOLSON2011_PRUEBAS"});
        using var runtime=new CargaComprobantesArcaRuntime();var service=ConciliacionAsientosTests.Service(connection,runtime);
        await using var cn=new Microsoft.Data.SqlClient.SqlConnection(connection);await cn.OpenAsync();
        using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            // La migración es idempotente y transaccional: se aplica dentro del ensayo y se revierte con él.
            var dir=new DirectoryInfo(AppContext.BaseDirectory);
            while(dir is not null&&!Directory.Exists(Path.Combine(dir.FullName,"src","AlfaCore","App_Data","updates")))dir=dir.Parent;
            await Dapper.SqlMapper.ExecuteAsync(cn,await File.ReadAllTextAsync(Path.Combine(dir!.FullName,"src","AlfaCore","App_Data","updates","2026-11-11-006__caja_conciliacion_gastos_bancarios.sql")),transaction:tx);
            await Dapper.SqlMapper.ExecuteAsync(cn,await File.ReadAllTextAsync(Path.Combine(dir!.FullName,"src","AlfaCore","App_Data","updates","2026-11-11-008__caja_conciliacion_gastos_comprobante_gb.sql")),transaction:tx);
            var catalogo=await service.AsientoCatalogoEnConexionAsync(cn,tx,default,"GB");
            var banco=catalogo.Bancos.First().Codigo;var cuentas=catalogo.Cuentas.Where(c=>c.Codigo!=banco).Take(5).ToArray();Assert.Equal(5,cuentas.Length);
            var unidad=catalogo.UnidadInicial;
            var tal=catalogo.Talonarios.First(t=>t.Unidad==unidad||t.Unidad is "" or "0").Talonario;
            var tag="ENSAYO_GASTOS_"+Guid.NewGuid().ToString("N");
            var import=await Dapper.SqlMapper.ExecuteScalarAsync<long>(cn,"""
                INSERT BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso,CuentaBanco)
                OUTPUT INSERTED.ImportacionId VALUES('Provincia',@tag,'',SYSUTCDATETIME(),'ENSAYO',0,0,'IMPORTED',@banco);
                """,new{tag,banco},tx);
            foreach(var (fecha,detalle,importe) in new[]{("20261018","COMISION MANTENIMIENTO CUENTA",-100.00m),("20261018","IVA ALICUOTA GENERAL",-21.00m),
                ("20261019","IVA PERCEPCION",-5.00m),("20261020","RET.IIBB SIRCREB",-30.00m),("20261020","IMP.DB/CR BANCARIOS P/DEBITOS",-40.00m),
                ("20261021","IMP.DB/CR BANCARIOS P/CREDITOS",-20.00m),("20261021","TRANSFERENCIA ENTRE CUENTAS",-500.00m)})
                await Dapper.SqlMapper.ExecuteAsync(cn,"""
                    INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe,ReferenciaExterna)
                    VALUES(@import,'Provincia',@fecha,@detalle,@importe,@tag+@detalle);
                    """,new{import,fecha,detalle,importe,tag},tx);
            var prep=await service.PrepararGastosEnConexionAsync(cn,tx,import,default);
            Assert.Empty(prep.Calculo.Errores);
            Assert.Equal(216m,prep.Calculo.Total);
            Assert.Equal(6,prep.Calculo.Movimientos.Count);
            var cuentasSolicitud=new Dictionary<string,string>{["Gastos"]=cuentas[0].Codigo,["IvaCredito"]=cuentas[1].Codigo,["IvaPercepcion"]=cuentas[2].Codigo,["Iibb"]=cuentas[3].Codigo,["Idc"]=cuentas[4].Codigo};
            var req=new ConciliacionGastosSolicitud(Guid.NewGuid(),import,prep.Calculo.Huella,new(2026,10,31),unidad,tal,"Gastos bancarios de ensayo",cuentasSolicitud);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarGastosEnTransaccionAsync(cn,tx,req with{Huella="vieja"}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarGastosEnTransaccionAsync(cn,tx,req with{Unidad="NO"}));
            var result=await service.GenerarGastosEnTransaccionAsync(cn,tx,req);
            var retry=await service.GenerarGastosEnTransaccionAsync(cn,tx,req);
            Assert.Equal(result.NumeroAsiento,retry.NumeroAsiento);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarGastosEnTransaccionAsync(cn,tx,req with{Detalle="Otro detalle"}));
            var lineas=(await Dapper.SqlMapper.QueryAsync(cn,"""
                SELECT SECUENCIA sec,CUENTA cuenta,[DEBE-HABER] dh,IMPORTE importe,LIVA_TIPO tipo,LIVA_ImpNetoGrav neto,LIVA_ImpNetoNGrav nog,LIVA_AlicIVA alic,LIVA_ImpIVA iva,
                    LIVA_Ret_Perc perc,LIVA_Ret_IBtos ibtos,LIVA_TOTAL total,CABCUENTA cab
                FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@NumeroAsiento AND TIPO_REG=@TipoRegistro ORDER BY SECUENCIA
                """,result,tx)).ToList();
            Assert.Equal(6,lineas.Count);
            Assert.Equal("H",(string)lineas[0].dh);Assert.Equal(216m,(decimal)lineas[0].importe);
            Assert.Equal("COMPRAS",(string)lineas[0].tipo);Assert.Equal(banco,((string)lineas[0].cab).Trim());
            Assert.Equal(100m,(decimal)lineas[0].neto);Assert.Equal(60m,(decimal)lineas[0].nog);Assert.Equal(21m,(decimal)lineas[0].alic);
            Assert.Equal(21m,(decimal)lineas[0].iva);Assert.Equal(5m,(decimal)lineas[0].perc);Assert.Equal(30m,(decimal)lineas[0].ibtos);Assert.Equal(216m,(decimal)lineas[0].total);
            Assert.All(lineas.Skip(1),l=>Assert.Equal("D",(string)l.dh));
            Assert.Equal(216m,lineas.Skip(1).Sum(l=>(decimal)l.importe));
            Assert.Equal(6,await Dapper.SqlMapper.ExecuteScalarAsync<int>(cn,"SELECT COUNT(*) FROM BK_ConciliacionBanco WHERE ConciliacionId=@ConciliacionId",result,tx));
            Assert.Equal(6,await Dapper.SqlMapper.ExecuteScalarAsync<int>(cn,"SELECT COUNT(*) FROM BK_ExtractoMovimiento WHERE ImportacionId=@import AND EstadoRevision='Registrado'",new{import},tx));
            Assert.Equal(1,await Dapper.SqlMapper.ExecuteScalarAsync<int>(cn,"SELECT COUNT(*) FROM TA_CONFIGURACION WHERE CLAVE='CONCILIACION-GASTOS-BANCARIOS'",transaction:tx));
            Assert.Equal("GB",result.Tc);
            Assert.Equal(1,await Dapper.SqlMapper.ExecuteScalarAsync<int>(cn,"SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@NumeroAsiento AND TIPO_REG=@TipoRegistro AND SECUENCIA=1 AND LTRIM(RTRIM(TC))='GB'",result,tx));
            Assert.Equal(1,await Dapper.SqlMapper.ExecuteScalarAsync<int>(cn,"SELECT COUNT(*) FROM V_MV_CpteAcciones WHERE LTRIM(RTRIM(TC))='GB' AND IDCOMPROBANTE=@Comprobante AND TIPO_ACCION='A' AND NUMEROASIENTO=@NumeroAsiento",result,tx));
            // Ya conciliados: no queda nada para generar otro asiento.
            var despues=await service.PrepararGastosEnConexionAsync(cn,tx,import,default);
            Assert.Equal(0m,despues.Calculo.Total);Assert.NotEmpty(despues.Calculo.Errores);
        }
        finally { tx.Rollback(); }
    }
}

public sealed class LibroIvaColumnasTests
{
    [Fact]
    public void LasColumnasDeIvaYPercepcionesSalenDeLoQueTieneElPeriodo()
    {
        var r=new AlfaCore.Models.LibroIvaResultado{Filas=[
            new(){NetoGravado=95000m,PercepcionIva=2850m,PercepcionIibb=2168897.99m,Total=3032068.83m,Alicuotas=[new(21m,95000m,19950m)]},
            new(){NetoGravado=1000m,Total=1270m,Alicuotas=[new(27m,1000m,270m)]}]};
        Assert.Equal(["IVA 21%","IVA 27%"],r.ColumnasIva.Select(c=>c.Titulo));
        Assert.Equal(["Percep. IVA","Percep./Ret. IIBB"],r.ColumnasPercepciones.Select(c=>c.Titulo));
        Assert.Equal(["Neto gravado","No gravado","Exento","IVA 21%","IVA 27%","Percep. IVA","Percep./Ret. IIBB","Imp. internos","Total"],r.ColumnasImportes.Select(c=>c.Titulo));
        Assert.Equal(2850m,r.ColumnasPercepciones[0].Valor(r.Filas[0]));Assert.Equal(270m,r.ColumnasIva[1].Valor(r.Filas[1]));
        Assert.Single(new AlfaCore.Models.LibroIvaResultado().ColumnasIva);Assert.Single(new AlfaCore.Models.LibroIvaResultado().ColumnasPercepciones);
    }
}
