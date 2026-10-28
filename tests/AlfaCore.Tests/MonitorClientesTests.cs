using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Monitor de Clientes (2026-10-28): formato del reporte del legacy y evaluación de estados.</summary>
public sealed class MonitorClientesTests
{
    private static readonly DateTime Ahora = new(2026, 10, 28, 15, 0, 0);

    private static readonly IReadOnlyList<MonitorTipoControlDto> Tipos =
    [
        new() { Codigo = "REPORTE", Nombre = "Reporte del sistema", Medida = "EDAD", AvisoMinutos = 60, CriticoMinutos = 180, Orden = 10, Activo = true },
        new() { Codigo = "ENVIO_PDV", Nombre = "Envío a punto de venta", Medida = "EDAD", AvisoMinutos = 120, CriticoMinutos = 180, Orden = 20, Activo = true },
        new() { Codigo = "BACKUP", Nombre = "Backup diario", Medida = "EDAD", AvisoMinutos = 1560, CriticoMinutos = 2160, Orden = 40, Activo = true },
        new() { Codigo = "TAREA", Nombre = "Tarea automática", Medida = "RESULTADO", Orden = 50, Activo = true }
    ];

    [Fact]
    public void ScriptEmbebido_EsIdenticoAlDeReferencia()
    {
        var path = Path.Combine(FindRepositoryRoot(), "docs", "base-datos", "sql-referencia", "2026-10-28-001__alfanet2007_monitor_clientes.sql");
        var referencia = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n');

        Assert.Equal(referencia, MonitorClientesSchema.Script.Replace("\r\n", "\n").TrimEnd('\n'));
    }

