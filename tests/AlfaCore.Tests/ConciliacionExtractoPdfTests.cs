using System.Reflection;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConciliacionExtractoPdfTests
{
    [Fact]
    public void ComprobanteNacionReutilizadoParaDosConceptosNoSeConfundeConDuplicado()
    {
        NormalizedMovementDto Row(string descripcion)=>new(){MovementDate=new(2026,4,1),Amount=-10m,ExternalReference="1234",Description=descripcion};
        Assert.NotEqual(ConciliacionBancariaService.Huella("111",BankSource.Nacion,Row("COMISION")),
            ConciliacionBancariaService.Huella("111",BankSource.Nacion,Row("IVA")));
    }
    private static ConciliacionExtractoLector Lector()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null&&!Directory.Exists(Path.Combine(dir.FullName,"src","AlfaCore","App_Data","extractos")))dir=dir.Parent;
        Assert.NotNull(dir);
        var env=DispatchProxy.Create<IWebHostEnvironment,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)env).Handler=(m,_)=>m.Name=="get_ContentRootPath"?Path.Combine(dir!.FullName,"src","AlfaCore"):null;
        return new(env);
    }
    [Fact]
    public async Task PdfFalsoSeRechazaAntesDeIniciarPython()
    {
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Lector().LeerAsync(new("falso.pdf",[1,2,3])));
        Assert.Contains("firma PDF",ex.Message);
    }
    [ExtractoPdfFact("NACION")]
    public async Task PdfNacionRealPasaPorElMismoLectorDeLaImportacion()
        =>await Verificar("NACION",BankSource.Nacion,812,-4924593.09m,10269221.99m);
    [ExtractoPdfFact("HIPOTECARIO")]
    public async Task PdfHipotecarioRealPasaPorElMismoLectorDeLaImportacion()
        =>await Verificar("HIPOTECARIO",BankSource.Hipotecario,14,15368.38m,3392.52m);
    [ExtractoPdfFact("PATAGONIA")]
    public async Task PdfPatagoniaRealPasaPorElMismoLectorDeLaImportacion()
        =>await Verificar("PATAGONIA",BankSource.Patagonia,201,11422248.38m,6335868.84m);
    [ExtractoPdfFact("GALICIA")]
    public async Task PdfGaliciaRealPasaPorElMismoLectorDeLaImportacion()
        =>await Verificar("GALICIA",BankSource.Galicia,5258,16616032.83m,3870973.08m);
    [ExtractoPdfFact("PATAGONIA")]
    public async Task GastosBancariosDeOctubreDePatagoniaCoincidenConElResumenDelBanco()
        =>await VerificarGastos("PATAGONIA",88402.98m,18564.63m,2645.79m,334113.10m,519587.91m,108);
    [ExtractoPdfFact("GALICIA")]
    public async Task GastosBancariosDeGaliciaSeCalculanDelExtractoCompleto()
        =>await VerificarGastos("GALICIA",95000.00m,19950.00m,2850.00m,2168897.99m,745370.84m,2666);
    private static async Task VerificarGastos(string formato,decimal gastos,decimal iva,decimal percepcion,decimal iibb,decimal idc,int movimientos)
    {
        var path=Environment.GetEnvironmentVariable("EXTRACTO_TEST_PDF_"+formato)!;
        var lectura=await Lector().LeerAsync(new(Path.GetFileName(path),await File.ReadAllBytesAsync(path)),ct:default,python:Environment.GetEnvironmentVariable("TARJETAS_TEST_PYTHON")!);
        var filas=lectura.Movimientos.Select((m,i)=>new ConciliacionFilaBanco{Id=i+1,Fecha=m.MovementDate,Detalle=m.Description,Importe=m.Amount,Huella="H"+i}).ToList();
        var c=ConciliacionGastosBancarios.Calcular(filas,new(),1m);
        Assert.Empty(c.Errores);
        Assert.Equal(gastos,c.Importe(ConciliacionGastosBancarios.Gastos));
        Assert.Equal(iva,c.Importe(ConciliacionGastosBancarios.IvaCredito));
        Assert.Equal(percepcion,c.Importe(ConciliacionGastosBancarios.IvaPercepcion));
        Assert.Equal(iibb,c.Importe(ConciliacionGastosBancarios.Iibb));
        Assert.Equal(idc,c.Importe(ConciliacionGastosBancarios.Idc));
        Assert.Equal(gastos+iva+percepcion+iibb+idc,c.Total);
        Assert.Equal(movimientos,c.Movimientos.Count);
    }
    private static async Task Verificar(string formato,BankSource banco,int cantidad,decimal inicial,decimal final)
    {
        var path=Environment.GetEnvironmentVariable("EXTRACTO_TEST_PDF_"+formato)!;
        var archivo=new ConciliacionArchivo(Path.GetFileName(path),await File.ReadAllBytesAsync(path));
        var python=Environment.GetEnvironmentVariable("TARJETAS_TEST_PYTHON")!;
        var lectura=await Lector().LeerAsync(archivo,ct:default,python:python);
        Assert.Equal(banco,lectura.Banco);Assert.Equal(cantidad,lectura.Movimientos.Count);
        Assert.Equal(final-inicial,lectura.Movimientos.Sum(m=>m.Amount));
        Assert.All(lectura.Movimientos,m=>Assert.Equal(banco,m.Source));
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Lector().LeerAsync(archivo,BankSource.Provincia,python:python));
        Assert.Contains("formato bancario",ex.Message);
    }
}
public sealed class ExtractoPdfFactAttribute:FactAttribute
{
    public ExtractoPdfFactAttribute(string formato)
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_TEST_PYTHON"))||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EXTRACTO_TEST_PDF_"+formato)))
            Skip="Requiere Python y PDF de prueba local autorizado.";
    }
}
