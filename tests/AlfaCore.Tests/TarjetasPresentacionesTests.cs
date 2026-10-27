using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasPresentacionesTests
{
    [TarjetasLecturaSqlFact]
    public async Task DeteccionConsultaTodasLasTjActivasEnPesosConLaMismaFuenteContable()
    {
        await using var cn=Conexion();await cn.OpenAsync();
        var rows=(await cn.QueryAsync<TarjetasMovimiento>(ConciliacionTarjetasService.MovimientosDeteccionSql,
            new{desde=new DateTime(2026,3,28),hasta=new DateTime(2026,5,2),sucursal="",usuario="SISTEMAS"})).AsList();
        Assert.NotEmpty(rows);Assert.True(rows.Count<=10000);
        var cuentas=(await cn.QueryAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='TJ' AND LTRIM(RTRIM(MONEDA))='1' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0")).ToHashSet();
        Assert.All(rows,m=>{Assert.Contains(m.Cuenta,cuentas);Assert.True(m.Importe>0);});
        Assert.Contains(rows,m=>m.Cuenta=="111010004"&&m.Presentacion==0);
    }
    private static SqlConnection Conexion()
    {
        var value=Environment.GetEnvironmentVariable("TARJETAS_LECTURA_TEST_SQL")!;
        Assert.Equal("ELBOLSON2011_PRUEBAS",new SqlConnectionStringBuilder(value).InitialCatalog);
        return new(value);
    }
    [TarjetasLecturaSqlFact]
    public async Task CobrosRealesSinPresentacionNumeradaSeIncluyenSinTraerAjustesInternos()
    {
        await using var cn=Conexion();await cn.OpenAsync();
        var rows=(await cn.QueryAsync<TarjetasMovimiento>(ConciliacionTarjetasService.MovimientosAlfaSql,
            new{cuenta="111010004",desde=new DateTime(2026,3,31),hasta=new DateTime(2026,4,29),sucursal="",usuario="SISTEMAS"})).AsList();
        Assert.NotEmpty(rows);
        Assert.Contains(rows,m=>m.Presentacion==0&&m.Fecha==new DateTime(2026,3,31)&&m.Comprobante.StartsWith("CBCT "));
        Assert.Contains(rows,m=>m.Presentacion==0&&m.Comprobante.StartsWith("CB "));
        Assert.All(rows,m=>Assert.True(m.Importe>0));
        Assert.DoesNotContain(rows,m=>m.Comprobante.StartsWith("CC ")||m.Comprobante.StartsWith("LT "));
        Assert.Equal(rows.Count,rows.Select(m=>m.Clave).Distinct().Count());
    }
    [TarjetasLecturaSqlFact]
    public async Task VentasSinPresentacionRespetanCuentaFechaSucursalUnidadMonedaYExclusiones()
    {
        await using var cn=Conexion();await cn.OpenAsync();
        // Solo tablas temporales: ejecutamos el mismo SQL de producción sin modificar registros del cliente.
        await cn.ExecuteAsync("""
            CREATE TABLE #Usuarios(NOMBRE nvarchar(50),AdminCentral bit,UNEGOCIO nvarchar(4));
            INSERT #Usuarios VALUES('ENSAYO',0,'1');
            CREATE TABLE #Tipos(Codigo nvarchar(4),SISTEMA nvarchar(20));
            INSERT #Tipos VALUES('CB','Ventas'),('CBCT','Ventas'),('CBFP','Ventas'),('CC','Contable'),('LT','Contable');
            CREATE TABLE #Movimientos(PERIODO nvarchar(6),MES_OPERATIVO tinyint,[NUMERO ASIENTO] int,TIPO_REG nvarchar(4),
                SECUENCIA int,FECHA date,IMPORTE decimal(19,2),CUENTA nvarchar(15),TC nvarchar(4),SUCURSAL nvarchar(4),
                NUMERO nvarchar(8),LETRA nvarchar(1),UNEGOCIO nvarchar(4),TJ_NroCupon nvarchar(30),
                TJ_Presentacion int,TJ_Liquidacion int,DETALLE nvarchar(100),MONEDA nvarchar(4),
                EsResumen bit,EsCierre bit,ES_SALDO_APERTURA bit,[DEBE-HABER] nvarchar(1));
            """);
        var casos=new[]{
            new{Id=1,Tc="CB",Presentacion=(int?)0,Cupon="111"},
            new{Id=2,Tc="CBCT",Presentacion=(int?)null,Cupon=""},
            new{Id=3,Tc="CC",Presentacion=(int?)0,Cupon="1"},
            new{Id=4,Tc="LT",Presentacion=(int?)0,Cupon=""},
            new{Id=5,Tc="CC",Presentacion=(int?)200,Cupon="222"},
            new{Id=6,Tc="CB",Presentacion=(int?)0,Cupon="1"},
            new{Id=7,Tc="CB",Presentacion=(int?)0,Cupon="1"},
            new{Id=8,Tc="CB",Presentacion=(int?)0,Cupon="1"},
            new{Id=9,Tc="CB",Presentacion=(int?)0,Cupon="1"},
            new{Id=10,Tc="CB",Presentacion=(int?)0,Cupon="1"},
            new{Id=11,Tc="CB",Presentacion=(int?)0,Cupon="1"},
            new{Id=12,Tc="CB",Presentacion=(int?)0,Cupon="1"},
            new{Id=13,Tc="CBFP",Presentacion=(int?)0,Cupon="1"},
            new{Id=14,Tc="CC",Presentacion=(int?)1,Cupon="   "},
            new{Id=15,Tc="CB",Presentacion=(int?)0,Cupon="1"},
            new{Id=16,Tc="CB",Presentacion=(int?)0,Cupon="1"}};
        foreach(var caso in casos)
            await cn.ExecuteAsync("""
                INSERT #Movimientos VALUES('18',3,@Id,'',1,@fecha,@importe,@cuenta,@Tc,@sucursal,'00000001','X',
                    @unidad,@Cupon,@Presentacion,0,'Ensayo',@moneda,@resumen,@cierre,@apertura,@sentido);
                """,new{caso.Id,caso.Tc,caso.Cupon,caso.Presentacion,fecha=caso.Id==11?new DateTime(2026,5,1):new DateTime(2026,3,31),
                    importe=caso.Id==12?-1m:caso.Id*10m,cuenta=caso.Id==15?"OTRA":"TJ-ENSAYO",sucursal=caso.Id==2?"0002":"0001",
                    unidad=caso.Id==10?"2":"1",moneda=caso.Id==16?"2":"1",resumen=caso.Id==6,cierre=caso.Id==7,apertura=caso.Id==8,sentido=caso.Id==9?"H":"D"});
        var sql=ConciliacionTarjetasService.MovimientosAlfaSql.Replace("dbo.MV_ASIENTOS","#Movimientos")
            .Replace("dbo.V_TA_Cpte","#Tipos").Replace("dbo.TA_USUARIOS","#Usuarios");
        async Task<int[]> Consultar(string sucursal="")=>(await cn.QueryAsync<TarjetasMovimiento>(sql,
            new{cuenta="TJ-ENSAYO",desde=new DateTime(2026,3,31),hasta=new DateTime(2026,4,29),sucursal,usuario="ENSAYO"}))
                .Select(m=>int.Parse(m.Clave.Split('|')[2])).Order().ToArray();
        Assert.Equal(new[]{1,2,5,13},await Consultar());
        Assert.Equal(new[]{2},await Consultar("0002"));
        await cn.ExecuteAsync("UPDATE #Usuarios SET AdminCentral=1 WHERE NOMBRE='ENSAYO'");
        Assert.Equal(new[]{1,2,5,10,13},await Consultar());
    }
}
public sealed class TarjetasLecturaSqlFactAttribute:FactAttribute
{
    public TarjetasLecturaSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_LECTURA_TEST_SQL")))Skip="Requiere conexión de lectura autorizada a ELBOLSON2011_PRUEBAS.";}
}
