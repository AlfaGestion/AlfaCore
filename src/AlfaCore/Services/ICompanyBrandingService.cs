using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICompanyBrandingService
{
    Task<CompanyBrandingDto> GetAsync(
        string? baseUrl = null,
        string? idWeb = null,
        int? idBase = null,
        CancellationToken ct = default);
}
