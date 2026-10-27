using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICargaViajesTarifaIaService
{
    Task<IReadOnlyList<CargaViajesTarifaImportRow>> ExtraerAsync(
        IReadOnlyList<CargaViajesTarifaIaImage> imagenes,
        CancellationToken ct = default);
}

public sealed record CargaViajesTarifaIaImage(string FileName, string MimeType, byte[] Bytes);
