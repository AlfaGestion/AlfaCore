namespace AlfaCore.Models;

public enum AppUiFeedbackSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
    InProgress = 4,
    ActionRequired = 5
}

public sealed class AppUiMessage
{
    public AppUiFeedbackSeverity Severity { get; init; } = AppUiFeedbackSeverity.Info;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;

    /// <summary>
    /// Etiqueta de la acción principal opcional (p. ej. "Reintentar", "Asignar usuarios"). El
    /// componente que renderiza este mensaje (p. ej. AlfaFeedbackPanel) es responsable de invocar
    /// el callback correspondiente; este modelo sólo transporta el rótulo, no el delegado.
    /// </summary>
    public string PrimaryActionLabel { get; init; } = string.Empty;
    public string SecondaryActionLabel { get; init; } = string.Empty;

    /// <summary>
    /// Identifica a qué intento/operación pertenece este mensaje (p. ej. un IdOnboarding o un
    /// IdNumero). Permite que la UI evite mezclar el feedback de una operación nueva con el estado
    /// ya resuelto de otra (ver auditoría Base4264: un error de un onboarding nuevo no debe
    /// aparecer debajo de la tarjeta verde de un número ya conectado).
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    public bool HasCode => !string.IsNullOrWhiteSpace(Code);
    public bool HasSuggestion => !string.IsNullOrWhiteSpace(Suggestion);
    public bool HasPrimaryAction => !string.IsNullOrWhiteSpace(PrimaryActionLabel);
    public bool HasSecondaryAction => !string.IsNullOrWhiteSpace(SecondaryActionLabel);

    public static AppUiMessage Success(string title, string message)
        => new()
        {
            Severity = AppUiFeedbackSeverity.Success,
            Title = title,
            Message = message
        };

    public static AppUiMessage Info(string title, string message)
        => new()
        {
            Severity = AppUiFeedbackSeverity.Info,
            Title = title,
            Message = message
        };

    public static AppUiMessage Warning(string title, string message, string suggestion = "")
        => new()
        {
            Severity = AppUiFeedbackSeverity.Warning,
            Title = title,
            Message = message,
            Suggestion = suggestion
        };

    public static AppUiMessage Error(string title, string message, string suggestion = "", string code = "")
        => new()
        {
            Severity = AppUiFeedbackSeverity.Error,
            Title = title,
            Message = message,
            Suggestion = suggestion,
            Code = code
        };

    public static AppUiMessage InProgress(string title, string message)
        => new()
        {
            Severity = AppUiFeedbackSeverity.InProgress,
            Title = title,
            Message = message
        };

    public static AppUiMessage ActionRequired(string title, string message, string suggestion = "")
        => new()
        {
            Severity = AppUiFeedbackSeverity.ActionRequired,
            Title = title,
            Message = message,
            Suggestion = suggestion
        };
}

public sealed class AppUiOperationResult
{
    public bool Success { get; init; }
    public AppUiMessage? Feedback { get; init; }

    public static AppUiOperationResult Ok()
        => new() { Success = true };

    public static AppUiOperationResult Fail(AppUiMessage feedback)
        => new() { Success = false, Feedback = feedback };
}

public sealed class AppUiOperationResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public AppUiMessage? Feedback { get; init; }

    public static AppUiOperationResult<T> Ok(T value)
        => new() { Success = true, Value = value };

    public static AppUiOperationResult<T> Fail(AppUiMessage feedback)
        => new() { Success = false, Feedback = feedback };
}
