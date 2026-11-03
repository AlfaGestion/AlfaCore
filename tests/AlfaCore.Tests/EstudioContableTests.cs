using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class EstudioContableTests
{
    [Fact]
    public void ScriptCentralEsIdenticoAlDeReferencia()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "src", "AlfaCore", "AlfaCore.csproj"))) dir = Directory.GetParent(dir)?.FullName ?? throw new InvalidOperationException("No se encontró el repositorio.");
        var referencia = File.ReadAllText(Path.Combine(dir, "docs", "base-datos", "sql-referencia", "2026-11-03-001__alfa_central_estudios.sql")).Replace("\r\n", "\n").TrimEnd('\n');
        Assert.Equal(referencia, CentralEstudiosSchema.Script.Replace("\r\n", "\n").TrimEnd('\n'));
        Assert.DoesNotMatch(@"(?im)^\s*GO\s*$", CentralEstudiosSchema.Script);
    }

    [Theory]
    [InlineData("Contador@Estudio.com.ar", "contador@estudio.com.ar")]
    [InlineData("  ana.perez@gmail.com ", "ana.perez@gmail.com")]
    public void EmailValido(string email, string esperado) => Assert.Equal(esperado, EstudioAccesosService.NormalizarEmail(email));

    [Theory]
    [InlineData("")]
    [InlineData("sinarroba")]
    [InlineData("a@b")]
    [InlineData("@estudio.com")]
    [InlineData("a b@estudio.com")]
    [InlineData("a@@estudio.com")]
    public void EmailInvalido(string email) => Assert.ThrowsAny<InvalidOperationException>(() => EstudioAccesosService.NormalizarEmail(email));

    [Theory]
    [InlineData("ana.perez@gmail.com", "EST_ana.perez")]
    [InlineData("estudio+fiscal@x.com", "EST_estudiofiscal")]
    [InlineData("@x.com", "EST_CONTADOR")]
    public void UsuarioDesdeEmail(string email, string esperado) => Assert.Equal(esperado, EstudioAccesosService.UsuarioDesdeEmail(email));

    [Fact]
    public void UsuarioDesdeEmailNoPasaDe30()
        => Assert.Equal(30, EstudioAccesosService.UsuarioDesdeEmail(new string('a', 80) + "@x.com").Length);

    [Fact]
    public void PerfilesSoloConClavesDeMenuYCargaIncluyeConsulta()
    {
        Assert.Equal(["CARGA", "CONSULTA"], EstudioContableCentral.Perfiles.Keys.OrderBy(k => k));
        Assert.All(EstudioContableCentral.Perfiles.Values.SelectMany(v => v), m => Assert.Matches("^D[0-9]+$", m));
        Assert.Subset(EstudioContableCentral.Perfiles["CARGA"].ToHashSet(), EstudioContableCentral.Perfiles["CONSULTA"].ToHashSet());
        Assert.Contains("D9832", EstudioContableCentral.Perfiles["CONSULTA"]);
        Assert.DoesNotContain("D016023", EstudioContableCentral.Perfiles["CONSULTA"]);
        // La lista literal descarta cualquier cosa que no sea una clave (no hay inyección posible).
        Assert.Equal("(VALUES (N'D6009'),(N'D5004')) m(Clave)", EstudioAccesosService.ValoresMenus(["D6009", "D5004", "x'); DROP TABLE y;--"]));
    }

    [Fact]
    public void TokenYHash()
    {
        var t = EstudioContableCentral.NuevoToken();
        Assert.Equal(32, t.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", t);
        Assert.NotEqual(t, EstudioContableCentral.NuevoToken());
        Assert.Equal(64, EstudioContableCentral.Hash(t).Length);
        Assert.Equal(EstudioContableCentral.Hash(t), EstudioContableCentral.Hash(t));
        Assert.EndsWith("/estudio/invitacion/abc", EstudioAccesosService.Enlace("https://x/ALFANET/", "abc"));
        Assert.Equal("https://x/ALFANET/estudio/invitacion/abc", EstudioAccesosService.Enlace("https://x/ALFANET/", "abc"));
    }

    [Fact]
    public void EstadoDelMes()
    {
        var e = new EstudioEstadoMes { Mes = new DateTime(2026, 10, 1), CierreVentas = new DateTime(2026, 10, 31), CierreCompras = new DateTime(2026, 10, 15) };
        Assert.True(e.VentasCerradas);
        Assert.False(e.ComprasCerradas);
        Assert.False(new EstudioEstadoMes { Mes = new DateTime(2026, 10, 1) }.VentasCerradas);
    }

    [Fact]
    public void RutaConBaseHabilitadaSeAceptaConElIdwebDelEstudio()
    {
        // Las bases habilitadas al estudio entran en userHasBase (LoadSaaSSessionsAsync); con el idweb del dueño ya se aceptaba.
        Assert.True(ConexionClienteService.IsRouteTenantAccepted("estudioperez", null, "estudioperez", userHasBase: true));
        Assert.True(ConexionClienteService.IsRouteTenantAccepted("empresa", "empresa", "estudioperez", userHasBase: false));
        Assert.False(ConexionClienteService.IsRouteTenantAccepted("estudioperez", "empresa", "estudioperez", userHasBase: false));
    }
}
