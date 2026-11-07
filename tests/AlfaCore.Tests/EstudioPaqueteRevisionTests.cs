using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class EstudioPaqueteRevisionTests
{
    [Fact]
    public async Task ElCsvManualQuedaDisponibleSoloParaElMismoCuitYMes()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "AlfaCoreRevision_" + Guid.NewGuid().ToString("N"));
        try
        {
            var contenido = System.Text.Encoding.UTF8.GetBytes("Archivo de prueba");
            await ControlArcaService.ConservarArchivoAsync(carpeta, new() { Recibidos = true, Cuit = "30709390356" }, new(2026, 10, 1), ".csv", contenido, default);
            var archivo = Assert.Single(ArcaDescargaService.Listar(carpeta, "30709390356"));
            Assert.True(archivo.Recibidos);
            Assert.Equal("202610", archivo.Periodo);
            Assert.Empty(ArcaDescargaService.Listar(carpeta, "27228073259"));
            Assert.Equal(contenido, await File.ReadAllBytesAsync(Path.Combine(carpeta, archivo.Nombre)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ControlArcaService.ConservarArchivoAsync(carpeta,
                new() { Cuit = "../30709390356" }, new(2026, 10, 1), ".csv", contenido, default));
        }
        finally { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true); }
    }

    [Fact]
    public void UnDuplicadoNoCoincideAunqueLaSumaSeaIgualAArca()
    {
        var fecha = new DateTime(2026, 10, 2);
        var arca = new ArcaMisComprobantesArchivo { Recibidos = true, Comprobantes = [new() { Fecha = fecha,
            Clase = "FC", Letra = "A", PuntoVenta = 11, NumeroDesde = 21200, NumeroHasta = 21200, DocContraparte = "30709390356", Total = 100 }] };
        var sistema = new[] {
            new ControlArcaService.ComprobanteSistema("FC", "A", 11, 21200, fecha, 40, "30709390356", "Proveedor", "FCC 0011-00021200 A"),
            new ControlArcaService.ComprobanteSistema("FC", "A", 11, 21200, fecha, 60, "30709390356", "Proveedor", "FCC 0011-00021200 A") };
        var r = ControlArcaService.Comparar(arca, sistema, new(2026, 10, 1), new(2026, 10, 31));
        var fila = Assert.Single(r.Filas);
        Assert.Equal(0, fila.Diferencia);
        Assert.Equal(ControlArcaEstado.PosibleDuplicado, fila.Estado);
        Assert.NotNull(ArcaTransmisionRegistro.MensajeControl(fila.Estado));
    }

    [Fact]
    public void ElMismoNumeroEnDistintosCuitsInformantesNoEsDuplicado()
    {
        var r = new LibroIvaResultado {
            CuitEmisor = "30709390356", Filtro = new() { Desde = new(2026, 10, 1), Hasta = new(2026, 10, 31) },
            Unidades = [new("1", "Uno", "30709390356", 1, 100, 21, 121), new("2", "Dos", "27228073259", 1, 100, 21, 121)],
            Filas = new[] { "1", "2" }.Select(u => new LibroIvaFila { UnidadNegocio = u, Fecha = new(2026, 10, 2), Tc = "FC", Letra = "A",
                CodigoArca = 1, PuntoVenta = 1, Numero = 1, Cuit = "30709390356", RazonSocial = "Cliente", NetoGravado = 100, Total = 121,
                Alicuotas = [new(21, 100, 21)] }).ToList() };
        Assert.DoesNotContain(LibroIvaArcaValidator.Validar(r), x => x.Contains("duplicado"));
    }

    [Fact]
    public void RevisionIdentificaFilaYCuentaYElCsvNoEjecutaFormulas()
    {
        var f = new LibroIvaFila { Fecha = new(2026, 10, 2), Tc = "FCC", Letra = "A", CodigoArca = 1, PuntoVenta = 1, Numero = 2,
            Cuenta = "211010692", Cuit = "30709390356", RazonSocial = "=SUM(1;1)", NetoGravado = 100, Total = 122, Alicuotas = [new(21, 100, 21)] };
        var r = new LibroIvaResultado { CuitEmisor = f.Cuit, Filtro = new() { Compras = true, Desde = new(2026, 10, 1), Hasta = new(2026, 10, 31) }, Filas = [f] };
        var observaciones = new List<EstudioPaqueteObservacion>();
        LibroIvaArcaValidator.Validar(r, (fila, detalle) => {
            Assert.Same(f, fila);
            observaciones.Add(new("Compras", EstudioPaqueteService.TipoObservacion(detalle), "1", f.Fecha, f.Tc, f.Letra, f.PuntoVenta,
                f.Numero, f.Cuenta, f.Cuit, f.RazonSocial, detalle, f.Total));
        });
        Assert.Contains(observaciones, o => o.Tipo == "Diferencia de totales" && o.Cuenta == "211010692");
        var csv = EstudioPaqueteService.RevisionCsv(new() { Observaciones = observaciones });
        Assert.Contains("\"'=SUM(1;1)\"", csv);
        Assert.Contains("211010692", csv);
    }
}
