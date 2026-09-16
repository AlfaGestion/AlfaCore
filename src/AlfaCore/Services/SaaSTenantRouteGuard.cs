using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface ISaaSTenantRouteGuard
{
    Task<SaaSTenantRouteContext> ResolveAsync(int? expectedBaseId, string? idWeb = null, CancellationToken ct = default);
    SaaSTenantConnectionSnapshot GetRequiredConnection(int? expectedBaseId, string operationName);
    long Invalidate();
    bool IsCurrent(SaaSTenantRouteLease lease, int? routeBaseId, int? sessionBaseId);
}

public sealed class SaaSTenantRouteGuard(
    IAppModeService appMode,
    ISessionService sessionService,
    ICentralBasesService centralBasesService) : ISaaSTenantRouteGuard
{
    private readonly object _gate = new();
    private long _generation;
    private int? _baseId;

    public async Task<SaaSTenantRouteContext> ResolveAsync(int? expectedBaseId, string? idWeb = null, CancellationToken ct = default)
    {
        var routeBaseId = expectedBaseId is > 0 ? expectedBaseId.Value : (int?)null;
        var active = sessionService.GetActiveSession();

        if (!appMode.IsSaaSMode || routeBaseId is null)
        {
            if (active is null || active.BaseId <= 0)
                return SaaSTenantRouteContext.Invalid;

            return Capture(active.BaseId, idWeb, sessionService.GetConnectionString());
        }

        if (active?.BaseId != routeBaseId)
        {
            var routeBase = await centralBasesService.GetByIdAsync(routeBaseId.Value, ct);
            if (routeBase is null)
                return SaaSTenantRouteContext.Invalid;

            active = new SessionDto
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{routeBase.IdBase:000000000000}"),
                BaseId = routeBase.IdBase,
                Nombre = routeBase.Nombre,
                Servidor = routeBase.DbServer,
                BaseDatos = routeBase.DbName,
                Usuario = routeBase.DbUser,
                Password = routeBase.DbPassword,
                TrustServerCertificate = true,
                Activa = true
            };
            sessionService.SetWebhookOverride(active);
        }

        if (active is null || active.BaseId != routeBaseId)
            return SaaSTenantRouteContext.Invalid;

        return Capture(active.BaseId, idWeb, BuildConnectionString(active));
    }

    public SaaSTenantConnectionSnapshot GetRequiredConnection(int? expectedBaseId, string operationName)
    {
        var active = sessionService.GetActiveSession();
        if (expectedBaseId is > 0 && active?.BaseId != expectedBaseId.Value)
        {
            throw new InvalidOperationException(
                $"{operationName}: la base activa ({active?.BaseId.ToString() ?? "sin base"}) no coincide con la base esperada ({expectedBaseId.Value}).");
        }

        var connectionString = active is null ? string.Empty : BuildConnectionString(active);
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = sessionService.GetConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"{operationName}: no hay una sesión SQL activa seleccionada.");

        return new SaaSTenantConnectionSnapshot(active?.BaseId, connectionString);
    }

    public long Invalidate()
    {
        lock (_gate)
        {
            _baseId = null;
            return ++_generation;
        }
    }

    public bool IsCurrent(SaaSTenantRouteLease lease, int? routeBaseId, int? sessionBaseId)
    {
        lock (_gate)
        {
            return lease.IsValid
                   && lease.Generation == _generation
                   && _baseId == lease.IdBase
                   && (routeBaseId is null or 0 || routeBaseId == lease.IdBase)
                   && sessionBaseId == lease.IdBase;
        }
    }

    private SaaSTenantRouteContext Capture(int idBase, string? idWeb, string connectionString)
    {
        if (idBase <= 0 || string.IsNullOrWhiteSpace(connectionString))
            return SaaSTenantRouteContext.Invalid;

        lock (_gate)
        {
            if (_baseId != idBase)
            {
                _baseId = idBase;
                _generation++;
            }

            return new SaaSTenantRouteContext(
                idBase,
                NormalizeIdWeb(idWeb),
                connectionString,
                new SaaSTenantRouteLease(idBase, _generation));
        }
    }

    private static string BuildConnectionString(SessionDto session)
        => new SqlConnectionStringBuilder
        {
            DataSource = session.Servidor,
            InitialCatalog = session.BaseDatos,
            UserID = session.Usuario,
            Password = session.Password,
            TrustServerCertificate = session.TrustServerCertificate
        }.ConnectionString;

    private static string? NormalizeIdWeb(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public readonly record struct SaaSTenantRouteLease(int IdBase, long Generation)
{
    public bool IsValid => IdBase > 0 && Generation > 0;
}

public sealed record SaaSTenantRouteContext(
    int IdBase,
    string? IdWeb,
    string ConnectionString,
    SaaSTenantRouteLease Lease)
{
    public static SaaSTenantRouteContext Invalid { get; } = new(0, null, string.Empty, default);
    public bool IsValid => IdBase > 0 && !string.IsNullOrWhiteSpace(ConnectionString) && Lease.IsValid;
}

public sealed record SaaSTenantConnectionSnapshot(int? IdBase, string ConnectionString);
