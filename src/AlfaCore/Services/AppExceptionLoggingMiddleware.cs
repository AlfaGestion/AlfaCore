using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class AppExceptionLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAppEventService appEvents)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            TryWriteWebhookFailureDiagnostic(context, ex);
            await appEvents.LogErrorAsync(
                "HTTP",
                $"{context.Request.Method} {context.Request.Path}",
                ex,
                "Se produjo un error inesperado procesando la solicitud.",
                new
                {
                    context.Request.QueryString,
                    context.TraceIdentifier
                });
            throw;
        }
    }

    private static void TryWriteWebhookFailureDiagnostic(HttpContext context, Exception exception)
    {
        if (!context.Request.Path.StartsWithSegments("/api/conversaciones/whatsapp/webhook/", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "diagnostics");
            Directory.CreateDirectory(directory);
            var record = new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                TraceIdentifier = context.TraceIdentifier,
                Path = "/api/conversaciones/whatsapp/webhook/[REDACTED]",
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                ExceptionMessage = Sanitize(exception.Message),
                InnerExceptionType = exception.InnerException?.GetType().FullName ?? string.Empty,
                InnerExceptionMessage = Sanitize(exception.InnerException?.Message),
                StackTrace = Sanitize(exception.StackTrace)
            };
            var file = Path.Combine(directory, $"tenant-webhook-exception-failures-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            File.AppendAllText(file, JsonSerializer.Serialize(record) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // The original exception remains authoritative even if diagnostics cannot be written.
        }
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sanitized = Regex.Replace(value, @"(?i)(bearer\s+|sha256=)[^\s]+", "$1[REDACTED]");
        sanitized = Regex.Replace(sanitized, @"(?i)(/(?:webhook)/)[^?\s/]+", "$1[REDACTED]");
        sanitized = Regex.Replace(sanitized, @"(?<!\d)\d{8,}(?!\d)", "[REDACTED_NUMBER]");
        return sanitized.Length <= 6000 ? sanitized : sanitized[..6000];
    }
}
