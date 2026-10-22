using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// 2026-10-22: soporte dejaba la pestaña abierta y a la mañana (o 2-3 veces por día) AlfaCore "no los
/// identificaba": las sesiones vivían sólo en memoria y cada reinicio del proceso (IIS, deploy) las borraba.
/// </summary>
public sealed class AppUserSessionStoreTests
{
    private static readonly IDataProtectionProvider Claves = new EphemeralDataProtectionProvider();

    private static AppUserSessionInfo Sesion() => new()
    {
        UserName = "soporte@alfanet.com.ar",
        Email = "soporte@alfanet.com.ar",
        CentralLogin = "soporte@alfanet.com.ar",
        IdCliente = "ALFANET",
        IdWeb = "ALFANET",
        RequiresInternalLogin = false,
        AuthorizedSessionId = Guid.Parse("00000000-0000-0000-0000-000000000106")
    };

    [Fact]
    public void LaSesionSobreviveAUnReinicioDelServidor()
    {
        var token = new AppUserSessionStore(Claves).Store(Sesion());

        var despuesDelReinicio = new AppUserSessionStore(Claves);
        Assert.True(despuesDelReinicio.TryGet(token, out var info));
        Assert.Equal("soporte@alfanet.com.ar", info!.UserName);
        Assert.False(info.RequiresInternalLogin);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000106"), info.AuthorizedSessionId);
    }

    [Fact]
    public void AlLoguearseEnLaBaseElTokenNuevoTraeLaAutorizacion()
    {
        var store = new AppUserSessionStore(Claves);
        var inicial = Sesion();
        var token = store.Store(new AppUserSessionInfo { UserName = inicial.UserName, RequiresInternalLogin = true });

        var nuevo = store.Reissue(token, inicial);

        Assert.NotEqual(token, nuevo);
        Assert.True(new AppUserSessionStore(Claves).TryGet(nuevo, out var info));
        Assert.False(info!.RequiresInternalLogin);
        Assert.True(store.TryGet(token, out var viejo)); // las otras pestañas siguen andando
        Assert.False(viejo!.RequiresInternalLogin);
    }

    [Fact]
    public void UnTokenAdulteradoOAjenoNoSirve()
    {
        var token = new AppUserSessionStore(Claves).Store(Sesion());
        var store = new AppUserSessionStore(Claves);

        Assert.False(store.TryGet(token[..^4] + "AAAA", out _));
        Assert.False(store.TryGet(AppUserSessionStore.ProtectedPrefix + "basura", out _));
        Assert.False(new AppUserSessionStore(new EphemeralDataProtectionProvider()).TryGet(token, out _));
        Assert.False(store.TryGet(Guid.NewGuid().ToString("N"), out _));
    }

    [Fact]
    public void CerrarSesionInvalidaElToken()
    {
        var store = new AppUserSessionStore(Claves);
        var token = store.Store(Sesion());

        store.Remove(token);

        Assert.False(store.TryGet(token, out _));
    }

    [Fact]
    public void SinDataProtectionSigueFuncionandoComoAntes()
    {
        var store = new AppUserSessionStore();
        var token = store.Store(Sesion());
        Assert.True(store.TryGet(token, out _));
        Assert.False(token.StartsWith(AppUserSessionStore.ProtectedPrefix, StringComparison.Ordinal));
    }
}
