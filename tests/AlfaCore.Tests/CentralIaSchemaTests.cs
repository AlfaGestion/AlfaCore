using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Esquema central de IA aplicado por la app (2026-10-05) y reimportación de consumos que quedaron en
/// archivos locales mientras la central no estaba disponible.
/// </summary>
public sealed class CentralIaSchemaTests
{
    [Fact]
    public void ScriptEmbebido_EsIdenticoAlDeReferencia()
    {
        var path = Path.Combine(FindRepositoryRoot(), "docs", "base-datos", "sql-referencia", "2026-10-05-002__alfa_central_ia_uso.sql");
        var referencia = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n');

        Assert.Equal(referencia, CentralIaSchema.Script.Replace("\r\n", "\n").TrimEnd('\n'));
    }

    [Fact]
    public void Lotes_SeparanPorGoYTerminanEnLaVista()
    {
        var lotes = CentralIaSchema.Lotes();

        Assert.Equal(3, lotes.Count);
        Assert.Contains("CREATE TABLE dbo.IA_USO", lotes[0]);
        Assert.Contains("CREATE TABLE dbo.IA_TOPE_CREDITOS", lotes[0]);
        Assert.Contains("CREATE TABLE dbo.IA_SOLICITUD_PLAN", lotes[0]);
        Assert.StartsWith("CREATE OR ALTER VIEW dbo.V_IA_USO_DIARIO", lotes[1].Trim());
        Assert.Contains("N'IA_INICIAL'", lotes[2]);
        Assert.Contains("BEGIN CATCH", lotes[2]);
        Assert.All(lotes, l => Assert.DoesNotMatch(@"(?im)^\s*GO\s*$", l));
    }

    [Fact]
    public void LeerPendientes_RecuperaLosRegistrosEIgnoraLineasDanadas()
    {
        var registro = new IaUsoRegistro(new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc), 4264, "AW_112012800",
            IaUsoFunciones.Bot, "gpt-4o-mini", 100, 0, 20, 0, 0, "conv:15");
        var proxy = registro with { IdBase = null, Funcion = IaUsoFunciones.Proxy, IdCliente = "112010001" };

        var leidos = IaUsoFlushService.LeerPendientes(
        [
            JsonSerializer.Serialize(registro),
            "{ esto no es json",
            "",
            JsonSerializer.Serialize(proxy)
        ]);

        Assert.Equal([registro, proxy], leidos);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AlfaCore")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
