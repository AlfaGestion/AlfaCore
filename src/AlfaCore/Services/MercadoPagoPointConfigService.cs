using AlfaCore.Services.MercadoPagoPoint;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Mismo patrón de lectura de TA_CONFIGURACION que ArcaConfigService, simplificado: acá no
/// hace falta la "columna de detalle" para textos largos (access token/terminal id entran de sobra en
/// VALOR) ni resolución por unidad de negocio -- una sola terminal fija por base en esta etapa.</summary>
public sealed class MercadoPagoPointConfigService(ISessionService sessionService, IConfiguration configuration)
    : IMercadoPagoPointConfigService
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<MercadoPagoPointOptions?> ResolveOptionsAsync(CancellationToken ct = default)
    {
        var values = await ReadConfigAsync(ct);
        var accessToken = ReadValue(values, "MERCADOPAGO_ACCESS_TOKEN");

        // TerminalId no es obligatorio para devolver opciones: "Listar terminales" es justamente cómo
        // se descubre el Terminal ID la primera vez (solo necesita el Access Token). Quien arma un
        // cobro real (CobrarAsync) valida con options.Validate() que estén los dos.
        if (accessToken.Length == 0)
            return null;

        return new MercadoPagoPointOptions
        {
            AccessToken = accessToken,
            TerminalId = ReadValue(values, "MERCADOPAGO_TERMINAL_ID")
        };
    }

    public async Task<string> ResolveWebhookSecretAsync(CancellationToken ct = default)
    {
        var values = await ReadConfigAsync(ct);
        return ReadValue(values, "MERCADOPAGO_WEBHOOK_SECRET");
    }

    public async Task<string> ResolvePosExternalIdAsync(CancellationToken ct = default)
    {
        var values = await ReadConfigAsync(ct);
        return ReadValue(values, "MERCADOPAGO_POS_EXTERNAL_ID");
    }

    public async Task<string> ResolveCodigoMedioPagoAsync(CancellationToken ct = default)
    {
        var values = await ReadConfigAsync(ct);
        return ReadValue(values, "MERCADOPAGO_CODIGO_MEDIO_PAGO");
    }

    public async Task GuardarConfiguracionAsync(string accessToken, string terminalId, string posExternalId, string webhookSecret, string codigoMedioPago, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tran = (SqlTransaction)await cn.BeginTransactionAsync(ct);

        await GuardarValorAsync(cn, tran, "MERCADOPAGO_ACCESS_TOKEN", accessToken, ct);
        await GuardarValorAsync(cn, tran, "MERCADOPAGO_TERMINAL_ID", terminalId, ct);
        await GuardarValorAsync(cn, tran, "MERCADOPAGO_POS_EXTERNAL_ID", posExternalId, ct);
        await GuardarValorAsync(cn, tran, "MERCADOPAGO_WEBHOOK_SECRET", webhookSecret, ct);
        await GuardarValorAsync(cn, tran, "MERCADOPAGO_CODIGO_MEDIO_PAGO", codigoMedioPago, ct);

        await tran.CommitAsync(ct);
    }

    private static async Task GuardarValorAsync(SqlConnection cn, SqlTransaction tran, string clave, string valor, CancellationToken ct)
    {
        // MERGE (no solo UPDATE): la clave puede no existir todavía si la base guarda esta config
        // antes de tener aplicada la migración que la da de alta (ej. MERCADOPAGO_CODIGO_MEDIO_PAGO,
        // agregada después de la migración base de Mercado Pago Point).
        await using var cmd = new SqlCommand("""
            MERGE dbo.TA_CONFIGURACION AS destino
            USING (SELECT @Clave AS Clave) AS origen ON UPPER(LTRIM(RTRIM(destino.CLAVE))) = origen.Clave
            WHEN MATCHED THEN UPDATE SET VALOR = @Valor
            WHEN NOT MATCHED THEN INSERT (GRUPO, CLAVE, VALOR, FechaHora_Grabacion)
                VALUES (N'MERCADOPAGO', @Clave, @Valor, GETDATE());
            """, cn, tran);
        cmd.Parameters.AddWithValue("@Clave", clave);
        cmd.Parameters.AddWithValue("@Valor", valor.Trim());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<Dictionary<string, string>> ReadConfigAsync(CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), ISNULL(VALOR, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (
                N'MERCADOPAGO_ACCESS_TOKEN', N'MERCADOPAGO_TERMINAL_ID',
                N'MERCADOPAGO_POS_EXTERNAL_ID', N'MERCADOPAGO_WEBHOOK_SECRET',
                N'MERCADOPAGO_CODIGO_MEDIO_PAGO'
            )
            """, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var key = rd.IsDBNull(0) ? string.Empty : rd.GetString(0);
            var value = rd.IsDBNull(1) ? string.Empty : rd.GetString(1);
            values[key] = value;
        }

        return values;
    }

    private static string ReadValue(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
}
