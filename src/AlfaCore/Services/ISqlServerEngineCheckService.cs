using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Chequea qué versión de motor SQL Server tiene la base activa. AlfaCore usa sintaxis de
/// SQL Server 2016+ (CREATE OR ALTER, THROW/re-throw, OFFSET/FETCH) en distintos lugares del código
/// y de las migraciones -- una base en un motor más viejo (ej.: SQL Server 2008) va a romper de forma
/// intermitente en módulos distintos a medida que se usan. En vez de seguir parchando caso por caso,
/// esto avisa explícitamente que hace falta actualizar el motor.</summary>
public interface ISqlServerEngineCheckService
{
    Task<SqlServerEngineInfoDto> GetActiveEngineInfoAsync(CancellationToken ct = default);
}
