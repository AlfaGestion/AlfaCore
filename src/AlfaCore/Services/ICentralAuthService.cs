using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
