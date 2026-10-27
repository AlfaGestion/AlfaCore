using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Application.Abstractions;

public interface IExcelStatementParser
{
    BankSource Source { get; }
    Task<IReadOnlyCollection<NormalizedMovementDto>> ParseAsync(string filePath, CancellationToken cancellationToken = default);
}
