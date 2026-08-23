using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IVb6BridgeService
{
    Task<string> CreateTicketAsync(Vb6AuthTicketRequest request, CancellationToken ct = default);
    Task<Vb6ConsumeTicketResult> ConsumeTicketAsync(string ticket, CancellationToken ct = default);

    /// <summary>
    /// Autentica una instalación VB6 para endpoints de integración livianos (ej. links públicos)
    /// que no necesitan el flujo completo de ticket/login de <see cref="CreateTicketAsync"/>.
    /// Reusa la misma prueba de identidad: IdBase (Cfg("ALFACORE_IDBASE")) + las credenciales SQL
    /// que el VB6 ya tiene para su propia base, comparadas contra ALFA_CENTRAL.dbo.bases. Devuelve
    /// null ante cualquier falla de autorización (IdBase inexistente, credenciales que no
    /// coinciden, modo no-SaaS, cliente sin IdWeb) — sin distinguir el motivo en la respuesta.
    /// </summary>
    Task<Vb6InstallationDto?> ValidateInstallationAsync(int idBase, string dbName, string dbUser, string dbPassword, CancellationToken ct = default);

    /// <summary>
    /// Resuelve el IdBase de este equipo a partir de LicenciaPrincipal + las credenciales SQL
    /// reales de su conexión, sin que nadie tenga que tipear ni adivinar un número. Devuelve el
    /// IdBase cuando hay una única base candidata; tira <see cref="Vb6MultiplesBasesException"/>
    /// si hay más de una (el cliente tiene varias bases con las mismas credenciales) o
    /// <see cref="InvalidOperationException"/> ante cualquier otro problema (licencia no
    /// registrada, ninguna base coincide, etc.).
    /// </summary>
    Task<int> ResolverIdBaseAsync(Vb6ResolverIdBaseRequest request, CancellationToken ct = default);
}
