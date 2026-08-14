using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IContactosService
{
    Task<PagedResult<ContactoGridItemDto>> SearchAsync(ContactosFilters filters, CancellationToken ct = default);
    Task<ContactoNavigationContextDto?> GetNavigationContextAsync(int contactId, ContactosFilters filters, CancellationToken ct = default);
    Task<ContactoDetailDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ContactoCuentaDto>> GetCuentasVinculadasAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ContactoMergeCandidateDto>> SearchMergeCandidatesAsync(int contactoPrincipalId, string texto, CancellationToken ct = default);
    Task<ContactoDetailDto?> MergeContactsAsync(ContactoMergeRequest request, CancellationToken ct = default);
    Task<int> SaveAsync(ContactoSaveRequest request, CancellationToken ct = default);
    Task DeactivateAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ProvinciaOptionDto>> GetProvinciasAsync(CancellationToken ct = default);
    Task<ContactosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, ContactosViewSettingsDto settings, CancellationToken ct = default);
}
