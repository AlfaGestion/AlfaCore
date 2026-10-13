using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Eventos repetidos del Calendario (2026-10-09): fechas de la serie y su descripción.</summary>
public sealed class CalendarioRepeticionTests
{
    // Martes 6 de octubre de 2026, 16:00.
    private static readonly DateTime Inicio = new(2026, 10, 6, 16, 0, 0);

    private static CalendarioRepeticionDto Regla(string frecuencia, int intervalo = 1, string fin = CalendarioFinesRepeticion.Veces, int veces = 5)
        => new() { Frecuencia = frecuencia, Intervalo = intervalo, Fin = fin, FinVeces = veces };

    [Fact]
    public void SinRepeticion_SoloElEventoOriginal()
        => Assert.Equal([Inicio], CalendarioRepeticion.GenerarInicios(Inicio, new CalendarioRepeticionDto()));

    [Fact]
    public void CadaDosSemanas_ElMismoDiaYHora()
    {
        var inicios = CalendarioRepeticion.GenerarInicios(Inicio, Regla(CalendarioFrecuencias.Semanal, 2, veces: 3));

        Assert.Equal([Inicio, Inicio.AddDays(14), Inicio.AddDays(28)], inicios);
    }

    [Fact]
    public void DiasHabiles_LunesAViernesSinFinDeSemana()
    {
        var regla = Regla(CalendarioFrecuencias.Semanal, veces: 6);
        regla.DiasSemana = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

        var inicios = CalendarioRepeticion.GenerarInicios(Inicio, regla);

        Assert.Equal(6, inicios.Count);
        Assert.DoesNotContain(inicios, d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.Equal(new DateTime(2026, 10, 12, 16, 0, 0), inicios[4]); // salta sábado y domingo
        Assert.All(inicios, d => Assert.Equal(16, d.Hour));
    }

    [Fact]
    public void Mensual_PorDiaDelMes_AjustaMesesCortos()
    {
        var inicio = new DateTime(2026, 1, 31, 9, 0, 0);

        var inicios = CalendarioRepeticion.GenerarInicios(inicio, Regla(CalendarioFrecuencias.Mensual, veces: 3));

        Assert.Equal(new DateTime(2026, 2, 28, 9, 0, 0), inicios[1]);
        Assert.Equal(new DateTime(2026, 3, 31, 9, 0, 0), inicios[2]);
    }

    [Fact]
    public void Mensual_PrimerMartes()
    {
        var regla = Regla(CalendarioFrecuencias.Mensual, veces: 3);
        regla.ModoMensual = CalendarioModosMensuales.DiaSemana;

        var inicios = CalendarioRepeticion.GenerarInicios(Inicio, regla); // 6/10 es el primer martes

        Assert.Equal(new DateTime(2026, 11, 3, 16, 0, 0), inicios[1]);
        Assert.Equal(new DateTime(2026, 12, 1, 16, 0, 0), inicios[2]);
    }

    [Fact]
    public void FinEnFecha_IncluyeElUltimoDia()
    {
        var regla = Regla(CalendarioFrecuencias.Semanal, fin: CalendarioFinesRepeticion.Fecha);
        regla.FinFecha = new DateTime(2026, 10, 27);

        var inicios = CalendarioRepeticion.GenerarInicios(Inicio, regla);

        Assert.Equal(4, inicios.Count);
        Assert.Equal(new DateTime(2026, 10, 27, 16, 0, 0), inicios[^1]);
    }

    [Fact]
    public void NoFinaliza_GeneraUnAnioYNuncaPasaElTope()
    {
        var semanal = CalendarioRepeticion.GenerarInicios(Inicio, Regla(CalendarioFrecuencias.Semanal, fin: CalendarioFinesRepeticion.Nunca));
        var diaria = CalendarioRepeticion.GenerarInicios(Inicio, Regla(CalendarioFrecuencias.Diaria, fin: CalendarioFinesRepeticion.Nunca));

        Assert.Equal(53, semanal.Count);
        Assert.True(semanal[^1] <= Inicio.AddYears(1));
        Assert.True(diaria.Count <= CalendarioRepeticion.MaximoOcurrencias);
    }

    [Fact]
    public void Describir_TextosComoGoogleCalendar()
    {
        Assert.Equal("No se repite", CalendarioRepeticion.Describir(Inicio, null));
        Assert.Equal("Cada 2 semanas el martes, 5 veces",
            CalendarioRepeticion.Describir(Inicio, Regla(CalendarioFrecuencias.Semanal, 2)));

        var habiles = Regla(CalendarioFrecuencias.Semanal, fin: CalendarioFinesRepeticion.Nunca);
        habiles.DiasSemana = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
        Assert.Equal("Días hábiles (lunes a viernes)", CalendarioRepeticion.Describir(Inicio, habiles));

        var ordinal = Regla(CalendarioFrecuencias.Mensual, fin: CalendarioFinesRepeticion.Nunca);
        ordinal.ModoMensual = CalendarioModosMensuales.DiaSemana;
        Assert.Equal("Todos los meses el primer martes", CalendarioRepeticion.Describir(Inicio, ordinal));
    }

    [Fact]
    public void Regla_SeGuardaYSeLeeIgual()
    {
        var regla = Regla(CalendarioFrecuencias.Semanal, 2);
        regla.DiasSemana = [DayOfWeek.Monday, DayOfWeek.Thursday];

        var leida = CalendarioRepeticion.Deserializar(CalendarioRepeticion.Serializar(regla));

        Assert.NotNull(leida);
        Assert.Equal(CalendarioRepeticion.Serializar(regla), CalendarioRepeticion.Serializar(leida!));
        Assert.Null(CalendarioRepeticion.Deserializar("{no es json"));
    }

    [Fact]
    public void SemanaEntera_RepetidaTodosLosDias_SeAcortaParaNoPisarse()
    {
        var habiles = Regla(CalendarioFrecuencias.Semanal, fin: CalendarioFinesRepeticion.Nunca);
        habiles.DiasSemana = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
        var inicio = new DateTime(2026, 10, 9, 8, 0, 0);

        // Una semana, todo el día, repitiendo días hábiles: se pisan; el fin queda el mismo día.
        Assert.Equal(new DateTime(2026, 10, 9, 22, 0, 0),
            CalendarioRepeticion.FinSinSuperponer(inicio, inicio.AddDays(6).AddHours(14), true, habiles));
        // Una semana repitiéndose cada semana: no se pisan.
        Assert.Null(CalendarioRepeticion.FinSinSuperponer(inicio, inicio.AddDays(6).AddHours(14), true,
            Regla(CalendarioFrecuencias.Semanal, fin: CalendarioFinesRepeticion.Nunca)));
        // Una hora, todos los días: no se pisan.
        Assert.Null(CalendarioRepeticion.FinSinSuperponer(inicio, inicio.AddHours(1), false, Regla(CalendarioFrecuencias.Diaria)));
    }

    [Fact]
    public void Rotacion_SeDetectaDesdeLosEventosDeLaSerie()
    {
        Assert.Equal(["A", "B", "C"], CalendarioRepeticion.DetectarRotacion(["A", "B", "C", "A", "B", "C", "A"]));
        Assert.Empty(CalendarioRepeticion.DetectarRotacion(["A", "A", "A"]));
        Assert.Equal(["A", "B"], CalendarioRepeticion.DetectarRotacion(["A", "B", "A"]));
        // Un evento cambiado a mano: las personas en orden de aparición.
        Assert.Equal(["A", "B", "C"], CalendarioRepeticion.DetectarRotacion(["A", "B", "C", "A", "C", "C"]));
        Assert.Equal(["B", "C", "A"], CalendarioRepeticion.EmpezarDesde(["A", "B", "C"], "B"));
    }

    [Fact]
    public void Responsable_UsuarioDelSistema_SeReconocePorElPrefijo()
    {
        Assert.True(CalendarioResponsables.EsUsuario(CalendarioResponsables.IdUsuario(" info@empresa.com "), out var usuario));
        Assert.Equal("info@empresa.com", usuario);
        Assert.False(CalendarioResponsables.EsUsuario("  12", out _)); // técnico
        Assert.False(CalendarioResponsables.EsUsuario("U:", out _));
        Assert.False(CalendarioResponsables.EsUsuario(null, out _));
    }
}
