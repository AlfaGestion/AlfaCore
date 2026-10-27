using System.Reflection;
using System.Text;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasPdfProcesoTests:IDisposable
{
    [Theory]
    [InlineData("python",@"C:\recepcion\.venv\Scripts\python.exe",@"C:\recepcion\.venv\Scripts\python.exe")]
    [InlineData(" PYTHON ",@" C:\Python314\python.exe ",@"C:\Python314\python.exe")]
    [InlineData(null,@"C:\Python314\python.exe",@"C:\Python314\python.exe")]
    [InlineData(@" C:\tarjetas\python.exe ",@"C:\recepcion\python.exe",@"C:\tarjetas\python.exe")]
    [InlineData("python","","python")]
    public void PythonHeredaRecepcionSalvoEjecutablePropio(string? tarjetas,string? recepcion,string esperado)
        =>Assert.Equal(esperado,TarjetasPdfLector.ResolverPython(tarjetas,recepcion));
    private readonly string _root=Path.Combine(Path.GetTempPath(),"alfacore-lector-test-"+Guid.NewGuid().ToString("N"));
    private TarjetasPdfLector Lector()
    {
        var env=DispatchProxy.Create<IWebHostEnvironment,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)env).Handler=(m,_)=>m.Name=="get_ContentRootPath"?_root:null;
        return new(env);
    }
    private static ConciliacionArchivo Pdf()
    {
        var bytes=new byte[2*1024*1024];Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(bytes,0);
        return new("prueba.pdf",bytes);
    }
    private async Task Script(string texto)
    {
        Directory.CreateDirectory(Path.Combine(_root,"App_Data","tarjetas"));
        await File.WriteAllTextAsync(Path.Combine(_root,"App_Data","tarjetas","leer_pdf.py"),texto,new UTF8Encoding(false));
    }
    [Fact]
    public async Task PublicacionSinScriptSeInformaAntesDeIniciarPython()
    {
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Lector().LeerAsync(Pdf(),"python",default));
        Assert.Contains("Volvé a publicar",ex.Message);
    }
    [Fact]
    public async Task EjecutableInexistenteSeInformaComoConfiguracionDelServidor()
    {
        await Script("print('prueba')");
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Lector().LeerAsync(Pdf(),Path.Combine(_root,"python-inexistente.exe"),default));
        Assert.Contains("TARJETAS-PYTHON",ex.Message);
        Assert.NotNull(ex.InnerException);
    }
    [PythonProcesoFact]
    public async Task CierreAntesDeLeerPdfConservaElErrorPypdfEnLugarDeCanalizacionTerminada()
    {
        await Script("import sys\nsys.stderr.write(\"ModuleNotFoundError: No module named 'pypdf'\")\nsys.exit(1)\n");
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Lector().LeerAsync(Pdf(),Environment.GetEnvironmentVariable("TARJETAS_TEST_PYTHON")!,default));
        Assert.Contains("Falta instalar pypdf",ex.Message);
        Assert.Contains("ModuleNotFoundError",ex.InnerException!.Message);
    }
    [PythonProcesoFact]
    public async Task LecturaPorStdinConservaUnicodeYValidaImportes()
    {
        await Script("""
            import sys,json
            pdf=sys.stdin.buffer.read()
            assert pdf.startswith(b'%PDF-') and len(pdf)==2*1024*1024
            print(json.dumps({'Banco':'Nación','Bruto':'100.00','Neto':'90.00','DetalleCompleto':True,'Detalles':[{'Fecha':'2026-04-01','Cantidad':1,'Importe':'100.00'}]},ensure_ascii=False))
            """);
        var lectura=await Lector().LeerAsync(Pdf(),Environment.GetEnvironmentVariable("TARJETAS_TEST_PYTHON")!,default);
        Assert.Equal("Nación",lectura.Banco);Assert.Equal(100m,lectura.Bruto);Assert.True(lectura.DetalleCompleto);
    }
    [PythonProcesoFact]
    public async Task CancelarInterrumpeElLectorSinDejarElProcesoEsperandoEntrada()
    {
        await Script("import time\ntime.sleep(30)\n");
        using var ct=new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Lector().LeerAsync(Pdf(),Environment.GetEnvironmentVariable("TARJETAS_TEST_PYTHON")!,ct.Token));
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
public sealed class PythonProcesoFactAttribute:FactAttribute
{
    public PythonProcesoFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_TEST_PYTHON")))Skip="Requiere ejecutable Python de pruebas en TARJETAS_TEST_PYTHON.";}
}
