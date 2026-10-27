namespace AlfaCore.ConciliacionBancaria.Application.Abstractions;

public interface IMatchingService
{
    Task GenerateMatchesAsync(long importId, CancellationToken cancellationToken = default);
}
