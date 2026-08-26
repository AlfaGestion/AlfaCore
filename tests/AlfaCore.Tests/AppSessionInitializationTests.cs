using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class AppSessionInitializationTests
{
    [Fact]
    public async Task ProtectedConsumerWaitsUntilAuthenticatedRestorationCompletes()
    {
        var initialization = new AppSessionInitialization();
        initialization.BeginRestoring();

        var waiting = initialization.WaitUntilReadyAsync();
        Assert.False(waiting.IsCompleted);

        initialization.Complete(authenticated: true);

        Assert.Equal(AppSessionInitializationState.ReadyAuthenticated, await waiting);
    }

    [Fact]
    public async Task MissingServerSideSessionCompletesAsUnauthenticated()
    {
        var initialization = new AppSessionInitialization();
        initialization.BeginRestoring();
        initialization.Complete(authenticated: false);

        Assert.Equal(
            AppSessionInitializationState.ReadyUnauthenticated,
            await initialization.WaitUntilReadyAsync());
    }
}
