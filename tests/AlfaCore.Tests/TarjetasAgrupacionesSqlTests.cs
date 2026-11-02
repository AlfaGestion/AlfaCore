using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasAgrupacionesSqlTests
{
    [AgrupacionSqlFact]
    public async Task AnalisisRealEncuentraCuatroGruposSinInvocarModeloNiGrabarConciliaciones()
    {
        var conexion=Environment.GetEnvironmentVariable("TARJETAS_AGRUPACION_TEST_SQL")!;
        Assert.Equal("PRUEBAS02122025",new SqlConnectionStringBuilder(conexion).InitialCatalog);
        var importacion=long.Parse(Environment.GetEnvironmentVariable("TARJETAS_AGRUPACION_IMPORTACION")!);
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
        {
            var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
        }
        var sesion=new SessionDto();var llamadasIa=0;
        using var runtime=new CargaComprobantesArcaRuntime();
        var servicio=new ConciliacionTarjetasService(
            Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>conexion,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Sistemas",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!,
            Proxy<IConciliacionIaModelo>((_,_)=>{llamadasIa++;throw new InvalidOperationException("No debería enviarse información al modelo para este caso");}));
        var filtro=new TarjetasFiltro(importacion,new(2026,9,25),new(2026,10,2));
        var antes=await servicio.CompararAsync(filtro);
        var avances=new List<TarjetasProgresoAnalisis>();
        var analisis=await servicio.AnalizarIaAsync(filtro,criterios:new(),avance:a=>{avances.Add(a);return Task.CompletedTask;},guardar:false);
        Assert.Equal(new[]{1,2,3,4},avances.Select(a=>a.Paso).Distinct());
        Assert.All(avances,a=>Assert.False(a.Finalizado));
        Assert.Contains(avances,a=>a.Paso==2&&a.Intentos>0);
        Assert.Equal(0,avances.Max(a=>a.TotalLotes));
        Assert.Equal(0,llamadasIa);Assert.Equal(4,analisis.Propuestas.Count);
        Assert.Equal(133,analisis.Propuestas.Sum(p=>p.Movimientos.Count));Assert.Equal(7,analisis.Propuestas.Sum(p=>p.Detalles.Count));
        Assert.Equal(4130191.29m,analisis.Propuestas.Sum(p=>p.Detalles.Sum(d=>d.Importe)));
        var solicitud=new TarjetasConfirmarVarias(filtro,analisis.Propuestas.Select(p=>new TarjetasIaSeleccion(
            p.Detalles.Select(d=>d.Id).ToList(),p.Movimientos.Select(m=>new TarjetasSeleccion(m.Clave,m.Huella)).ToList(),p.Motivo)).ToList(),false,"");
        Assert.Equal(4,ConciliacionTarjetasService.PrepararConfirmaciones(antes,solicitud).Count);
        var despues=await servicio.CompararAsync(filtro);
        Assert.Equal(antes.Detalles.Select(d=>d.Id),despues.Detalles.Select(d=>d.Id));
        Assert.Equal(antes.Movimientos.Select(m=>m.Huella),despues.Movimientos.Select(m=>m.Huella));
    }
}
public sealed class AgrupacionSqlFactAttribute:FactAttribute
{
    public AgrupacionSqlFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_AGRUPACION_TEST_SQL"))
            ||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_AGRUPACION_IMPORTACION")))
            Skip="Requiere conexión autorizada a PRUEBAS02122025 e importación del caso de septiembre de 2026. Solo lecturas.";
    }
}
