using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICalendarioService
{
    Task<CalendarioMonthDto> GetMonthAsync(CalendarioMonthRequest request, CancellationToken ct = default);
    Task<CalendarioEventoDto?> GetByIdAsync(long idEvento, CancellationToken ct = default);
    Task<long> SaveAsync(CalendarioEventoSaveRequest request, CancellationToken ct = default);
    Task DeleteAsync(long idEvento, string? usuarioAccion = null, CancellationToken ct = default);
    /// <summary>Baja de un evento de una serie: solo este, este y los siguientes, o toda la serie.</summary>
    Task DeleteSerieAsync(long idEvento, string alcance, string? usuarioAccion = null, CancellationToken ct = default);
    Task<CalendarioIndicadorConfigDto> GetIndicadorConfigAsync(CancellationToken ct = default);
    Task SaveIndicadorConfigAsync(CalendarioIndicadorConfigDto config, CancellationToken ct = default);
    /// <summary>Guardia vigente y siguiente para la barra superior (null si la base no tiene Calendario).</summary>
    Task<CalendarioGuardiaResumenDto?> GetGuardiaResumenAsync(CancellationToken ct = default);
    Task<CalendarioRecordatorioSendResult> SendWhatsAppReminderAsync(long idRecordatorio, string? usuarioAccion = null, CancellationToken ct = default);
    Task<int> ProcesarRecordatoriosPendientesAsync(CancellationToken ct = default);
}
