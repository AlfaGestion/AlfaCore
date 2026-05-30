using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ITareasService
{
    Task<TareasPageDto> GetPageAsync(string usuario, CancellationToken ct = default);
    Task<int> SaveListAsync(TareaListaSaveRequest request, CancellationToken ct = default);
    Task DeleteListAsync(int idLista, string usuarioAccion, CancellationToken ct = default);
    Task<long> SaveTaskAsync(TareaSaveRequest request, CancellationToken ct = default);
    Task MoveTaskToListAsync(long idTarea, int idLista, string usuarioAccion, CancellationToken ct = default);
    Task<IReadOnlyList<TareaAdjuntoDto>> GetTaskAttachmentsAsync(long idTarea, string usuarioAccion, CancellationToken ct = default);
    Task AddTaskAttachmentsAsync(long idTarea, IReadOnlyList<TareaAdjuntoUploadDto> adjuntos, string usuarioAccion, CancellationToken ct = default);
    Task DeleteTaskAttachmentAsync(long idAdjunto, string usuarioAccion, CancellationToken ct = default);
    Task ChangeTaskStateAsync(long idTarea, string estado, string usuarioAccion, CancellationToken ct = default);
    Task DuplicateTaskAsync(long idTarea, string usuarioAccion, CancellationToken ct = default);
    Task DeleteTaskAsync(long idTarea, string usuarioAccion, CancellationToken ct = default);
    Task<long> AddQuickNoteAsync(string texto, string usuario, CancellationToken ct = default);
    Task ToggleQuickNoteAsync(long idNota, bool completada, string usuario, CancellationToken ct = default);
    Task DeleteQuickNoteAsync(long idNota, string usuario, CancellationToken ct = default);
    Task ClearCompletedQuickNotesAsync(string usuario, CancellationToken ct = default);
    Task SaveSharingAsync(TareaCompartirRequest request, CancellationToken ct = default);
}
