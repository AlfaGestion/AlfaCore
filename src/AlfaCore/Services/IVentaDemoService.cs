using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Demos públicas de la página de venta (/demo/{rubro}). Lee ALFA_CENTRAL.dbo.VentaDemos.
/// Ver docs/modulos/venta-online.md.
/// </summary>
public interface IVentaDemoService
{
    /// <summary>Demo activa de la familia, o null si no hay (tabla vacía, inactiva o sin script aplicado).</summary>
    Task<VentaDemoDto?> GetDemoActivaAsync(string familia, CancellationToken ct = default);

    /// <summary>Demo a la que pertenece el usuario central, o null si no es un usuario demo.</summary>
    Task<VentaDemoDto?> GetDemoPorLoginAsync(string? centralLogin, CancellationToken ct = default);

    /// <summary>Clave del usuario demo desde configuración (VentaDemo:Claves:{Familia}). Nunca desde la base ni el repo.</summary>
    string? GetClaveDemo(string familia);
}
