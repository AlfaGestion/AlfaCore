using AlfaCore.Services;
using Xunit;
using static AlfaCore.Services.ReunionesPublicasLimite;

namespace AlfaCore.Tests;

/// <summary>Reservas públicas (2026-10-09): un mismo cliente, una reunión por semana.</summary>
public sealed class ReunionesPublicasLimiteTests
{
    // Martes 13 y jueves 15 de octubre de 2026 (misma semana); lunes 19 (semana siguiente).
    private static readonly ReservaCliente Existente = new(new DateTime(2026, 10, 13, 10, 0, 0), "cliente@empresa.com", "+54 9 11 4444-5555", "Empresa SA");

    [Fact]
    public void MismoEmail_MismaSemana_NoPuedeReservar()
        => Assert.NotNull(Verificar(new(new DateTime(2026, 10, 15, 11, 0, 0), "CLIENTE@empresa.com ", "", ""), [Existente]));

    [Fact]
    public void MismoTelefonoConOtroFormato_MismaSemana_NoPuedeReservar()
        => Assert.NotNull(Verificar(new(new DateTime(2026, 10, 18, 11, 0, 0), "otro@mail.com", "1144445555", ""), [Existente]));

    [Fact]
    public void MismaRazonSocial_MismaSemana_NoPuedeReservar()
        => Assert.NotNull(Verificar(new(new DateTime(2026, 10, 14, 9, 0, 0), "otro@mail.com", "", "empresa s.a."), [Existente]));

    [Fact]
    public void SemanaSiguiente_PuedeReservar()
        => Assert.Null(Verificar(new(new DateTime(2026, 10, 19, 9, 0, 0), "cliente@empresa.com", "", ""), [Existente]));

    [Fact]
    public void OtroCliente_MismaSemana_PuedeReservar()
        => Assert.Null(Verificar(new(new DateTime(2026, 10, 15, 9, 0, 0), "otro@mail.com", "1122223333", "Otra SRL"), [Existente]));

    [Fact]
    public void Mensaje_IndicaDesdeCuandoPuedeVolverAReservar()
        => Assert.Contains("19/10", Verificar(new(new DateTime(2026, 10, 15, 11, 0, 0), "cliente@empresa.com", "", ""), [Existente]));
}
