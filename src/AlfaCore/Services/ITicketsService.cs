using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ITicketsService
{
    Task<TicketLookupDto> GetLookupsAsync(CancellationToken ct = default);
    Task<PagedResult<TicketGridItemDto>> SearchAsync(TicketsFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<TicketGridItemDto>> GetKanbanAsync(TicketsFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<TicketRelatedMatchDto>> FindRelatedOpenTicketsAsync(TicketRelatedSearchRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TicketClienteOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<TicketContactoOptionDto>> SearchContactosAsync(string texto, string? clienteCodigo = null, CancellationToken ct = default);
    Task<TicketDetailDto?> GetByIdAsync(long idTicket, CancellationToken ct = default);
    Task<TicketDetailDto?> GetByNumeroAsync(int numero, CancellationToken ct = default);
    Task<long> CreateAsync(TicketCreateRequest request, CancellationToken ct = default);
    Task UpdateAsync(TicketUpdateRequest request, CancellationToken ct = default);
    Task QuickUpdateAsync(TicketQuickUpdateRequest request, CancellationToken ct = default);
    Task BulkUpdateAsync(TicketBulkUpdateRequest request, CancellationToken ct = default);
    Task AddNoteAsync(TicketNotaRequest request, CancellationToken ct = default);
    Task<int> SaveEtiquetaAsync(TicketEtiquetaSaveRequest request, CancellationToken ct = default);
    /// <summary>Id de la etiqueta con ese nombre; si no existe la crea. No cambia color ni estado de una existente.</summary>
    Task<int> AsegurarEtiquetaAsync(string nombre, CancellationToken ct = default);
    /// <summary>Bandejas de "Atender primero" con su contador (respetando los demás filtros).</summary>
    Task<IReadOnlyList<TicketBandejaDto>> GetBandejasAsync(TicketsFilters filters, CancellationToken ct = default);
    /// <summary>"Recordarme el...": el ticket sale de la cola hasta esa fecha (o hasta que el cliente escriba).</summary>
    Task SetSeguimientoAsync(TicketSeguimientoRequest request, CancellationToken ct = default);
    Task<TicketsAtencionConfigDto> GetAtencionConfigAsync(CancellationToken ct = default);
    Task SaveAtencionConfigAsync(TicketsAtencionConfigDto config, CancellationToken ct = default);
    /// <summary>Panel de seguimiento: entradas/cierres, atraso, cumplimiento de plazos, etapas, técnicos y clientes.</summary>
    Task<TicketsSeguimientoDto> GetSeguimientoAsync(int dias, CancellationToken ct = default);
    Task<TicketsAvisosConfigDto> GetAvisosConfigAsync(CancellationToken ct = default);
    Task SaveAvisosConfigAsync(TicketsAvisosConfigDto config, CancellationToken ct = default);
    Task<IReadOnlyList<TicketAvisoEnviadoDto>> GetUltimosAvisosAsync(int cantidad, CancellationToken ct = default);
    /// <summary>Manda los avisos pendientes de la base activa. Manual = botón «Revisar avisos ahora» (corre fuera de horario).</summary>
    Task<TicketsAvisosResultadoDto> ProcesarAvisosAsync(bool manual, CancellationToken ct = default);
    Task<TicketSugerenciaIaDto> SugerirPrioridadAsync(TicketSugerenciaIaRequest request, CancellationToken ct = default);
    Task DeleteEtiquetaAsync(TicketEtiquetaDeleteRequest request, CancellationToken ct = default);
    Task<TicketViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, TicketViewSettingsDto settings, CancellationToken ct = default);
}
