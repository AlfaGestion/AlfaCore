namespace AlfaCore.Services;

/// <summary>
/// Mantiene el contexto de tenant de una pantalla de Conversaciones. Una respuesta iniciada para
/// otra base nunca puede aplicarse cuando la ruta o la sesión activa cambiaron durante el await.
/// </summary>
public sealed class ConversationTenantLoadGuard
{
    private long _generation;
    private int? _baseId;

    public ConversationTenantLoadLease Capture(int? routeBaseId, int? sessionBaseId)
    {
        if (routeBaseId is > 0 && sessionBaseId != routeBaseId)
            return ConversationTenantLoadLease.Invalid;

        var resolvedBaseId = routeBaseId ?? sessionBaseId;
        if (resolvedBaseId is not > 0)
            return ConversationTenantLoadLease.Invalid;

        if (_baseId != resolvedBaseId)
        {
            _baseId = resolvedBaseId;
            _generation++;
        }

        return new ConversationTenantLoadLease(resolvedBaseId.Value, _generation);
    }

    public long Invalidate()
    {
        _baseId = null;
        return ++_generation;
    }

    public bool IsCurrent(ConversationTenantLoadLease lease, int? routeBaseId, int? sessionBaseId)
        => lease.IsValid
           && lease.Generation == _generation
           && _baseId == lease.BaseId
           && (routeBaseId is null or 0 || routeBaseId == lease.BaseId)
           && sessionBaseId == lease.BaseId;
}

public readonly record struct ConversationTenantLoadLease(int BaseId, long Generation)
{
    public static ConversationTenantLoadLease Invalid => new(0, 0);
    public bool IsValid => BaseId > 0 && Generation > 0;
}
