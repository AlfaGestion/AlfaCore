using System.Net.Http.Headers;
using System.Text.Json;
using AlfaCore.Configuration;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Fachada temporal de la API Python existente.
/// Conserva los contratos públicos mientras la implementación se migra por módulos.
/// </summary>
public sealed class LegacyPythonApiProxy(
    IHttpClientFactory httpClientFactory,
    IOptions<LegacyPythonApiOptions> options,
    IAppEventService appEvents,
    ILogger<LegacyPythonApiProxy> logger)
{
    public const string HttpClientName = "LegacyPythonApi";

    private static readonly string[] HopByHopHeaders =
    [
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    ];

    public async Task ForwardAsync(HttpContext context, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            await WriteErrorAsync(context, StatusCodes.Status404NotFound,
                "La ruta de la API está deshabilitada en esta instalación.");
            return;
        }

        if (!settings.IsConfigured)
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                "La API Python no está configurada en el servidor.");
            return;
        }

        var targetText = settings.BaseUrl.TrimEnd('/') + context.Request.Path + context.Request.QueryString;
        if (!Uri.TryCreate(targetText, UriKind.Absolute, out var targetUri))
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                "La URL de la API Python no es válida.");
            return;
        }

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
        CopyRequestHeaders(context.Request, request);

        if (RequestCanHaveBody(context.Request.Method))
        {
            request.Content = new StreamContent(context.Request.Body);
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
            if (context.Request.ContentLength is long contentLength)
                request.Content.Headers.ContentLength = contentLength;

            // wsAlfa corre detrás de IIS/wfastcgi y algunas rutas POST no aceptan el cuerpo
            // enviado como chunked ni la negociación Expect: 100-continue.
            request.Headers.ExpectContinue = false;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            context.Response.StatusCode = (int)response.StatusCode;
            CopyResponseHeaders(response, context.Response);
            await response.Content.CopyToAsync(context.Response.Body, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var exception = new TimeoutException("Se agotó el tiempo de espera al comunicarse con la API Python.");
            await LogProxyErrorAsync(context, exception, "La API Python no respondió a tiempo.", ct);
            await WriteErrorAsync(context, StatusCodes.Status504GatewayTimeout,
                "La API no respondió a tiempo.");
        }
        catch (HttpRequestException ex)
        {
            await LogProxyErrorAsync(context, ex, "No se pudo conectar con la API Python.", ct);
            await WriteErrorAsync(context, StatusCodes.Status502BadGateway,
                "No se pudo conectar con la API central de servicios.");
        }
    }

    private async Task LogProxyErrorAsync(HttpContext context, Exception exception, string message, CancellationToken ct)
    {
        logger.LogError(exception, "Error al derivar {Method} {Path} a la API Python.",
            context.Request.Method, context.Request.Path);

        await appEvents.LogErrorAsync(
            "Api",
            "PythonLegacyProxy",
            exception,
            message,
            new
            {
                context.Request.Method,
                Path = context.Request.Path.Value,
                Query = context.Request.QueryString.Value,
                context.TraceIdentifier
            },
            ct: ct);
    }

    private static void CopyRequestHeaders(HttpRequest source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (IsHopByHopHeader(header.Key) || string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;

            target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse target)
    {
        foreach (var header in source.Headers)
        {
            if (!IsHopByHopHeader(header.Key))
                target.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in source.Content.Headers)
        {
            if (!IsHopByHopHeader(header.Key))
                target.Headers[header.Key] = header.Value.ToArray();
        }

        target.Headers.Remove("transfer-encoding");
    }

    private static bool RequestCanHaveBody(string method)
        => !HttpMethods.IsGet(method)
           && !HttpMethods.IsHead(method)
           && !HttpMethods.IsOptions(method)
           && !HttpMethods.IsTrace(method);

    private static bool IsHopByHopHeader(string name)
        => HopByHopHeaders.Contains(name, StringComparer.OrdinalIgnoreCase)
           || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = "error",
            message,
            error = true,
            traceId = context.TraceIdentifier
        });
    }
}
