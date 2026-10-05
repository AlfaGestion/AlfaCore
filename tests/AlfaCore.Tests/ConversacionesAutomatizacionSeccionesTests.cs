using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Guardado por sección de Configuración → Automatización / Asistente IA (2026-10-05): cada pantalla
/// graba solo sus claves. La unión de todas las secciones tiene que seguir siendo exactamente el
/// conjunto de claves que se grababa antes en un solo guardado.
/// </summary>
public sealed class ConversacionesAutomatizacionSeccionesTests
{
    private static readonly string[] ClavesHistoricas =
    [
        "CONV_AUTOMATIZACIONES_ACTIVO", "CONV_AUTOMATIZACIONES_MENSAJE", "CONV_AUTOMATIZACIONES_DIAS",
        "CONV_AUTOMATIZACIONES_HORA_DESDE", "CONV_AUTOMATIZACIONES_HORA_HASTA",
        "CONV_BIENVENIDA_ACTIVO", "CONV_BIENVENIDA_MENSAJE",
        "CONV_BOT_ACTIVO", "CONV_BOT_SOLO_SIN_ASIGNAR", "CONV_BOT_PALABRAS_ESCALADO", "CONV_BOT_MAX_RESPUESTAS",
        "CONV_BOT_SOLO_FUERA_HORARIO", "CONV_BOT_ESPERA_MINUTOS",
        "CONV_AUTOCIERRE_ACTIVO", "CONV_AUTOCIERRE_HORAS_AVISO", "CONV_AUTOCIERRE_HORAS_CIERRE",
        "CONV_AUTOCIERRE_MENSAJE_AVISO", "CONV_AUTOCIERRE_MENSAJE_CIERRE",
        "CONV_SLA_ACTIVO", "CONV_SLA_HORAS_RECORDATORIO", "CONV_SLA_HORAS_REASIGNAR",
        "CONV_ASISTENTE_FUERA_HORARIO", "CONV_ASISTENTE_URGENCIA_PALABRAS", "CONV_ASISTENTE_URGENCIA_TEMPLATE",
        "CONV_ASISTENTE_URGENCIA_TECNICOS", "CONV_ASISTENTE_USA_KNOWLEDGE",
        "CONV_ASISTENTE_HERRAMIENTA_PRECIOS", "CONV_ASISTENTE_HERRAMIENTA_SALDO_CLIENTE",
        "CONV_ASISTENTE_HERRAMIENTA_SALDO_PROVEEDOR", "CONV_ASISTENTE_HERRAMIENTA_PEDIDOS",
        "CONV_ASISTENTE_HERRAMIENTA_PORTAL_LINK",
        CatalogoPrecioConsumidorSetting.Clave,
        "CONV_INFORME_INSTRUCCIONES"
    ];

    private static readonly ConversacionAutomatizacionSeccion[] SeccionesIndividuales =
    [
        ConversacionAutomatizacionSeccion.Horario,
        ConversacionAutomatizacionSeccion.Bienvenida,
        ConversacionAutomatizacionSeccion.Asistente,
        ConversacionAutomatizacionSeccion.AutoCierre,
        ConversacionAutomatizacionSeccion.Sla,
        ConversacionAutomatizacionSeccion.Informe
    ];

    private static string[] Claves(ConversacionAutomatizacionSeccion secciones)
        => ConversacionesConfigService.BuildAutomatizacionesItems(new ConversacionAutomatizacionesConfigDto(), secciones)
            .Select(x => x.Clave)
            .ToArray();

    [Fact]
    public void Todas_GrabaExactamenteLasClavesHistoricas()
    {
        var claves = Claves(ConversacionAutomatizacionSeccion.Todas);

        Assert.Equal(claves.Length, claves.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            ClavesHistoricas.OrderBy(x => x, StringComparer.Ordinal),
            claves.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void CadaClavePerteneceAUnaSolaSeccion()
    {
        var porSeccion = SeccionesIndividuales.SelectMany(Claves).ToArray();

        Assert.Equal(porSeccion.Length, porSeccion.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            ClavesHistoricas.OrderBy(x => x, StringComparer.Ordinal),
            porSeccion.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Asistente_IncluyeHerramientasYPrecioConsumidor_PeroNoHorario()
    {
        var claves = Claves(ConversacionAutomatizacionSeccion.Asistente);

        Assert.Contains("CONV_BOT_ACTIVO", claves);
        Assert.Contains("CONV_ASISTENTE_HERRAMIENTA_PRECIOS", claves);
        Assert.Contains(CatalogoPrecioConsumidorSetting.Clave, claves);
        Assert.DoesNotContain("CONV_AUTOMATIZACIONES_MENSAJE", claves);
        Assert.DoesNotContain("CONV_INFORME_INSTRUCCIONES", claves);
    }

    [Fact]
    public void Horario_NoTocaClavesDelAsistente()
    {
        var claves = Claves(ConversacionAutomatizacionSeccion.Horario);

        Assert.Equal(5, claves.Length);
        Assert.All(claves, clave => Assert.StartsWith("CONV_AUTOMATIZACIONES_", clave));
    }

    [Fact]
    public void Ninguna_NoGrabaNada()
        => Assert.Empty(Claves(ConversacionAutomatizacionSeccion.Ninguna));
}
