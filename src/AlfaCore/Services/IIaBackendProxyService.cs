namespace AlfaCore.Services;

/// <summary>
/// Backend propio para IA_ProcesarDocumentos (y cualquier otro cliente HMAC-firmado que hable el
/// mismo protocolo) -- reemplaza al servidor Python aparte (alfanetac.ddns.net:8805,
/// ia_backend_proxy_server.py) centralizando OPENAI_API_KEY en AlfaCore. Valida firma HMAC por
/// cliente contra dbo.clientes.KeyIa (base ALFA_CENTRAL), aplica el límite mensual de
/// dbo.IA_LimitesConsultasGPT si existe, llama a la Responses API de OpenAI y audita en
/// dbo.IA_ConsultasGPT -- mismo contrato HTTP que el servidor viejo, para no tener que tocar el
/// cliente más allá de la URL.
/// </summary>
public interface IIaBackendProxyService
{
    Task<IaBackendProcessOutcome> ProcessAsync(
        string rawBody,
        string clientId,
        string timestamp,
        string nonce,
        string signature,
        CancellationToken ct = default);

    /// <summary>Resuelve idcliente+KeyIa a partir de LicenciaPrincipal -- el token de licencia que
    /// cada instalación ya tiene grabado localmente (NW_ESTADISTICAS.TA_CONFIGURACION), para que el
    /// cliente nunca necesite guardar KeyIa en un archivo propio. Si el cliente existe pero todavía
    /// no tiene KeyIa, se genera uno nuevo (256 bits) y se graba en el momento.</summary>
    Task<IaBackendProcessOutcome> ResolveCredentialsAsync(string licenciaPrincipal, CancellationToken ct = default);
}

/// <summary>Resultado ya serializable: Program.cs solo hace Results.Json(Body, statusCode: StatusCode).</summary>
public sealed record IaBackendProcessOutcome(int StatusCode, object Body)
{
    public static IaBackendProcessOutcome Error(int statusCode, string error)
        => new(statusCode, new { ok = false, error });

    public static IaBackendProcessOutcome Success(string model, string outputText)
        => new(200, new { ok = true, model, output_text = outputText });
}
