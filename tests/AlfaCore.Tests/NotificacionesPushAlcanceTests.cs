using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Push (2026-10-06): sin preferencia guardada el alcance es "todas las accesibles"; con "asignadas"
/// una conversación nueva sin técnico nunca generaba push.
/// </summary>
public sealed class NotificacionesPushAlcanceTests
{
    [Fact]
    public void PreferenciasPorDefecto_SonTodasLasAccesibles()
    {
        Assert.Equal(NotificacionesPushScopes.Accesibles, new NotificacionesPushPreferencesDto().Alcance);
        Assert.Equal(NotificacionesPushScopes.Accesibles, NotificacionesPushService.NormalizePreferences(null).Alcance);
    }

    [Theory]
    [InlineData("", "accesibles")]
    [InlineData("cualquiera", "accesibles")]
    [InlineData("asignadas", "asignadas")]
    [InlineData("ASIGNADAS", "asignadas")]
    public void Normalizar_SoloRespetaAsignadasSiSeEligio(string alcance, string esperado)
    {
        var normalizado = NotificacionesPushService.NormalizePreferences(new NotificacionesPushPreferencesDto { Alcance = alcance });

        Assert.Equal(esperado, normalizado.Alcance, ignoreCase: true);
    }
}
