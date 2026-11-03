using System.Text.Json;
using Microsoft.JSInterop;

namespace AlfaCore.Services;

/// <summary>
/// Últimos filtros de una pantalla, guardados en el navegador (localStorage) por base.
/// Es una comodidad del puesto: si no se puede leer o grabar, la pantalla sigue con sus valores por defecto.
/// Solo se usa después del primer render (sin JS durante el prerender).
/// </summary>
public static class FiltrosRecordados
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Clave(string pantalla, ISessionService sessions)
    {
        string baseId;
        try { baseId = sessions.GetActiveSession()?.Id.ToString("N") ?? "sin-base"; }
        catch { baseId = "sin-base"; }
        return $"alfacore.filtros.{pantalla}.{baseId}";
    }

    public static async Task<T?> LeerAsync<T>(IJSRuntime js, ISessionService sessions, string pantalla) where T : class
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", Clave(pantalla, sessions));
            return string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<T>(raw, Json);
        }
        catch { return null; }
    }

    public static async Task GuardarAsync<T>(IJSRuntime js, ISessionService sessions, string pantalla, T valor)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", Clave(pantalla, sessions), JsonSerializer.Serialize(valor, Json)); }
        catch { /* sin almacenamiento: no se recuerda */ }
    }
}
