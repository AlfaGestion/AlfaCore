using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Cache de "qué secciones del Portal Cliente están habilitadas", con vida de un circuito
/// de Blazor (una pestaña/sesión de navegador). Cada página del Portal Cliente es un componente
/// distinto -- Blazor crea una instancia nueva por cada navegación -- así que sin este cache cada
/// clic entre pestañas repetía la consulta a TA_CONFIGURACION y el tab bar parpadeaba (placeholder
/// mientras se esperaba la vuelta de la base). Al ser Scoped, sobrevive entre navegaciones dentro
/// del mismo circuito pero no se filtra entre distintos clientes/tenants (cada circuito es su
/// propia instancia).</summary>
public interface IPortalClienteSeccionesCache
{
    ConfiguracionVentasPortalClienteDto? Cached { get; }
    ConfiguracionVentasPortalClienteDto? Get(string? idWeb, int? idBase);
    void Set(string? idWeb, int? idBase, ConfiguracionVentasPortalClienteDto secciones);
    void Set(ConfiguracionVentasPortalClienteDto secciones);
}

public sealed class PortalClienteSeccionesCache : IPortalClienteSeccionesCache
{
    private string? _idWeb;
    private int? _idBase;
    public ConfiguracionVentasPortalClienteDto? Cached { get; private set; }

    public ConfiguracionVentasPortalClienteDto? Get(string? idWeb, int? idBase)
        => Cached is not null
           && _idBase == idBase
           && string.Equals(_idWeb, Normalize(idWeb), StringComparison.OrdinalIgnoreCase)
            ? Cached
            : null;

    public void Set(string? idWeb, int? idBase, ConfiguracionVentasPortalClienteDto secciones)
    {
        _idWeb = Normalize(idWeb);
        _idBase = idBase;
        Cached = secciones;
    }

    public void Set(ConfiguracionVentasPortalClienteDto secciones)
        => Cached = secciones;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
