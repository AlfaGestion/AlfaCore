using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConversacionesAdjuntosAlmacenTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "alfacore-adjuntos-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
            Directory.Delete(_raiz, recursive: true);
    }

    [Fact]
    public async Task MismoContenidoEnVariosMensajesOcupaUnSoloArchivo()
    {
        var pdf = new byte[256 * 1024];
        Random.Shared.NextBytes(pdf);

        var rutas = new List<string>();
        for (var i = 0; i < 5; i++)
            rutas.Add(await ConversacionesAdjuntosAlmacen.GuardarAsync(_raiz, pdf, $"Catalogo {i}.PDF", CancellationToken.None));

        Assert.Single(rutas.Distinct(StringComparer.OrdinalIgnoreCase));
        var archivos = Directory.GetFiles(_raiz, "*", SearchOption.AllDirectories);
        Assert.Single(archivos);
        Assert.Equal(pdf, await File.ReadAllBytesAsync(archivos[0]));
        Assert.EndsWith(".pdf", archivos[0]);
        Assert.Contains(Path.Combine(_raiz, ConversacionesAdjuntosAlmacen.CarpetaContenido), archivos[0]);
    }

    [Fact]
    public async Task ContenidoDistintoVaAArchivosDistintos()
    {
        var a = await ConversacionesAdjuntosAlmacen.GuardarAsync(_raiz, [1, 2, 3], "a.jpg", CancellationToken.None);
        var b = await ConversacionesAdjuntosAlmacen.GuardarAsync(_raiz, [1, 2, 4], "a.jpg", CancellationToken.None);

        Assert.NotEqual(a, b);
        Assert.Equal(2, Directory.GetFiles(_raiz, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task GuardadosEnParaleloDelMismoArchivoNoDejanTemporalesNiFallan()
    {
        var audio = new byte[64 * 1024];
        Random.Shared.NextBytes(audio);

        var rutas = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => ConversacionesAdjuntosAlmacen.GuardarAsync(_raiz, audio, "nota.ogg", CancellationToken.None))));

        Assert.Single(rutas.Distinct());
        Assert.Single(Directory.GetFiles(_raiz, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ArchivoIncompletoSeReescribe()
    {
        var contenido = new byte[] { 9, 8, 7, 6, 5 };
        var ruta = await ConversacionesAdjuntosAlmacen.GuardarAsync(_raiz, contenido, "x.bin", CancellationToken.None);
        await File.WriteAllBytesAsync(ruta, [9, 8]);

        var otra = await ConversacionesAdjuntosAlmacen.GuardarAsync(_raiz, contenido, "x.bin", CancellationToken.None);

        Assert.Equal(ruta, otra);
        Assert.Equal(contenido, await File.ReadAllBytesAsync(ruta));
    }

    [Theory]
    [InlineData("factura.PDF", ".pdf")]
    [InlineData("sin-extension", "")]
    [InlineData("raro.p d/f", "")]
    [InlineData("muy.largaextension123", "")]
    [InlineData(".ogg", ".ogg")]
    public async Task LaExtensionSeLimpia(string nombre, string esperada)
    {
        var ruta = await ConversacionesAdjuntosAlmacen.GuardarAsync(_raiz, [1], nombre, CancellationToken.None);

        Assert.Equal(esperada, Path.GetExtension(ruta));
        Assert.StartsWith(_raiz, Path.GetFullPath(ruta));
    }
}
