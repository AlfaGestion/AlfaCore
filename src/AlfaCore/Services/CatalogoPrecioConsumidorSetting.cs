namespace AlfaCore.Services;

/// <summary>
/// Única interpretación de CATALOGO_MUESTRA_PRECIO_CONSUMIDOR ("Mostrar precios a consumidores
/// finales / leads" en Configuración → Conversaciones). La autoridad es la semántica visible del
/// checkbox: se muestra tildado sólo si el valor guardado es "1"; ausente, vacío o cualquier otro
/// valor se muestra destildado. La comparten la UI y el bot (vía ConversacionesConfigService) y el
/// catálogo público + su PDF (vía InterfacesCatalogosService.MuestraPreciosConsumidorFinalAsync),
/// para que no vuelvan a divergir.
/// </summary>
public static class CatalogoPrecioConsumidorSetting
{
    public const string Clave = "CATALOGO_MUESTRA_PRECIO_CONSUMIDOR";

    /// <summary>true sólo con "1" (como el checkbox); clave ausente/vacía → false (OFF).</summary>
    public static bool EstaActivo(string? valorAlmacenado)
        => string.Equals((valorAlmacenado ?? string.Empty).Trim(), "1", StringComparison.Ordinal);

    public static string Serializar(bool activo) => activo ? "1" : "0";
}
