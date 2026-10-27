using AlfaCore.Models;
using AlfaCore.Repositories;
using AlfaCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// 2026-10-22: AUX_ERR y la auditoría de todas las empresas se grababan en la base por defecto del servidor
/// (Alfa Net). Ahora van a la base de la empresa activa.
/// </summary>
public sealed class TenantLogConnectionTests
{
    private const string PorDefecto = "Server=default;Database=ALFANET2007";
    private const string DeLaEmpresa = "Server=tenant;Database=DW";

    private static IConfiguration Config()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AlfaGestion"] = PorDefecto
        }).Build();

    private static IServiceProvider Services(ISessionService? session)
    {
        var services = new ServiceCollection();
        if (session is not null)
            services.AddSingleton(session);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void UsaLaBaseDeLaEmpresaActiva()
        => Assert.Equal(DeLaEmpresa, TenantLogConnection.Resolve(Services(new SesionFalsa(() => DeLaEmpresa)), new HttpContextAccessor(), Config()));

    [Fact]
    public void SinSesionUsaLaBasePorDefecto()
    {
        Assert.Equal(PorDefecto, TenantLogConnection.Resolve(Services(null), new HttpContextAccessor(), Config()));
        Assert.Equal(PorDefecto, TenantLogConnection.Resolve(Services(new SesionFalsa(() => string.Empty)), new HttpContextAccessor(), Config()));
    }

    [Fact]
    public void SiResolverLaSesionFallaUsaLaBasePorDefecto()
        => Assert.Equal(PorDefecto, TenantLogConnection.Resolve(Services(new SesionFalsa(() => throw new InvalidOperationException("central caída"))), new HttpContextAccessor(), Config()));

    [Fact]
    public void SiResolverLaSesionVuelveARegistrarUnErrorNoHayBucle()
    {
        IServiceProvider? provider = null;
        string? interno = null;
        var session = new SesionFalsa(() =>
        {
            // Mientras se resuelve la sesión se registra otro error: ese va a la base por defecto.
            interno = TenantLogConnection.Resolve(provider!, new HttpContextAccessor(), Config());
            return DeLaEmpresa;
        });
        provider = Services(session);

        Assert.Equal(DeLaEmpresa, TenantLogConnection.Resolve(provider, new HttpContextAccessor(), Config()));
        Assert.Equal(PorDefecto, interno);
    }

    private sealed class SesionFalsa(Func<string> connection) : ISessionService
    {
        public string GetConnectionString() => connection();
        public SessionDto? GetActiveSession() => null;
        public void SetWebhookOverride(SessionDto session) { }
        public SessionDto? GetWebhookOverride(int expectedBaseId) => null;
        public void ClearWebhookOverride() { }
        public IReadOnlyList<SessionDto> GetAllSessions() => [];
        public void SwitchSession(Guid id) { }
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => Guid.Empty;
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) { }
        public void DeleteSession(Guid id) { }
        public void ClearActiveSession() { }
        public event Action? SessionChanged { add { } remove { } }
    }
}
