using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Monitor de Clientes, fase 2 (2026-10-28): seguimiento y reglas del aviso al chat de soporte.</summary>
public sealed class MonitorClientesAvisosTests
{
    private static readonly DateTime Ahora = new(2026, 10, 28, 15, 0, 0);

    private static MonitorFilaDto Fila(int origen, string tipo, string destino, MonitorEstado estado, string prioridad = "1",
        string cliente = "C1", string razon = "CLIENTE UNO")
        => new()
        {
            IdOrigen = origen,
            TipoCodigo = tipo,
            TipoNombre = tipo,
            Destino = destino,
            Estado = estado,
            PrioridadCodigo = prioridad,
            ClienteCodigo = cliente,
            RazonSocial = razon,
            Motivo = "Sin OK hace 3 h"
        };

    private static MonitorAvisoConfigDto Config(string firma = "", DateTime? ultimoEnvio = null, int repetir = 120)
        => new() { Prioridades = "1,2", RepetirMinutos = repetir, UltimaFirma = firma, UltimoEnvio = ultimoEnvio };

    [Fact]
    public void Clave_NoDistingueMayusculasNiEspacios()
        => Assert.Equal(MonitorClave.Armar(5, "envio_pdv", " server1\\alfanet/base "), MonitorClave.Armar(5, "ENVIO_PDV", "SERVER1\\ALFANET/BASE"));

    [Fact]
    public void Pendientes_SoloCriticosDePrioridadConfiguradaSinTomarNiSilenciar()
    {
        var tomada = Fila(1, "BACKUP", "", MonitorEstado.Critico);
        tomada.TomadoPor = "alberto";
        var silenciada = Fila(1, "ENVIO_PDV", "A", MonitorEstado.Critico);
        silenciada.Silenciado = true;
        var filas = new[]
        {
            Fila(1, "ENVIO_PDV", "B", MonitorEstado.Critico),
            Fila(2, "ENVIO_PDV", "B", MonitorEstado.Critico, prioridad: "3"),
            Fila(1, "RECEPCION_PDV", "B", MonitorEstado.Aviso),
            tomada,
            silenciada
        };

        var pendientes = MonitorClientesAvisos.Pendientes(filas, Config());

        var p = Assert.Single(pendientes);
        Assert.Equal("ENVIO_PDV", p.TipoCodigo);
        Assert.Equal("B", p.Destino);
        Assert.Equal(1, p.IdOrigen);
    }

    [Fact]
    public void Pendientes_SinPrioridadesConfiguradas_TomaTodas()
    {
        var filas = new[] { Fila(1, "BACKUP", "", MonitorEstado.Critico, prioridad: "4"), Fila(2, "BACKUP", "", MonitorEstado.Critico, prioridad: "") };
        Assert.Equal(2, MonitorClientesAvisos.Pendientes(filas, new MonitorAvisoConfigDto { Prioridades = "" }).Count);
    }

    [Fact]
    public void Decidir_PrimeraVezConAlertas_Avisa()
        => Assert.Equal(MonitorClientesAvisos.Decision.Alertas,
            MonitorClientesAvisos.Decidir([Fila(1, "BACKUP", "", MonitorEstado.Critico)], Config(), Ahora, forzar: false));

    [Fact]
    public void Decidir_MismasAlertasAntesDeRepetir_NoAvisa()
    {
        var f = Fila(1, "BACKUP", "", MonitorEstado.Critico);
        var config = Config(firma: f.Clave, ultimoEnvio: Ahora.AddMinutes(-30));
        Assert.Equal(MonitorClientesAvisos.Decision.Nada, MonitorClientesAvisos.Decidir([f], config, Ahora, forzar: false));
    }

    [Fact]
    public void Decidir_MismasAlertasPasadoElRepetir_AvisaDeNuevo()
    {
        var f = Fila(1, "BACKUP", "", MonitorEstado.Critico);
        var config = Config(firma: f.Clave, ultimoEnvio: Ahora.AddMinutes(-121));
        Assert.Equal(MonitorClientesAvisos.Decision.Alertas, MonitorClientesAvisos.Decidir([f], config, Ahora, forzar: false));
    }

    [Fact]
    public void Decidir_RepetirCero_SoloAvisaAlertasNuevas()
    {
        var vieja = Fila(1, "BACKUP", "", MonitorEstado.Critico);
        var config = Config(firma: vieja.Clave, ultimoEnvio: Ahora.AddDays(-3), repetir: 0);
        Assert.Equal(MonitorClientesAvisos.Decision.Nada, MonitorClientesAvisos.Decidir([vieja], config, Ahora, forzar: false));

        var nueva = Fila(2, "BACKUP", "", MonitorEstado.Critico);
        Assert.Equal(MonitorClientesAvisos.Decision.Alertas, MonitorClientesAvisos.Decidir([vieja, nueva], config, Ahora, forzar: false));
    }

