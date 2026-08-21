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
public sealed record ConversacionCuentaVinculadaDto(string Codigo, CuentaComercialTipo Tipo, string RazonSocial);
