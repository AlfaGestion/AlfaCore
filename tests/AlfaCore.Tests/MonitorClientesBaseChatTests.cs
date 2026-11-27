using AlfaCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlfaCore.Tests;

public sealed class MonitorClientesBaseChatTests
{
    private static MonitorClientesService Servicio(string? alfaGestion)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AlfaGestion"] = alfaGestion })
            .Build();
        return new MonitorClientesService(config, null!, null!, NullLogger<MonitorClientesService>.Instance);
    }

    [Theory]
    [InlineData("Server=10.8.0.31;Database=ALFANET2007;User Id=x;Password=y", true)]
    [InlineData("Data Source=10.8.0.31 ;Initial Catalog=alfanet2007;Integrated Security=true", true)]
    [InlineData("Server=10.8.0.31;Database=ALFANET_DEMO;User Id=x;Password=y", false)]
    [InlineData("Server=10.8.0.10;Database=ALFANET2007;User Id=x;Password=y", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("esto no es una cadena de conexión", false)]
    public void ComparaServidorYBase(string? sesion, bool esperado)
        => Assert.Equal(esperado, Servicio("Server=10.8.0.31;Database=ALFANET2007;User Id=a;Password=b").EsBaseDelChat(sesion));

    [Fact]
    public void SinConexionAlfaGestionNuncaEsLaBaseDelChat()
        => Assert.False(Servicio(null).EsBaseDelChat("Server=10.8.0.31;Database=ALFANET2007"));
}