    [Fact]
    public void Script_NoUsaSintaxisQueNoCorreEnCompatibilidad100()
    {
        Assert.DoesNotContain("THROW", MonitorClientesSchema.Script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("STRING_SPLIT", MonitorClientesSchema.Script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OPENJSON", MonitorClientesSchema.Script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParsearItems_LeeLineasYDejaElMensajeConBarras()
    {
        var items = MonitorClientesService.ParsearItems(
            "ENVIO_PDV|SERVER1\\ALFANET/LAPAPELERA|2026-10-28 11:35:22|2026-10-28 11:35:22|OK|\r\n" +
            "\r\n" +
            "TAREA|BACKUP_DIARIO|2026-10-27 22:16:34||ERROR|Fallo | disco lleno\n");

        Assert.Equal(2, items.Count);
        Assert.Equal("ENVIO_PDV", items[0].Tipo);
        // El destino no puede traer "|": el sistema lo arma como SERVIDOR\INSTANCIA/BASE.
        Assert.Equal("SERVER1\\ALFANET/LAPAPELERA", items[0].Destino);
        Assert.Equal(new DateTime(2026, 10, 28, 11, 35, 22), items[0].UltimoOk);
        Assert.Equal("TAREA", items[1].Tipo);
        Assert.Equal("BACKUP_DIARIO", items[1].Destino);
        Assert.Equal(new DateTime(2026, 10, 27, 22, 16, 34), items[1].UltimoOk);
        Assert.Null(items[1].UltimoIntento);
        Assert.Equal("ERROR", items[1].Resultado);
        Assert.Equal("Fallo | disco lleno", items[1].Mensaje);
    }

    [Fact]
    public void ParsearFecha_InvalidaOVacia_DevuelveNull()
    {
        Assert.Null(MonitorClientesService.ParsearFecha(""));
        Assert.Null(MonitorClientesService.ParsearFecha("28/10/2026"));
        Assert.Equal(new DateTime(2026, 10, 28, 8, 5, 0), MonitorClientesService.ParsearFecha("2026-10-28 08:05"));
    }

    [Theory]
    [InlineData(30, MonitorEstado.Ok)]
    [InlineData(150, MonitorEstado.Aviso)]
    [InlineData(200, MonitorEstado.Critico)]
    public void EnvioPdv_SeEvaluaPorAntiguedadDelUltimoOk(int minutos, MonitorEstado esperado)
    {
        var filas = Evaluar(estados: [Estado("ENVIO_PDV", "LAPAPELERA", Ahora.AddMinutes(-minutos), "OK")]);

        Assert.Equal(esperado, filas.Single(f => f.TipoCodigo == "ENVIO_PDV").Estado);
    }

    [Fact]
    public void UltimoIntentoConError_ConOkReciente_EsAviso()
    {
        var filas = Evaluar(estados: [Estado("ENVIO_PDV", "LAPAPELERA", Ahora.AddMinutes(-20), "ERROR")]);

        var fila = filas.Single(f => f.TipoCodigo == "ENVIO_PDV");
        Assert.Equal(MonitorEstado.Aviso, fila.Estado);
        Assert.Contains("falló", fila.Motivo);
    }

    [Fact]
    public void NuncaTuvoOk_EsCritico()
    {
        var filas = Evaluar(estados: [Estado("BACKUP", "", null, "ERROR")]);

        Assert.Equal(MonitorEstado.Critico, filas.Single(f => f.TipoCodigo == "BACKUP").Estado);
    }

    [Theory]
    [InlineData("ERROR", MonitorEstado.Critico)]
    [InlineData("ADVERTENCIA", MonitorEstado.Aviso)]
    [InlineData("CORRECTO", MonitorEstado.Ok)]
    public void Tarea_SeEvaluaPorResultado(string resultado, MonitorEstado esperado)
    {
        var filas = Evaluar(estados: [Estado("TAREA", "ARCA", Ahora.AddDays(-10), resultado, ultimoIntento: Ahora.AddHours(-1))]);

        Assert.Equal(esperado, filas.Single(f => f.TipoCodigo == "TAREA").Estado);
    }

    [Fact]
    public void SistemaQueNoReporta_MarcaReporteYDejaElRestoSinDatos()
    {
        var filas = Evaluar(
            ultimoReporte: Ahora.AddHours(-4),
            estados: [Estado("ENVIO_PDV", "LAPAPELERA", Ahora.AddHours(-4), "OK")]);

        Assert.Equal(MonitorEstado.Critico, filas.Single(f => f.TipoCodigo == "REPORTE").Estado);
        Assert.Equal(MonitorEstado.SinDatos, filas.Single(f => f.TipoCodigo == "ENVIO_PDV").Estado);
    }

    [Fact]
    public void LimitePropioDelCliente_ReemplazaAlDelTipo_YDesactivadoNoSeMuestra()
    {
        var filas = Evaluar(
            limites:
            [
                new MonitorClientesService.LimiteCliente { ClienteCodigo = "112010001", TipoCodigo = "ENVIO_PDV", CriticoMinutos = 600 },
                new MonitorClientesService.LimiteCliente { ClienteCodigo = "112010001", TipoCodigo = "BACKUP", Desactivado = true }
            ],
            estados:
            [
                Estado("ENVIO_PDV", "LAPAPELERA", Ahora.AddMinutes(-200), "OK"),
                Estado("BACKUP", "", Ahora.AddDays(-5), "OK")
            ]);

        // 200 min: con el límite del tipo sería crítico; con el propio (600) queda en aviso (aviso del tipo: 120).
        Assert.Equal(MonitorEstado.Aviso, filas.Single(f => f.TipoCodigo == "ENVIO_PDV").Estado);
        Assert.DoesNotContain(filas, f => f.TipoCodigo == "BACKUP");
    }

    [Fact]
    public void Orden_PrimeroLoPeorYDentroLaPrioridadMasAlta()
    {
        var origenes = new List<MonitorClientesService.OrigenFila>
        {
            Origen(1, "A", "4", Ahora),
            Origen(2, "B", "1", Ahora),
            Origen(3, "C", "", Ahora)
        };
        var estados = new List<MonitorClientesService.EstadoFila>
        {
            Estado("ENVIO_PDV", "X", Ahora.AddHours(-5), "OK", idOrigen: 1),
            Estado("ENVIO_PDV", "X", Ahora.AddHours(-5), "OK", idOrigen: 2),
            Estado("ENVIO_PDV", "X", Ahora.AddHours(-5), "OK", idOrigen: 3)
        };

        var criticos = MonitorClientesService.Evaluar(Ahora, Tipos, [], origenes, estados)
            .Where(f => f.Estado == MonitorEstado.Critico)
            .Select(f => f.ClienteCodigo)
            .ToList();

        Assert.Equal(["B", "A", "C"], criticos);
    }

    [Theory]
    [InlineData(0.5, "menos de 1 min")]
    [InlineData(45, "45 min")]
    [InlineData(200, "3 h 20 min")]
    [InlineData(180, "3 h")]
    [InlineData(3000, "2 d 2 h")]
    public void Duracion_EnPalabras(double minutos, string esperado)
        => Assert.Equal(esperado, MonitorClientesService.Duracion(TimeSpan.FromMinutes(minutos)));

    [Theory]
    [InlineData(400, MonitorEstado.Ok)]          // faltan 400 días
    [InlineData(20, MonitorEstado.Aviso)]        // dentro de los 30 días
    [InlineData(5, MonitorEstado.Critico)]       // dentro de los 7 días
    [InlineData(-2, MonitorEstado.Critico)]      // vencido
    public void PorVencimiento_SegunLoQueFalta(int dias, MonitorEstado esperado)
    {
        var (estado, motivo) = MonitorClientesService.PorVencimiento(Ahora.AddDays(dias), "OK", Ahora, 43200, 10080);
        Assert.Equal(esperado, estado);
        Assert.Contains(Ahora.AddDays(dias).ToString("dd/MM/yyyy"), motivo);
        Assert.StartsWith(dias < 0 ? "Venció" : "Vence", motivo);
    }

    [Fact]
    public void PorVencimiento_SinFechaOConError_QuedaEnAviso()
    {
        Assert.Equal(MonitorEstado.Aviso, MonitorClientesService.PorVencimiento(null, "OK", Ahora, 43200, 10080).Item1);
        Assert.Equal(MonitorEstado.Aviso, MonitorClientesService.PorVencimiento(Ahora.AddDays(400), "ERROR", Ahora, 43200, 10080).Item1);
    }

    [Fact]
    public void Evaluar_CertificadoUsaLaMedidaVencimiento()
    {
        var tipos = Tipos.Append(new MonitorTipoControlDto
        {
            Codigo = "CERT_ARCA", Nombre = "Certificado ARCA", Medida = "VENCE", AvisoMinutos = 43200, CriticoMinutos = 10080, Orden = 60, Activo = true
        }).ToList();
        var filas = MonitorClientesService.Evaluar(Ahora, tipos, [], [Origen(1, "112010001", "1", Ahora.AddMinutes(-5))],
            [Estado("CERT_ARCA", "30707388850 / cert.crt", Ahora.AddDays(3), "OK")]);

        var cert = Assert.Single(filas, f => f.TipoCodigo == "CERT_ARCA");
        Assert.Equal(MonitorEstado.Critico, cert.Estado);
        Assert.True(cert.EsVencimiento);
    }

    private static IReadOnlyList<MonitorFilaDto> Evaluar(
        IReadOnlyList<MonitorClientesService.EstadoFila> estados,
        DateTime? ultimoReporte = null,
        IReadOnlyList<MonitorClientesService.LimiteCliente>? limites = null)
        => MonitorClientesService.Evaluar(Ahora, Tipos, limites ?? [], [Origen(1, "112010001", "1", ultimoReporte ?? Ahora.AddMinutes(-5))], estados);

    private static MonitorClientesService.OrigenFila Origen(int id, string cliente, string prioridad, DateTime ultimoReporte)
        => new()
        {
            IdOrigen = id,
            ClienteCodigo = cliente,
            RazonSocial = cliente,
            PrioridadCodigo = prioridad,
            DbServidor = "SERVER",
            DbNombre = "BASE",
            UltimoReporte = ultimoReporte
        };

    private static MonitorClientesService.EstadoFila Estado(string tipo, string destino, DateTime? ultimoOk, string? resultado,
        DateTime? ultimoIntento = null, int idOrigen = 1)
        => new()
        {
            IdOrigen = idOrigen,
            TipoCodigo = tipo,
            Destino = destino,
            UltimoOk = ultimoOk,
            UltimoIntento = ultimoIntento ?? ultimoOk,
            Resultado = resultado
        };

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AlfaCore")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
