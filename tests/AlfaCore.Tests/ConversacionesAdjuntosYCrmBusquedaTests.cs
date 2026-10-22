using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// 2026-10-22, incidentes 12079-12083 de Alfa Net:
/// - CRM: buscar cliente en una oportunidad tiraba la pantalla ("No se pudieron cargar los clientes") cuando
///   la búsqueda anterior se cancelaba a mitad de camino porque el usuario seguía escribiendo.
/// - Conversaciones: un adjunto cuyo contenido no se guarda en la base fallaba con "conversión implícita
///   nvarchar a varbinary(max)" y quedaba sin archivo (audios en 0:00).
/// </summary>
public sealed class ConversacionesAdjuntosYCrmBusquedaTests
{
    private static string Leer(params string[] partes)
        => File.ReadAllText(Path.Combine([FindRepoRoot(), "src", "AlfaCore", .. partes]));

    [Fact]
    public void ElContenidoDelAdjuntoSeMandaComoBinarioAunqueVengaVacio()
    {
        var source = Leer("Services", "ConversacionesService.cs");
        Assert.DoesNotContain("AddWithValue(\"@ArchivoContenido\"", source, StringComparison.Ordinal);
        Assert.Equal(2, source.Split("Parameters.Add(\"@ArchivoContenido\", System.Data.SqlDbType.VarBinary, -1)").Length - 1);
    }

    [Fact]
    public void UnaBusquedaDeClientesCanceladaNoEsUnError()
    {
        var source = Leer("Services", "CuentasComercialesService.cs");
        Assert.Equal(2, source.Split("catch (Exception) when (ct.IsCancellationRequested)").Length - 1);
    }

    [Fact]
    public void LaOportunidadNoSeCaeSiFallaLaBusquedaDeCliente()
    {
        var source = Leer("Components", "Pages", "Crm.razor");
        Assert.Contains("new Feedback(\"No se pudo buscar el cliente\"", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        return current!.FullName;
    }
}
