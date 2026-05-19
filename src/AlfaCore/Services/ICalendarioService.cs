using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICalendarioService
{
    Task<CalendarioMonthDto> GetMonthAsync(CalendarioMonthRequest request, CancellationToken ct = default);
    Task<CalendarioEventoDto?> GetByIdAsync(long idEvento, CancellationToken ct = default);
    Task<long> SaveAsync(CalendarioEventoSaveRequest request, CancellationToken ct = default);
    Task DeleteAsync(long idEvento, string? usuarioAccion = null, CancellationToken ct = default);
    Task<CalendarioRecordatorioSendResult> SendWhatsAppReminderAsync(long idRecordatorio, string? usuarioAccion = null, CancellationToken ct = default);
}
