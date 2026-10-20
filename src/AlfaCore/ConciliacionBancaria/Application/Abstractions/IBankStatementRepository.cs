using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Domain.Entities;
using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Application.Abstractions;

public interface IBankStatementRepository
{
    // --- Clasificación ---

    Task<IReadOnlyCollection<BankStatementMovement>> GetMovementsByImportAsync(long importId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<InterpretationRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);

    Task SaveClassificationsAsync(IReadOnlyCollection<ClassifiedMovement> classifications, CancellationToken cancellationToken = default);

    // --- Matching ---

    /// <summary>
    /// Devuelve los movimientos clasificados como Cobranza para una importación,
    /// que todavía no tienen propuesta de match generada.
    /// </summary>
    Task<IReadOnlyCollection<CobranzaCandidateDto>> GetCobranzasForMatchingAsync(long importId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve todos los alias de clientes bancarios activos.
    /// Se carga una vez por proceso de matching y se reutiliza en memoria.
    /// </summary>
    Task<IReadOnlyCollection<ClientAliasDto>> GetClientAliasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta VE_CPTES_SALDOS_VENTAS para obtener comprobantes pendientes del cliente.
    /// No recalcula saldos; usa exactamente lo que devuelve la vista de Alfa.
    /// </summary>
    Task<IReadOnlyCollection<PendingDocumentDto>> GetPendingDocumentsAsync(string clientCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca clientes en VT_CLIENTES cuyo nombre sea similar al texto buscado.
    /// Se usa como fallback cuando no hay alias registrado en BK_AliasClienteBanco.
    /// Devuelve hasta 5 candidatos ordenados por similitud descendente.
    /// </summary>
    Task<IReadOnlyCollection<ClientSearchResultDto>> SearchClientByNameAsync(
        string name,
        string? taxId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica si existe en VE_COBRANZAS_REALIZADAS un posible cobro ya registrado en Alfa
    /// para el cliente con importe y fecha cercanos al movimiento bancario.
    /// Ventana temporal: ±15 días de la fecha del movimiento.
    /// Coincidencia de importe a un centavo; no confirma identidad de la operación bancaria.
    /// </summary>
    Task<bool> HasRecentlyRegisteredPaymentAsync(
        string clientCode,
        decimal amount,
        DateTime movementDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda la propuesta de match y sus líneas de aplicación en una transacción.
    /// Asigna el MatchId generado a cada MovementApplicationDto.
    /// </summary>
    Task SaveMatchAsync(MovementMatchProposal match, IReadOnlyCollection<MovementApplicationDto> applications, CancellationToken cancellationToken = default);

}
