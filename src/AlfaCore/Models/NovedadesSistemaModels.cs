namespace AlfaCore.Models;

/// <summary>
/// Novedad de AlfaCore (nota de versión). La escribimos nosotros, viaja con el código y es la misma
/// para todas las empresas: no se guarda en la base del cliente ni se edita desde la pantalla.
/// El contenido es un HTML en App_Data/novedades-sistema/ (mismos bloques que el editor de
/// comunicados: llamados, tarjetas con ícono, antes/después, capturas).
/// </summary>
public sealed record NovedadSistemaInfo(
    string Id,
    string Version,
    string Titulo,
    string Bajada,
    DateTime Fecha,
    string Icono,
    string Acento,
    string Archivo)
{
    /// <summary>Clave del aviso en la campana y del registro de lectura (ALFACORE_AVISOS_LEIDOS).</summary>
    public string ClaveAviso => NovedadesSistemaCatalogo.PrefijoClave + Id;
}

public sealed class NovedadSistemaDto
{
    public required NovedadSistemaInfo Info { get; init; }
    public string Contenido { get; init; } = string.Empty;
    public bool Leida { get; set; }
}

public static class NovedadesSistemaCatalogo
{
    public const string PrefijoClave = "sis:";

    /// <summary>Durante cuántos días una novedad de AlfaCore sin leer aparece en la campana.</summary>
    public const int DiasEnCampana = 60;

    /// <summary>La más nueva primero. Para una versión nueva: agregar la entrada y su archivo HTML.</summary>
    public static readonly IReadOnlyList<NovedadSistemaInfo> Todas =
    [
        new("alfacore-1.1",
            "AlfaCore 1.1",
            "AlfaCore 1.1: conciliación bancaria, reportes y carga de comprobantes",
            "Cómo usar lo nuevo de Caja y Bancos, Compras, Ventas y Conversaciones.",
            new DateTime(2026, 10, 22),
            "bi-bank",
            NovedadAcentos.Verde,
            "2026-10-22__alfacore-1-1.html"),
        new("alfacore-1.0",
            "AlfaCore 1.0",
            "AlfaCore 1.0: nueva imagen y un menú más ordenado",
            "Todo lo nuevo de esta versión, módulo por módulo.",
            new DateTime(2026, 10, 13),
            "bi-rocket-takeoff",
            NovedadAcentos.Cian,
            "2026-10-13__alfacore-1-0.html")
    ];

    public static NovedadSistemaInfo? Buscar(string? id)
        => Todas.FirstOrDefault(n => string.Equals(n.Id, (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
}
