using System.Reflection;
using AlfaCore.ConciliacionBancaria.Application.Abstractions;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Application.Services;
using AlfaCore.ConciliacionBancaria.Domain.Entities;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("Conciliacion SQL")]
public sealed class ConciliacionClientesTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
    }

    [Theory]
    [InlineData(false,false,"112")]
    [InlineData(true,false,"MANUAL")]
    [InlineData(false,true,"")]
    public async Task RecuerdaPagadorSinColumnaContraparteRespetaManualYEvitaAliasAmbiguos(bool manual,bool ambiguo,string esperado)
    {
        MovementMatchProposal? guardado=null;
        IReadOnlyCollection<ClientAliasDto> aliases=ambiguo
            ? [new(){ClientCode="112",AliasText="Maderas San Blas",ClientName="CLIENTE UNO"},new(){ClientCode="113",AliasText="San Blas",ClientName="CLIENTE DOS"}]
            : [new(){ClientCode="112",AliasText="Maderas San Blas",ClientName="CLIENTE UNO"}];
        var repo=Proxy<IBankStatementRepository>((m,a)=>m.Name switch {
            "GetCobranzasForMatchingAsync"=>Task.FromResult<IReadOnlyCollection<CobranzaCandidateDto>>([
                new(){MovementId=1,MovementDate=new(2026,10,20),Amount=100,Description="Maderas San Blas Srl · Transferencia recibida",
                    CounterpartyName="",ClientManuallyAssigned=manual,ClassifiedClientCode=manual?"MANUAL":null,ClassifiedClientName="CLIENTE MANUAL"}]),
            "GetClientAliasesAsync"=>Task.FromResult(aliases),
            "GetPendingDocumentsAsync"=>Task.FromResult<IReadOnlyCollection<PendingDocumentDto>>([]),
            "HasRecentlyRegisteredPaymentAsync"=>Task.FromResult(false),
            "SearchClientByNameAsync"=>Task.FromResult<IReadOnlyCollection<ClientSearchResultDto>>([]),
            "SaveMatchAsync"=>Guardar((MovementMatchProposal)a![0]!),_=>throw new NotSupportedException(m.Name)
        });
        Task Guardar(MovementMatchProposal p){guardado=p;return Task.CompletedTask;}
        await new DeterministicMatchingService(repo,1).GenerateMatchesAsync(1);
        Assert.NotNull(guardado);Assert.Equal(esperado,guardado!.ClientCode);
    }

    [ConciliacionSqlFact]
    public async Task CuitCompartidoConsultaLasDosCuentasConYsinSeparadores()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        using var tx=cn.BeginTransaction();
        var repo=new AlfaCore.ConciliacionBancaria.Infrastructure.ConciliacionRepository(cn,tx);
        var clientes=await repo.SearchClientByNameAsync("","20.237/39559-0");
        Assert.Contains(clientes,c=>c.ClientCode=="112012764");Assert.Contains(clientes,c=>c.ClientCode=="112010786");
        Assert.All(clientes,c=>Assert.Equal("20237395590",ContraparteBancaria.NormalizarCuit(c.Cuit)));
        tx.Rollback();
    }

    [Theory]
    [InlineData(100,100,200,false,"A")]
    [InlineData(200,100,200,false,"B")]
    [InlineData(100,100,100,false,"")]
    [InlineData(150,100,200,false,"")]
    [InlineData(200,100,200,true,"B")]
    [InlineData(150,100,200,true,"")]
    public async Task CuitCompartidoResuelveSoloUnaCoincidenciaDeImporteAunqueHayaAlias(int importe,int saldoA,int saldoB,bool alias,string esperado)
    {
        MovementMatchProposal? guardado=null;
        IReadOnlyCollection<ClientSearchResultDto> grupo=[new(){ClientCode="A",ClientName="EMPRESA A",Cuit="20-23739559-0",MatchScore=100},new(){ClientCode="B",ClientName="EMPRESA B",Cuit="20237395590",MatchScore=100}];
        var repo=Proxy<IBankStatementRepository>((m,a)=>m.Name switch {
            "GetCobranzasForMatchingAsync"=>Task.FromResult<IReadOnlyCollection<CobranzaCandidateDto>>([new(){MovementId=1,MovementDate=new(2026,10,20),Amount=importe,
                Description="ADRIAN MIELNICZUK · Transferencia recibida",CounterpartyTaxId=alias?"":"20-23739559-0"}]),
            "GetClientAliasesAsync"=>Task.FromResult<IReadOnlyCollection<ClientAliasDto>>(alias?[new(){ClientCode="A",ClientName="EMPRESA A",AliasText="ADRIAN MIELNICZUK",ClientTaxId="20-23739559-0"}]:[]),
            "SearchClientByNameAsync"=>Task.FromResult(grupo),
            "GetPendingDocumentsAsync"=>Task.FromResult<IReadOnlyCollection<PendingDocumentDto>>([new(){ClientCode=(string)a![0]!,DocumentType="FC",Letter="A",EmissionCenter=1,DocumentNumber=1,PendingBalance=(string)a[0]! == "A"?saldoA:saldoB}]),
            "HasRecentlyRegisteredPaymentAsync"=>Task.FromResult(false),
            "SaveMatchAsync"=>Guardar((MovementMatchProposal)a![0]!),_=>throw new NotSupportedException(m.Name)
        });
        Task Guardar(MovementMatchProposal p){guardado=p;return Task.CompletedTask;}
        await new DeterministicMatchingService(repo,1).GenerateMatchesAsync(1);
        Assert.NotNull(guardado);Assert.Equal(esperado,guardado!.ClientCode);
        if(esperado.Length==0) Assert.Contains("Varios clientes posibles",guardado.ProposalSummary);
    }

    [Theory]
    [InlineData("20-23739559-0")]
    [InlineData("20237395590")]
    [InlineData(" (20).237 39559/0 ")]
    public void NormalizaCuitQuitandoTodoLoQueNoSeaDigito(string documento)
        => Assert.Equal("20237395590",ContraparteBancaria.NormalizarCuit(documento));

    [ConciliacionSqlFact]
    public async Task AsignacionManualRecuerdaEnNuevaImportacionSinModificarContabilidad()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var sesion=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>sesion,_=>null});
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null});
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        var clientes=(await cn.QueryAsync<string>("SELECT TOP(2) LTRIM(RTRIM(CODIGO)) FROM dbo.VT_CLIENTES WHERE ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 ORDER BY CODIGO;")).AsList();
        var antes=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_ASIENTOS;");
        var prefijo="ENSAYO_PAGADOR_"+Guid.NewGuid().ToString("N");
        var aliasA=prefijo+" MADERAS";var aliasB=prefijo+" OTRO";var aliasCompartido=prefijo+" COMPARTIDO";
        var importaciones=new List<long>();
        try
        {
            Assert.Contains(await service.BuscarClientesAsync(clientes[0]),c=>c.ClientCode==clientes[0]);
            Assert.Empty(await service.BuscarClientesAsync("%_[]NOEXISTE"));
            var banco=(await service.CuentasAsync()).First().Codigo;
            async Task<long> Importar(string pagador,decimal importe)
            {
                using var wb=new XLWorkbook();var ws=wb.AddWorksheet("Extracto");
                string[] headers=["Número Secuencia","Fecha","Importe","Descripción"];
                for(var c=0;c<headers.Length;c++) ws.Cell(1,c+1).Value=headers[c];
                ws.Cell(2,1).Value=Guid.NewGuid().ToString("N");ws.Cell(2,2).Value="20/10/2026";
                ws.Cell(2,3).Value=importe;ws.Cell(2,4).Value=pagador+" · Transferencia recibida";
                using var ms=new MemoryStream();wb.SaveAs(ms);
                var result=await service.ImportarAsync(new("ensayo-clientes.xlsx",ms.ToArray()),BankSource.Provincia,banco);
                importaciones.Add(result.ImportacionId);
                return (await service.MovimientosAsync(result.ImportacionId,1)).Items.Single().Id;
            }
            var primero=await Importar(aliasA,7.13m);
            Assert.Empty((await service.DetalleAsync(primero)).First().ClientCode);
            await service.ReevaluarPropuestaAsync(primero);
            Assert.Empty((await service.DetalleAsync(primero)).First().ClientCode);
            await service.AsignarClienteAsync(primero,clientes[0],true,aliasA);
            Assert.Equal(clientes[0],(await service.DetalleAsync(primero)).First().ClientCode);
            await service.AsignarClienteAsync(primero,clientes[0],true,aliasA);
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BK_AliasClienteBanco WHERE AliasTexto=@aliasA;",new{aliasA}));
            var siguiente=await Importar(aliasA,9.38m);
            Assert.Equal(clientes[0],(await service.DetalleAsync(siguiente)).First().ClientCode);
            Assert.True(string.IsNullOrWhiteSpace(await cn.ExecuteScalarAsync<string>("SELECT ContraparteNombre FROM dbo.BK_ExtractoMovimiento WHERE MovimientoId=@siguiente;",new{siguiente})));
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.AsignarClienteAsync(primero,clientes[1],true,aliasA));
            Assert.Equal(clientes[0],(await service.DetalleAsync(primero)).First().ClientCode);
            await service.AsignarClienteAsync(primero,clientes[1],false,null);
            Assert.Equal(clientes[1],(await service.DetalleAsync(primero)).First().ClientCode); // prioridad manual sobre alias
            await service.ReevaluarPropuestaAsync(primero);
            Assert.Equal(clientes[1],(await service.DetalleAsync(primero)).First().ClientCode);
            var soloEste=await Importar(aliasB,11.13m);
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.AsignarClienteAsync(soloEste,"NO_EXISTE",true,aliasB));
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.AsignarClienteAsync(soloEste,clientes[0],true,"Transferencia recibida"));
            await service.AsignarClienteAsync(soloEste,clientes[0],false,null);
            Assert.Empty((await service.DetalleAsync(await Importar(aliasB,12.13m))).First().ClientCode);
            var compartido=await Importar(aliasCompartido,3.17m);
            await service.AsignarClienteAsync(compartido,"112012764",true,aliasCompartido);
            await service.AsignarClienteAsync(compartido,"112010786",true,aliasCompartido);
            Assert.Equal("112010786",(await service.DetalleAsync(compartido)).First().ClientCode);
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BK_AliasClienteBanco WHERE AliasTexto=@aliasCompartido;",new{aliasCompartido}));
            await cn.ExecuteAsync("UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Cobrado' WHERE MovimientoId=@primero;",new{primero});
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.AsignarClienteAsync(primero,clientes[0],true,aliasA));
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.ReevaluarPropuestaAsync(primero));
            Assert.Equal(antes,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_ASIENTOS;"));
        }
        finally
        {
            foreach(var id in importaciones)
            {
                await cn.ExecuteAsync("UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Pendiente' WHERE ImportacionId=@id;",new{id});
                await service.EliminarAsync(id);
            }
            await cn.ExecuteAsync("DELETE FROM dbo.BK_AliasClienteBanco WHERE AliasTexto IN (@aliasA,@aliasB,@aliasCompartido);",new{aliasA,aliasB,aliasCompartido});
        }
    }
}
