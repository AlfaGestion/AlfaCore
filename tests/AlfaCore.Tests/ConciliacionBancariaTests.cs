using System.Reflection;
using AlfaCore.ConciliacionBancaria.Application.Abstractions;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Application.Services;
using AlfaCore.ConciliacionBancaria.Domain.Entities;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.ConciliacionBancaria.Infrastructure.Excel;
using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlfaCore.Tests;

[Collection("Conciliacion SQL")]
public sealed class ConciliacionBancariaTests(ITestOutputHelper output)
{
    private static ConciliacionArchivo Excel(string nombre, string[] headers, params object[][] rows)
    {
        using var wb = new XLWorkbook(); var ws=wb.AddWorksheet("Movimientos");
        for(var col=0;col<headers.Length;col++) ws.Cell(1,col+1).Value=headers[col];
        for(var row=0;row<rows.Length;row++)
            for(var col=0;col<rows[row].Length;col++) ws.Cell(row+2,col+1).Value=XLCellValue.FromObject(rows[row][col]);
        using var ms=new MemoryStream(); wb.SaveAs(ms); return new(nombre,ms.ToArray());
    }

    [Fact]
    public async Task ProvinciaSeDetectaPorContenidoAunqueElNombreNoIndiqueBanco()
    {
        var file=Excel("descarga.xlsx",["Número Secuencia","Fecha","Importe","Saldo","Descripción Extendida"],
            ["123","19/10/2026",210583.31m,210583.31m,"CR.TRAN. CLIENTE"]);
        var result=await new ConciliacionExtractoLector().LeerAsync(file);
        Assert.Equal(BankSource.Provincia,result.Banco);Assert.Equal(210583.31m,Assert.Single(result.Movimientos).Amount);
    }

    [Theory]
    [InlineData("-1883.40",-1883.40)]
    [InlineData("1.883,40",1883.40)]
    [InlineData("1,883.40",1883.40)]
    [InlineData("35000.00",35000)]
    public void ImportesNoSeMultiplicanPorSeparadorDecimal(string text,decimal expected)
        => Assert.Equal(expected,BaseExcelStatementParser.ParseAmount(text));

    [Fact]
    public async Task SantanderDetectaYConservaCentavosDeImportesTexto()
    {
        var file=Excel("descarga.xlsx",["Fecha","Descripción","Referencia","Caja de Ahorro","Cuenta Corriente","Saldo"],
            ["20/08/2026","Transferencia de mercado pago","06921806","-1883.40","","7533.62"]);
        var result=await new ConciliacionExtractoLector().LeerAsync(file);
        Assert.Equal(BankSource.Santander,result.Banco);var row=Assert.Single(result.Movimientos);
        Assert.Equal(-1883.40m,row.Amount);Assert.Equal(7533.62m,row.Balance);
    }

    [Fact]
    public async Task ArchivoAmbiguoPideBancoYConservaElArchivoParaReintentar()
    {
        var file=Excel("descarga.xlsx",["Fecha","Descripción","Importe"],["19/10/2026","Transferencia",15m]);
        var lector=new ConciliacionExtractoLector();
        Assert.Null((await lector.LeerAsync(file)).Banco);
        Assert.Single((await lector.LeerAsync(file,BankSource.Provincia)).Movimientos);
    }

    [Fact]
    public async Task MercadoPagoResuelveFechasRelativasConFechaDeDescargaYConservaTipo()
    {
        var file=Excel("mercadopago_2026-01-02.xlsx",["Fecha","Hora","Descripción","Tipo","Medio de pago","Monto"],
            ["Hoy","12:30","CLIENTE","Transferencia recibida","Dinero disponible",125.50m],
            ["31 de diciembre","10:00","CLIENTE","Pago recibido","Dinero disponible",100m]);
        var result=await new ConciliacionExtractoLector().LeerAsync(file);
        Assert.Equal(BankSource.MercadoPago,result.Banco);
        Assert.Equal(new DateTime(2026,1,2),result.Movimientos.First().MovementDate);
        Assert.Equal(new DateTime(2025,12,31),result.Movimientos.Last().MovementDate);
        Assert.Contains("Transferencia recibida",result.Movimientos.First().Description);
        var renombrado=await new ConciliacionExtractoLector().LeerAsync(file with { Nombre="renombrado.xlsx" });
        Assert.True(renombrado.RequiereFechaReferencia);Assert.Equal(BankSource.MercadoPago,renombrado.Banco);Assert.Empty(renombrado.Movimientos);
        var confirmado=await new ConciliacionExtractoLector().LeerAsync(file with { Nombre="renombrado.xlsx",FechaReferencia=new DateTime(2026,1,2) });
        Assert.Equal(result.Movimientos.Select(m=>m.MovementDate),confirmado.Movimientos.Select(m=>m.MovementDate));
    }

    [Fact]
    public async Task MercadoPagoRenombradoSugiereCreacionPeroRequiereConfirmarFecha()
    {
        var file=Excel("mercadopago 102026.xlsx",["Fecha","Descripción","Tipo","Medio de pago","Monto"],
            ["Hoy","CLIENTE","Transferencia recibida","Dinero disponible",100m],
            ["18 de octubre","CLIENTE","Pago","Dinero disponible",-20m]);
        using(var wb=new XLWorkbook(new MemoryStream(file.Contenido)))
        {
            wb.Properties.Created=new DateTime(2026,10,19,16,45,58);
            using var ms=new MemoryStream();wb.SaveAs(ms);file=file with{Contenido=ms.ToArray()};
        }
        var lector=new ConciliacionExtractoLector();var previo=await lector.LeerAsync(file);
        Assert.True(previo.RequiereFechaReferencia);Assert.Equal(new DateTime(2026,10,19),previo.FechaReferenciaSugerida);Assert.Empty(previo.Movimientos);
        var confirmado=await lector.LeerAsync(file with{FechaReferencia=previo.FechaReferenciaSugerida});
        Assert.False(confirmado.RequiereFechaReferencia);Assert.Equal(80m,confirmado.Movimientos.Sum(m=>m.Amount));
        Assert.Contains(confirmado.Movimientos,m=>m.MovementDate==new DateTime(2026,10,19));
        var corregido=await lector.LeerAsync(file with{FechaReferencia=new DateTime(2026,10,20)});
        Assert.Contains(corregido.Movimientos,m=>m.MovementDate==new DateTime(2026,10,20));
        var absoluto=Excel("sin_fecha.xlsx",["Fecha","Descripción","Tipo","Medio de pago","Monto"],
            ["18/10/2026","CLIENTE","Pago","Dinero disponible",10m]);
        Assert.False((await lector.LeerAsync(absoluto)).RequiereFechaReferencia);
    }

    [Fact]
    public async Task MercadoPagoOctubreRealSeProcesaConReferenciaConfirmadaSinRenombrar()
    {
        var ruta=@"C:\dev\Extractos\MercadoPago\mercadopago 102026.xlsx";
        if(!File.Exists(ruta))return;
        var file=new ConciliacionArchivo(Path.GetFileName(ruta),await File.ReadAllBytesAsync(ruta));
        var lector=new ConciliacionExtractoLector();var previo=await lector.LeerAsync(file);
        Assert.Equal(BankSource.MercadoPago,previo.Banco);Assert.True(previo.RequiereFechaReferencia);
        Assert.Equal(new DateTime(2026,10,19),previo.FechaReferenciaSugerida);
        var lectura=await lector.LeerAsync(file with{FechaReferencia=new DateTime(2026,10,19)});
        Assert.Equal(49,lectura.Movimientos.Count);Assert.Equal(-128534m,lectura.Movimientos.Sum(m=>m.Amount));
        Assert.Equal(new DateTime(2026,10,1),lectura.Movimientos.Min(m=>m.MovementDate));
        Assert.Equal(new DateTime(2026,10,19),lectura.Movimientos.Max(m=>m.MovementDate));
    }

    [Fact]
    public async Task FechaInvalidaConImporteNoDesapareceSilenciosamente()
    {
        var file=Excel("descarga.xlsx",["Número Secuencia","Fecha","Importe","Descripción"],["1","fecha incorrecta",200m,"Transferencia"]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new ConciliacionExtractoLector().LeerAsync(file));
    }

    [Fact]
    public void HuellaDistingueCuentaYConservaReferenciaAunqueCambieDescripcion()
    {
        var a=new NormalizedMovementDto { MovementDate=new(2026,10,19),Amount=100m,ExternalReference="ABC",Description="Corta" };
        var b=new NormalizedMovementDto { MovementDate=a.MovementDate,Amount=a.Amount,ExternalReference=a.ExternalReference,Description="Extendida" };
        Assert.Equal(ConciliacionBancariaService.Huella("1",BankSource.Provincia,a),ConciliacionBancariaService.Huella("1",BankSource.Provincia,b));
        Assert.NotEqual(ConciliacionBancariaService.Huella("1",BankSource.Provincia,a),ConciliacionBancariaService.Huella("2",BankSource.Provincia,a));
    }

