using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.ComponentModel;
using System.IO;
using System.Net;

namespace AlfaCore.Services;

public sealed class AppUiOperationService : IAppUiOperationService
{
    public async Task<AppUiOperationResult> RunAsync(Func<Task> operation, string fallbackTitle, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await operation();
            return AppUiOperationResult.Ok();
        }
        catch (Exception ex)
        {
            return AppUiOperationResult.Fail(BuildMessage(ex, fallbackTitle));
        }
    }

    public async Task<AppUiOperationResult<T>> RunAsync<T>(Func<Task<T>> operation, string fallbackTitle, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var value = await operation();
            return AppUiOperationResult<T>.Ok(value);
        }
        catch (Exception ex)
        {
            return AppUiOperationResult<T>.Fail(BuildMessage(ex, fallbackTitle));
        }
    }

    public AppUiMessage BuildMessage(Exception exception, string fallbackTitle)
    {
        if (exception is AppValidationException validationEx)
        {
            return new AppUiMessage
            {
                Severity = AppUiFeedbackSeverity.Warning,
                Title = fallbackTitle,
                Message = validationEx.UserMessage,
                Suggestion = "Revisá los campos marcados y volvé a intentar."
            };
        }

        if (exception is AppUserFacingException appEx)
            return BuildFromKnownException(appEx.UserMessage, appEx.ErrorCode, appEx.InnerException, fallbackTitle);

        if (exception is InvalidOperationException invalidOp &&
            TryExtractCode(invalidOp.Message, out var plainMessage, out var code))
        {
            return BuildFromKnownException(plainMessage, code, invalidOp.InnerException, fallbackTitle);
        }

        return new AppUiMessage
        {
            Severity = AppUiFeedbackSeverity.Error,
            Title = fallbackTitle,
            Message = "Ocurrió un problema inesperado. Si persiste, revisá el detalle técnico registrado por el sistema.",
            Suggestion = "Volvé a intentar la operación y, si sigue fallando, compartí el código de error con soporte."
        };
    }

    private static AppUiMessage BuildFromKnownException(string userMessage, string code, Exception? innerException, string fallbackTitle)
    {
        var sqlException = FindSqlException(innerException);
        var win32Code = FindInnermostWin32Code(innerException);

        if (sqlException?.Number is 53 or 64 || win32Code is 53 or 64)
        {
            return new AppUiMessage
            {
                Severity = AppUiFeedbackSeverity.Error,
                Title = "No se pudo conectar a la base activa",
                Message = "La sesión SQL seleccionada no está accesible en este momento. El sistema no pudo llegar al servidor o a la instancia configurada.",
                Code = code,
                Suggestion = "Revisá la sesión activa, el nombre del servidor SQL, la instancia, la red y las credenciales antes de volver a intentar."
            };
        }

        if (IsFtpAccessError(innerException, win32Code))
        {
            return new AppUiMessage
            {
                Severity = AppUiFeedbackSeverity.Error,
                Title = "No se pudo acceder al FTP configurado",
                Message = "El sistema no pudo conectarse o subir los archivos al FTP configurado para Interfaces.",
                Code = code,
                Suggestion = "Revisá el host, puerto, usuario, clave, modo pasivo y la conectividad al servidor FTP antes de volver a intentar."
            };
        }

        if (IsSharedFolderAccessError(innerException, win32Code))
        {
            return new AppUiMessage
            {
                Severity = AppUiFeedbackSeverity.Error,
                Title = "No se pudo acceder a la carpeta compartida",
                Message = "El sistema no pudo guardar los archivos en la carpeta compartida configurada para Interfaces.",
                Code = code,
                Suggestion = "Revisá la ruta configurada, que la carpeta exista, que el servidor esté accesible y que este equipo tenga permisos de lectura y escritura."
            };
        }

        if (sqlException?.Number == 208)
        {
            return new AppUiMessage
            {
                Severity = AppUiFeedbackSeverity.Error,
                Title = fallbackTitle,
                Message = userMessage,
                Code = code,
                Suggestion = "La base activa parece no tener completo el esquema esperado. Revisá los scripts de inicialización del módulo."
            };
        }

        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            return new AppUiMessage
            {
                Severity = AppUiFeedbackSeverity.Error,
                Title = fallbackTitle,
                Message = userMessage,
                Code = code,
                Suggestion = string.IsNullOrWhiteSpace(code)
                    ? "Volvé a intentar la operación. Si persiste, revisá el log del sistema."
                    : "Si el problema persiste, compartí el código con soporte para revisar el incidente."
            };
        }

        return new AppUiMessage
        {
            Severity = AppUiFeedbackSeverity.Error,
            Title = fallbackTitle,
            Message = "No fue posible completar la operación solicitada.",
            Code = code,
            Suggestion = "Volvé a intentar la operación. Si persiste, revisá el log del sistema."
        };
    }

    private static bool TryExtractCode(string message, out string plainMessage, out string code)
    {
        plainMessage = message?.Trim() ?? string.Empty;
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(plainMessage))
            return false;

        var markerIndex = plainMessage.LastIndexOf("Código:", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            markerIndex = plainMessage.LastIndexOf("Codigo:", StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
            return false;

        code = plainMessage[(markerIndex + 7)..].Trim();
        plainMessage = plainMessage[..markerIndex].Trim().TrimEnd('.', ':');
        return !string.IsNullOrWhiteSpace(code);
    }

    private static SqlException? FindSqlException(Exception? exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is SqlException sqlException)
                return sqlException;
            current = current.InnerException;
        }

        return null;
    }

    private static int? FindInnermostWin32Code(Exception? exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is Win32Exception win32)
                return win32.NativeErrorCode;
            current = current.InnerException;
        }

        return null;
    }

    private static bool IsSharedFolderAccessError(Exception? exception, int? win32Code)
    {
        if (win32Code is 5 or 53 or 64 or 67)
            return true;

        var current = exception;
        while (current is not null)
        {
            if (current is UnauthorizedAccessException or DirectoryNotFoundException)
                return true;

            if (current is IOException ioEx)
            {
                var message = ioEx.Message ?? string.Empty;
                if (message.Contains("network path", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("nombre de red", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("ruta de acceso", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("cannot find the path", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("no se encuentra la ruta", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("acceso denegado", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        return false;
    }

    private static bool IsFtpAccessError(Exception? exception, int? win32Code)
    {
        if (win32Code is 53 or 64 or 67 or 11001)
            return true;

        var current = exception;
        while (current is not null)
        {
            if (current is WebException webEx)
            {
                if (webEx.Response is FtpWebResponse)
                    return true;

                var message = webEx.Message ?? string.Empty;
                if (message.Contains("ftp", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("remote name", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("servidor remoto", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("tiempo de espera", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        return false;
    }
}
