using System.Reflection;
using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraCargaProveedorUiTests
{
    private const BindingFlags Privado=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Campo(ComprasCarga pagina,string campo,object valor)=>typeof(ComprasCarga).GetField(campo,Privado)!.SetValue(pagina,valor);
    private static T Leer<T>(ComprasCarga pagina,string campo)=>(T)typeof(ComprasCarga).GetField(campo,Privado)!.GetValue(pagina)!;
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;}
    [Fact]
    public void NingunaPaginaDeshabilitaCamposConUnNombreSinArroba()
    {
        // disabled="Propiedad" (sin @) es texto literal: el campo queda siempre deshabilitado.
        var raiz=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../src/AlfaCore"));
        var literal=new System.Text.RegularExpressions.Regex(@"(?<![\w-])disabled=""[A-Za-z_!][\w.!]*""");
        var casos=Directory.GetFiles(raiz,"*.razor",SearchOption.AllDirectories)
            .Where(f=>!f.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar)&&!f.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar))
            .SelectMany(f=>literal.Matches(File.ReadAllText(f)).Select(m=>$"{Path.GetFileName(f)}: {m.Value}")).Where(c=>!c.EndsWith("disabled=\"disabled\"")).ToList();
        Assert.True(casos.Count==0,"Atributos disabled sin @: "+string.Join(" | ",casos));
    }
    [Fact]
    public async Task TeclearEnProveedorSinSeleccionNoReiniciaMotivoNiDatos()
    {
        var pagina=new ComprasCarga();var borrador=new CompraCargaBorrador{Motivo="42",Numero="123"};Campo(pagina,"_borrador",borrador);
        await (Task)typeof(ComprasCarga).GetMethod("ProveedorChanged",Privado)!.Invoke(pagina,[null])!;
        Assert.Equal("42",borrador.Motivo);Assert.Equal("123",borrador.Numero);
    }
    [Fact]
    public async Task UnRenderDelShellNoRecargaLaCompraNiBorraLaBusqueda()
    {
        var pagina=new ComprasCarga();var sesion=new SessionDto();
        typeof(ComprasCarga).GetProperty("Sesiones",Privado)!.SetValue(pagina,Proxy<ISessionService>((m,_)=>m.Name=="GetActiveSession"?sesion:null));
        typeof(ComprasCarga).GetProperty("Usuarios",Privado)!.SetValue(pagina,Proxy<IAppUserSessionService>((m,_)=>m.Name=="GetCurrentUserName"?"Operador":null));
        typeof(ComprasCarga).GetProperty("Nav",Privado)!.SetValue(pagina,new Navegacion());
        Campo(pagina,"_contexto",new CompraCargaContexto([],[],[],[],[],"1","42",null,null,null));Campo(pagina,"_buscarProveedor","proveedor buscado");
        var key=(string)typeof(ComprasCarga).GetProperty("ParametrosActuales",Privado)!.GetValue(pagina)!;Campo(pagina,"_rutaCargada",key);
        // No hay servicio SQL ni PageHeader inyectados: reconsultar o republicar el header rompería esta prueba.
        await (Task)typeof(ComprasCarga).GetMethod("OnParametersSetAsync",Privado)!.Invoke(pagina,null)!;
        await (Task)typeof(ComprasCarga).GetMethod("OnParametersSetAsync",Privado)!.Invoke(pagina,null)!;
        Assert.Equal("proveedor buscado",Leer<string>(pagina,"_buscarProveedor"));Assert.NotNull(Leer<CompraCargaContexto>(pagina,"_contexto"));
    }
    private sealed class Navegacion:NavigationManager
    {
        public Navegacion()=>Initialize("http://localhost/","http://localhost/compras/carga");
        protected override void NavigateToCore(string uri,bool forceLoad){}
    }
}