    [Fact]
    public async Task ClienteNoIdentificadoNoSeProponeComoAnticipo()
    {
        MovementMatchProposal? proposal=null;
        var repo=Proxy<IBankStatementRepository>((method,args)=>method.Name switch {
            "GetCobranzasForMatchingAsync"=>Task.FromResult<IReadOnlyCollection<CobranzaCandidateDto>>([new() { MovementId=1,Amount=500,Description="SIN CLIENTE" }]),
            "GetClientAliasesAsync"=>Task.FromResult<IReadOnlyCollection<ClientAliasDto>>([]),
            "SearchClientByNameAsync"=>Task.FromResult<IReadOnlyCollection<ClientSearchResultDto>>([]),
            "SaveMatchAsync"=>Capture((MovementMatchProposal)args![0]!),_=>throw new InvalidOperationException(method.Name)
        });
        Task Capture(MovementMatchProposal value) { proposal=value; return Task.CompletedTask; }
        await new DeterministicMatchingService(repo,1m).GenerateMatchesAsync(1);
        Assert.Equal(MatchingType.NoIdentificado,proposal!.MatchingType);
    }

    [Theory]
    [InlineData("Transferencia recibida De tires ensenada sa / h1fc3849 - fac / 30715187953","30715187953","tires ensenada sa")]
    [InlineData("Pago a proveedores recibido Productos el molinero s.a. 34612073475 03 3239080","34612073475","Productos el molinero s.a")]
    [InlineData("Transf recibida cvu dif titular De angel nunez / prex /20309819781","20309819781","angel nunez")]
    [InlineData("CR.TRAN. 30672598164 REN FOR SRL","30672598164","REN FOR SRL")]
    public void ExtraeNombreYCuitSinConfundirFacturaProveedorDeBilleteraONumeroDeReferencia(string texto,string cuit,string nombre)
    {
        var r=ContraparteBancaria.Extraer(texto);Assert.Equal(cuit,r.Cuit);Assert.Equal(nombre,r.Nombre);
    }

    [Fact]
    public async Task AliasConPuntoIdentificaPorCuitPeroUsaRazonSocialOficialEnPropuesta()
    {
        MovementMatchProposal? proposal=null;
        var repo=Proxy<IBankStatementRepository>((m,args)=>m.Name switch {
            "GetCobranzasForMatchingAsync"=>Task.FromResult<IReadOnlyCollection<CobranzaCandidateDto>>([new() { MovementId=1,Amount=100,Description="Transferencia recibida De tires ensenada sa / fac / 30715187953",CounterpartyTaxId="30715187953" }]),
            "GetClientAliasesAsync"=>Task.FromResult<IReadOnlyCollection<ClientAliasDto>>([new() {ClientCode="112010978",AliasText=".",ClientName="TIRES ENSENADA S.A.",Cuit="30715187953"}]),
            "SearchClientByNameAsync"=>Task.FromResult<IReadOnlyCollection<ClientSearchResultDto>>([]),
            "GetPendingDocumentsAsync"=>Task.FromResult<IReadOnlyCollection<PendingDocumentDto>>([new(){DocumentType="FC",PendingBalance=100}]),
            "HasRecentlyRegisteredPaymentAsync"=>Task.FromResult(false),
            "SaveMatchAsync"=>Capture((MovementMatchProposal)args![0]!),_=>throw new InvalidOperationException(m.Name)
        });
        Task Capture(MovementMatchProposal value){proposal=value;return Task.CompletedTask;}
        await new DeterministicMatchingService(repo,1).GenerateMatchesAsync(1);
        Assert.Equal("112010978",proposal!.ClientCode);Assert.Equal("TIRES ENSENADA S.A.",proposal.ClientName);
        Assert.Contains("TIRES ENSENADA S.A.",proposal.ProposalSummary);Assert.Equal(MatchingType.Exacto,proposal.MatchingType);
    }

