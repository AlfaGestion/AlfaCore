using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Resuelve el emisor (CUIT, certificado, punto de venta electrónico, ambiente) para pedir un
/// CAE, y el modo de fallo configurado. Reutiliza la misma conexión ya abierta por el llamador (no abre
/// una propia) porque siempre se invoca dentro del flujo de venta del POS.</summary>
public interface IArcaConfigService
{
    /// <summary>Null si la facturación electrónica no aplica (apagada globalmente, o la unidad/config
    /// no tiene certificado y punto de venta resueltos) -- el llamador sigue el flujo normal sin CAE.</summary>
    Task<ArcaEmisorConfig?> ResolveEmisorAsync(SqlConnection cn, string? uNegocio, CancellationToken ct);

    Task<ArcaModoFalloCae> ResolveModoFalloCaeAsync(SqlConnection cn, CancellationToken ct);

    // ---- Pantalla de configuración (Configuración General > Ventas > Facturación electrónica) ----
    // Estos métodos abren su propia conexión (no participan del flujo de venta del POS).

    Task<ArcaConfiguracionGeneralDto> GetConfiguracionGeneralAsync(CancellationToken ct = default);
    Task GuardarConfiguracionGeneralAsync(ArcaConfiguracionGeneralDto dto, CancellationToken ct = default);
    Task GuardarUnidadAsync(ArcaUnidadNegocioConfigDto dto, CancellationToken ct = default);
    /// <summary>uNegocioOGlobal: código de unidad de negocio o "GLOBAL" para el certificado de
    /// fallback general. Cada archivo se actualiza solo si viene con contenido no nulo/vacío.</summary>
    Task SubirCertificadoAsync(string uNegocioOGlobal, byte[]? crt, string? nombreCrt, byte[]? clave, string? nombreClave, CancellationToken ct = default);
    Task EliminarCertificadoAsync(string uNegocioOGlobal, CancellationToken ct = default);
}

public enum ArcaModoFalloCae
{
    Estricto,
    Degradado
}
