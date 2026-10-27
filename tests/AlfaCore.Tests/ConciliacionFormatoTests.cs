using System.Text;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConciliacionFormatoTests
{
    [Theory]
    [InlineData("29-sept-2026")]
    [InlineData("29-SEPT-2026")]
    [InlineData("29-sept.-2026")]
    [InlineData("29/sept/2026")]
    public async Task ProvinciaAceptaSeptiembreAbreviadoSinCambiarElDia(string fecha)
    {
        var archivo=new ConciliacionArchivo("provincia.csv",Encoding.UTF8.GetBytes($"Fecha;Descripción;Importe;Saldo\n{fecha};Transferencia;123,45;1000,00"));
        var resultado=await new ConciliacionExtractoLector().LeerAsync(archivo,BankSource.Provincia);
        var movimiento=Assert.Single(resultado.Movimientos);
        Assert.Equal(new DateTime(2026,9,29),movimiento.MovementDate);
        Assert.Equal(123.45m,movimiento.Amount);
    }

    [Fact]
    public async Task ProvinciaNoAceptaUnDiaInexistenteDeSeptiembre()
    {
        var archivo=new ConciliacionArchivo("provincia.csv",Encoding.UTF8.GetBytes("Fecha;Descripción;Importe;Saldo\n31-sept-2026;Transferencia;123,45;1000,00"));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>new ConciliacionExtractoLector().LeerAsync(archivo,BankSource.Provincia));
        Assert.Contains("fecha",error.Message);
    }

    [ProvinciaSeptiembreRealFact]
    public async Task ProvinciaSeptiembreRealSeLeeCompletoSinModificarElArchivo()
    {
        var path=@"C:\dev\Extractos\Provincia\provincia_2026-09-01_2026-09-30.xls";
        var contenido=await File.ReadAllBytesAsync(path);
        var resultado=await new ConciliacionExtractoLector().LeerAsync(new(Path.GetFileName(path),contenido));
        Assert.Equal(BankSource.Provincia,resultado.Banco);
        Assert.Equal(20,resultado.Movimientos.Count);
        Assert.Equal(672335.08m,resultado.Movimientos.Sum(m=>m.Amount));
        Assert.All(resultado.Movimientos,m=>Assert.InRange(m.MovementDate,new DateTime(2026,9,1),new DateTime(2026,9,30)));
        Assert.Contains(resultado.Movimientos,m=>m.MovementDate==new DateTime(2026,9,29));
        Assert.Equal(contenido,await File.ReadAllBytesAsync(path));
        Console.WriteLine($"Provincia septiembre: {resultado.Movimientos.Count} movimientos; total {resultado.Movimientos.Sum(m=>m.Amount)}.");
    }

    [ProvinciaRealFact]
    public async Task ProvinciaRealXlsYXlsxConCabeceraEnFilaSeisProducenLosMismosMovimientos()
    {
        var lector=new ConciliacionExtractoLector();
        var resultados=new List<ConciliacionLectura>();
        foreach(var extension in new[]{"xls","xlsx"})
        {
            var path=@"C:\dev\Extractos\MercadoPago\Provincia 102026."+extension;
            var r=await lector.LeerAsync(new(Path.GetFileName(path),await File.ReadAllBytesAsync(path)));
            Assert.Equal(BankSource.Provincia,r.Banco);Assert.Equal(34,r.Movimientos.Count);
            Assert.Equal(-729577.61m,r.Movimientos.Sum(m=>m.Amount));
            Assert.Equal(new DateTime(2026,10,19),r.Movimientos.First().MovementDate);
            Assert.Contains(r.Movimientos,m=>m.Amount==704134.73m && m.Description.Contains("OLIVA MAS"));
            resultados.Add(r);
        }
        Assert.Equal(resultados[0].Movimientos.Select(m=>(m.MovementDate,m.Description,m.Amount,m.Balance)),
            resultados[1].Movimientos.Select(m=>(m.MovementDate,m.Description,m.Amount,m.Balance)));
    }

    [Theory]
    [InlineData(";",false)]
    [InlineData(",",false)]
    [InlineData("\t",false)]
    [InlineData(";",true)]
    public async Task CsvDetectaProvinciaConservaComillasAcentosYCentavos(string separador,bool ansi)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        static string Campo(string v)=>"\""+v.Replace("\"","\"\"")+"\"";
        var texto=string.Join(separador,new[]{"Número Secuencia","Fecha","Descripción","Importe","Saldo"}.Select(Campo))+"\r\n"+
            string.Join(separador,new[]{"000123","19/10/2026","Cobro, retención \"IIBB\"\r\nDetalle","1.234,56","2.000,00"}.Select(Campo))+"\r\n"+
            string.Join(separador,new[]{"000124","18/10/2026","Comisión","-50,01","765,44"}.Select(Campo));
        var bytes=(ansi?Encoding.GetEncoding(1252):new UTF8Encoding(false)).GetBytes(texto);
        var r=await new ConciliacionExtractoLector().LeerAsync(new("extracto.csv",bytes));
        Assert.Equal(BankSource.Provincia,r.Banco);Assert.Equal(2,r.Movimientos.Count);
        Assert.Equal(1184.55m,r.Movimientos.Sum(m=>m.Amount));
        Assert.Contains("retención \"IIBB\"",r.Movimientos.First().Description);Assert.Equal(2000m,r.Movimientos.First().Balance);
        Assert.Equal("000123",r.Movimientos.First().ExternalReference);
    }

    [Fact]
    public async Task CsvMercadoPagoRelativoRequiereFechaSinInventarCreacion()
    {
        var file=new ConciliacionArchivo("mp.csv",Encoding.UTF8.GetBytes("Fecha;Descripción;Tipo de operación;Monto\r\nHoy;José;Transferencia recibida;97,25"));
        var lector=new ConciliacionExtractoLector();var previo=await lector.LeerAsync(file);
        Assert.Equal(BankSource.MercadoPago,previo.Banco);Assert.True(previo.RequiereFechaReferencia);Assert.Null(previo.FechaReferenciaSugerida);
        var r=await lector.LeerAsync(file with{FechaReferencia=new(2026,10,19)});
        Assert.Equal(new DateTime(2026,10,19),Assert.Single(r.Movimientos).MovementDate);Assert.Equal(97.25m,r.Movimientos.Single().Amount);
    }

    [Fact]
    public async Task CsvSantanderUsaElMismoParserYConservaReferencia()
    {
        var file=new ConciliacionArchivo("santander.csv",Encoding.UTF8.GetBytes("Fecha;Descripción;Referencia;Caja de Ahorro;Cuenta Corriente;Saldo\r\n19/10/2026;Transferencia;000123;-23,45;;100,00"));
        var r=await new ConciliacionExtractoLector().LeerAsync(file);
        Assert.Equal(BankSource.Santander,r.Banco);Assert.Equal(-23.45m,Assert.Single(r.Movimientos).Amount);Assert.Equal("000123",r.Movimientos.Single().ExternalReference);
    }

    [Theory]
    [InlineData("Provincia")]
    [InlineData("Santander")]
    public async Task FechaNativaExcelNoInvierteDiaYMes(string banco)
    {
        using var wb=new XLWorkbook();var ws=wb.AddWorksheet("Extracto");
        var headers=banco=="Provincia" ? new[]{"Fecha","Descripción","Importe","Saldo"} : new[]{"Fecha","Descripción","Referencia","Caja de Ahorro","Saldo"};
        for(var i=0;i<headers.Length;i++) ws.Cell(1,i+1).Value=headers[i];
        ws.Cell(2,1).Value=new DateTime(2026,10,2);ws.Cell(2,2).Value="Ensayo";
        ws.Cell(2,3).Value=100;ws.Cell(2,4).Value=100;ws.Cell(2,5).Value=100;
        using var ms=new MemoryStream();wb.SaveAs(ms);
        var r=await new ConciliacionExtractoLector().LeerAsync(new("fechas.xlsx",ms.ToArray()),Enum.Parse<BankSource>(banco));
        Assert.Equal(new DateTime(2026,10,2),Assert.Single(r.Movimientos).MovementDate);
    }

    [Fact]
    public async Task CsvImporteInvalidoSeInformaEnLugarDeOmitirMovimiento()
    {
        var file=new ConciliacionArchivo("provincia.csv",Encoding.UTF8.GetBytes("Fecha;Descripción;Importe;Saldo\n19/10/2026;Cobro;mal;100,00"));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>new ConciliacionExtractoLector().LeerAsync(file,BankSource.Provincia));
        Assert.Contains("fila 2",error.Message);Assert.Contains("inválido",error.Message);
    }

    [Fact]
    public async Task CsvSinDatosDelBancoPideFormatoSinAdivinarPorColumnasGenericas()
    {
        var file=new ConciliacionArchivo("generico.csv",Encoding.UTF8.GetBytes("Fecha;Descripción;Importe;Saldo\n19/10/2026;Cobro;100,00;100,00"));
        var lector=new ConciliacionExtractoLector();Assert.Null((await lector.LeerAsync(file)).Banco);
        Assert.Single((await lector.LeerAsync(file,BankSource.Provincia)).Movimientos);
    }

    [Fact]
    public async Task ArchivoDeExtensionNoAdmitidaSeRechaza()
        => await Assert.ThrowsAsync<InvalidOperationException>(()=>new ConciliacionExtractoLector().LeerAsync(new("extracto.pdf",[1,2,3])));
}

public sealed class ProvinciaRealFactAttribute:FactAttribute
{
    public ProvinciaRealFactAttribute()
    {
        if(!File.Exists(@"C:\dev\Extractos\MercadoPago\Provincia 102026.xls")||!File.Exists(@"C:\dev\Extractos\MercadoPago\Provincia 102026.xlsx"))
            Skip="Requiere los dos extractos Provincia 102026 del usuario (solo lectura).";
    }
}

public sealed class ProvinciaSeptiembreRealFactAttribute:FactAttribute
{
    public ProvinciaSeptiembreRealFactAttribute()
    {
        if(!File.Exists(@"C:\dev\Extractos\Provincia\provincia_2026-09-01_2026-09-30.xls"))
            Skip="Requiere el extracto Provincia septiembre del usuario (solo lectura).";
    }
}
