namespace AlfaCore.Services;

public interface IConversacionesAuthorizationService
{
    Task<bool> CanManageAsync(CancellationToken ct = default);
    Task EnsureCanManageAsync(CancellationToken ct = default);
    Task EnsureCanAttendConversationAsync(long idConversacion, CancellationToken ct = default);
    Task EnsureCanUseWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default);
}
