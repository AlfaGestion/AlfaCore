using System.Diagnostics;
using System.Text.Json;
using AlfaCore.Services;
using ClosedXML.Excel;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConciliacionDemoTests
{
    [Theory]
    [InlineData("ALFANET")]
    [InlineData("ALFANET_DEMO")]
    public async Task LotesFicticiosConservanFormatosYLaSegundaDescargaTiene42RepetidosYDosNuevos(string baseDestino)
    {
        var repo=new DirectoryInfo(AppContext.BaseDirectory);
        while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"AlfaCore.sln")))repo=repo.Parent;
        Assert.NotNull(repo);
        var salida=Path.Combine(Path.GetTempPath(),"alfacore-demo-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var process=new Process {StartInfo=new("python") {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}};
            process.StartInfo.ArgumentList.Add(Path.Combine(repo!.FullName,"tools/conciliacion_demo/generar_lotes.py"));
            process.StartInfo.ArgumentList.Add("--salida");process.StartInfo.ArgumentList.Add(salida);
            process.StartInfo.ArgumentList.Add("--base");process.StartInfo.ArgumentList.Add(baseDestino);
            process.Start();var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();Assert.True(process.ExitCode==0,await stderr);await stdout;
            using var manifest=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(salida,"manifiesto_demo.json")));
            Assert.Equal(baseDestino,manifest.RootElement.GetProperty("base_destino").GetString());
            Assert.Equal(60,manifest.RootElement.GetProperty("clientes").GetArrayLength());
            var lector=new ConciliacionExtractoLector();
            foreach(var banco in new[]{"Santander","Provincia","MercadoPago"})
            {
                async Task<AlfaCore.Models.ConciliacionLectura> Leer(string nombre)=>await lector.LeerAsync(new(nombre,await File.ReadAllBytesAsync(Path.Combine(salida,nombre))));
                var mes=await Leer(banco+"_DEMO_01_mes.xlsx");var semana=await Leer(banco+"_DEMO_02_semana_superpuesta.csv");
                Assert.Equal(banco,mes.Banco.ToString());Assert.Equal(mes.Banco,semana.Banco);
                Assert.Equal(61,mes.Movimientos.Count);Assert.Equal(44,semana.Movimientos.Count);
                static string Clave(AlfaCore.ConciliacionBancaria.Application.DTOs.NormalizedMovementDto m)=>$"{m.MovementDate:yyyyMMdd}|{m.Amount}|{m.Description}|{m.ExternalReference}|{m.Balance}";
                var claves=mes.Movimientos.Select(Clave).ToHashSet();
                Assert.Equal(42,semana.Movimientos.Count(m=>claves.Contains(Clave(m))));
                Assert.Equal(2,semana.Movimientos.Count(m=>!claves.Contains(Clave(m))));
                var cuenta=manifest.RootElement.GetProperty("bancos").EnumerateArray().Single(b=>b.GetProperty("nombre").GetString()==banco).GetProperty("codigo").GetString()!;
                var huellas=mes.Movimientos.Select(m=>ConciliacionBancariaService.Huella(cuenta,mes.Banco!.Value,m)).ToHashSet();
                Assert.Equal(42,semana.Movimientos.Count(m=>huellas.Contains(ConciliacionBancariaService.Huella(cuenta,semana.Banco!.Value,m))));
                using var wb=new XLWorkbook(Path.Combine(salida,banco+"_DEMO_01_mes.xlsx"));
                Assert.Equal("AlfaCore DEMO",wb.Properties.Author);Assert.Equal("AlfaCore DEMO",wb.Properties.LastModifiedBy);
                Assert.Single(wb.Worksheets);Assert.Empty(wb.Worksheets.First().CellsUsed().Where(c=>c.HasComment));
            }
            var docs=manifest.RootElement.GetProperty("documentos").EnumerateArray().ToArray();
            foreach(var recibo in docs.Where(d=>d.GetProperty("tc").GetString()=="CB"))
                Assert.Equal(decimal.Parse(recibo.GetProperty("importe").GetString()!,System.Globalization.CultureInfo.InvariantCulture),
                    recibo.GetProperty("aplicaciones").EnumerateArray().Sum(a=>decimal.Parse(a.GetProperty("importe").GetString()!,System.Globalization.CultureInfo.InvariantCulture)));
        }
        finally { if(Directory.Exists(salida))Directory.Delete(salida,true); }
    }
}
