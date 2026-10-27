using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Webhook de WhatsApp (2026-10-14): algunos eventos de Meta traen la fecha en milisegundos y
/// FromUnixTimeSeconds tiraba excepción, con lo que se perdía el webhook entero.
/// </summary>
public sealed class ConversacionesUnixTimestampTests
{
    [Fact]
    public void SegundosYMilisegundos_DanLaMismaFecha()
    {
        var segundos = ConversacionesService.ParseUnixTimestamp("1760000000");
        var milisegundos = ConversacionesService.ParseUnixTimestamp("1760000000000");

        Assert.Equal(segundos, milisegundos);
        Assert.Equal(2025, segundos.Year);
    }

    [Theory]
    [InlineData("99999999999999999")]
    [InlineData("-5")]
    [InlineData("no-es-numero")]
    [InlineData(null)]
    public void ValoresFueraDeRango_NoTiranExcepcion(string? valor)
    {
        var fecha = ConversacionesService.ParseUnixTimestamp(valor);

        Assert.True(Math.Abs((DateTime.Now - fecha).TotalDays) < 2);
    }
}
