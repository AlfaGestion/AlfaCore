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
    public async Task AyudaAbreEnLaTareaIndicadaBuscaCambiaDeManualYVuelveSinOperar()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(DispatchProxy.Create<IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        var ia=DispatchProxy.Create<IAyudaIaService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)ia).Handler=(m,a)=>m.Name=="get_Disponible" ? true :
            Task.FromResult(new AyudaRespuesta("Revisá la cuenta corriente antes de cobrar.",
                [new AyudaFuente("cobranzas","registrar-una-cobranza","Cobranzas · Registrar una cobranza","")]));
        services.AddSingleton(ia);
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
