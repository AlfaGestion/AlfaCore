using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.Playwright;
using Xunit;

namespace AlfaCore.Tests;

public sealed class AyudaContextualTests
{
    [Theory]
    [InlineData("conciliacion","clasificacion")]
    [InlineData("cobranzas","retencion")]
    [InlineData("cobranzas","retenciones")]
    [InlineData("conciliacion","duplicados")]
    [InlineData("tarjetas","sucursal")]
    [InlineData("salon","agotado")]
    [InlineData("salon","comandera")]
    [InlineData("salon","cambio de turno")]
    [InlineData("mostrador","corte x")]
    [InlineData("mostrador","consumidor final")]
    [InlineData("mostrador","caja recaudadora")]
    public void ManualesIncluidosBuscanSinAcentosYNoExponenOtrosArchivos(string tema,string consulta)
    {
        var manual=AyudaManuales.Obtener(tema);
        Assert.NotEmpty(manual.Articulos);
        Assert.Equal(manual.Articulos.Count,manual.Articulos.Select(a=>a.Id).Distinct().Count());
        Assert.NotEmpty(AyudaManuales.Buscar(manual,consulta));
        Assert.Empty(AyudaManuales.Buscar(manual,"zzzzNoExisteEstaTarea"));
        Assert.Throws<ArgumentException>(()=>AyudaManuales.Obtener("../../appsettings.json"));
        Assert.DoesNotContain("<script>",AyudaManuales.Html("<script>alert('x')</script>"));
    }

    [Fact]
    public void CadaPantallaDocumentadaAbreUnArticuloQueExiste()
    {
        foreach(var (ruta,tema,articulo) in AyudaManuales.Pantallas)
        {
            var manual=AyudaManuales.Obtener(tema);
            Assert.NotEmpty(manual.Articulos);
            if(articulo is not null)
                Assert.True(manual.Articulos.Any(a=>a.Id==AyudaManuales.IdArticulo(articulo)),$"La ruta {ruta} apunta a «{articulo}», que no está en el manual {tema}.");
        }
        Assert.Equal("ventas",AyudaManuales.TemaParaRuta("http://localhost:5055/ALFANET/4283/ventas/carga-comprobantes/visor?x=1"));
        Assert.Equal("ver-comprobantes",AyudaManuales.SeccionParaRuta("http://localhost:5055/ALFANET/4283/ventas/carga-comprobantes/visor?x=1"));
        Assert.Equal("buscar-pendientes-y-filtrar",AyudaManuales.SeccionParaRuta("/ventas/carga-comprobantes"));
        Assert.Equal("libros-de-iva",AyudaManuales.SeccionParaRuta("/ALFANET/1/compras/libro-iva"));
        Assert.Equal("mayor-de-cuentas",AyudaManuales.SeccionParaRuta("/contabilidad/mayor?cuenta=11101"));
        Assert.Equal("autorizacion-de-tareas",AyudaManuales.SeccionParaRuta("/seguridad/autorizacion-tareas"));
        Assert.Equal("cobranzas",AyudaManuales.TemaParaRuta("/caja-bancos/cobranzas"));
        Assert.Equal("general",AyudaManuales.TemaParaRuta("/inicio"));
        Assert.Null(AyudaManuales.SeccionParaRuta("/inicio"));
        // Lo específico gana a lo general aunque la ruta corta esté contenida en la larga.
        Assert.Equal("compras",AyudaManuales.TemaParaRuta("/ALFANET/1/compras/articulos"));
        Assert.Equal("archivos",AyudaManuales.TemaParaRuta("/ALFANET/1/articulos"));
        Assert.Equal("auditoria",AyudaManuales.TemaParaRuta("/auditoria/usuarios"));
        Assert.Equal("seguridad",AyudaManuales.TemaParaRuta("/usuarios"));
        Assert.Equal("utilidades",AyudaManuales.TemaParaRuta("/ALFANET/1/configuracion"));
        Assert.Equal("conversaciones",AyudaManuales.TemaParaRuta("/conversaciones/configuracion"));
    }

    [Fact]
    public void CadaPestanaDeModuloYCadaConfiguracionTieneSuAyuda()
    {
        // Toda pantalla a la que se llega por una pestaña de módulo o desde el engranaje abre su sección de ayuda.
        var presets=File.ReadAllText(Path.Combine(RepoRoot(),"src","AlfaCore","Services","ModuleTopNavPresets.cs"));
        var rutas=System.Text.RegularExpressions.Regex.Matches(presets,@"\(""[^""]+"",\s*""[^""]+"",\s*""(/[^""]+)""\)")
            .Select(m=>m.Groups[1].Value)
            .Concat(ConfiguracionCatalogo.Entradas.Select(e=>e.RutaBase))
            .Where(r=>!r.StartsWith("/admin",StringComparison.Ordinal) && r!="/ayuda")
            .Distinct().ToList();
        Assert.NotEmpty(rutas);
        var sinAyuda=rutas.Where(r=>AyudaManuales.TemaParaRuta(r)=="general" && AyudaManuales.SeccionParaRuta(r) is null).ToList();
        Assert.True(sinAyuda.Count==0,"Pantallas sin ayuda: "+string.Join(", ",sinAyuda));
    }

