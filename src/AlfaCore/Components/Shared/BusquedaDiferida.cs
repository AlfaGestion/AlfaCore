namespace AlfaCore.Components.Shared;

/// <summary>
/// Búsqueda mientras se escribe: ejecuta la acción cuando el usuario deja de tipear por un instante.
/// Cada tecla reprograma la búsqueda; si hay una consulta en curso, espera a que termine en lugar de
/// descartar lo último que se escribió.
/// </summary>
public sealed class BusquedaDiferida : IDisposable
{
    private CancellationTokenSource? _cts;

    public async Task ProgramarAsync(Func<Task> buscar, Func<bool> ocupado, int esperaMs = 350)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(esperaMs, cts.Token);
            for (var i = 0; i < 40 && ocupado(); i++)
                await Task.Delay(150, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
            await buscar();
    }

    public void Dispose() => _cts?.Cancel();
}
