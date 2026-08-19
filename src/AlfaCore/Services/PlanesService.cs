using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// CRUD de planes de comercialización — acceso Dapper directo a ALFA_CENTRAL, mismo patrón de
/// connection string que <see cref="CentralAdminService"/>. Ver
/// docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md, Fase 3.
/// </summary>
public sealed class PlanesService(IConfiguration configuration, IAppEventService appEvents) : IPlanesService
{
    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<PagedResult<PlanDto>> SearchAsync(PlanesFilters filters, CancellationToken ct = default)
    {
        filters ??= new PlanesFilters();
        var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
        var pageNumber = Math.Max(1, filters.PageNumber);
        var skip = (pageNumber - 1) * pageSize;
        var texto = NormalizeText(filters.Texto);

        const string whereSql = """
            WHERE (@IdModulo IS NULL OR IdModulo = @IdModulo)
              AND (@Activo IS NULL OR Activo = @Activo)
              AND (
                    @TextoLike = ''
                    OR Codigo COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR Nombre COLLATE Latin1_General_CI_AI LIKE @TextoLike
                  )
            """;

        var sql = $"""
            SELECT Id, IdModulo, Codigo, Nombre, Descripcion, TipoFacturacion, Precio, Moneda,
                   DiasPrueba, DiasGracia, CantidadIncluida, PermiteExcedentes, PrecioExcedente,
                   CantidadAgentesIncluida, PrecioPorAgenteExcedente, RenovacionAutomaticaDefault,
                   Activo, VisibleCatalogo, CreadoUtc, ModificadoUtc
            FROM dbo.Planes
            {whereSql}
            ORDER BY IdModulo, Nombre
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT COUNT(*)
            FROM dbo.Planes
            {whereSql};
            """;

        var parameters = new
        {
            filters.IdModulo,
            filters.Activo,
            TextoLike = string.IsNullOrEmpty(texto) ? "" : $"%{texto}%",
            Skip = skip,
            PageSize = pageSize
        };

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        var items = (await multi.ReadAsync<PlanDto>().ConfigureAwait(false)).ToArray();
        var total = await multi.ReadSingleAsync<int>().ConfigureAwait(false);

        return new PagedResult<PlanDto>
        {
            Items = items,
            Total = total,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<PlanDto>> GetByModuloAsync(int idModulo, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id, IdModulo, Codigo, Nombre, Descripcion, TipoFacturacion, Precio, Moneda,
                   DiasPrueba, DiasGracia, CantidadIncluida, PermiteExcedentes, PrecioExcedente,
                   CantidadAgentesIncluida, PrecioPorAgenteExcedente, RenovacionAutomaticaDefault,
                   Activo, VisibleCatalogo, CreadoUtc, ModificadoUtc
            FROM dbo.Planes
            WHERE IdModulo = @IdModulo
            ORDER BY Nombre;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<PlanDto>(new CommandDefinition(sql, new { IdModulo = idModulo }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToArray();
    }

    public async Task<PlanDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id, IdModulo, Codigo, Nombre, Descripcion, TipoFacturacion, Precio, Moneda,
                   DiasPrueba, DiasGracia, CantidadIncluida, PermiteExcedentes, PrecioExcedente,
                   CantidadAgentesIncluida, PrecioPorAgenteExcedente, RenovacionAutomaticaDefault,
                   Activo, VisibleCatalogo, CreadoUtc, ModificadoUtc
            FROM dbo.Planes
            WHERE Id = @Id;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        return await cn.QuerySingleOrDefaultAsync<PlanDto>(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task CreateAsync(CrearPlanRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        if (await ExistsCodigoAsync(cn, request.IdModulo, request.Codigo, excludeId: null, ct).ConfigureAwait(false))
            throw new AppUserFacingException("Ya existe un plan con ese código para este módulo.", "PLANES_CODIGO_DUPLICADO");

        const string sql = """
            INSERT INTO dbo.Planes
                (IdModulo, Codigo, Nombre, Descripcion, TipoFacturacion, Precio, Moneda, DiasPrueba,
                 DiasGracia, CantidadIncluida, PermiteExcedentes, PrecioExcedente,
                 CantidadAgentesIncluida, PrecioPorAgenteExcedente, RenovacionAutomaticaDefault, VisibleCatalogo)
            OUTPUT INSERTED.Id
            VALUES
                (@IdModulo, @Codigo, @Nombre, @Descripcion, @TipoFacturacion, @Precio, @Moneda, @DiasPrueba,
                 @DiasGracia, @CantidadIncluida, @PermiteExcedentes, @PrecioExcedente,
                 @CantidadAgentesIncluida, @PrecioPorAgenteExcedente, @RenovacionAutomaticaDefault, @VisibleCatalogo);
            """;

        var id = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, BuildParameters(request), cancellationToken: ct)).ConfigureAwait(false);

        await appEvents.LogAuditAsync("Planes", "Create", "Planes", id.ToString(), $"Plan '{request.Nombre}' creado para el módulo {request.IdModulo}.", request, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(CrearPlanRequest request, CancellationToken ct = default)
    {
        if (request.Id is null or <= 0)
            throw new AppUserFacingException("El plan seleccionado es inválido.", "PLANES_ID");

        ValidateRequest(request);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        if (await ExistsCodigoAsync(cn, request.IdModulo, request.Codigo, request.Id, ct).ConfigureAwait(false))
            throw new AppUserFacingException("Ya existe otro plan con ese código para este módulo.", "PLANES_CODIGO_DUPLICADO");

        const string sql = """
            UPDATE dbo.Planes
            SET IdModulo = @IdModulo,
                Codigo = @Codigo,
                Nombre = @Nombre,
                Descripcion = @Descripcion,
                TipoFacturacion = @TipoFacturacion,
                Precio = @Precio,
                Moneda = @Moneda,
                DiasPrueba = @DiasPrueba,
                DiasGracia = @DiasGracia,
                CantidadIncluida = @CantidadIncluida,
                PermiteExcedentes = @PermiteExcedentes,
                PrecioExcedente = @PrecioExcedente,
                CantidadAgentesIncluida = @CantidadAgentesIncluida,
                PrecioPorAgenteExcedente = @PrecioPorAgenteExcedente,
                RenovacionAutomaticaDefault = @RenovacionAutomaticaDefault,
                VisibleCatalogo = @VisibleCatalogo,
                ModificadoUtc = GETUTCDATE()
            WHERE Id = @Id;
            """;

        var filas = await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            request.IdModulo,
            request.Codigo,
            request.Nombre,
            request.Descripcion,
            request.TipoFacturacion,
            request.Precio,
            request.Moneda,
            request.DiasPrueba,
            request.DiasGracia,
            request.CantidadIncluida,
            request.PermiteExcedentes,
            request.PrecioExcedente,
            request.CantidadAgentesIncluida,
            request.PrecioPorAgenteExcedente,
            request.RenovacionAutomaticaDefault,
            request.VisibleCatalogo,
            request.Id
        }, cancellationToken: ct)).ConfigureAwait(false);

        if (filas == 0)
            throw new AppUserFacingException("El plan no existe.", "PLANES_NO_EXISTE");

        await appEvents.LogAuditAsync("Planes", "Update", "Planes", request.Id.Value.ToString(), $"Plan '{request.Nombre}' actualizado.", request, ct).ConfigureAwait(false);
    }

    public async Task SetActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Planes
            SET Activo = @Activo, ModificadoUtc = GETUTCDATE()
            WHERE Id = @Id;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var filas = await cn.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Activo = activo }, cancellationToken: ct)).ConfigureAwait(false);
        if (filas == 0)
            throw new AppUserFacingException("El plan no existe.", "PLANES_NO_EXISTE");

        await appEvents.LogAuditAsync("Planes", activo ? "Reactivar" : "Suspender", "Planes", id.ToString(), activo ? "Plan reactivado." : "Plan dado de baja (lógica).", new { id, activo }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PlanDto>>> GetPlanesVisiblesPorCodigoModuloAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT p.Id, p.IdModulo, p.Codigo, p.Nombre, p.Descripcion, p.TipoFacturacion, p.Precio, p.Moneda,
                   p.DiasPrueba, p.DiasGracia, p.CantidadIncluida, p.PermiteExcedentes, p.PrecioExcedente,
                   p.CantidadAgentesIncluida, p.PrecioPorAgenteExcedente, p.RenovacionAutomaticaDefault,
                   p.Activo, p.VisibleCatalogo, p.CreadoUtc, p.ModificadoUtc,
                   m.Codigo AS ModuloCodigo
            FROM dbo.Planes p
            JOIN dbo.Modulos m ON m.Id = p.IdModulo
            WHERE p.Activo = 1 AND p.VisibleCatalogo = 1 AND m.Activo = 1
            ORDER BY p.IdModulo, p.Precio;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var rows = await cn.QueryAsync<PlanDto, string, (PlanDto Plan, string ModuloCodigo)>(
            new CommandDefinition(sql, cancellationToken: ct),
            (plan, moduloCodigo) => (plan, moduloCodigo),
            splitOn: "ModuloCodigo").ConfigureAwait(false);

        return rows
            .GroupBy(r => r.ModuloCodigo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PlanDto>)g.Select(r => r.Plan).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateRequest(CrearPlanRequest request)
    {
        if (request.IdModulo <= 0)
            throw new AppUserFacingException("El módulo es obligatorio.", "PLANES_MODULO");
        if (string.IsNullOrWhiteSpace(request.Codigo))
            throw new AppUserFacingException("El código del plan es obligatorio.", "PLANES_CODIGO");
        if (string.IsNullOrWhiteSpace(request.Nombre))
            throw new AppUserFacingException("El nombre del plan es obligatorio.", "PLANES_NOMBRE");
        if (!PlanTipoFacturacion.Todos.Contains(request.TipoFacturacion))
            throw new AppUserFacingException("El tipo de facturación no es válido.", "PLANES_TIPO_FACTURACION");
        if (!MonedaValores.Todas.Contains(request.Moneda))
            throw new AppUserFacingException("La moneda no es válida.", "PLANES_MONEDA");
        if (request.Precio < 0)
            throw new AppUserFacingException("El precio no puede ser negativo.", "PLANES_PRECIO");
        if (request.DiasPrueba < 0)
            throw new AppUserFacingException("Los días de prueba no pueden ser negativos.", "PLANES_DIAS_PRUEBA");
        if (request.DiasGracia < 0)
            throw new AppUserFacingException("Los días de gracia no pueden ser negativos.", "PLANES_DIAS_GRACIA");
    }

    private static object BuildParameters(CrearPlanRequest request) => new
    {
        request.IdModulo,
        Codigo = NormalizeText(request.Codigo),
        Nombre = NormalizeText(request.Nombre),
        Descripcion = string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim(),
        request.TipoFacturacion,
        request.Precio,
        request.Moneda,
        request.DiasPrueba,
        request.DiasGracia,
        request.CantidadIncluida,
        request.PermiteExcedentes,
        request.PrecioExcedente,
        request.CantidadAgentesIncluida,
        request.PrecioPorAgenteExcedente,
        request.RenovacionAutomaticaDefault,
        request.VisibleCatalogo
    };

    private static async Task<bool> ExistsCodigoAsync(SqlConnection cn, int idModulo, string codigo, int? excludeId, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1) FROM dbo.Planes
            WHERE IdModulo = @IdModulo
              AND UPPER(LTRIM(RTRIM(Codigo))) = UPPER(LTRIM(RTRIM(@Codigo)))
              AND (@ExcludeId IS NULL OR Id <> @ExcludeId);
            """;
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { IdModulo = idModulo, Codigo = codigo, ExcludeId = excludeId }, cancellationToken: ct)).ConfigureAwait(false);
        return count > 0;
    }

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
