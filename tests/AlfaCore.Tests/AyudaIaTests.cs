using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class AyudaIaTests
{
    [Theory]
    [InlineData("/ALFANET/106/caja-bancos/conciliacion-bancaria","conciliacion")]
    [InlineData("/ALFANET/106/caja-bancos/cobranzas","cobranzas")]
    [InlineData("/ALFANET/106/consultas?directo=1","consultas")]
    [InlineData("/ALFANET/106/clientes","general")]
    public void AyudaGlobalEligeLaGuiaSegunLaRuta(string ruta,string tema)
        => Assert.Equal(tema,AyudaManuales.TemaParaRuta(ruta));

    [Fact]
    public void RecuperaTareasYRechazaFuentesInventadas()
    {
        var fuentes=AyudaIaService.Recuperar("¿Cómo pagar varias facturas con un depósito?","cobranzas");
        Assert.Contains(fuentes,f=>f.Tema=="cobranzas" && f.Titulo.Contains("varias facturas"));
        Assert.Empty(AyudaIaService.Recuperar("zzzzsincoincidencias","cobranzas"));
        using var doc=JsonDocument.Parse("""{"respuesta":"Hacé clic en un botón inventado","fuentes":["archivo/no-existe"]}""");
        var respuesta=AyudaIaService.ValidarRespuesta(doc.RootElement,fuentes);
        Assert.Empty(respuesta.Fuentes);Assert.Equal(AyudaIaService.SinDocumentacion,respuesta.Texto);
    }

    [Fact]
    public async Task ChatEnviaSoloGuiasEHistorialAcotadoValidaFuentesYMideConsumo()
    {
        var h=new Handler();var f=new Fixture(h);
        var historia=Enumerable.Range(0,9).Select(i=>new AyudaTurno("Pregunta "+i,"Respuesta "+i)).ToArray();
        var respuesta=await f.Service.PreguntarConClaveAsync("¿Cómo pagar varias facturas juntas?","cobranzas",historia,"clave-ensayo","modelo-ensayo",default);
        Assert.NotEmpty(respuesta.Fuentes);Assert.Equal("Revisá las facturas seleccionadas.",respuesta.Texto);
        Assert.Equal(1,f.Consumos);Assert.Equal(0,f.Errores);
        using var payload=JsonDocument.Parse(h.Payload!);
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
        Assert.False(payload.RootElement.TryGetProperty("tools",out _));
        var datos=JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal(4,datos.RootElement.GetProperty("historial").GetArrayLength());
        Assert.NotEmpty(datos.RootElement.GetProperty("documentos").EnumerateArray());
        Assert.DoesNotContain("Password",h.Payload!);Assert.DoesNotContain("clave-ensayo",h.Payload!);
    }

    [Fact]
    public async Task SinDocumentacionNoLlamaAlProveedorYSinLoginNoPermiteConsulta()
    {
        var h=new Handler();var f=new Fixture(h);
        var respuesta=await f.Service.PreguntarConClaveAsync("zzzzsincoincidencias","cobranzas",[],"clave","modelo",default);
        Assert.Empty(respuesta.Fuentes);Assert.Equal(0,h.Llamadas);
        f.Autorizado=false;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.PreguntarConClaveAsync("saldo cliente","cobranzas",[],"clave","modelo",default));
        Assert.Equal(0,h.Llamadas);
    }

    [Fact]
    public async Task ErroresSeRegistranYLaRespuestaNoCruzaSesiones()
    {
        var h=new Handler {Estado=HttpStatusCode.TooManyRequests};var f=new Fixture(h);
        var ex=await Assert.ThrowsAsync<AppUserFacingException>(()=>f.Service.PreguntarConClaveAsync("saldo cliente","cobranzas",[],"clave","modelo",default));
        Assert.Equal("TEST-AYUDA",ex.ErrorCode);Assert.Equal(1,f.Errores);Assert.Equal(0,f.Consumos);
        h.Estado=HttpStatusCode.OK;h.AntesDeResponder=()=>f.Sesion=new SessionDto();
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Service.PreguntarConClaveAsync("saldo cliente","cobranzas",[],"clave","modelo",default));
        Assert.Equal(0,f.Consumos);
        using var ct=new CancellationTokenSource();ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Service.PreguntarConClaveAsync("saldo cliente","cobranzas",[],"clave","modelo",ct.Token));
    }

    [Fact]
    public async Task CancelarConsultaEnCursoInterrumpeHttpSinRegistrarError()
    {
        var h=new Handler {EsperarCancelacion=true};var f=new Fixture(h);
        using var ct=new CancellationTokenSource();
        var tarea=f.Service.PreguntarConClaveAsync("saldo cliente","cobranzas",[],"clave","modelo",ct.Token);
        await h.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>tarea);
        Assert.Equal(0,f.Errores);Assert.Equal(0,f.Consumos);
    }

    private sealed class Fixture
    {
        public SessionDto Sesion=new();public bool Autorizado=true;public int Consumos,Errores;
        public AyudaIaService Service {get;}
        public Fixture(Handler handler)
        {
            Service=new(Proxy<IHttpClientFactory>((_,_)=>new HttpClient(handler,false)),
                Proxy<IIaUsoRecorder>((m,a)=>{if(m.Name=="Registrar"){Assert.Equal(IaUsoFunciones.Ayuda,a![0]);Consumos++;}return null;}),
                Proxy<ISessionService>((m,_)=>m.Name=="GetActiveSession"?Sesion:null),
                Proxy<IAppUserSessionService>((m,_)=>m.Name switch {"get_IsAuthenticated" or "IsAuthorizedForSession"=>Autorizado,"GetCurrentUserName"=>"Alberto",_=>null}),
                Proxy<IAppEventService>((m,_)=>{Errores++;return Task.FromResult("TEST-AYUDA");}));
        }
    }
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }
    private sealed class Handler:HttpMessageHandler
    {
        public string? Payload;public int Llamadas;public HttpStatusCode Estado=HttpStatusCode.OK;public Action? AntesDeResponder;
        public bool EsperarCancelacion;public TaskCompletionSource Iniciada=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Llamadas++;Payload=await request.Content!.ReadAsStringAsync(ct);Iniciada.TrySetResult();
            if(EsperarCancelacion)await Task.Delay(Timeout.Infinite,ct);
            using var payload=JsonDocument.Parse(Payload);
            using var datos=JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var id=datos.RootElement.GetProperty("documentos")[0].GetProperty("id").GetString();
            AntesDeResponder?.Invoke();
            return new(Estado){Content=new StringContent(JsonSerializer.Serialize(new {model="modelo-ensayo",usage=new{prompt_tokens=50,completion_tokens=20},
                choices=new[]{new {finish_reason="stop",message=new {content=JsonSerializer.Serialize(new {respuesta="Revisá las facturas seleccionadas.",fuentes=new[]{id}})}}}}),Encoding.UTF8,"application/json")};
        }
    }
}
