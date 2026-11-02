using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using System.Reflection;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasExcelTests
{
    private static TarjetasDatosExportacion Datos(IReadOnlyList<TarjetasDetalle> detalles,IReadOnlyList<TarjetasMovimiento> movimientos,
        IReadOnlyList<TarjetasVinculoExportacion>? vinculos=null)
        =>new(new(new(){Id=1,Nombre="ensayo.pdf",Cuenta="00110004",Banco="Ensayo",Tarjeta="VISA",Comercio="000123",Desde=new(2026,9,1),Hasta=new(2026,9,30),DetalleCompleto=true},detalles,movimientos,1),
            new(new(1,new(2026,9,1),new(2026,9,30),Estado:"Todos")),vinculos??[],"BASE ENSAYO","OPERADOR",new(2026,10,30,15,0,0,DateTimeKind.Utc));
    private static XLWorkbook Abrir(TarjetasDatosExportacion datos)=>new(new MemoryStream(TarjetasExcelExporter.Crear(datos)));

    [Fact]
    public void ExportaTodasLasPaginasConFechasImportesYCodigosDeTextoSinFormulasInyectadas()
    {
        var fecha=new DateTime(2026,9,1);
        var detalles=Enumerable.Range(1,150).Select(n=>new TarjetasDetalle{Id=n,Fecha=fecha,FechaPago=fecha.AddDays(2),Importe=10.25m,Lote="0012",Cupon="000045",Cantidad=1}).ToList();
        var movimientos=Enumerable.Range(1,110).Select(n=>new TarjetasMovimiento{Clave=n.ToString(),Cuenta="00110004",Fecha=fecha,Importe=10.25m,Sucursal="0001",Cupon="000045",Detalle="=HYPERLINK(\"https://ejemplo.invalid\")"}).ToList();
        using var libro=Abrir(Datos(detalles,movimientos));
        Assert.Equal(new[]{"Resumen","Liquidacion","Alfa","Conciliaciones"},libro.Worksheets.Select(h=>h.Name));
        var l=libro.Worksheet("Liquidacion");Assert.Equal("150",l.Cell(151,1).GetString());Assert.Equal(1537.5m,l.Cell(153,10).GetValue<decimal>());
        Assert.Equal(XLDataType.DateTime,l.Cell(2,2).DataType);Assert.Equal(fecha,l.Cell(2,2).GetDateTime());
        Assert.Equal(XLDataType.Number,l.Cell(2,10).DataType);Assert.Equal("0012",l.Cell(2,5).GetString());Assert.Equal("000045",l.Cell(2,6).GetString());
        var a=libro.Worksheet("Alfa");Assert.Equal("110",a.Cell(111,2).GetString());Assert.Equal("00110004",a.Cell(2,1).GetString());
        Assert.Equal("0001",a.Cell(2,5).GetString());Assert.Equal(XLDataType.Text,a.Cell(2,5).DataType);
        Assert.Equal(movimientos[0].Detalle,a.Cell(2,6).GetString());Assert.False(a.Cell(2,6).HasFormula);
        Assert.True(l.AutoFilter.IsEnabled);Assert.Equal(1,l.SheetView.SplitRow);
        Assert.Contains(libro.Worksheet("Resumen").CellsUsed(),celda=>celda.GetString()==TarjetasExcelExporter.Advertencia);
    }

    [Fact]
    public void VinculosCompletosInformanAuditoriaYParcialesNoRevelanTotalesNiDatosFueraDelAlcance()
    {
        var detalles=new[]{new TarjetasDetalle{Id=1,Fecha=new(2026,9,1),Importe=100,Vinculo=7},new TarjetasDetalle{Id=2,Fecha=new(2026,9,1),Importe=200,Vinculo=8}};
        var movimientos=new[]{new TarjetasMovimiento{Clave="A",Fecha=new(2026,9,1),Importe=100,Vinculo=7},new TarjetasMovimiento{Clave="B",Fecha=new(2026,9,1),Importe=150,Vinculo=8},new TarjetasMovimiento{Clave="C",Fecha=new(2026,9,1),Importe=40,LegacyLiquidado=true}};
        using var libro=Abrir(Datos(detalles,movimientos,[
            new(){Id=7,ImportacionId=1,CantidadDetalles=1,CantidadMovimientos=1,ImporteResumen=100,ImporteAlfa=100,Usuario="AUTORIZADO",Motivo="Revisado",FechaUtc=new(2026,10,1)},
            new(){Id=8,ImportacionId=1,CantidadDetalles=2,CantidadMovimientos=3,ImporteResumen=900,ImporteAlfa=900,Usuario="USUARIO_FUERA_DEL_ALCANCE",Motivo="MOTIVO_PRIVADO",FechaUtc=new(2026,10,1)}]));
        var hoja=libro.Worksheet("Conciliaciones");Assert.Equal("Completo",hoja.Cell(2,2).GetString());Assert.Equal("AUTORIZADO",hoja.Cell(2,11).GetString());
        Assert.Equal(100m,hoja.Cell(2,8).GetValue<decimal>());Assert.Equal("Revisado",hoja.Cell(2,13).GetString());
        Assert.Contains("Parcial",hoja.Cell(3,2).GetString());Assert.Equal(200m,hoja.Cell(3,5).GetValue<decimal>());Assert.Equal(150m,hoja.Cell(3,6).GetValue<decimal>());
        Assert.Equal(50m,hoja.Cell(3,7).GetValue<decimal>());Assert.True(hoja.Range(3,8,3,13).IsEmpty());
        Assert.DoesNotContain(libro.Worksheets.SelectMany(h=>h.CellsUsed()),c=>c.GetString().Contains("PRIVADO")||c.GetString().Contains("USUARIO_FUERA"));
        Assert.Equal("Liquidado en sistema anterior",libro.Worksheet("Alfa").Cell(4,10).GetString());
    }

    [Fact]
    public void CambioContablePosteriorImpidePresentarGrupoComoCompleto()
    {
        using var libro=Abrir(Datos([new(){Id=1,Fecha=new(2026,9,1),Importe=100,Vinculo=7}],
            [new(){Clave="A",Fecha=new(2026,9,1),Importe=101,Vinculo=7,Alterado=true}],
            [new(){Id=7,ImportacionId=1,CantidadDetalles=1,CantidadMovimientos=1,ImporteResumen=100,ImporteAlfa=100,Usuario="ANTES"}]));
        Assert.Contains("Parcial",libro.Worksheet("Conciliaciones").Cell(2,2).GetString());
        Assert.True(libro.Worksheet("Conciliaciones").Cell(2,8).IsEmpty());Assert.Equal("Sí",libro.Worksheet("Alfa").Cell(2,12).GetString());
    }

    [Fact]
    public void RespetaFiltrosLocalesYSeleccionSinDependerDeLaPagina()
    {
        var datos=Datos([new(){Id=1,Fecha=new(2026,9,1)},new(){Id=2,Fecha=new(2026,9,2)},new(){Id=3,Fecha=new(2026,9,2)}],
            [new(){Clave="A",Fecha=new(2026,9,2),Sucursal="0001"},new(){Clave="B",Fecha=new(2026,9,2),Sucursal="0002"},new(){Clave="C",Fecha=new(2026,9,1),Sucursal="0001"}]);
        var solicitud=datos.Solicitud with{FechaLiquidacion="2026-09-02",FechaAlfa="2026-09-02",SucursalAlfa="0001",SoloSeleccionados=true,DetallesSeleccionados=[2],MovimientosSeleccionados=["A","B"]};
        var filtrado=ConciliacionTarjetasService.FiltrarExportacion(datos.Comparacion,solicitud);
        Assert.Equal(2,Assert.Single(filtrado.Detalles).Id);Assert.Equal("A",Assert.Single(filtrado.Movimientos).Clave);
        Assert.Equal("Pendientes",ConciliacionTarjetasService.FiltroExportacion(solicitud with{Alcance=TarjetasExportacionAlcance.Pendientes}).Estado);
        Assert.Equal("Conciliados",ConciliacionTarjetasService.FiltroExportacion(solicitud with{Alcance=TarjetasExportacionAlcance.Conciliados}).Estado);
        Assert.Equal("Todos",ConciliacionTarjetasService.FiltroExportacion(solicitud).Estado);
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.FiltroExportacion(solicitud with{FechaAlfa="30/10/2026"}));
    }

    [Fact]
    public void ConsultaVaciaProduceCuatroHojasSinInventarFilasYCancelaAntesDeEntregarArchivo()
    {
        using var libro=Abrir(Datos([],[]));Assert.Equal(4,libro.Worksheets.Count);
        Assert.Equal("Estado",libro.Worksheet("Liquidacion").Cell(1,11).GetString());Assert.True(libro.Worksheet("Liquidacion").Cell(2,1).IsEmpty());
        using var cancelado=new CancellationTokenSource();cancelado.Cancel();
        Assert.Throws<OperationCanceledException>(()=>TarjetasExcelExporter.Crear(Datos([],[]),cancelado.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportarExigeSesionAutorizadaYPermisoAntesDeAccederALaBase(bool autenticado)
    {
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
        {
            var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
        }
        var consultoConexion=false;var sesion=new SessionDto();
        var servicio=new ConciliacionTarjetasService(
            Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>Conexion(),_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>autenticado,"IsAuthorizedForSession"=>true,_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(false)),
            Proxy<IAppEventService>((m,_)=>m.Name=="LogErrorAsync"?Task.FromResult("ENSAYO"):null),null!);
        string Conexion(){consultoConexion=true;throw new Exception("No debe consultar una conexión sin permiso");}
        var solicitud=new TarjetasExportar(new(1,new(2026,9,1),new(2026,9,30)));
        if(autenticado)await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.ExportarExcelAsync(solicitud));
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ExportarExcelAsync(solicitud));
        Assert.False(consultoConexion);
    }
}
