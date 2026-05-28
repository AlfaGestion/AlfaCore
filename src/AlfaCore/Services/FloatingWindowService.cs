namespace AlfaCore.Services;

public sealed class FloatingWindowService : IFloatingWindowService
{
    private readonly object _sync = new();
    private readonly List<FloatingWindowRegistration> _registrations = [];

    public IDisposable Register(FloatingWindowType type, Func<Task> closeAsync)
    {
        ArgumentNullException.ThrowIfNull(closeAsync);
        var registration = new FloatingWindowRegistration(type, closeAsync);

        lock (_sync)
        {
            _registrations.Add(registration);
        }

        return new DisposableRegistration(this, registration);
    }

    public async Task CloseAllAsync()
    {
        FloatingWindowRegistration[] registrations;
        lock (_sync)
        {
            registrations = [.. _registrations];
        }

        foreach (var registration in registrations)
            await registration.CloseAsync();
    }

    private void Unregister(FloatingWindowRegistration registration)
    {
        lock (_sync)
        {
            _registrations.Remove(registration);
        }
    }

    private sealed record FloatingWindowRegistration(FloatingWindowType Type, Func<Task> CloseAsync);

    private sealed class DisposableRegistration(FloatingWindowService owner, FloatingWindowRegistration registration) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            owner.Unregister(registration);
            _disposed = true;
        }
    }
}
