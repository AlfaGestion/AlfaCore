namespace AlfaCore.Services;

/// <summary>
/// Aviso dentro del circuito (scoped): la pantalla de Calendario lo dispara al guardar la
/// configuración del indicador o al cambiar eventos, y el indicador de la barra superior se
/// actualiza en el momento en vez de esperar su próxima consulta periódica.
/// </summary>
public sealed class CalendarioIndicadorNotifier
{
    public event Action? Cambio;

    public void Notificar() => Cambio?.Invoke();
}