    [Fact]
    public void Decidir_SeVacianLasAlertas_AvisaNormalizadoUnaSolaVez()
    {
        var f = Fila(1, "BACKUP", "", MonitorEstado.Critico);
        Assert.Equal(MonitorClientesAvisos.Decision.Normalizado,
            MonitorClientesAvisos.Decidir([], Config(firma: f.Clave, ultimoEnvio: Ahora.AddMinutes(-10)), Ahora, forzar: false));
        Assert.Equal(MonitorClientesAvisos.Decision.Nada,
            MonitorClientesAvisos.Decidir([], Config(firma: "", ultimoEnvio: Ahora.AddMinutes(-5)), Ahora, forzar: false));
    }

    [Fact]
    public void Decidir_Forzado_SiempreManda()
    {
        var f = Fila(1, "BACKUP", "", MonitorEstado.Critico);
        Assert.Equal(MonitorClientesAvisos.Decision.Alertas,
            MonitorClientesAvisos.Decidir([f], Config(firma: f.Clave, ultimoEnvio: Ahora.AddMinutes(-1)), Ahora, forzar: true));
        Assert.Equal(MonitorClientesAvisos.Decision.Normalizado, MonitorClientesAvisos.Decidir([], Config(), Ahora, forzar: true));
    }

    [Fact]
    public void Firma_EsEstableYSinRepetidos()
    {
        var a = Fila(2, "BACKUP", "", MonitorEstado.Critico);
        var b = Fila(1, "ENVIO_PDV", "X", MonitorEstado.Critico);
        Assert.Equal(MonitorClientesAvisos.Firma([a, b, a]), MonitorClientesAvisos.Firma([b, a]));
        Assert.Equal(2, MonitorClientesAvisos.ClavesDeFirma(MonitorClientesAvisos.Firma([a, b])).Count);
    }

    [Fact]
    public void MensajeAlertas_MarcaLasNuevasYLimitaLasLineas()
    {
        var vieja = Fila(1, "BACKUP", "", MonitorEstado.Critico);
        var filas = new List<MonitorFilaDto> { vieja };
        for (var i = 0; i < MonitorClientesAvisos.MaxLineas + 2; i++)
            filas.Add(Fila(10 + i, "ENVIO_PDV", $"PDV{i}", MonitorEstado.Critico, cliente: $"C{i}", razon: $"CLIENTE {i}"));

        var texto = MonitorClientesAvisos.MensajeAlertas(filas, MonitorClientesAvisos.ClavesDeFirma(vieja.Clave), Config());

        Assert.StartsWith("🔴 Monitor de Clientes: 18 alertas críticas sin tomar", texto);
        Assert.Contains("P1/P2", texto);
        Assert.Contains("• P1 CLIENTE UNO — BACKUP: Sin OK hace 3 h\n", texto);
        Assert.Contains("CLIENTE 0 — ENVIO_PDV PDV0: Sin OK hace 3 h (nueva)", texto);
        Assert.Contains("… y 3 más.", texto);
        Assert.DoesNotContain("CLIENTE 16", texto);
    }

    [Fact]
    public void TextoPush_NombraHastaTresClientes()
    {
        var filas = Enumerable.Range(0, 5).Select(i => Fila(i, "BACKUP", "", MonitorEstado.Critico, razon: $"CLI{i}")).ToList();
        Assert.Equal("5 alertas críticas: CLI0, CLI1, CLI2 y 2 más", MonitorClientesAvisos.TextoPush(filas));
    }

    [Fact]
    public void AplicarSeguimiento_SilencioVencidoNoCuentaYTomadoSeMuestra()
    {
        var filas = new[] { Fila(1, "BACKUP", "", MonitorEstado.Critico), Fila(1, "ENVIO_PDV", "x", MonitorEstado.Critico) };
        MonitorClientesService.AplicarSeguimiento(filas,
        [
            new MonitorClientesService.SeguimientoFila { IdOrigen = 1, TipoCodigo = "backup", Destino = "", TomadoPor = "alberto", SilenciadoHasta = Ahora.AddHours(-1) },
            new MonitorClientesService.SeguimientoFila { IdOrigen = 1, TipoCodigo = "ENVIO_PDV", Destino = "X", SilenciadoHasta = Ahora.AddHours(2), Nota = " llamar " }
        ], Ahora);

        Assert.True(filas[0].Tomado);
        Assert.False(filas[0].Silenciado);
        Assert.Null(filas[0].SilenciadoHasta);
        Assert.True(filas[1].Silenciado);
        Assert.Equal("llamar", filas[1].Nota);
        Assert.False(filas[1].Tomado);
    }
}
