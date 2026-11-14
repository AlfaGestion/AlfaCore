using System.IO.Compression;
using System.Text;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>El TXT de ARCA con observaciones: el zip trae OBSERVACIONES.txt y el nombre lo avisa; sin observaciones es el zip de siempre.</summary>
public sealed class LibroIvaObservacionesTests
{
    private static LibroIvaResultado Libro() => new()
    {
        Filtro = new LibroIvaFiltro { Compras = true, Desde = new DateTime(2026, 10, 1), Hasta = new DateTime(2026, 10, 31) },
        CuitEmisor = "30-50001091-2"
    };

    private static Dictionary<string, byte[]> Entradas(LibroIvaArchivo a)
    {
        using var zip = new ZipArchive(new MemoryStream(a.Contenido));
        return zip.Entries.ToDictionary(e => e.Name, e => { using var m = new MemoryStream(); using var s = e.Open(); s.CopyTo(m); return m.ToArray(); });
    }

    [Fact]
    public async Task SinObservacionesElZipTraeSoloLosDosArchivosDeArca()
    {
        var a = await LibroIvaService.ArmarZipAsync(Libro(), "cbte\r\n", "alic\r\n", null, CancellationToken.None);
        Assert.Equal("LibroIvaDigital_Compras_30500010912_202610.zip", a.Nombre);
        Assert.Equal(["LIBRO_IVA_DIGITAL_COMPRAS_ALICUOTAS.txt", "LIBRO_IVA_DIGITAL_COMPRAS_CBTE.txt"], Entradas(a).Keys.Order().ToArray());
    }

    [Fact]
    public async Task ConObservacionesElZipTraeElDetalleYElNombreLoAvisa()
    {
        var a = await LibroIvaService.ArmarZipAsync(Libro(), "cbte\r\n", "alic\r\n", "LIQC A 0005-00000100 · sin equivalencia ARCA", CancellationToken.None);
        Assert.EndsWith("_CON_OBSERVACIONES.zip", a.Nombre);
        var e = Entradas(a);
        Assert.Contains("OBSERVACIONES_COMPRAS.txt", e.Keys);
        Assert.Contains("LIQC A 0005-00000100", Encoding.UTF8.GetString(e["OBSERVACIONES_COMPRAS.txt"]));
    }

    [Fact]
    public void ElComprobanteDeLaObservacionSeArmaConPuntoDeVentaYNumero()
    {
        Assert.Equal("LIQC A 0005-00000100", new LibroIvaObservacion("1", "LIQC", "A", 5, 100, null, "x").Comprobante);
        Assert.Equal("General", new LibroIvaObservacion("", "", "", 0, 0, null, "CUIT del informante inválido").Comprobante);
    }
}
