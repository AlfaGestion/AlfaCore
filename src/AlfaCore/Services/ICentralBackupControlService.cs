using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralBackupControlService
{
    Task<long> RegistrarAsync(BackupStatusRequest request, CancellationToken ct = default);
}
