using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasAutomaticasSqlTests
{
    [AutomaticasSqlFact]
    public async Task RecuperaLosCincoGruposDePatagoniaYSenalaAmbiguedadYVinculo13SinGrabar()
    {
        var conexion=Environment.GetEnvironmentVariable("TARJETAS_AUTO_TEST_SQL")!;
        Assert.Equal("PRUEBAS02122025",new SqlConnectionStringBuilder(conexion).InitialCatalog);
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
        {
            var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
        }
        var sesion=new SessionDto();var llamadasIa=0;using var runtime=new CargaComprobantesArcaRuntime();
        var servicio=new ConciliacionTarjetasService(
            Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>conexion,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Sistemas",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!,Proxy<IConciliacionIaModelo>((_,_)=>{llamadasIa++;throw new InvalidOperationException("No se debe enviar un caso ambiguo o ya vinculado al modelo");}));
        var filtro=new TarjetasFiltro(10008,new(2026,8,28),new(2026,10,2));
        var antes=await servicio.CompararAsync(filtro);
        Assert.Equal(11,antes.Detalles.Count);
        var r=await servicio.AnalizarIaAsync(filtro,criterios:new(){Dias=4,Automatico=true,ConsultarIa=true},guardar:false);
        Assert.Equal(0,llamadasIa);Assert.Equal(5,r.Propuestas.Count);Assert.Equal(8,r.Propuestas.Sum(p=>p.Detalles.Count));
        Assert.Equal(119,r.Propuestas.Sum(p=>p.Movimientos.Count));Assert.Equal(3453461.89m,r.Propuestas.Sum(p=>p.Detalles.Sum(d=>d.Importe)));
        Assert.Contains(r.Diagnosticos,d=>d.Tipo=="Ambiguo"&&d.Titulo.Contains("0386"));
        Assert.Contains(r.Diagnosticos,d=>d.Tipo=="YaConciliado"&&d.Vinculos.Contains(13));
        var despues=await servicio.CompararAsync(filtro);
        Assert.Equal(antes.Detalles.Select(d=>d.Id),despues.Detalles.Select(d=>d.Id));
        Assert.Equal(antes.Movimientos.OrderBy(m=>m.Clave,StringComparer.Ordinal).Select(m=>(m.Clave,m.Huella,m.Vinculo)),despues.Movimientos.OrderBy(m=>m.Clave,StringComparer.Ordinal).Select(m=>(m.Clave,m.Huella,m.Vinculo)));
    }
}
public sealed class AutomaticasSqlFactAttribute:FactAttribute
{
    public AutomaticasSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_AUTO_TEST_SQL")))Skip="Ensayo autorizado de lectura sobre los 11 pendientes de Patagonia en PRUEBAS02122025. No invoca IA externa ni graba análisis/conciliaciones.";}
}
