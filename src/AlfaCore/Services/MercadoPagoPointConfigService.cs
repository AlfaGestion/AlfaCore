using AlfaCore.Models;
using AlfaCore.Services.MercadoPagoPoint;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Mismo patrón de lectura de TA_CONFIGURACION que ArcaConfigService, simplificado: acá no
/// hace falta la "columna de detalle" para textos largos (access token/terminal id entran de sobra en
/// VALOR) ni resolución por unidad de negocio -- una sola terminal fija por base en esta etapa.</summary>
public sealed class MercadoPagoPointConfigService(ISessionService sessionService, IConfiguration configuration)
    : IMercadoPagoPointConfigService
{
    private const string KeyLegacyPaymentMethod = "MERCADOPAGO_CODIGO_MEDIO_PAGO";
    private const string KeyEnabledPaymentMethods = "MERCADOPAGO_MEDIOS_PAGO";
    private const string KeyQrPaymentMethod = "MERCADOPAGO_MEDIO_PAGO_QR";
    private const string KeyCardPaymentMethod = "MERCADOPAGO_MEDIO_PAGO_TARJETA";
    private const string KeyAutomaticPaymentMethod = "MERCADOPAGO_OBTENER_MEDIO_PAGO_AUTOMATICO";
    private const string KeyTestMode = "MERCADOPAGO_MODO_PRUEBA";
    private const string KeyPointOfSaleReadersPrefix = "MERCADOPAGO_LECTORES_PV_";
    private const string LegacyTokenKey = "Point_Token";
    private const string LegacyPaymentMethodsKey = "Point_Medios_Pago";
    private const string LegacyAutomaticPaymentMethodKey = "Point_Obtener_Medio_Pago_Automatico";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<MercadoPagoPointConfigDto> GetConfiguracionAsync(CancellationToken ct = default)
    {
        var values = await ReadConfigAsync(ct);
        var legacy = ReadValue(values, KeyLegacyPaymentMethod);
        var qr = ReadValue(values, KeyQrPaymentMethod);
        var tarjeta = ReadValue(values, KeyCardPaymentMethod);

        if (qr.Length == 0) qr = legacy;
        if (tarjeta.Length == 0) tarjeta = qr;

        var enabled = SplitCodes(ReadCompatValue(values, KeyEnabledPaymentMethods, LegacyPaymentMethodsKey));
        if (enabled.Count == 0)
        {
            if (qr.Length > 0) enabled.Add(qr);
            if (tarjeta.Length > 0 && !enabled.Contains(tarjeta, StringComparer.OrdinalIgnoreCase)) enabled.Add(tarjeta);
        }

        var puntosVenta = await GetPuntosVentaAsync(values, ct);
        return new MercadoPagoPointConfigDto
        {
            AccessToken = ReadCompatValue(values, "MERCADOPAGO_ACCESS_TOKEN", LegacyTokenKey),
            TerminalId = ReadValue(values, "MERCADOPAGO_TERMINAL_ID"),
            PosExternalId = ReadValue(values, "MERCADOPAGO_POS_EXTERNAL_ID"),
            WebhookSecret = ReadValue(values, "MERCADOPAGO_WEBHOOK_SECRET"),
            ObtenerMedioPagoAutomatico = IsYes(ReadCompatValue(values, KeyAutomaticPaymentMethod, LegacyAutomaticPaymentMethodKey)),
            ModoPrueba = IsYes(ReadValue(values, KeyTestMode))
                         && !string.Equals(ReadValue(values, "ARCA_AMBIENTE"), "PRODUCCION", StringComparison.OrdinalIgnoreCase),
            MismoMedioParaQrYTarjeta = string.Equals(qr, tarjeta, StringComparison.OrdinalIgnoreCase),
            MedioPagoQr = qr,
            MedioPagoTarjeta = tarjeta,
            MediosPagoHabilitados = enabled,
            PuntosVenta = puntosVenta
        };
    }

    public async Task<MercadoPagoPointOptions?> ResolveOptionsAsync(CancellationToken ct = default)
    {
        var values = await ReadConfigAsync(ct);
        var accessToken = ReadCompatValue(values, "MERCADOPAGO_ACCESS_TOKEN", LegacyTokenKey);

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

    public async Task<MercadoPagoPointOptions?> ResolveOptionsForPuntoVentaAsync(int idPuntoVenta, CancellationToken ct = default)
    {
        var options = await ResolveOptionsAsync(ct);
        if (options is null || idPuntoVenta <= 0)
            return options;

        var config = await GetConfiguracionAsync(ct);
        var puntoVenta = config.PuntosVenta.FirstOrDefault(x => x.Id == idPuntoVenta);
        var terminalAsignada = puntoVenta?.TerminalesAsignadas.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(terminalAsignada))
            options.TerminalId = terminalAsignada.Trim();

        return options;
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

    public async Task<IReadOnlySet<string>> ResolveMediosConfiguradosAsync(CancellationToken ct = default)
    {
        var config = await GetConfiguracionAsync(ct);
        if (config.MediosPagoHabilitados.Count > 0)
            return config.MediosPagoHabilitados
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Compatibilidad con la primera versión web y con configuraciones VB6 antiguas.
        return new[] { config.MedioPagoQr, config.MedioPagoTarjeta }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task GuardarConfiguracionAsync(MercadoPagoPointConfigDto configuracion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var arcaAmbiente = await cn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT TOP (1) ISNULL(VALOR, '') FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'ARCA_AMBIENTE';",
            cancellationToken: ct));
        await using var tran = (SqlTransaction)await cn.BeginTransactionAsync(ct);

        await GuardarValorAsync(cn, tran, "MERCADOPAGO_ACCESS_TOKEN", configuracion.AccessToken, ct);
        await GuardarValorAsync(cn, tran, LegacyTokenKey, configuracion.AccessToken, ct);
        await GuardarValorAsync(cn, tran, "MERCADOPAGO_TERMINAL_ID", configuracion.TerminalId, ct);
        await GuardarValorAsync(cn, tran, "MERCADOPAGO_POS_EXTERNAL_ID", configuracion.PosExternalId, ct);
        await GuardarValorAsync(cn, tran, "MERCADOPAGO_WEBHOOK_SECRET", configuracion.WebhookSecret, ct);
        var medios = string.Join(',', NormalizeCodes(configuracion.MediosPagoHabilitados));
        await GuardarValorAsync(cn, tran, KeyEnabledPaymentMethods, medios, ct);
        await GuardarValorAsync(cn, tran, LegacyPaymentMethodsKey, medios, ct);
        await GuardarValorAsync(cn, tran, KeyQrPaymentMethod, configuracion.MedioPagoQr, ct);
        await GuardarValorAsync(cn, tran, KeyCardPaymentMethod, configuracion.MismoMedioParaQrYTarjeta ? configuracion.MedioPagoQr : configuracion.MedioPagoTarjeta, ct);
        var automatico = configuracion.ObtenerMedioPagoAutomatico ? "SI" : "NO";
        await GuardarValorAsync(cn, tran, KeyAutomaticPaymentMethod, automatico, ct);
        await GuardarValorAsync(cn, tran, LegacyAutomaticPaymentMethodKey, automatico, ct);
        var modoPrueba = configuracion.ModoPrueba
                         && !string.Equals(arcaAmbiente, "PRODUCCION", StringComparison.OrdinalIgnoreCase)
            ? "SI"
            : "NO";
        await GuardarValorAsync(cn, tran, KeyTestMode, modoPrueba, ct);

        // Compatibilidad con la primera versión web, que tenía un solo medio vinculado.
        var legacy = NormalizeCodes(configuracion.MediosPagoHabilitados).FirstOrDefault()
            ?? configuracion.MedioPagoQr.Trim();
        await GuardarValorAsync(cn, tran, KeyLegacyPaymentMethod, legacy, ct);

        foreach (var puntoVenta in configuracion.PuntosVenta)
        {
            if (puntoVenta.Id <= 0) continue;
            await GuardarValorAsync(cn, tran, KeyPointOfSaleReadersPrefix + puntoVenta.Id,
                string.Join(',', NormalizeCodes(puntoVenta.TerminalesAsignadas)), ct);
        }

        await tran.CommitAsync(ct);
    }

    public async Task GuardarConfiguracionAsync(string accessToken, string terminalId, string posExternalId, string webhookSecret, string codigoMedioPago, CancellationToken ct = default)
    {
        await GuardarConfiguracionAsync(new MercadoPagoPointConfigDto
        {
            AccessToken = accessToken,
            TerminalId = terminalId,
            PosExternalId = posExternalId,
            WebhookSecret = webhookSecret,
            MedioPagoQr = codigoMedioPago,
            MedioPagoTarjeta = codigoMedioPago,
            MediosPagoHabilitados = string.IsNullOrWhiteSpace(codigoMedioPago) ? [] : [codigoMedioPago]
        }, ct);
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
                N'MERCADOPAGO_CODIGO_MEDIO_PAGO', N'MERCADOPAGO_MEDIOS_PAGO',
                N'MERCADOPAGO_MEDIO_PAGO_QR', N'MERCADOPAGO_MEDIO_PAGO_TARJETA',
                N'MERCADOPAGO_OBTENER_MEDIO_PAGO_AUTOMATICO',
                N'MERCADOPAGO_MODO_PRUEBA', N'ARCA_AMBIENTE',
                N'POINT_TOKEN', N'POINT_MEDIOS_PAGO', N'POINT_OBTENER_MEDIO_PAGO_AUTOMATICO'
            )
            OR UPPER(LTRIM(RTRIM(CLAVE))) LIKE N'MERCADOPAGO_LECTORES_PV_%'
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

    private async Task<List<MercadoPagoPointPuntoVentaDto>> GetPuntosVentaAsync(Dictionary<string, string> values, CancellationToken ct)
    {
        var puntosVenta = new List<MercadoPagoPointPuntoVentaDto>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.POS_PUNTOVENTA') IS NOT NULL
                SELECT ID, ISNULL(LTRIM(RTRIM(CODIGO)), '') AS Codigo,
                       ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS Descripcion
                FROM dbo.POS_PUNTOVENTA
                WHERE ISNULL(ACTIVO, 1) = 1
                ORDER BY NOMBRE, CODIGO;
            """, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var id = rd.IsDBNull(0) ? 0 : rd.GetInt32(0);
            var codigo = rd.IsDBNull(1) ? string.Empty : rd.GetString(1).Trim();
            if (id <= 0) continue;
            values.TryGetValue((KeyPointOfSaleReadersPrefix + id).ToUpperInvariant(), out var assigned);
            puntosVenta.Add(new MercadoPagoPointPuntoVentaDto
            {
                Id = id,
                Codigo = codigo,
                Descripcion = rd.IsDBNull(2) ? string.Empty : rd.GetString(2).Trim(),
                TerminalesAsignadas = SplitCodes(assigned ?? string.Empty)
            });
        }
        return puntosVenta;
    }

    private static List<string> SplitCodes(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> NormalizeCodes(IEnumerable<string> values)
        => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsYes(string value)
        => value.Equals("SI", StringComparison.OrdinalIgnoreCase)
            || value.Equals("S", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

    private static string ReadValue(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static string ReadCompatValue(Dictionary<string, string> values, string webKey, string legacyKey)
    {
        var legacy = ReadValue(values, legacyKey);
        return legacy.Length > 0 ? legacy : ReadValue(values, webKey);
    }
}