    [Fact]
    public void ElBuscadorEncuentraEnTodosLosManuales()
    {
        var resultados=AyudaManuales.BuscarEnTodo("chequera","general");
        Assert.Contains(resultados,r=>r.Tema.Clave=="archivos");
        var propios=AyudaManuales.BuscarEnTodo("retencion","cobranzas");
        Assert.Equal("cobranzas",propios[0].Tema.Clave);
        Assert.Empty(AyudaManuales.BuscarEnTodo("zzzzNoExisteEstaTarea"));
        // Los títulos que solo agrupan o el índice de un manual no son tareas.
        Assert.DoesNotContain(AyudaManuales.Temas.SelectMany(t=>AyudaManuales.Obtener(t.Clave).Articulos),a=>a.Id=="indice" || a.Contenido.Length==0);
    }

    // "DE DONDE DOY DE ALTA LOS" no encontraba nada: se exigían todas las palabras (2026-11-26).
    [Fact]
    public void ElBuscadorIgnoraPalabrasVaciasYOrdenaPorCoincidencias()
    {
        Assert.Equal(new[]{"alta"},AyudaManuales.TerminosBusqueda("DE DONDE DOY DE ALTA LOS"));
        var resultados=AyudaManuales.BuscarEnTodo("de dónde doy de alta un cliente","general");
        Assert.NotEmpty(resultados);
        Assert.Equal("dar-de-alta-un-cliente",resultados[0].Articulo.Id);
        Assert.Empty(AyudaManuales.BuscarEnTodo("de donde los"));
        // La respuesta de la IA se muestra con formato (Markdown sin HTML): negritas y listas, sin asteriscos sueltos.
        Assert.Contains("<strong>Nuevo cliente</strong>",AyudaManuales.Html("Pulsá **Nuevo cliente**."),StringComparison.Ordinal);
        Assert.DoesNotContain("<script>",AyudaManuales.Html("<script>alert(1)</script>"),StringComparison.Ordinal);
        // El historial sólo devuelve fuentes que siguen en los manuales.
        var fuentes=AyudaIaService.Recuperar("como doy de alta un cliente","archivos");
        var guardadas=AyudaHistorialService.LeerFuentes($$"""[{"Tema":"{{fuentes[0].Tema}}","Articulo":"{{fuentes[0].Articulo}}","Titulo":"x"},{"Tema":"nada","Articulo":"y","Titulo":"z"}]""");
        Assert.Single(guardadas);
        Assert.Equal(fuentes[0].Id,guardadas[0].Id);
        // El campo no recibe el texto de vuelta mientras se escribe y busca al hacer una pausa.
        var dialogo=File.ReadAllText(Path.Combine(RepoRoot(),"src","AlfaCore","Components","Shared","AyudaContextualDialog.razor"));
        Assert.Contains("@oninput=\"AlEscribir\"",dialogo,StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"@_buscar\"",dialogo,StringComparison.Ordinal);
        // Seleccionar texto y soltar el mouse fuera de una ventana no la cierra (todas las ventanas).
        Assert.Contains("alfa-seleccion-sin-cerrar.js",File.ReadAllText(Path.Combine(RepoRoot(),"src","AlfaCore","Components","App.razor")),StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null && !File.Exists(Path.Combine(dir.FullName,"AlfaCore.sln")))dir=dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró AlfaCore.sln.");
    }

