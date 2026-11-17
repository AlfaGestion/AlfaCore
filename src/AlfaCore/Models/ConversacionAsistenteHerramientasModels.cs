namespace AlfaCore.Models;

/// <summary>
/// Definición de una herramienta (function-calling de OpenAI) que el asistente de Conversaciones
/// puede invocar para consultar datos reales durante una respuesta. Se arma dinámicamente por
/// conversación según los toggles de <see cref="ConversacionAutomatizacionesConfigDto"/> y el tipo
/// de cuenta (Cliente/Proveedor) vinculada -- ver ConversacionAsistenteHerramientasService.
/// </summary>
public sealed class ConversacionAsistenteHerramientaDefinicionDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema (texto) de los parámetros, formato "parameters" de function-calling de OpenAI.
    /// Ninguna herramienta sensible (saldo/pedidos/portal) declara acá un identificador de cuenta:
    /// la cuenta siempre se resuelve server-side a partir de la conversación, nunca la manda el modelo.
    /// </summary>
    public string ParametrosJsonSchema { get; set; } = "{\"type\":\"object\",\"properties\":{},\"required\":[]}";
}

/// <summary>Cuenta comercial (Cliente o Proveedor) resuelta server-side para una conversación.</summary>
public sealed record ConversacionCuentaVinculadaDto(string Codigo, CuentaComercialTipo Tipo, string RazonSocial)
{
    public bool EsAmbigua { get; init; }
    /// <summary>Reconocida por el teléfono cargado del Cliente (no por un vínculo guardado en la conversación).</summary>
    public bool PorTelefono { get; init; }
}

/// <summary>Resultado de la tool registrar_cliente: el texto para el modelo y, si corresponde, una nota para el equipo.</summary>
public sealed record ConversacionBotAltaPreparada(string TextoParaModelo, string? NotaInterna = null, ConversacionCuentaVinculadaDto? ClienteVinculado = null);

/// <summary>Resultado de confirmar el alta: el texto para el contacto y el Cliente creado (null si falló).</summary>
public sealed record ConversacionBotAltaConfirmada(string Respuesta, ConversacionCuentaVinculadaDto? Cliente = null, string? NotaInterna = null);
