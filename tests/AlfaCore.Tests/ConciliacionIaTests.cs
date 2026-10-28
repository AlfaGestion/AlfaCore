using System.Net;
using System.Reflection;
using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace AlfaCore.Tests;

[Collection("Conciliacion SQL")]
public sealed class ConciliacionIaTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }
    private static ConciliacionIaContexto Contexto()=>new(new("BANCO",new(2026,10,1),new(2026,10,31)),new(3,1,true,false),
        [new(){Id=1,Fecha=new(2026,10,10),Importe=100,Huella="B1",Detalle="Cliente"},new(){Id=2,Fecha=new(2026,10,10),Importe=-25,Huella="B2",Detalle="Comisión"}],
        [new(){Periodo="2026",Mes=10,Asiento=1,Secuencia=1,Fecha=new(2026,10,10),Importe=60,Moneda="1",Huella="A1"},
         new(){Periodo="2026",Mes=10,Asiento=1,Secuencia=2,Fecha=new(2026,10,11),Importe=40,Moneda="1",Huella="A2"}],
        [new("GASTO","Gastos Bancarios")]);

    [Fact]
    public void ValidaGruposImportesFechasMonedasIndicesYConflictosSinConfiarEnElModelo()
    {
        var c=Contexto();
        const string valido="""{"grupos":[{"banco":[0],"alfa":[0,1],"confianza":"Alta","motivo":"Pago de dos documentos"}],"orientaciones":[{"banco":1,"categoria":"Comision","cuenta":"GASTO","motivo":"Revisar cargo del banco"}]}""";
        var v=ConciliacionBancariaService.ValidarRespuestaIa(c,valido);
        Assert.Equal(2,Assert.Single(v.Propuestas).Alfa.Count);Assert.Equal(0,v.Descartadas);
        Assert.Equal("GASTO",Assert.Single(v.Orientaciones).Cuenta);
        foreach(var invalido in new[]{valido.Replace("[0,1]","[0,0]"),valido.Replace("[0,1]","[0,99]"),valido.Replace("[0,1]","[0]"),valido.Replace("[0],\"alfa\"","[1],\"alfa\"")})
            Assert.Empty(ConciliacionBancariaService.ValidarRespuestaIa(c,invalido).Propuestas);
        c.Alfa[0].Moneda="2";Assert.Empty(ConciliacionBancariaService.ValidarRespuestaIa(c,valido).Propuestas);c.Alfa[0].Moneda="1";
        c.Alfa[0].Fecha=new(2026,10,20);Assert.Empty(ConciliacionBancariaService.ValidarRespuestaIa(c,valido).Propuestas);c.Alfa[0].Fecha=new(2026,10,10);
        c.Alfa[0].Legacy=true;Assert.Empty(ConciliacionBancariaService.ValidarRespuestaIa(c,valido).Propuestas);c.Alfa[0].Legacy=false;
        Assert.Empty(ConciliacionBancariaService.ValidarRespuestaIa(c with{Criterios=new(3,1,true,true)},valido).Propuestas);
        c.Banco[0].Referencia=" REF ";foreach(var a in c.Alfa)a.Referencia="REF";
        Assert.Single(ConciliacionBancariaService.ValidarRespuestaIa(c with{Criterios=new(3,1,true,true)},valido).Propuestas);
        c.Alfa[1].Importe=40.22m;Assert.Equal(-0.22m,Assert.Single(ConciliacionBancariaService.ValidarRespuestaIa(c,valido).Propuestas).Diferencia);
        Assert.Empty(ConciliacionBancariaService.ValidarRespuestaIa(c with{Criterios=new(3,0.1m,true,false)},valido).Propuestas);
        var repetidos=new ConciliacionIaSalida{Grupos=[new(){Banco=[0],Alfa=[0,1],Confianza="Alta",Motivo="Uno"},new(){Banco=[0],Alfa=[0,1],Confianza="Alta",Motivo="Otro"}]};
        var conflictos=ConciliacionBancariaService.ValidarRespuestaIa(c,JsonSerializer.Serialize(repetidos));Assert.Empty(conflictos.Propuestas);Assert.Equal(2,conflictos.Descartadas);
        Assert.Empty(ConciliacionBancariaService.ValidarRespuestaIa(c,valido.Replace("GASTO","CLIENTE_INVENTADO")).Orientaciones);
        Assert.Throws<InvalidOperationException>(()=>ConciliacionBancariaService.ValidarRespuestaIa(c,"SQL UPDATE tabla"));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Estado=HttpStatusCode.OK;
        public string Finish="stop";
        public string? Refusal;
        public string? Payload;
        public Action? AlResponder;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Assert.Equal("https://api.openai.com/v1/chat/completions",request.RequestUri!.ToString());
            Assert.Equal("Bearer",request.Headers.Authorization!.Scheme);
            Payload=await request.Content!.ReadAsStringAsync(ct);
            AlResponder?.Invoke();
            return new(Estado){Content=new StringContent(JsonSerializer.Serialize(new{model="modelo-ensayo",usage=new{prompt_tokens=10,completion_tokens=2},
                choices=new[]{new{finish_reason=Finish,message=new{content="{\"grupos\":[],\"orientaciones\":[]}",refusal=Refusal}}}}))};
        }
    }
    [Fact]
    public async Task TransporteUsaJsonSinGuardarYRegistraConsumoSoloEnLaSesionOriginal()
    {
        var handler=new Handler();var sesion=new SessionDto();var consumos=0;
        var modelo=new ConciliacionIaModelo(Proxy<IHttpClientFactory>((_,_)=>new HttpClient(handler,false)),Proxy<IIaUsoRecorder>((m,a)=>{Assert.Equal(IaUsoFunciones.Conciliacion,a![0]);consumos++;return null;}),
            Proxy<ISessionService>((_,_)=>sesion));
        var respuesta=await modelo.AnalizarConClaveAsync(Contexto(),"modelo-ensayo","clave-de-ensayo",default);
        Assert.Equal("modelo-ensayo",respuesta.Modelo);Assert.Equal(1,consumos);
        using var p=JsonDocument.Parse(handler.Payload!);Assert.False(p.RootElement.GetProperty("store").GetBoolean());
        var formato=p.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema",formato.GetProperty("type").GetString());
        Assert.True(formato.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        var esquema=formato.GetProperty("json_schema").GetProperty("schema");
        Assert.False(esquema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[]{"grupos","orientaciones"},esquema.GetProperty("required").EnumerateArray().Select(v=>v.GetString()));
        var grupo=esquema.GetProperty("properties").GetProperty("grupos").GetProperty("items");
        Assert.Equal("integer",grupo.GetProperty("properties").GetProperty("alfa").GetProperty("items").GetProperty("type").GetString());
        Assert.False(grupo.GetProperty("additionalProperties").GetBoolean());
        handler.Finish="length";await Assert.ThrowsAsync<InvalidOperationException>(()=>modelo.AnalizarConClaveAsync(Contexto(),"modelo-ensayo","clave-de-ensayo",default));
        Assert.Equal(2,consumos);
        handler.Estado=HttpStatusCode.TooManyRequests;await Assert.ThrowsAsync<InvalidOperationException>(()=>modelo.AnalizarConClaveAsync(Contexto(),"modelo-ensayo","clave-de-ensayo",default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>modelo.AnalizarConClaveAsync(Contexto(),"modelo-ensayo","",default));
        handler.Estado=HttpStatusCode.OK;handler.Finish="stop";handler.AlResponder=()=>sesion=new();
        await Assert.ThrowsAsync<OperationCanceledException>(()=>modelo.AnalizarConClaveAsync(Contexto(),"modelo-ensayo","clave-de-ensayo",default));
        Assert.Equal(2,consumos);
    }

    [Fact]
    public async Task TransporteDeTarjetasUsaBrutosSinGenerarLtNiEnviarOriginal()
    {
        var handler=new Handler();var sesion=new SessionDto();
        var modelo=new ConciliacionIaModelo(Proxy<IHttpClientFactory>((_,_)=>new HttpClient(handler,false)),
            Proxy<IIaUsoRecorder>((_,_)=>null),Proxy<ISessionService>((_,_)=>sesion));
        await modelo.AnalizarConClaveAsync(Contexto() with{EsTarjetas=true},"modelo-ensayo","clave-de-ensayo",default);
        using var payload=JsonDocument.Parse(handler.Payload!);
        var instrucciones=payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.Contains("BRUTOS",instrucciones);Assert.Contains("No generes LT",instrucciones);
        Assert.Contains("no reemplaza la fecha de operación",instrucciones);
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
        using var datos=JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal(JsonValueKind.Object,datos.RootElement.ValueKind);
        Assert.Equal("indice",datos.RootElement.GetProperty("columnasBanco")[0].GetString());
        Assert.Equal(0,datos.RootElement.GetProperty("banco")[0][0].GetInt32());
        Assert.Equal(100,datos.RootElement.GetProperty("banco")[0][2].GetDecimal());
    }

    [Fact]
    public void ConsultaGrandeConservaTodosLosRegistrosConMenosContexto()
    {
        var c=Contexto() with{
            Banco=Enumerable.Range(0,845).Select(i=>new ConciliacionFilaBanco{Id=i+1,Fecha=new(2025,9,1),Importe=i+100,Detalle="Cupón; cantidad 1; pago 2025-09-05",Referencia=$"Cupón {i+10}; lote 422; liquidación 507788",Categoria="Tarjetas"}).ToList(),
            Alfa=Enumerable.Range(0,1354).Select(i=>new ConciliacionFilaAlfa{Secuencia=i,Fecha=new(2025,9,1),Importe=i+10000,Detalle="Cob.Factura CLIENTE SINTÉTICO DEMOSTRACIÓN",Referencia="Cupón 1; presentación 1424; sucursal 0003",Comprobante="CBCT 0003-00000001 X"}).ToList()};
        var compacto=ConciliacionIaModelo.DatosCompactos(c);
        var anterior=JsonSerializer.Serialize(new{
            banco=c.Banco.Select((b,i)=>new{indice=i,fecha=b.Fecha.ToString("yyyy-MM-dd"),b.Importe,detalle=b.Detalle,referencia=b.Referencia,b.Categoria}),
            alfa=c.Alfa.Select((a,i)=>new{indice=i,fecha=a.Fecha.ToString("yyyy-MM-dd"),a.Importe,detalle=a.Detalle,referencia=a.Referencia,a.Comprobante})});
        Assert.True(compacto.Length<anterior.Length*0.75);
        using var datos=JsonDocument.Parse(compacto);
        Assert.Equal(845,datos.RootElement.GetProperty("banco").GetArrayLength());
        Assert.Equal(1354,datos.RootElement.GetProperty("alfa").GetArrayLength());
        Assert.Equal(1353,datos.RootElement.GetProperty("alfa")[1353][0].GetInt32());
        Assert.Contains("SINTÉTICO",compacto);
    }

    [Fact]
    public void ErrorDelProveedorConservaDiagnosticoEnAuxErrSinExponerCredencialesEnPantalla()
    {
        var error=ConciliacionIaModelo.ErrorApi(HttpStatusCode.BadRequest,
            """{"error":{"code":"context_length_exceeded","param":"messages","type":"invalid_request_error","message":"maximum context length exceeded sk-secreto"}}""",
            "modelo-ensayo",845,1354,"sk-secreto");
        Assert.Contains("supera la capacidad",error.Message);
        Assert.DoesNotContain("sk-secreto",error.ToString());
        Assert.Contains("code=context_length_exceeded",error.InnerException!.Message);
        Assert.Contains("modelo=modelo-ensayo",error.InnerException.Message);
        Assert.Contains("param=messages",error.InnerException.Message);
        var invalido=ConciliacionIaModelo.ErrorApi(HttpStatusCode.BadRequest,"HTML ilegible", "ensayo",1,1,"");
        Assert.Contains("HTTP 400",invalido.Message);
        Assert.DoesNotContain("HTML ilegible",invalido.Message);
    }

    [Theory]
    [InlineData("{\"grupos\":{},\"orientaciones\":[]}")]
    [InlineData("{\"grupos\":[{\"banco\":[\"0\"],\"alfa\":[0],\"confianza\":\"Alta\",\"motivo\":\"CLIENTE PRIVADO\"}],\"orientaciones\":[]}")]
    [InlineData("{\"error\":\"CLIENTE PRIVADO\"}")]
    public void FormatoIncompatibleDejaDiagnosticoSinGuardarContenidoNiAceptarListasOmitidas(string json)
    {
        var error=Assert.Throws<InvalidOperationException>(()=>ConciliacionBancariaService.ValidarRespuestaIa(Contexto(),json));
        Assert.Contains("formato incompatible",error.Message);
        Assert.IsType<FormatException>(error.InnerException);
        Assert.Contains("SHA256=",error.InnerException!.Message);Assert.Contains("longitud=",error.InnerException.Message);
        Assert.DoesNotContain("CLIENTE PRIVADO",error.ToString());
    }
    [Fact]
    public async Task RechazoExplicitoDelModeloNoSeInterpretaComoJsonNiSeMuestraContenido()
    {
        var handler=new Handler{Refusal="CONTENIDO PRIVADO"};var sesion=new SessionDto();
        var modelo=new ConciliacionIaModelo(Proxy<IHttpClientFactory>((_,_)=>new HttpClient(handler,false)),Proxy<IIaUsoRecorder>((_,_)=>null),Proxy<ISessionService>((_,_)=>sesion));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>modelo.AnalizarConClaveAsync(Contexto(),"modelo-ensayo","clave-de-ensayo",default));
        Assert.Contains("selección manual",error.Message);Assert.DoesNotContain("CONTENIDO PRIVADO",error.ToString());
    }

    private sealed class IaLiveFactAttribute : FactAttribute
    {
        public IaLiveFactAttribute() { if(Environment.GetEnvironmentVariable("CONCILIACION_IA_LIVE")!="1")Skip="La prueba real de OpenAI se habilita explícitamente con CONCILIACION_IA_LIVE=1."; }
    }
    [IaLiveFact]
    public async Task OpenAiRealProponeUnPagoAgrupadoConDatosSinteticosSinConsultarLaBase()
    {
        AlfaCore.Configuration.DotEnvLoader.LoadIfPresent(AppContext.BaseDirectory);
        var c=Contexto();c.Banco[0].Detalle="Transferencia Cliente ENSAYO, pago de dos documentos, referencia PRUEBA-IA";c.Banco[0].Referencia="PRUEBA-IA";
        foreach(var a in c.Alfa) { a.Detalle="Cobranza Cliente ENSAYO";a.Referencia="PRUEBA-IA";a.Fecha=c.Banco[0].Fecha;a.Comprobante=$"CB ENSAYO-{a.Secuencia}"; }
        var sesion=new SessionDto();var consumos=0;
        var modelo=new ConciliacionIaModelo(Proxy<IHttpClientFactory>((_,_)=>new HttpClient()),Proxy<IIaUsoRecorder>((_,_)=>{consumos++;return null;}),Proxy<ISessionService>((_,_)=>sesion));
        var respuesta=await modelo.AnalizarAsync(c,"",default);
        var validas=ConciliacionBancariaService.ValidarRespuestaIa(c,respuesta.Json);
        Assert.Contains(validas.Propuestas,p=>p.Banco.Count==1&&p.Banco[0].Id==1&&p.Alfa.Count==2);
        Assert.Equal(1,consumos);
    }

    [IaLiveFact]
    public async Task OpenAiRealAcepta845LiquidacionesY1354MovimientosSinteticosSinCoincidencias()
    {
        AlfaCore.Configuration.DotEnvLoader.LoadIfPresent(AppContext.BaseDirectory);
        var contexto=Contexto() with{EsTarjetas=true,
            Banco=Enumerable.Range(0,845).Select(i=>new ConciliacionFilaBanco{Id=i+1,Fecha=new(2025,9,1),Importe=i+100,Detalle="Cupón; cantidad 1; pago 2025-09-05",Referencia=$"Cupón {i+10}; lote 422; liquidación 507788",Categoria="Tarjetas"}).ToList(),
            Alfa=Enumerable.Range(0,1354).Select(i=>new ConciliacionFilaAlfa{Secuencia=i,Fecha=new(2026,9,1),Importe=i+10000,Detalle="Cob.Factura CLIENTE SINTÉTICO DEMOSTRACIÓN",Referencia="Cupón 1; presentación 1424; sucursal 0003",Comprobante="CBCT 0003-00000001 X"}).ToList()};
        var sesion=new SessionDto();var consumos=0;
        var modelo=new ConciliacionIaModelo(Proxy<IHttpClientFactory>((_,_)=>new HttpClient()),Proxy<IIaUsoRecorder>((_,_)=>{consumos++;return null;}),Proxy<ISessionService>((_,_)=>sesion));
        var respuesta=await modelo.AnalizarAsync(contexto,"",default);
        var resultado=ConciliacionBancariaService.ValidarRespuestaIa(contexto,respuesta.Json);
        Assert.Empty(resultado.Propuestas);Assert.Equal(1,consumos);
    }

    private sealed class ModeloFalso : IConciliacionIaModelo
    {
        public int Llamadas;
        public Task<ConciliacionIaRespuesta> AnalizarAsync(ConciliacionIaContexto c,string modelo,CancellationToken ct) {
            Llamadas++;Assert.True(c.Banco.Count>50);
            var b=c.Banco.ToList().FindIndex(b=>b.Importe==-123.45m);
            return Task.FromResult(new ConciliacionIaRespuesta("modelo-ensayo",JsonSerializer.Serialize(new ConciliacionIaSalida{Grupos=[new(){Banco=[b],Alfa=[0,1],Confianza="Media",Motivo="Dos renglones de una registración por el mismo total"}]})));
        }
    }
    [ConciliacionSqlFact]
    public async Task AnalizaTodoElFiltroAuditaYConfirmaUnGrupoRevalidandoSesionHuellasCriteriosConRollback()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var sesion=new SessionDto();var original=sesion;var modelo=new ModeloFalso();
        var service=new ConciliacionBancariaService(Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new(),modelo);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        var tag="ENSAYO_IA_"+Guid.NewGuid().ToString("N");Guid id=default;
        try {
            var cuenta=await cn.QueryFirstAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='DB' AND LTRIM(RTRIM(MONEDA))='1' AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0;",transaction:tx);
            var periodo=await cn.QuerySingleAsync<string>("SELECT PERIODO FROM MV_EJERCICIOS WHERE '20261018' BETWEEN [FECHA DESDE] AND [FECHA HASTA] AND ISNULL(CERRADO,0)=0;",transaction:tx);
            var asiento=await cn.ExecuteScalarAsync<int>("SELECT ISNULL(MAX([NUMERO ASIENTO]),0)+1000 FROM MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=10;",new{periodo},tx);
            var import=await cn.ExecuteScalarAsync<long>("""
                INSERT BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso,CuentaBanco)
                OUTPUT INSERTED.ImportacionId VALUES('Provincia',@tag,'',SYSUTCDATETIME(),'ENSAYO',0,0,'IMPORTED',@cuenta);
                INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe)
                VALUES(SCOPE_IDENTITY(),'Provincia','20261018',@tag,-123.45);
                """,new{tag,cuenta},tx);
            for(var i=0;i<51;i++)await cn.ExecuteAsync("INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe) VALUES(@import,'Provincia','20261018',@tag,@importe);",new{import,tag,importe=-10000-i},tx);
            await cn.ExecuteAsync("INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe) VALUES(@import,'Provincia','20261018',@tag,-35.17);",new{import,tag},tx);
            for(var i=1;i<=3;i++)await cn.ExecuteAsync("""
                INSERT MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,PERIODO,CABIMPORTE,TIPO_REG,UNEGOCIO)
                VALUES(@cuenta,@i,10,@asiento,'20261018',@tag,'CC','9999','99999999','X','H',@importe,'   1',@periodo,@importe,'','   1');
                """,new{cuenta,i,asiento,tag,periodo,importe=i==1 ? 100m : i==2 ? 23.45m : 35.17m},tx);
            var filtro=new ConciliacionCompararFiltro(cuenta,new(2026,10,18),new(2026,10,18),Buscar:tag);
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='50' WHERE CLAVE='CONCILIACION-IA-MAX-FILAS';",transaction:tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AnalizarIaEnConexionAsync(cn,tx,filtro,default));Assert.Equal(0,modelo.Llamadas);
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='300' WHERE CLAVE='CONCILIACION-IA-MAX-FILAS';",transaction:tx);
            var r=await service.AnalizarIaEnConexionAsync(cn,tx,filtro,default);id=r.Id;
            Assert.Equal(53,r.BancosAnalizados);Assert.Equal(3,r.AsientosAnalizados);Assert.Equal(2,r.Propuestas.Count);
            var propuesta=Assert.Single(r.Propuestas.Where(p=>p.Origen=="IA"));
            var regla=Assert.Single(r.Propuestas.Where(p=>p.Origen=="Reglas"));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ConciliacionIaAnalisis WHERE Id=@id;",new{id},tx));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_Conciliacion WHERE Operacion=@id;",new{id=propuesta.Id},tx));
            sesion=new();await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConfirmarIaEnTransaccionAsync(cn,tx,id,[propuesta.Id],false,"",default));sesion=original;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConfirmarIaEnTransaccionAsync(cn,tx,id,[Guid.NewGuid()],false,"",default));
            var dias=await cn.ExecuteScalarAsync<string>("SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE='CONCILIACION-DIAS-BANCO';",transaction:tx);
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR=@otro WHERE CLAVE='CONCILIACION-DIAS-BANCO';",new{otro=r.Criterios.Dias==0 ? "1" : "0"},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConfirmarIaEnTransaccionAsync(cn,tx,id,[propuesta.Id],false,"",default));
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR=@dias WHERE CLAVE='CONCILIACION-DIAS-BANCO';",new{dias},tx);
            await cn.ExecuteAsync("UPDATE MV_ASIENTOS SET DETALLE='CAMBIADO' WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento AND SECUENCIA=1;",new{periodo,asiento},tx);
            tx.Save("antes_del_lote");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConfirmarIaEnTransaccionAsync(cn,tx,id,[regla.Id,propuesta.Id],false,"",default));
            tx.Rollback("antes_del_lote"); // El wrapper público revierte la transacción completa ante cualquier fallo.
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_Conciliacion WHERE Operacion IN @operaciones;",new{operaciones=new[]{regla.Id,propuesta.Id}},tx));
            await cn.ExecuteAsync("UPDATE MV_ASIENTOS SET DETALLE=@tag WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento;",new{periodo,asiento,tag},tx);
            var ids=await service.ConfirmarIaEnTransaccionAsync(cn,tx,id,[propuesta.Id],false,"",default);
            Assert.Equal(ids,await service.ConfirmarIaEnTransaccionAsync(cn,tx,id,[propuesta.Id],false,"",default));
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ConciliacionAlfa WHERE ConciliacionId=@id;",new{id=ids[0]},tx));
        } finally { tx.Rollback(); }
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ConciliacionIaAnalisis WHERE Id=@id;",new{id}));
    }

    [Fact]
    public async Task DialogoMuestraExplicacionYGruposSinSeleccionAutomaticaEnTresAnchos()
    {
        var c=Contexto();var p=new ConciliacionIaPropuesta(Guid.NewGuid(),"IA","Media","Dos documentos del mismo cliente",c.Banco.Take(1).ToList(),c.Alfa,
            new(Guid.NewGuid(),c.Filtro,[],[]));
        var r=new ConciliacionIaResultado(Guid.NewGuid(),"modelo-ensayo",2,2,0,[p],[new(c.Banco[1],"Comision","GASTO","Revisar gasto del banco")]);
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>{var root=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.ConciliacionIaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Open",true},{"Resultado",r}}));return root.ToHtmlString();});
        Assert.Contains("Confirmar 0 propuestas",html);Assert.Contains("Dos documentos del mismo cliente",html);Assert.DoesNotContain(" checked",html);
        if(CargaComprobantesBrowserFactAttribute.BrowserPath is not { } browserPath)return;
        var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"src/AlfaCore/AlfaCore.csproj")))repo=repo.Parent;Assert.NotNull(repo);
        var css=await File.ReadAllTextAsync(Path.Combine(repo!.FullName,"src/AlfaCore/wwwroot/bootstrap/bootstrap.min.css"))+await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/app.css"))+
            await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/css/alfacore-design.css"))+await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"));
        using var pw=await Playwright.CreateAsync();await using var browser=await pw.Chromium.LaunchAsync(new(){Headless=true,ExecutablePath=browserPath});var page=await browser.NewPageAsync();
        foreach(var width in new[]{2048,1440,1024}) {
            await page.SetViewportSizeAsync(width,1000);await page.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style></head><body>{html}</body></html>");
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth<=window.innerWidth"));
            var button=page.GetByRole(AriaRole.Button,new(){Name="Confirmar 0 propuestas",Exact=true});Assert.True(await button.IsVisibleAsync());Assert.True(await button.IsDisabledAsync());
            var dir=Path.Combine(repo.FullName,"artifacts/conciliacion-ia");Directory.CreateDirectory(dir);await page.ScreenshotAsync(new(){Path=Path.Combine(dir,$"ia-{width}.png")});
        }
    }
}
