using System.Text.Json;

namespace AlfaCore.Models;

/// <summary>Funciones de la app que consumen OpenAI, para medir y cobrar el consumo por base.</summary>
public static class IaUsoFunciones
{
    public const string Bot = "BOT";
    public const string Resumen = "RESUMEN";
    public const string Analisis = "ANALISIS";
    public const string Oportunidad = "OPORTUNIDAD";
    public const string Reescritura = "REESCRITURA";
    public const string InformesIa = "INFORMES_IA";
    public const string Cotizacion = "COTIZACION";
    public const string Proxy = "PROXY";
    public const string ArchivosBusqueda = "ARCHIVOS_BUSQUEDA";
    public const string ArchivosAlmacenamiento = "ARCHIVOS_ALMACENAMIENTO";
}

/// <summary>Tokens informados por OpenAI en el campo <c>usage</c> de una respuesta.</summary>
public readonly record struct IaUsoTokens(int Entrada, int EntradaCacheada, int Salida)
{
    public bool Vacio => Entrada == 0 && EntradaCacheada == 0 && Salida == 0;

    /// <summary>
    /// Lee <c>usage</c> de una respuesta de OpenAI. Soporta Chat Completions
    /// (<c>prompt_tokens</c>, <c>completion_tokens</c>, <c>prompt_tokens_details.cached_tokens</c>) y
    /// Responses (<c>input_tokens</c>, <c>output_tokens</c>, <c>input_tokens_details.cached_tokens</c>).
    /// Si no hay <c>usage</c> devuelve ceros.
    /// </summary>
    public static IaUsoTokens Desde(JsonElement respuesta)
    {
        if (respuesta.ValueKind != JsonValueKind.Object
            || !respuesta.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object)
            return default;

        var entrada = Entero(usage, "prompt_tokens") ?? Entero(usage, "input_tokens") ?? 0;
        var salida = Entero(usage, "completion_tokens") ?? Entero(usage, "output_tokens") ?? 0;
        var cacheada = Detalle(usage, "prompt_tokens_details") ?? Detalle(usage, "input_tokens_details") ?? 0;
        return new IaUsoTokens(entrada, Math.Min(cacheada, entrada), salida);

        static int? Detalle(JsonElement usage, string nombre)
            => usage.TryGetProperty(nombre, out var detalle) && detalle.ValueKind == JsonValueKind.Object
                ? Entero(detalle, "cached_tokens")
                : null;
    }

    private static int? Entero(JsonElement element, string nombre)
        => element.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.Number && valor.TryGetInt32(out var n)
            ? n
            : null;

    /// <summary>Modelo efectivamente usado según la respuesta (ej. "gpt-4o-mini-2024-07-18"), o el pedido.</summary>
    public static string Modelo(JsonElement respuesta, string? modeloPedido)
        => respuesta.ValueKind == JsonValueKind.Object
           && respuesta.TryGetProperty("model", out var model)
           && model.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(model.GetString())
            ? model.GetString()!.Trim()
            : (modeloPedido ?? string.Empty).Trim();
}

/// <summary>Un consumo de IA a registrar en ALFA_CENTRAL (IA_USO).</summary>
public sealed record IaUsoRegistro(
    DateTime FechaHoraUtc,
    int? IdBase,
    string BaseDatos,
    string Funcion,
    string Modelo,
    int TokensEntrada,
    int TokensEntradaCacheados,
    int TokensSalida,
    int Busquedas,
    long BytesAlmacenados,
    string Referencia,
    string? IdCliente = null);
