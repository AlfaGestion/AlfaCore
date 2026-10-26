using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralClientesService
{
    Task<ClienteCentralDto?> GetByIdClienteAsync(string idCliente, CancellationToken ct = default);
    Task<ClienteCentralDto?> GetByIdWebAsync(string idWeb, CancellationToken ct = default);
    Task<ClienteCentralDto?> GetByLicenciaPrincipalAsync(string licenciaPrincipal, CancellationToken ct = default);
    /// <summary>Para el legacy (VB6): IdCliente y datos FTP de la licencia. Reemplaza la lectura de
    /// ESTADISTICAS.dbo.clientes / sp_ObtenerDatosLicencia.</summary>
    Task<LicenciaLegacyDto?> GetLicenciaLegacyAsync(string licenciaPrincipal, CancellationToken ct = default);
    /// <summary>Graba la fecha de activación si todavía no la tiene (antes sp_ActivaLicencia en ESTADISTICAS).
    /// Devuelve false si la licencia no existe.</summary>
    Task<bool> ActivarLicenciaAsync(string licenciaPrincipal, CancellationToken ct = default);
    Task<IReadOnlyList<ClienteCentralDto>> GetAllAsync(CancellationToken ct = default);
    Task<string> GenerateAndSaveIdWebAsync(string idCliente, string razonSocial, CancellationToken ct = default);
}
