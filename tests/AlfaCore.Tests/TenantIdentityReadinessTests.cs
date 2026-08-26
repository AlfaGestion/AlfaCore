using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TenantIdentityReadinessTests
{
    private static readonly Guid BaseA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void UnauthenticatedUserRequiresLogin()
    {
        var state = TenantIdentityReadiness.ResolveState(false, false, null, false);

        Assert.Equal(TenantIdentityState.Unauthenticated, state);
    }

    [Fact]
    public void CentralUserPendingInternalIdentityIsNotTenantReady()
    {
        var state = TenantIdentityReadiness.ResolveState(true, true, BaseA, false);

        Assert.Equal(TenantIdentityState.CentralAuthenticatedPendingInternalIdentity, state);
    }

    [Fact]
    public void InternalIdentityForActiveBaseIsTenantReady()
    {
        var state = TenantIdentityReadiness.ResolveState(true, false, BaseA, true);

        Assert.Equal(TenantIdentityState.TenantIdentityReady, state);
    }

    [Fact]
    public void InternalIdentityFromAnotherBaseIsNotReused()
    {
        var state = TenantIdentityReadiness.ResolveState(true, false, BaseA, false);

        Assert.Equal(TenantIdentityState.CentralAuthenticatedPendingInternalIdentity, state);
    }
}
