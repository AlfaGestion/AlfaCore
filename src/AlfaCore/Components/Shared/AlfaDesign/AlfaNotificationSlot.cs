using AlfaCore.Models;

namespace AlfaCore.Components.Shared.AlfaDesign;

/// <summary>
/// Adapta los avisos legacy que una pantalla guarda como texto (o como su propio record) al
/// <see cref="AppUiMessage"/> que espera <c>AlfaNotification</c>. Devuelve siempre la misma
/// instancia mientras el aviso no cambie: AlfaNotification reprograma su cierre automático cada vez
/// que recibe un objeto distinto, así que crear uno nuevo en cada render lo dejaría fijo en pantalla.
/// </summary>
public sealed class AlfaNotificationSlot
{
    private string _key = string.Empty;
    private AppUiMessage? _message;

    public AppUiMessage? For(string? text, bool isError, string? title = null, string? code = null)
        => For(text, isError ? AppUiFeedbackSeverity.Error : AppUiFeedbackSeverity.Success, title, code);

    /// <summary>Colores AlfaDesign: Error rojo, Warning (avisos) amarillo, Success verde.</summary>
    public AppUiMessage? For(string? text, AppUiFeedbackSeverity severity, string? title = null, string? code = null)
    {
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(title))
        {
            _key = string.Empty;
            _message = null;
            return null;
        }

        var key = string.Join('\u001f', severity, title, text, code);
        if (key != _key || _message is null)
        {
            _key = key;
            _message = new AppUiMessage
            {
                Severity = severity,
                Title = string.IsNullOrWhiteSpace(title)
                    ? (severity == AppUiFeedbackSeverity.Error ? "No se pudo completar la operación" : "Listo")
                    : title,
                Message = text ?? string.Empty,
                Code = code ?? string.Empty
            };
        }

        return _message;
    }
}