    [ConciliacionSqlFact]
    public async Task AliasYPropuestasHistoricasResuelvenRazonSocialDesdeVistaOficial()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        using var tx=cn.BeginTransaction();
        var repo=new AlfaCore.ConciliacionBancaria.Infrastructure.ConciliacionRepository(cn,tx);
        var aliases=await repo.GetClientAliasesAsync();
        var alias=Assert.Single(aliases.Where(a=>a.ClientCode=="112010978"));
        Assert.Equal("TIRES ENSENADA S.A.",alias.ClientName);
        tx.Rollback();
        using var runtime=new CargaComprobantesArcaRuntime();var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch {"GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null});
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch {"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,_=>null});
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        var rows=await service.MovimientosAsync(new ConciliacionMovimientoFiltros {ImportacionId=43,Buscar="TIRES ENSENADA"});
        Assert.NotEmpty(rows.Items);Assert.All(rows.Items,m=>Assert.Equal("TIRES ENSENADA S.A.",m.Cliente));
        var detail=await service.DetalleAsync(rows.Items.First().Id);
        Assert.All(detail,d=>Assert.Equal("TIRES ENSENADA S.A.",d.ClientName));
    }

    [ConciliacionSqlFact]
    public async Task ImportacionRealTransaccionalDuplicadosSuperpuestosConsultaYEliminacionEnBaseDePruebas()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        var builder=new SqlConnectionStringBuilder(connection);
        Assert.Equal("ALFANET2007_PRUEBAS",builder.InitialCatalog);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        using var runtime=new CargaComprobantesArcaRuntime();
        var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch { "GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null });
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"PRUEBA_CONCILIACION",_=>null });
        var permissions=Proxy<IPermissionService>((_,_)=>Task.FromResult(true));
        var service=new ConciliacionBancariaService(sessions,users,permissions,runtime.Events,new());
        var cuenta=(await service.CuentasAsync()).First().Codigo;
        var before=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_ASIENTOS");
        var reference=Guid.NewGuid().ToString("N");
        var file=Excel("prueba.xlsx",["Número Secuencia","Fecha","Importe","Descripción"],[reference,"19/10/2026",137.23m,"PRUEBA EXTRACTO SIN CLIENTE"]);
        var ids=new List<long>();
        try
        {
            var result=await service.ImportarAsync(file,BankSource.Provincia,cuenta);ids.Add(result.ImportacionId);
            Assert.Equal(1,result.Movimientos);Assert.Equal(137.23m,result.Total);
            var original=await service.OriginalAsync(result.ImportacionId);
            Assert.Equal(file.Nombre,original.Nombre);
            Assert.Equal(file.Contenido,original.Contenido);
            Assert.True((await service.ImportacionesAsync(1)).Items.Single(x=>x.Id==result.ImportacionId).TieneOriginal);
            Assert.Single((await service.MovimientosAsync(result.ImportacionId,1)).Items);
            var duplicate=await Assert.ThrowsAsync<AppUserFacingException>(()=>service.ImportarAsync(file with { Nombre="otro_nombre.xlsx" },BankSource.Provincia,cuenta));
            Assert.Contains("ya fue importado",duplicate.UserMessage);
            var overlap=Excel("otro.xlsx",["Número Secuencia","Fecha","Importe","Descripción"],
                [reference,"19/10/2026",137.23m,"PRUEBA EXTRACTO SIN CLIENTE"],[reference+"B","19/10/2026",55m,"PRUEBA NUEVO"]);
            var next=await service.ImportarAsync(overlap,BankSource.Provincia,cuenta);ids.Add(next.ImportacionId);
            Assert.Equal(1,next.Movimientos);Assert.Equal(1,next.Duplicados);Assert.Equal(55m,next.Total);
            // Error a mitad de importación: no deben quedar cabecera ni primeras filas.
            var imports=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BK_ImportacionExtracto");
            var invalid=Excel("invalido.xlsx",["Número Secuencia","Fecha","Importe","Descripción"],
                [reference+"C","19/10/2026",1m,"Primera fila"],[reference+"D","19/10/2026",2m,new string('X',600)]);
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.ImportarAsync(invalid,BankSource.Provincia,cuenta));
            Assert.Equal(imports,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BK_ImportacionExtracto"));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BK_ExtractoMovimiento WHERE ReferenciaExterna=@ref",new { @ref=reference+"C" }));
            Assert.Equal(before,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_ASIENTOS"));
            output.WriteLine($"SQL: importaciones {result.ImportacionId}/{next.ImportacionId}, duplicado rechazado, solapamiento omitido, sin asientos nuevos.");
        }
        finally { foreach(var id in ids) await service.EliminarAsync(id); }
    }

    [Fact]
    public async Task ExtractosLocalesRealesCuandoEstanDisponibles()
    {
        var root=Environment.GetEnvironmentVariable("CONCILIACION_EXTRACTOS");
        if(string.IsNullOrWhiteSpace(root)) return;
        foreach(var dir in Directory.GetDirectories(root))
        {
            var path=Directory.GetFiles(dir,"*.xlsx").OrderBy(p=>p).LastOrDefault();if(path is null) continue;
            var r=await new ConciliacionExtractoLector().LeerAsync(new(Path.GetFileName(path),await File.ReadAllBytesAsync(path)));
            Assert.NotNull(r.Banco);Assert.NotEmpty(r.Movimientos);
            output.WriteLine($"{r.Banco}: {r.Movimientos.Count} movimientos; total {r.Movimientos.Sum(m=>m.Amount):N2}");
        }
        var screenshot=Path.Combine(root,"Provincia","provincia_2026-04-01 (1).xlsx");
        if(File.Exists(screenshot))
        {
            var r=await new ConciliacionExtractoLector().LeerAsync(new(Path.GetFileName(screenshot),await File.ReadAllBytesAsync(screenshot)));
            Assert.Equal(20,r.Movimientos.Count);Assert.Equal(210583.31m,r.Movimientos.Sum(m=>m.Amount));
            output.WriteLine("Archivo de la imagen: 20 movimientos; total 210.583,31, coincidente con WinForms.");
        }
    }

    [Fact]
    public void CobranzaRechazaImportesInvalidosYConservaTotalBancoMasRetenciones()
    {
        var s=new CobranzaSolicitud(Guid.NewGuid(),1,new DateTime(2026,10,19),0,[new("111",97m),new("112",3m,"CERT-1")]);
        Assert.Equal(100m,CobranzasService.ValidarImportes(s));
        Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(s with { Pagos=[new("111",0)] }));
        Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(s with { Pagos=[new("111",1.001m)] }));
        Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(s with { Operacion=Guid.Empty }));
        Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(s with { Detalle=new string('x',101) }));
    }

    [Fact]
    public async Task VentanaCobranzaPrecargaBancoYPermiteAgregarRetencionConCertificado()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));
        var activator=new ActivadorCobranza();services.AddSingleton<IComponentActivator>(activator);
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        CobranzaSolicitud? solicitud=null;
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var prep=new CobranzaPreparacion { MovimientoId=9,Fecha=new DateTime(2026,10,19),CuentaBanco="111",ImporteBanco=97,
                Documentos=[new() { Id=5,Cliente="123",Nombre="CLIENTE ENSAYO",Tc="FC",Comprobante="000200000123A",Unidad="2",Moneda="1",Saldo=100 }],
                Medios=[new() { Cuenta="111",Descripcion="BANCO ENSAYO",Tipo="DB",Moneda="1" },new(){Cuenta="112",Descripcion="RET GANANCIAS",Tipo="RG",Moneda="1"}],
                Talonarios=[new(){Talonario=2,Unidad="2",Sucursal=2}] };
            var root=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.CobranzaRapidaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Preparacion"]=prep,["OnConfirm"]=EventCallback.Factory.Create<CobranzaSolicitud>(new object(),(CobranzaSolicitud s)=>solicitud=s)
            }));
            var dialogo=activator.Pagina!;var type=dialogo.GetType();
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("role=\"dialog\"",html);Assert.Contains("BANCO ENSAYO",html);Assert.Contains("Sucursal 0002",html);
            Assert.Contains("Agregar retención",html);
            Assert.Contains("Cob. CLIENTE ENSAYO",html);
            type.GetField("_detalle",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(dialogo,"Cobro con comentario del usuario");
            type.GetMethod("AgregarRetencion",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialogo,null);
            var lineas=(System.Collections.IList)type.GetField("_lineas",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialogo)!;
            var ret=lineas[1]!;
            ret.GetType().GetProperty("Importe")!.SetValue(ret,"3.00");
            await (Task)type.GetMethod("Confirmar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialogo,null)!;
            Assert.Null(solicitud); // no se envía una retención sin cuenta
            ret.GetType().GetProperty("Cuenta")!.SetValue(ret,"112");
            ret.GetType().GetProperty("Certificado")!.SetValue(ret,"CERT-123");
            await (Task)type.GetMethod("Confirmar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialogo,null)!;
            Assert.NotNull(solicitud);Assert.Equal(100m,solicitud!.Pagos.Sum(p=>p.Importe));
            Assert.Equal(5,solicitud.FacturaId);Assert.Equal(2,solicitud.Talonario);Assert.Equal(9,solicitud.MovimientoId);
            Assert.Equal("CERT-123",solicitud.Pagos[1].Certificado);
            Assert.Equal("Cobro con comentario del usuario",solicitud.Detalle);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialogo,null);
            GuardarVista("cobranza.html",root.ToHtmlString());
        });
    }

    [Theory]
    [InlineData(100,97,"123","2","112",2)]
    [InlineData(97,97,"123","2","112",1)]
    [InlineData(96,97,"123","2","112",1)]
    [InlineData(100,97,"otro","2","112",1)]
    [InlineData(100,97,"123","otra","112",1)]
    [InlineData(100,97,"123","2","inactiva",1)]
    public async Task RetencionSugiereSoloDiferenciaPositivaConHistorialDelClienteYUnidad(
        int saldo,int banco,string cliente,string unidad,string cuenta,int cantidad)
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));
        var activator=new ActivadorCobranza();services.AddSingleton<IComponentActivator>(activator);
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            CobranzaSolicitud? enviada=null;
            var prep=new CobranzaPreparacion { Fecha=new(2026,10,20),CuentaBanco="111",ImporteBanco=banco,
                Documentos=[new(){Id=5,Cliente="123",Nombre="CLIENTE ENSAYO",Unidad="2",Tc="FC",Comprobante="000200000123A",Saldo=saldo},
                    new(){Id=6,Cliente="123",Nombre="CLIENTE ENSAYO",Unidad="2",Tc="FC",Comprobante="000200000124A",Saldo=101}],
                Medios=[new(){Cuenta="111",Descripcion="BANCO ENSAYO",Tipo="DB",Moneda="1"},new(){Cuenta="112",Descripcion="RET GANANCIAS",Tipo="RG",Moneda="1"}],
                Talonarios=[new(){Talonario=2,Unidad="2",Sucursal=2}],
                RetencionesSugeridas=[new(){Cliente=cliente,Unidad=unidad,Cuenta=cuenta,Comprobante="000100007540X",Fecha=new(2026,10,16)}] };
            var root=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.CobranzaRapidaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Preparacion"]=prep,["OnConfirm"]=EventCallback.Factory.Create<CobranzaSolicitud>(new object(),(CobranzaSolicitud s)=>enviada=s)
            }));
            var dialogo=activator.Pagina!;var type=dialogo.GetType();
            var lineas=(System.Collections.IList)type.GetField("_lineas",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialogo)!;
            Assert.Equal(cantidad,lineas.Count);
            Assert.Null(enviada); // una sugerencia nunca graba por sí sola
            if(cantidad==1) return;
            Assert.Contains("Retención sugerida",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            var ret=lineas[1]!; var lineaType=ret.GetType();
            Assert.Equal("112",lineaType.GetProperty("Cuenta")!.GetValue(ret));
            Assert.Equal("3.00",lineaType.GetProperty("Importe")!.GetValue(ret));
            Assert.Equal("",lineaType.GetProperty("Certificado")!.GetValue(ret));
            GuardarVista("cobranza-retencion-sugerida.html",root.ToHtmlString());
            type.GetMethod("CambiarFactura",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialogo,["6"]);
            ret=lineas[1]!;
            Assert.Equal("4.00",lineaType.GetProperty("Importe")!.GetValue(ret));
            // Simula la edición del usuario; ni un render ni cambiar factura la reemplazan.
            lineaType.GetProperty("Sugerida")!.SetValue(ret,false);
            lineaType.GetProperty("Importe")!.SetValue(ret,"2.00");
            lineaType.GetProperty("Certificado")!.SetValue(ret,"NUEVO-CERT");
            await dialogo.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string,object?> { ["Preparacion"]=prep }));
            type.GetMethod("CambiarFactura",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialogo,["5"]);
            Assert.Same(ret,lineas[1]);Assert.Equal(2,lineas.Count);
            await (Task)type.GetMethod("Confirmar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialogo,null)!;
            Assert.NotNull(enviada);Assert.Equal(99m,enviada!.Pagos.Sum(p=>p.Importe));
            Assert.Equal("NUEVO-CERT",enviada.Pagos[1].Certificado);
            // Puede quitar la propuesta y confirmar solamente el importe bancario.
            lineas.Remove(ret);enviada=null;
            await (Task)type.GetMethod("Confirmar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialogo,null)!;
            Assert.Single(enviada!.Pagos);Assert.Equal(97m,enviada.Pagos[0].Importe);
        });
    }

    private sealed class ActivadorCobranza:IComponentActivator
    {
        public AlfaCore.Components.Shared.CobranzaRapidaDialog? Pagina {get;private set;}
        public IComponent CreateInstance(Type type)
        {
            var c=(IComponent)Activator.CreateInstance(type)!;
            if(c is AlfaCore.Components.Shared.CobranzaRapidaDialog page) Pagina=page;
            return c;
        }
    }

    [ConciliacionSqlFact]
    public async Task CobranzaBancoRetencionParcialIdempotenciaYRollback()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime(); var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch {"GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null});
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch {"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null});
        var service=new CobranzasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        var operacion=Guid.NewGuid();int recibo=0;
        // Preparación pública contra una propuesta real: solo lectura, sin confirmar el recibo.
        var real=await cn.ExecuteScalarAsync<long?>("""
            SELECT TOP 1 m.MovimientoId FROM dbo.BK_ExtractoMovimiento m
            JOIN dbo.BK_ImportacionExtracto i ON i.ImportacionId=m.ImportacionId
            JOIN dbo.BK_MovimientoMatch p ON p.MovimientoId=m.MovimientoId
            WHERE m.EstadoRevision<>'Cobrado' AND m.Importe>0 AND p.TipoMatch='Exacto'
                AND ISNULL(i.CuentaBanco,'')<>'' AND EXISTS(SELECT 1 FROM dbo.VE_CPTES_SALDOS_VENTAS s WHERE LTRIM(RTRIM(s.CUENTA))=LTRIM(RTRIM(p.ClienteCodigo)) AND s.SALDO>0)
            ORDER BY m.MovimientoId DESC;
            """);
        if(real.HasValue)
        {
            var prep=await service.PrepararConciliacionAsync(real.Value);
            Assert.NotEmpty(prep.Documentos);Assert.NotEmpty(prep.Talonarios);Assert.NotEmpty(prep.CuentaBanco);
            Assert.Equal(DateTime.Today,prep.SaldoAl);
            var consulta=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
            var cuentaActual=await consulta.CuentaCorrienteAsync(real.Value,30,prep.SaldoAl);
            Assert.Equal(cuentaActual.Saldos.Select(s=>(s.Moneda,s.Saldo)),prep.SaldosCliente.Select(s=>(s.Moneda,s.Saldo)));
            Assert.All(prep.SaldosCliente,s=>Assert.Equal(prep.Documentos[0].Cliente,s.Cliente));
        }
        using(var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                var f=await cn.QueryFirstAsync<CobranzaDocumento>("""
                    SELECT f.ID Id,LTRIM(RTRIM(f.CUENTA)) Cliente,LTRIM(RTRIM(f.UNEGOCIO)) Unidad,s.SALDO Saldo
                    FROM dbo.VE_CPTES_SALDOS_VENTAS s JOIN dbo.V_MV_CPTE f ON f.CUENTA=s.CUENTA AND f.TC=s.TC
                        AND f.SUCURSAL=s.SUCURSAL AND f.NUMERO=s.NUMERO AND f.LETRA=s.LETRA AND f.IDCOMPLEMENTO=0
                    WHERE s.SALDO>100 AND LTRIM(RTRIM(f.TC))='FC' AND ISNULL(f.ANULADA,0)=0 AND LTRIM(RTRIM(f.MONEDA))='1'
                        AND EXISTS(SELECT 1 FROM dbo.MA_CUENTASADIC cl WHERE LTRIM(RTRIM(cl.CODIGO))=LTRIM(RTRIM(f.CUENTA)))
                    ORDER BY f.ID DESC;
                    """,transaction:tx);
                var banco=await cn.QueryFirstAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='DB' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 AND LTRIM(RTRIM(MONEDA))='1';",transaction:tx);
                var ret=await cn.QueryFirstAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='RG' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 AND LTRIM(RTRIM(MONEDA))='1';",transaction:tx);
                var tal=await cn.QueryFirstAsync<int>("SELECT Talonario FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CB' ORDER BY CASE WHEN LTRIM(RTRIM(UNEGOCIO))=@Unidad THEN 0 ELSE 1 END,Talonario;",new{f.Unidad},tx);
                var contador=await cn.ExecuteScalarAsync<int>("SELECT X_ULTIMO_NRO FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CB' AND Talonario=@tal;",new{tal},tx);
                var mov=await cn.ExecuteScalarAsync<long>("""
                    INSERT INTO dbo.BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso,CuentaBanco)
                    VALUES('Provincia','ENSAYO_COBRANZA_ROLLBACK.xlsx','',SYSUTCDATETIME(),'PRUEBA',1,10,'IMPORTED',@banco);
                    DECLARE @i bigint= SCOPE_IDENTITY();
                    INSERT INTO dbo.BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe)
                    VALUES(@i,'Provincia','20261019','ENSAYO COBRANZA ROLLBACK',10);
                    DECLARE @m bigint=SCOPE_IDENTITY();
                    INSERT INTO dbo.BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza) VALUES(@m,'Cobranza',99);
                    INSERT INTO dbo.BK_MovimientoMatch(MovimientoId,TipoMatch,ClienteCodigo,Confianza,ResumenPropuesta) VALUES(@m,'Parcial',@Cliente,99,'ENSAYO');
                    SELECT @m;
                    """,new{banco,f.Cliente},tx);
                var solicitud=new CobranzaSolicitud(operacion,f.Id,new DateTime(2026,10,19),tal,[new(banco,10),new(ret,2,"CERT-ENSAYO")],mov,"Cobro ensayo con comentario");
                // Reproduce el error 5731: cabecera legacy sin moneda, asiento en pesos.
                await cn.ExecuteAsync("""
                    UPDATE dbo.V_MV_CPTE SET MONEDA=NULL,IdVendedor='  21' WHERE ID=@Id;
                    UPDATE dbo.MA_CUENTASADIC SET IdVendedor='   1' WHERE LTRIM(RTRIM(CODIGO))=@Cliente;
                    UPDATE dbo.TA_USUARIOS SET IdCaja='   2' WHERE LTRIM(RTRIM(NOMBRE))='Alberto';
                    """,f,tx);
                var resultado=await service.GrabarEnTransaccionAsync(cn,tx,solicitud,default);recibo=resultado.CobranzaId;
                Assert.Equal(12m,resultado.Importe);
                Assert.Equal("Cobro ensayo con comentario",await cn.ExecuteScalarAsync<string>("SELECT COMENTARIOS FROM dbo.V_MV_CPTE WHERE ID=@recibo;",new{recibo},tx));
                Assert.Equal(3,await cn.ExecuteScalarAsync<int>("""
                    SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE TC='CB' AND SUCURSAL+NUMERO+LETRA=@Comprobante
                        AND CABCUENTA IS NULL AND CABNOMBRE IS NULL AND IdVendedor='  21' AND IdCajas='   2'
                        AND DETALLE='Cobro ensayo con comentario';
                    """,resultado,tx));
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_APLICACION WHERE TC='CB' AND IDCOMPROBANTE=@Comprobante AND TC_PRINT='CB' AND IDCOMPROBANTE_PRINT=IDCOMPROBANTE;",resultado,tx));
                Assert.Equal("1",await cn.ExecuteScalarAsync<string>("SELECT LTRIM(RTRIM(MONEDA)) FROM dbo.V_MV_CPTE WHERE ID=@recibo;",new{recibo},tx));
                Assert.Equal("CB",(await cn.QuerySingleAsync<string>("SELECT LTRIM(RTRIM(TC)) FROM dbo.V_MV_CPTE WHERE ID=@recibo;",new{recibo},tx)));
                Assert.Equal(f.Unidad,await cn.QuerySingleAsync<string>("SELECT LTRIM(RTRIM(UNEGOCIO)) FROM dbo.V_MV_CPTE WHERE ID=@recibo;",new{recibo},tx));
                Assert.Equal(3,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='CB' AND SUCURSAL+NUMERO+LETRA=@Comprobante;",resultado,tx));
                Assert.Equal("CERT-ENSAYO",await cn.QuerySingleAsync<string>("SELECT TJ_AUTORIZO FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='CB' AND SUCURSAL+NUMERO+LETRA=@Comprobante AND LTRIM(RTRIM(CUENTA))=@ret;",new{resultado.Comprobante,ret},tx));
                var sugerida=await CobranzasService.UltimaRetencionAsync(cn,f.Cliente,f.Unidad,tx,default);
                Assert.NotNull(sugerida);Assert.Equal(ret,sugerida!.Cuenta);Assert.Equal(resultado.Comprobante,sugerida.Comprobante);
                Assert.Null(await CobranzasService.UltimaRetencionAsync(cn,"CLIENTE_AJENO",f.Unidad,tx,default));
                Assert.Null(await CobranzasService.UltimaRetencionAsync(cn,f.Cliente,"OTRA_UNIDAD",tx,default));
                Assert.Equal(12m,await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE FROM dbo.MV_APLICACION WHERE LTRIM(RTRIM(TC))='CB' AND IDCOMPROBANTE=@Comprobante;",resultado,tx));
                Assert.Equal(0m,await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='CB' AND SUCURSAL+NUMERO+LETRA=@Comprobante;",resultado,tx));
                Assert.Equal(f.Saldo-12m,await cn.ExecuteScalarAsync<decimal>("""
                    SELECT s.SALDO FROM dbo.VE_CPTES_SALDOS_VENTAS s JOIN dbo.V_MV_CPTE f ON f.CUENTA=s.CUENTA AND f.TC=s.TC AND f.SUCURSAL=s.SUCURSAL AND f.NUMERO=s.NUMERO AND f.LETRA=s.LETRA WHERE f.ID=@Id;
                    """,f,tx));
                Assert.Equal("Cobrado",await cn.ExecuteScalarAsync<string>("SELECT EstadoRevision FROM dbo.BK_ExtractoMovimiento WHERE MovimientoId=@mov;",new{mov},tx));
                Assert.Equal(resultado,await service.GrabarEnTransaccionAsync(cn,tx,solicitud,default));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,solicitud with { Operacion=Guid.NewGuid() },default));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,solicitud with { Pagos=[new(banco,11),new(ret,2,"CERT-ENSAYO")] },default));
                Assert.True(await cn.ExecuteScalarAsync<int>("SELECT X_ULTIMO_NRO FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CB' AND Talonario=@tal;",new{tal},tx)>contador);
                var otra=solicitud with { Operacion=Guid.NewGuid(),MovimientoId=null,Pagos=[new(banco,1)],Detalle=null };
                await cn.ExecuteAsync("""
                    UPDATE dbo.V_MV_CPTE SET MONEDA='    ',IdVendedor=NULL WHERE ID=@Id;
                    UPDATE dbo.TA_USUARIOS SET IdCaja=NULL WHERE LTRIM(RTRIM(NOMBRE))='Alberto';
                    """,f,tx);
                var porDefecto=await service.GrabarEnTransaccionAsync(cn,tx,otra,default);
                Assert.Equal(1m,porDefecto.Importe);
                // Un cobro posterior sin retenciones conserva la última retención conocida.
                Assert.Equal(ret,(await CobranzasService.UltimaRetencionAsync(cn,f.Cliente,f.Unidad,tx,default))!.Cuenta);
                var retCorregida=await cn.QueryFirstAsync<string>("""
                    SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO)) IN ('RI','RB','RG','RO')
                        AND LTRIM(RTRIM(CODIGO))<>@ret AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0
                        AND LTRIM(RTRIM(MONEDA))='1';
                    """,new{ret},tx);
                var corregido=await service.GrabarEnTransaccionAsync(cn,tx,otra with {
                    Operacion=Guid.NewGuid(),Pagos=[new(banco,1),new(retCorregida,1)] },default);
                Assert.Equal(retCorregida,(await CobranzasService.UltimaRetencionAsync(cn,f.Cliente,f.Unidad,tx,default))!.Cuenta);
                // Un recibo anulado no debe ser la referencia para la próxima cobranza.
                await cn.ExecuteAsync("UPDATE dbo.V_MV_CPTE SET ANULADA=1 WHERE ID=@CobranzaId;",corregido,tx);
                Assert.Equal(ret,(await CobranzasService.UltimaRetencionAsync(cn,f.Cliente,f.Unidad,tx,default))!.Cuenta);
                var vendedorCliente=await cn.ExecuteScalarAsync<string>("SELECT LTRIM(RTRIM(IdVendedor)) FROM dbo.VT_CLIENTES WHERE LTRIM(RTRIM(CODIGO))=@Cliente;",f,tx);
                var detalleDefecto=await cn.ExecuteScalarAsync<string>("SELECT N'Cob. '+LTRIM(RTRIM(NOMBRE)) FROM dbo.V_MV_CPTE WHERE ID=@Id;",f,tx);
                Assert.Equal(detalleDefecto,await cn.ExecuteScalarAsync<string>("SELECT COMENTARIOS FROM dbo.V_MV_CPTE WHERE ID=@CobranzaId;",porDefecto,tx));
                Assert.Equal(2,await cn.ExecuteScalarAsync<int>("""
                    SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE TC='CB' AND SUCURSAL+NUMERO+LETRA=@Comprobante
                        AND CABCUENTA IS NULL AND CABNOMBRE IS NULL AND LTRIM(RTRIM(IdVendedor))=@vendedorCliente
                        AND IdCajas='   1' AND DETALLE=@detalleDefecto;
                    """,new{porDefecto.Comprobante,vendedorCliente,detalleDefecto},tx));
                await cn.ExecuteAsync("UPDATE dbo.V_MV_CPTE SET MONEDA='   2' WHERE ID=@Id;",f,tx);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,otra with{Operacion=Guid.NewGuid()},default));
                await cn.ExecuteAsync("""
                    UPDATE dbo.V_MV_CPTE SET MONEDA=NULL WHERE ID=@Id;
                    UPDATE a SET MONEDA='   2' FROM dbo.MV_ASIENTOS a JOIN dbo.V_MV_CPTE f ON a.TC=f.TC AND a.SUCURSAL=f.SUCURSAL AND a.NUMERO=f.NUMERO AND a.LETRA=f.LETRA AND a.CUENTA=f.CUENTA WHERE f.ID=@Id;
                    """,f,tx);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,otra with{Operacion=Guid.NewGuid()},default));
                await cn.ExecuteAsync("""
                    UPDATE a SET MONEDA='    ' FROM dbo.MV_ASIENTOS a JOIN dbo.V_MV_CPTE f ON a.TC=f.TC AND a.SUCURSAL=f.SUCURSAL AND a.NUMERO=f.NUMERO AND a.LETRA=f.LETRA AND a.CUENTA=f.CUENTA WHERE f.ID=@Id;
                    """,f,tx);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,otra with{Operacion=Guid.NewGuid()},default));
                await cn.ExecuteAsync("""
                    UPDATE a SET MONEDA='   1' FROM dbo.MV_ASIENTOS a JOIN dbo.V_MV_CPTE f ON a.TC=f.TC AND a.SUCURSAL=f.SUCURSAL AND a.NUMERO=f.NUMERO AND a.LETRA=f.LETRA AND a.CUENTA=f.CUENTA WHERE f.ID=@Id;
                    """,f,tx);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,
                    solicitud with { Operacion=Guid.NewGuid(),MovimientoId=null,Pagos=[new(banco,f.Saldo+1)] },default));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,
                    solicitud with { Operacion=Guid.NewGuid(),MovimientoId=null,Pagos=[new(banco,1),new("NO_EXISTE",1)] },default));
                // Otro talonario y unidad, dentro de la misma transacción reversible.
                await cn.ExecuteAsync("""
                    INSERT INTO dbo.V_TA_CPTE(CODIGO,DESCRIPCION,SISTEMA,DEBEHABER,LETRAS,Talonario,UNEGOCIO,
                        X_SUC_DEFAULT,X_ULTIMO_NRO,X_NRO_DESDE,X_NRO_HASTA)
                    VALUES('CB','ENSAYO ROLLBACK','Ventas','H','X',9001,'   2',9001,1,1,99999999);
                    UPDATE dbo.V_MV_CPTE SET UNEGOCIO='   2',UNEGOCIO_DESTINO='   2' WHERE ID=@Id;
                    """,f,tx);
                var segunda=await service.GrabarEnTransaccionAsync(cn,tx,solicitud with { Operacion=Guid.NewGuid(),MovimientoId=null,Talonario=9001,Pagos=[new(banco,1)] },default);
                Assert.StartsWith("9001",segunda.Comprobante);
                Assert.Equal("2",await cn.ExecuteScalarAsync<string>("SELECT LTRIM(RTRIM(UNEGOCIO)) FROM dbo.V_MV_CPTE WHERE ID=@CobranzaId;",segunda,tx));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,solicitud with { Operacion=Guid.NewGuid(),MovimientoId=null,Pagos=[new(banco,1)] },default));
            }
            finally { if(tx.Connection is not null) tx.Rollback(); }
        }
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ALFACORE_COBRANZA_OPERACION WHERE Operacion=@operacion;",new{operacion}));
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_CPTE WHERE ID=@recibo;",new{recibo}));
    }

    [ConciliacionSqlFact]
    public async Task ReclasificacionPersisteSinCambiarReglasYRegeneraSoloElMovimientoCorregido()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null});
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null});
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        var reglasAntes=await cn.ExecuteScalarAsync<string>("SELECT CONVERT(varchar(30),CHECKSUM_AGG(BINARY_CHECKSUM(*))) FROM dbo.BK_ReglaInterpretacion;");
        var prefix=Guid.NewGuid().ToString("N");
        var file=Excel("revision.xlsx",["Número Secuencia","Fecha","Importe","Descripción"],
            [prefix+"1","20/10/2026",12m,"ENSAYO REVISION "+prefix],[prefix+"2","20/10/2026",14m,"ENSAYO REVISION "+prefix]);
        var cuenta=(await service.CuentasAsync()).First().Codigo;
        var import=await service.ImportarAsync(file,BankSource.Provincia,cuenta);
        try
        {
            var rows=await service.MovimientosAsync(import.ImportacionId,1);
            var id=rows.Items.First().Id;var otro=rows.Items.Last().Id;
            var otroMatch=await cn.ExecuteScalarAsync<long>("SELECT MAX(MatchId) FROM BK_MovimientoMatch WHERE MovimientoId=@otro;",new{otro});
            Assert.True(await service.ReclasificarAsync(id,"TransferenciaInterna","Es movimiento propio"));
            var cobros=await service.MovimientosAsync(new ConciliacionMovimientoFiltros{ImportacionId=import.ImportacionId,Clasificacion="Cobranza"});
            Assert.Single(cobros.Items);Assert.DoesNotContain(cobros.Items,m=>m.Id==id);
            var revisado=(await service.MovimientosAsync(import.ImportacionId,1)).Items.Single(m=>m.Id==id);
            Assert.Equal("TransferenciaInterna",revisado.Categoria);Assert.Equal("Revisado",revisado.Estado);
            Assert.Empty(revisado.Cliente);Assert.Empty(revisado.Propuesta);Assert.Empty(await service.DetalleAsync(id));
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_MovimientoClasificado WHERE MovimientoId=@id;",new{id}));
            Assert.Equal("RECLASIFICADO",await cn.ExecuteScalarAsync<string>("SELECT TOP 1 EstadoPropuesta FROM BK_MovimientoMatch WHERE MovimientoId=@id ORDER BY MatchId DESC;",new{id}));
            Assert.True(await service.ReclasificarAsync(id,"Cobranza","Revisión del criterio"));
            Assert.Single(await service.DetalleAsync(id));
            Assert.Equal(otroMatch,await cn.ExecuteScalarAsync<long>("SELECT MAX(MatchId) FROM BK_MovimientoMatch WHERE MovimientoId=@otro;",new{otro}));
            Assert.Equal(reglasAntes,await cn.ExecuteScalarAsync<string>("SELECT CONVERT(varchar(30),CHECKSUM_AGG(BINARY_CHECKSUM(*))) FROM dbo.BK_ReglaInterpretacion;"));
            await cn.ExecuteAsync("UPDATE BK_ExtractoMovimiento SET EstadoRevision='Cobrado' WHERE MovimientoId=@id;",new{id});
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.ReclasificarAsync(id,"Gasto","No permitido"));
            Assert.Equal("Cobranza",(await service.MovimientosAsync(import.ImportacionId,1)).Items.Single(m=>m.Id==id).Categoria);
            await cn.ExecuteAsync("UPDATE BK_ExtractoMovimiento SET EstadoRevision='Pendiente' WHERE MovimientoId=@id;",new{id});
        }
        finally { await service.EliminarAsync(import.ImportacionId); }
    }

    [ConciliacionSqlFact]
    public async Task ContextoConsultaFechasSaldosConceptosYOrigenDePropuestasReales()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null});
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null});
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        var id=await cn.QueryFirstAsync<long>("""
            SELECT TOP 1 p.MovimientoId FROM BK_MovimientoMatch p JOIN BK_MovimientoAplicacion a ON a.MatchId=p.MatchId
            WHERE p.EstadoPropuesta<>'RECLASIFICADO' AND a.TipoComprobante='FC' ORDER BY p.MatchId DESC;
            """);
        var info=await service.ContextoComprobantesAsync(id);
        Assert.InRange(info.Comprobantes.Count,1,40);Assert.Contains(info.Comprobantes,d=>d.Propuesto);
        Assert.All(info.Comprobantes,d=>{Assert.NotNull(d.Fecha);Assert.NotEmpty(d.Comprobante);Assert.NotEmpty(d.Nombre);});
        Assert.All(info.Origenes,o=>{Assert.Contains(o.Tc,new[]{"NP","RM"});Assert.Contains(info.Comprobantes,d=>d.Id==o.FacturaId);});
        Assert.Empty((await service.ContextoComprobantesAsync(-1)).Comprobantes);
    }

    [ConciliacionSqlFact]
    public async Task CuentaCorrienteIncluyeCbLegacyContrapartidasAplicacionYSaldosCompletos()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null});
        var username="Alberto";
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>username,_=>null});
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        var cuenta=await service.CuentaCorrienteAsync(548);
        Assert.InRange(cuenta.Movimientos.Count,1,30);
        var recibo=Assert.Single(cuenta.Coincidencias.Where(c=>c.Tc=="CB"&&c.Comprobante=="000100007539X"));
        Assert.Equal(new DateTime(2026,10,10),recibo.Fecha);Assert.Equal(1074748.62m,recibo.Haber);
        var detalle=await service.DetalleAsientoAsync(548,recibo.Clave);
        Assert.Contains(detalle.Contrapartidas,p=>p.Cuenta=="111020304"&&p.Importe==1074748.62m&&p.Signo=="D");
        Assert.Contains(detalle.Aplicaciones,a=>a.Tc=="FC"&&a.Comprobante=="000200003771A"&&a.Importe==1074748.62m);
        Assert.Equal("Daniel",detalle.Movimiento.Usuario.Trim());
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        foreach(var row in cuenta.Movimientos.GroupBy(m=>m.CodigoMoneda).Select(g=>g.First()))
        {
            var saldo=await cn.ExecuteScalarAsync<decimal>("""
                SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE WHEN [DEBE-HABER]='H' THEN -IMPORTE ELSE 0 END)
                FROM MV_ASIENTOS WHERE LTRIM(RTRIM(CUENTA))='112010762' AND EsResumen=0
                    AND ISNULL(LTRIM(RTRIM(MONEDA)),'')=@CodigoMoneda AND FECHA<=@Fecha;
                """,row);
            Assert.Equal(saldo,row.Saldo);
        }
        var anterior=await service.CuentaCorrienteAsync(548,50,new DateTime(2026,10,9));
        Assert.All(anterior.Movimientos,m=>Assert.True(m.Fecha<new DateTime(2026,10,10)));
        Assert.Contains(anterior.Coincidencias,m=>m.Comprobante==recibo.Comprobante);
        var ampliada=await service.CuentaCorrienteAsync(548,100);
        Assert.InRange(ampliada.Movimientos.Count,cuenta.Movimientos.Count,100);
        Assert.Equal(cuenta.Saldos.Select(s=>s.Saldo),ampliada.Saldos.Select(s=>s.Saldo));
        Assert.Empty((await service.CuentaCorrienteAsync(-1)).Movimientos);
        await Assert.ThrowsAsync<AppUserFacingException>(()=>service.DetalleAsientoAsync(-1,recibo.Clave));
        await Assert.ThrowsAsync<AppUserFacingException>(()=>service.DetalleAsientoAsync(548,recibo.Clave with { Asiento=-1 }));
        username="USUARIO_SIN_UNIDADES";
        Assert.Empty((await service.CuentaCorrienteAsync(548)).Movimientos);
        await Assert.ThrowsAsync<AppUserFacingException>(()=>service.DetalleAsientoAsync(548,recibo.Clave));
        username="Alberto";
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();services.AddSingleton(runtime.Events);
        services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            RenderFragment contenido=b=>
            {
                b.OpenComponent<AlfaCore.Components.Shared.AlfaDesign.AlfaTabs>(0);
                b.AddAttribute(1,"Items",new AlfaCore.Components.Shared.AlfaDesign.AlfaTabItem[] { new("cuenta","Cuenta corriente"),new("comprobantes","Comprobantes") });
                b.AddAttribute(2,"ActiveKey","cuenta");b.CloseComponent();
                b.OpenComponent<AlfaCore.Components.Shared.ConciliacionCuentaCorrientePanel>(3);
                b.AddAttribute(4,"MovimientoId",548L);b.AddAttribute(5,"FechaMovimiento",new DateTime(2026,10,9));
                b.AddAttribute(6,"PuedeReconocer",true);b.CloseComponent();
            };
            var root=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.AlfaDesign.AlfaDialog>(
                ParameterView.FromDictionary(new Dictionary<string,object?> { ["Open"]=true,["Title"]="Detalle del movimiento #548",
                    ["Size"]=AlfaCore.Components.Shared.AlfaDesign.AlfaDialogSize.Xl,["ChildContent"]=contenido }));
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Posibles cobranzas ya registradas",html);Assert.Contains("000100007539X",html);
            Assert.Contains("BANCO SANTANDER DANIEL",html);Assert.Contains("000200003771A",html);
            Assert.Contains("1.074.748,62",html);Assert.Contains("Daniel",html);Assert.Contains("2026-10-24",html);
            Assert.Contains("Marcar como cobrado",html);Assert.Contains("Confirmo que este recibo corresponde",html);
            GuardarVista("detalle-cuenta-corriente.html",root.ToHtmlString());
            RenderFragment molinero=b=>
            {
                b.OpenComponent<AlfaCore.Components.Shared.ConciliacionCuentaCorrientePanel>(0);
                b.AddAttribute(1,"MovimientoId",559L);b.AddAttribute(2,"FechaMovimiento",new DateTime(2026,10,8));
                b.AddAttribute(3,"PuedeReconocer",true);b.CloseComponent();
            };
            var mol=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.AlfaDesign.AlfaDialog>(
                ParameterView.FromDictionary(new Dictionary<string,object?> { ["Open"]=true,["Title"]="Detalle del movimiento #559",
                    ["Size"]=AlfaCore.Components.Shared.AlfaDesign.AlfaDialogSize.Xl,["ChildContent"]=molinero }));
            var molHtml=System.Net.WebUtility.HtmlDecode(mol.ToHtmlString());
            Assert.Contains("Importes distintos:",molHtml);Assert.Contains("389.332,86",molHtml);Assert.Contains("397.428,87",molHtml);
            Assert.Contains("Observación sobre la diferencia (obligatoria)",molHtml);Assert.Contains("Marcar como cobrado",molHtml);
            GuardarVista("detalle-cobro-diferencia.html",mol.ToHtmlString());
        });
    }

    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
    }

    [ConciliacionSqlFact]
    public async Task PantallaArchivoPrimeroDetectaBancoYAbreRevisionDesdeElHeader()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        using var runtime=new CargaComprobantesArcaRuntime();var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch { "GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null });
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"PRUEBA_CONCILIACION_UI",_=>null });
        var permissions=Proxy<IPermissionService>((_,_)=>Task.FromResult(true));
        var service=new ConciliacionBancariaService(sessions,users,permissions,runtime.Events,new());
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(service);services.AddSingleton(sessions);services.AddSingleton(users);services.AddSingleton(runtime.Events);
        services.AddSingleton(new CobranzasService(sessions,users,permissions,runtime.Events));
        services.AddSingleton<IPageHeaderService,PageHeaderService>();services.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton<IRouteContextService,RouteContextService>();
        services.AddSingleton(Proxy<IAppModeService>((m,_)=>m.Name=="get_IsSaaSMode" ? true : null));
        services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));
        var activator=new ActivatorPagina();services.AddSingleton<IComponentActivator>(activator);
        using var provider=services.BuildServiceProvider();
        provider.GetRequiredService<NavigationManager>().NavigateTo("/ALFANET/4283/caja-bancos/conciliacion-bancaria?directo=1");
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        long? import=null;
        try
        {
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                var root=await renderer.RenderComponentAsync<AlfaCore.Components.Pages.ConciliacionBancaria>();
                var page=activator.Pagina!;var header=provider.GetRequiredService<IPageHeaderService>();
                Assert.Equal("caja-bancos",header.Current.ModuleKey);
                var navigation=Assert.Single(header.Current.TopNavigationItems);
                Assert.Equal("Conciliación bancaria",navigation.Label);
                Assert.Equal("/ALFANET/4283/caja-bancos/conciliacion-bancaria?directo=1",navigation.Url);
                await navigation.OnClick!();
                Assert.EndsWith(navigation.Url,provider.GetRequiredService<NavigationManager>().Uri);
                var caja=ModuleTopNavPresets.BuildCajaBancos(provider.GetRequiredService<IRouteContextService>(),provider.GetRequiredService<NavigationManager>(),"resumen");
                Assert.DoesNotContain(caja,i=>i.Key=="conciliacion");
                Assert.True(header.Current.Actions.Single(a=>a.Key=="importar").Disabled);
                Assert.NotNull(header.Current.Search);
                Assert.Contains("Todos los bancos",header.Current.Search!.Placeholder);
                var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("Archivo Excel",html);Assert.DoesNotContain("Formato del extracto",html);
                var file=Excel("descarga.xlsx",["Número Secuencia","Fecha","Importe","Descripción"],
                    [Guid.NewGuid().ToString("N"),"19/10/2026",35.17m,"PRUEBA PANTALLA"]);
                var selected=page.GetType().GetMethod("SeleccionarArchivo",BindingFlags.NonPublic|BindingFlags.Instance)!;
                var desconocido=Excel("formato-sin-marca.xlsx",["Fecha","Descripción","Importe"],["19/10/2026","ENSAYO FORMATO",1m]);
                await (Task)selected.Invoke(page,[new InputFileChangeEventArgs([new ArchivoNavegador(desconocido)])])!;
                Assert.Contains("Formato del extracto",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
                await (Task)page.GetType().GetMethod("CambiarBanco",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,["Provincia"])!;
                Assert.DoesNotContain("Formato del extracto",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
                var rutaProvincia=@"C:\dev\Extractos\MercadoPago\Provincia 102026.xlsx";
                if(File.Exists(rutaProvincia))
                {
                    var provincia=new ConciliacionArchivo(Path.GetFileName(rutaProvincia),await File.ReadAllBytesAsync(rutaProvincia));
                    await (Task)selected.Invoke(page,[new InputFileChangeEventArgs([new ArchivoNavegador(provincia)])])!;
                    Assert.Equal(34,page.GetType().GetField("_cantidad",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page));
                    Assert.DoesNotContain("Formato del extracto",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
                    GuardarVista("importacion-provincia-formatos.html",root.ToHtmlString());
                }
                await (Task)selected.Invoke(page,[new InputFileChangeEventArgs([new ArchivoNavegador(file)])])!;
                html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());Assert.DoesNotContain("Formato del extracto",html);Assert.Contains("Provincia",html);
                GuardarVista("importacion.html",root.ToHtmlString());
                var cuenta=(await service.CuentasAsync()).First().Codigo;
                page.GetType().GetMethod("CambiarCuenta",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,[cuenta]);
                Assert.False(header.Current.Actions.Single(a=>a.Key=="importar").Disabled);
                await header.Current.Actions.Single(a=>a.Key=="importar").OnClick!();
                import=(long?)page.GetType().GetField("_filtro",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page);
                Assert.NotNull(import);
                html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("PRUEBA PANTALLA",html);Assert.Contains("35,17",html);Assert.Contains("Detalle",html);Assert.Contains("Marcar como cobrado",html);
                GuardarVista("revision.html",root.ToHtmlString());
                // La búsqueda del encabezado sale de una importación restringida y limpia filtros anteriores.
                page.GetType().GetField("_filtro",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(page,(long?)-1);
                page.GetType().GetField("_clienteAplicado",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(page,"NO_COINCIDE");
                await header.Current.Search!.OnTextChanged!("35,17");
                await header.Current.Search!.OnSearch!();
                Assert.Null(page.GetType().GetField("_filtro",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page));
                Assert.Equal("",page.GetType().GetField("_clienteAplicado",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page));
                Assert.Contains("Todas las importaciones",root.ToHtmlString());
                Assert.Contains("PRUEBA PANTALLA",root.ToHtmlString());
                await (Task)page.GetType().GetMethod("AbrirImportacion",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,[import!.Value])!;
                Assert.DoesNotContain("class=\"conciliacion-filtros\"",html);
                Assert.NotNull(header.Current.SearchContent);
                var busqueda=await renderer.RenderComponentAsync<CascadingValue<bool>>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                    ["Value"]=false,["ChildContent"]=header.Current.SearchContent }));
                Assert.Contains("Filtros de movimientos",busqueda.ToHtmlString());
                Assert.Contains("Buscar movimientos",busqueda.ToHtmlString());
                page.GetType().GetMethod("AlternarFiltrosRevision",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null);
                var filtros=await renderer.RenderComponentAsync<CascadingValue<bool>>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                    ["Value"]=false,["ChildContent"]=header.Current.SearchContent }));
                var filtrosTexto=System.Net.WebUtility.HtmlDecode(filtros.ToHtmlString());
                Assert.Contains("Cliente probable",filtrosTexto);Assert.Contains("Registración",filtrosTexto);
                Assert.Contains("Conciliación",filtrosTexto);Assert.Contains("Aplicar",filtrosTexto);
                page.GetType().GetMethod("AlternarFiltrosRevision",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null);
                await (Task)page.GetType().GetMethod("Ordenar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,["importe"])!;
                Assert.Contains("aria-sort=\"ascending\"",root.ToHtmlString());
                page.GetType().GetField("_buscar",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(page,"__NO_COINCIDE__");
                await (Task)page.GetType().GetMethod("AplicarFiltros",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null)!;
                Assert.Contains("No hay coincidencias",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
                await (Task)page.GetType().GetMethod("LimpiarFiltros",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null)!;
                Assert.Contains("PRUEBA PANTALLA",root.ToHtmlString());
                var movimientos=(PagedResult<ConciliacionMovimiento>)page.GetType().GetField("_movimientos",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
                await (Task)page.GetType().GetMethod("SeleccionarMovimiento",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,[movimientos.Items.First()])!;
                html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("role=\"dialog\"",html);
                Assert.Contains("aria-modal=\"true\"",html);
                Assert.Contains($"Detalle del movimiento #{movimientos.Items.First().Id}",html);
                Assert.Contains("Asignar cliente",html);Assert.Contains("Recordar este nombre bancario",html);
                Assert.Contains("Buscar por nombre, código o CUIT",html);
                GuardarVista("detalle-asignar-cliente.html",root.ToHtmlString());
                var lookup=activator.LookupCliente!;
                Assert.NotNull(lookup);
                var elegidoCliente=new ClientSearchResultDto {ClientCode="112010129",ClientName="MADERERA SAN BLAS",Cuit="30-68877493-0"};
                await (Task)lookup.GetType().GetMethod("SelectAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(lookup,[elegidoCliente])!;
                Assert.Same(elegidoCliente,page.GetType().GetField("_clienteAsignar",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page));
                Assert.Equal("112010129 · MADERERA SAN BLAS",page.GetType().GetField("_buscarCliente",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page));
                Assert.Contains("Cliente seleccionado:",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
                Assert.Equal("cuenta",page.GetType().GetField("_detalleTab",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page));
                GuardarVista("detalle.html",root.ToHtmlString());
                Assert.Contains("Guardar clasificaci",html);
                var comparacion=new ConciliacionContextoComprobantes([
                    new(){Id=100,Tc="FC",Comprobante="000200000100A",Nombre="CLIENTE ENSAYO",Cuit="30123456789",Unidad="2",Moneda="$",
                        Fecha=new DateTime(2026,10,1),Vencimiento=new DateTime(2026,10,10),Importe=100,SaldoActual=80,Propuesto=true,
                        Comentarios="Servicio correspondiente a octubre",Conceptos="Abono del mes"},
                    new(){Id=99,Tc="FC",Comprobante="000200000099A",Nombre="CLIENTE ENSAYO",Cuit="30123456789",Unidad="2",Moneda="$",
                        Fecha=new DateTime(2026,9,1),Importe=90,SaldoActual=10}],
                    [new(){FacturaId=100,Tc="NP",Comprobante="000100000010X",Fecha=new DateTime(2026,9,30),Comentarios="Pedido del cliente"}]);
                page.GetType().GetField("_contexto",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(page,comparacion);
                page.GetType().GetField("_detalleTab",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(page,"comprobantes");
                page.GetType().GetField("_comprobanteDetalleId",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(page,100);
                typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null);
                html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("01/10/2026",html);Assert.Contains("01/09/2026",html);Assert.Contains("Servicio correspondiente a octubre",html);
                Assert.Contains("Pedido del cliente",html);Assert.Contains("Abono del mes",html);Assert.Contains("Saldo actual",html);
                GuardarVista("detalle-comparacion.html",root.ToHtmlString());
                page.GetType().GetMethod("CerrarDetalle",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null);
                await root.QuiescenceTask;
                // El handler del botón provoca el render; al invocarlo por reflexión lo solicitamos explícitamente.
                typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null);
                Assert.DoesNotContain("role=\"dialog\"",root.ToHtmlString());
                Assert.Contains("PRUEBA PANTALLA",root.ToHtmlString());
                Assert.Contains("aria-sort=\"ascending\"",root.ToHtmlString());
                await (Task)page.GetType().GetMethod("SeleccionarMovimiento",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,[movimientos.Items.First()])!;
                page.GetType().GetField("_categoriaRevision",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(page,"TransferenciaInterna");
                await (Task)page.GetType().GetMethod("GuardarClasificacion",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null)!;
                Assert.DoesNotContain("role=\"dialog\"",root.ToHtmlString());
                Assert.Contains("Clasificaci",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
                await (Task)page.GetType().GetMethod("CambiarTab",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,["importar"])!;
                await header.Current.Search!.OnTextChanged!("PRUEBA PANTALLA");
                await header.Current.Search!.OnSearch!();
                Assert.Equal("revisar",page.GetType().GetField("_tab",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page));
                Assert.Contains("PRUEBA PANTALLA",root.ToHtmlString());
                await (Task)page.GetType().GetMethod("CambiarTab",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,["importar"])!;
                var mp=Excel("mercadopago 102026.xlsx",["Fecha","Descripción","Tipo","Medio de pago","Monto"],
                    ["Hoy","CLIENTE","Transferencia recibida","Dinero disponible",100m]);
                await (Task)selected.Invoke(page,[new InputFileChangeEventArgs([new ArchivoNavegador(mp)])])!;
                html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("Fecha de referencia de la descarga",html);Assert.Contains("Procesar archivo",html);
                Assert.DoesNotContain("Formato del extracto",html);Assert.True(header.Current.Actions.Single(a=>a.Key=="importar").Disabled);
                GuardarVista("importacion-fecha-mercadopago.html",root.ToHtmlString());
                page.GetType().GetMethod("CambiarFechaReferencia",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,["2026-10-19"]);
                await (Task)page.GetType().GetMethod("ProcesarArchivo",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,null)!;
                Assert.True((bool)page.GetType().GetField("_listo",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!);
                Assert.Equal(new DateTime(2026,10,19),((ConciliacionArchivo)page.GetType().GetField("_archivo",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!).FechaReferencia);
                Assert.Contains("19/10/2026",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
                page.GetType().GetMethod("CambiarFechaReferencia",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(page,["2026-10-20"]);
                Assert.False((bool)page.GetType().GetField("_listo",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!);
                output.WriteLine("Pantalla: archivo primero, banco detectado, cuenta DB, revisión y confirmación editable de fechas relativas.");
            });
        }
        finally { if(import.HasValue)await service.EliminarAsync(import.Value); }
    }

    private static void GuardarVista(string nombre,string html)
    {
        var dir=Environment.GetEnvironmentVariable("CONCILIACION_VISUAL_DIR");
        if(string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,nombre),html,new System.Text.UTF8Encoding(false));
    }

    [Fact]
    public void SugiereCuentaDelBancoDetectadoYSolicitaSeleccionSiHayVariosTitulares()
    {
        ConciliacionCuenta[] cuentas=[new(){Codigo="1",Descripcion="cheque bco provincia"},new(){Codigo="2",Descripcion="BANCO PROVINCIA ESTER"},
            new(){Codigo="3",Descripcion="BANCO SANTANDER ESTER"},new(){Codigo="4",Descripcion="BANCO SANTANDER DANIEL"}];
        Assert.Equal("2",ConciliacionExtractoLector.SugerirCuenta(BankSource.Provincia,cuentas));
        Assert.Equal("",ConciliacionExtractoLector.SugerirCuenta(BankSource.Santander,cuentas));
        Assert.Equal("",ConciliacionExtractoLector.SugerirCuenta(BankSource.MercadoPago,cuentas));
    }

    [ConciliacionSqlFact]
    public async Task FiltrosBusquedaYOrdenSeAplicanAntesDePaginarYCompartenConteo()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch { "GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null });
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"PRUEBA_CONCILIACION_FILTROS",_=>null });
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        var prefix=Guid.NewGuid().ToString("N");
        var rows=Enumerable.Range(1,60).Select(i=>new object[]{prefix+i,"19/10/2026",(decimal)i,$"PRUEBA {prefix} {i}"}).ToList();
        rows.Add([prefix+"C1","19/10/2026",-1m,$"COMISION {prefix}"]);
        rows.Add([prefix+"C2","19/10/2026",-2m,$"COMISION {prefix} literal_%"]);
        var file=Excel("filtros.xlsx",["Número Secuencia","Fecha","Importe","Descripción"],rows.ToArray());
        var cuenta=(await service.CuentasAsync()).First().Codigo;
        var r=await service.ImportarAsync(file,BankSource.Provincia,cuenta);
        try
        {
            var primera=await service.MovimientosAsync(new ConciliacionMovimientoFiltros { ImportacionId=r.ImportacionId,Clasificacion="Cobranza",OrdenPor="importe",Descendente=true });
            Assert.Equal(60,primera.Total);Assert.Equal(50,primera.Items.Count);Assert.Equal(60m,primera.Items.First().Importe);Assert.Equal(11m,primera.Items.Last().Importe);
            var segunda=await service.MovimientosAsync(new ConciliacionMovimientoFiltros { ImportacionId=r.ImportacionId,Clasificacion="Cobranza",OrdenPor="importe",Descendente=true,Pagina=2 });
            Assert.Equal(60,segunda.Total);Assert.Equal(10,segunda.Items.Count);Assert.Equal(10m,segunda.Items.First().Importe);Assert.Equal(1m,segunda.Items.Last().Importe);
            var comisiones=await service.MovimientosAsync(new ConciliacionMovimientoFiltros { ImportacionId=r.ImportacionId,Clasificacion="Comision",Buscar=prefix,OrdenPor="importe",Descendente=false });
            Assert.Equal(2,comisiones.Total);Assert.Equal(-2m,comisiones.Items.First().Importe);
            var literal=await service.MovimientosAsync(new ConciliacionMovimientoFiltros { ImportacionId=r.ImportacionId,Buscar="literal_%" });
            Assert.Single(literal.Items);Assert.Equal(1,literal.Total);
            foreach(var key in new[]{"fecha","banco","descripcion","referencia","importe","clasificacion","cliente","propuesta","confianza","estado"})
                Assert.Equal(62,(await service.MovimientosAsync(new ConciliacionMovimientoFiltros { ImportacionId=r.ImportacionId,OrdenPor=key })).Total);
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.MovimientosAsync(new ConciliacionMovimientoFiltros { OrdenPor="importe; SELECT 1" }));
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.MovimientosAsync(new() { Registracion="Estado inexistente" }));
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.MovimientosAsync(new() { Conciliacion="Estado inexistente" }));
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();
            try
            {
                // Solo datos sintéticos de esta importación; se revierten antes de limpiarla.
                await cn.ExecuteAsync("""
                    UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision=CASE WHEN Importe=60 THEN 'Cobrado'
                        WHEN Importe=59 THEN 'Registrado' ELSE 'Pendiente' END WHERE ImportacionId=@id;
                    UPDATE p SET ClienteCodigo='112010762' FROM dbo.BK_MovimientoMatch p
                    JOIN dbo.BK_ExtractoMovimiento m ON m.MovimientoId=p.MovimientoId
                    WHERE m.ImportacionId=@id AND m.Importe=60;
                    """, new { id=r.ImportacionId }, tx);
                var cobrado = await service.MovimientosCoreAsync(cn, new() { ImportacionId=r.ImportacionId,
                    Clasificacion="Cobranza",Cliente="112010762",Registracion="Cobrado",Conciliacion="Pendiente" },default,tx);
                Assert.Equal(1,cobrado.Total);
                Assert.Equal(60m,Assert.Single(cobrado.Items).Importe);
                var nombre = await service.MovimientosCoreAsync(cn,new() { ImportacionId=r.ImportacionId,
                    Cliente=cobrado.Items.Single().Cliente,Registracion="Cobrado" },default,tx);
                Assert.Equal(1,nombre.Total);
                var pendientes = await service.MovimientosCoreAsync(cn,new() { ImportacionId=r.ImportacionId,
                    Registracion="Pendiente",Conciliacion="Pendiente",Pagina=2 },default,tx);
                Assert.Equal(60,pendientes.Total);Assert.Equal(10,pendientes.Items.Count);
                Assert.All(pendientes.Items,m=>Assert.Equal("Pendiente",m.Estado));
                Assert.Equal(1,(await service.MovimientosCoreAsync(cn,new() { ImportacionId=r.ImportacionId,Registracion="Registrado" },default,tx)).Total);
                Assert.Equal(0,(await service.MovimientosCoreAsync(cn,new() { ImportacionId=r.ImportacionId,Conciliacion="Conciliado" },default,tx)).Total);
                Assert.Equal(0,(await service.MovimientosCoreAsync(cn,new() { ImportacionId=r.ImportacionId,Cliente="112010762%" },default,tx)).Total);
                await cn.ExecuteAsync("UPDATE dbo.BK_ExtractoMovimiento SET Importe=1234567.89 WHERE ImportacionId=@id AND Importe=60;",new { id=r.ImportacionId },tx);
                foreach (var texto in new[] { "1.234.567,89", "$ 1.234.567,89", "1234567.89" })
                {
                    var porImporte=await service.MovimientosCoreAsync(cn,new() { ImportacionId=r.ImportacionId,Buscar=texto },default,tx);
                    Assert.Equal(1,porImporte.Total);
                    Assert.Equal(1234567.89m,Assert.Single(porImporte.Items).Importe);
                }
            }
            finally { tx.Rollback(); }
        }
        finally { await service.EliminarAsync(r.ImportacionId); }
    }

    private sealed class ActivatorPagina:IComponentActivator
    {
        public AlfaCore.Components.Pages.ConciliacionBancaria? Pagina { get;private set; }
        public IComponent? LookupCliente { get;private set; }
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is AlfaCore.Components.Pages.ConciliacionBancaria page) Pagina=page;
            if(component is AlfaCore.Components.Shared.AlfaDesign.AlfaLookup<ClientSearchResultDto>) LookupCliente=component;
            return component;
        }
    }
    private sealed class ArchivoNavegador(ConciliacionArchivo file):IBrowserFile
    {
        public string Name=>file.Nombre;
        public DateTimeOffset LastModified=>DateTimeOffset.UtcNow;
        public long Size=>file.Contenido.Length;
        public string ContentType=>"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public Stream OpenReadStream(long maxAllowedSize=512000,CancellationToken cancellationToken=default)=>new MemoryStream(file.Contenido);
    }
}

public sealed class ConciliacionSqlFactAttribute:FactAttribute
{
    public ConciliacionSqlFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL"))) Skip="Requiere CONCILIACION_TEST_SQL sobre ALFANET2007_PRUEBAS.";
    }
}
