using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Equivalente web de FrmAsistenteConfig.frm (solapas Datos empresa / Logo y Estilo). Ver
/// ConfiguracionGeneralService para el detalle de qué clave de TA_CONFIGURACION corresponde a
/// cada campo -- son las mismas que usa el form de escritorio, verificadas contra el .frm y
/// contra datos reales, no inventadas.
/// </summary>
public interface IConfiguracionGeneralService
{
    Task<ConfiguracionEmpresaDto> GetEmpresaAsync(CancellationToken ct = default);

    Task SaveEmpresaAsync(ConfiguracionEmpresaDto dto, string? usuarioAccion, CancellationToken ct = default);

    Task<IReadOnlyList<ConfiguracionCondIvaOptionDto>> GetCondicionesIvaAsync(CancellationToken ct = default);

    Task<ConfiguracionLogoDto> GetLogoInfoAsync(CancellationToken ct = default);

    Task SaveLogoOpcionesAsync(ConfiguracionLogoDto dto, CancellationToken ct = default);

    /// <summary>Bytes del logo actual (TA_LOGOS.IMAGEN), o null si no hay logo cargado.</summary>
    Task<byte[]?> GetLogoBytesAsync(CancellationToken ct = default);

    Task SaveLogoAsync(byte[] contenido, CancellationToken ct = default);

    Task DeleteLogoAsync(CancellationToken ct = default);

    /// <summary>Correo saliente general de la empresa (una sola cuenta para toda la instalación,
    /// salvo que un usuario tenga la suya propia en Usuarios → Email propio).</summary>
    Task<ConfiguracionEmailDto> GetEmailAsync(CancellationToken ct = default);

    Task SaveEmailAsync(ConfiguracionEmailDto dto, CancellationToken ct = default);
}
