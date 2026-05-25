namespace AlfaCore.Services;

public interface IPermissionService
{
    Task<IReadOnlySet<string>?> GetAllowedTaskKeysAsync(CancellationToken ct = default);
    Task<bool> HasAccessAsync(string clave, CancellationToken ct = default);
}
