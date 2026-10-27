namespace AlfaCore.ConciliacionBancaria.Application.Abstractions;

public interface IClassificationService
{
    Task ClassifyImportAsync(long importId, CancellationToken cancellationToken = default);
}
