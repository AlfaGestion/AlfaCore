using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Arma y ejecuta las herramientas (tool-calling) que el asistente de Conversaciones puede usar
/// para consultar datos reales -- precios, saldos y pedidos. Ver diseño en memoria del proyecto
/// "conversaciones_agente_ia_plan.md". Separado de <see cref="IConversacionAsistenteService"/> a
/// propósito: ese servicio solo habla el protocolo de OpenAI (HTTP, sin SQL); acá vive la lógica
/// de negocio real, con acceso a los servicios existentes (precios, Portal Cliente, saldo proveedor).
/// </summary>
public interface IConversacionAsistenteHerramientasService
{
    /// <summary>
    /// Arma la lista de herramientas habilitadas para esta conversación: cruza cada toggle de
    /// <paramref name="config"/> contra si aplica al tipo de cuenta vinculada. Sin cuenta vinculada,
    /// solo puede quedar habilitado "consultar_precio" (funciona con lista de precios por defecto).
    /// </summary>
    IReadOnlyList<ConversacionAsistenteHerramientaDefinicionDto> ObtenerHerramientasDisponibles(
        ConversacionAutomatizacionesConfigDto config,
        ConversacionCuentaVinculadaDto? cuenta,
        string mensajeCliente);

    /// <summary>
    /// Ejecuta una herramienta por nombre. La cuenta viene resuelta server-side (nunca del modelo);
    /// <paramref name="argumentosJson"/> es lo que mandó OpenAI, pero ninguna herramienta sensible
    /// acepta de ahí un identificador de cuenta -- ver guardrail en ObtenerHerramientasDisponibles.
    /// Devuelve texto plano/compacto listo para pasarle de vuelta al modelo como resultado de la tool.
    /// </summary>
    Task<string> EjecutarAsync(
        string nombre,
        string argumentosJson,
        ConversacionCuentaVinculadaDto? cuenta,
        CancellationToken ct = default);
}