    [Fact]
    public async Task AyudaAbreEnLaTareaIndicadaBuscaCambiaDeManualYVuelveSinOperar()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(DispatchProxy.Create<IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        var ia=DispatchProxy.Create<IAyudaIaService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)ia).Handler=(m,a)=>m.Name=="get_Disponible" ? true :
            Task.FromResult(new AyudaRespuesta("Revisá la cuenta corriente antes de cobrar.",
                [new AyudaFuente("cobranzas","registrar-una-cobranza","Cobranzas · Registrar una cobranza","")]));
        services.AddSingleton(ia);
        // Historial de la ayuda: vacío y sin guardar nada.
        var historial=DispatchProxy.Create<IAyudaHistorialService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)historial).Handler=(m,a)=>m.Name switch {
            "ListarAsync"=>Task.FromResult<IReadOnlyList<AyudaConversacionResumen>>([]),
            "CargarAsync"=>Task.FromResult<IReadOnlyList<AyudaTurnoGuardado>>([]),
            "DiasAsync"=>Task.FromResult(60),
            _=>Task.CompletedTask };
        services.AddSingleton(historial);
        var capture=new Capture();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var cerrados=0;string html="",chatHtml="";
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var root=await renderer.RenderComponentAsync<AyudaContextualDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Open"]=true,["Tema"]="cobranzas",["SeccionInicial"]="registrar-una-cobranza",
                ["OnClose"]=EventCallback.Factory.Create(new object(),()=>cerrados++) }));
            html=root.ToHtmlString();
            Assert.Contains("Registrar una cobranza",System.Net.WebUtility.HtmlDecode(html));
            Assert.Contains("saldo actual",System.Net.WebUtility.HtmlDecode(html),StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Ver manual completo",html);
            var dialog=capture.Dialog!;
            void Cambiar(string metodo,string valor) {
                typeof(AyudaContextualDialog).GetMethod(metodo,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,[valor]);
                typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null);
            }
            Cambiar("Buscar","zzzzNoExisteEstaTarea");
            Assert.Contains("No encontramos esa explicaci",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            Cambiar("CambiarTema","conciliacion");Cambiar("Elegir","importar-extractos");
            Assert.Contains("Importar extractos",root.ToHtmlString());
            Assert.Contains("Archivo Excel",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            typeof(AyudaContextualDialog).GetMethod("AbrirChat",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null);
            Assert.Contains("Tu pregunta",root.ToHtmlString());
            var chat=capture.Chat!;
            typeof(AyudaChatPanel).GetField("_pregunta",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(chat,"¿Cómo verifico una cobranza?");
            await (Task)typeof(AyudaChatPanel).GetMethod("Enviar",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(chat,null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(chat,null);
            Assert.Contains("Revisá la cuenta corriente",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            chatHtml=root.ToHtmlString();
            await chat.OnFuente.InvokeAsync(new AyudaFuente("cobranzas","registrar-una-cobranza","Registrar una cobranza",""));
            Assert.Contains("Registrar una cobranza",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            await dialog.OnClose.InvokeAsync();Assert.Equal(1,cerrados);
        });
        if(CargaComprobantesBrowserFactAttribute.BrowserPath is null)return;
        var repo=new DirectoryInfo(AppContext.BaseDirectory);
        while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"AlfaCore.sln")))repo=repo.Parent;
        Assert.NotNull(repo);
        var css=File.ReadAllText(Path.Combine(repo!.FullName,"src/AlfaCore/wwwroot/app.css"))+
            File.ReadAllText(Path.Combine(repo.FullName,"src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"))+
            File.ReadAllText(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/css/alfacore-design.css"));
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true,ExecutablePath=CargaComprobantesBrowserFactAttribute.BrowserPath});
        var page=await browser.NewPageAsync();
        foreach(var vista in new[]{("contextual",html),("chat",chatHtml)})
        foreach(var width in new[]{2048,1440,1024,640}) {
            await page.SetViewportSizeAsync(width,768);
            await page.SetContentAsync($"<html><head><style>{css}</style><style>body{{margin:0}}*{{box-sizing:border-box}}</style></head><body>{vista.Item2}</body></html>");
            var dialog=page.GetByRole(AriaRole.Dialog);
            Assert.True(await dialog.IsVisibleAsync());
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth<=innerWidth"));
            Assert.True(await page.GetByRole(AriaRole.Button,new(){Name="Volver a la operación",Exact=true}).EvaluateAsync<bool>("e=>{const r=e.getBoundingClientRect();return r.bottom<=innerHeight&&r.right<=innerWidth;}"));
            if(vista.Item1=="chat")
            {
                Assert.Equal("",await page.GetByRole(AriaRole.Textbox,new(){Name="Tu pregunta",Exact=true}).InputValueAsync());
                Assert.True(await page.GetByRole(AriaRole.Button,new(){Name="Preguntar",Exact=true}).EvaluateAsync<bool>("e=>{const r=e.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight&&r.right<=innerWidth;}"));
            }
            var path=Path.Combine(repo.FullName,$"artifacts/ayuda/{vista.Item1}-{width}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);await page.ScreenshotAsync(new(){Path=path});
        }
    }
    private sealed class Capture:IComponentActivator
    {
        public AyudaContextualDialog? Dialog {get;private set;}
        public AyudaChatPanel? Chat {get;private set;}
        public IComponent CreateInstance(Type type) { var c=(IComponent)Activator.CreateInstance(type)!;if(c is AyudaContextualDialog dialog)Dialog=dialog;if(c is AyudaChatPanel chat)Chat=chat;return c; }
    }
}
