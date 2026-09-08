using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConversationTenantLoadGuardTests
{
    [Fact]
    public void Route84WithSession106_IsRejectedBeforeAnyLoad()
    {
        var guard = new ConversationTenantLoadGuard();

        var lease = guard.Capture(routeBaseId: 84, sessionBaseId: 106);

        Assert.False(lease.IsValid);
    }

    [Fact]
    public void LateBase106Result_IsDiscardedAfterBase84BecomesCurrent()
    {
        var guard = new ConversationTenantLoadGuard();
        var oldLease = guard.Capture(routeBaseId: 106, sessionBaseId: 106);
        var currentLease = guard.Capture(routeBaseId: 84, sessionBaseId: 84);

        Assert.True(currentLease.IsValid);
        Assert.False(guard.IsCurrent(oldLease, routeBaseId: 84, sessionBaseId: 84));
        Assert.True(guard.IsCurrent(currentLease, routeBaseId: 84, sessionBaseId: 84));
    }

    [Fact]
    public void SameNumeroIdInDifferentBases_IsNotTheSameLoadContext()
    {
        var guard = new ConversationTenantLoadGuard();
        var base84 = guard.Capture(routeBaseId: 84, sessionBaseId: 84);
        var base106 = guard.Capture(routeBaseId: 106, sessionBaseId: 106);

        Assert.False(guard.IsCurrent(base84, routeBaseId: 106, sessionBaseId: 106));
        Assert.True(guard.IsCurrent(base106, routeBaseId: 106, sessionBaseId: 106));
    }
}
