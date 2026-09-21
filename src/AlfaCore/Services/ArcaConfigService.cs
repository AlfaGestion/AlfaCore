using System.Text;
using System.Security.Cryptography.X509Certificates;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ArcaConfigService(
    IAppEventService appEvents,
    ISessionService sessionService,
    IConfiguration configuration) : IArcaConfigService
{
    private const string ModuleName = "ArcaFacturacionElectronica";
    private const string ClaveGlobal = "GLOBAL";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ArcaEmisorConfig?> ResolveEmisorAsync(SqlConnection cn, string? uNegocio, CancellationToken ct)
    {
        try
        {
            var global = await ReadConfigAsync(cn, ct);

            if (!string.Equals(ReadValue(global, "USAFCELECTRONICA", "NO"), "SI", StringComparison.OrdinalIgnoreCase))
                return null;

            var ambiente = string.Equals(ReadValue(global, "ARCA_AMBIENTE", "HOMOLOGACION"), "PRODUCCION", StringComparison.OrdinalIgnoreCase)
                ? ArcaAmbiente.Produccion
                : ArcaAmbiente.Homologacion;

            var condicionIva = ReadValue(global, "CONDIVA", ReadValue(global, "CONDIVAEMPRESA", string.Empty));

            var unidad = string.IsNullOrWhiteSpace(uNegocio) ? null : await ReadUnidadEmisorAsync(cn, uNegocio!.Trim(), ct);

            string cuit, razonSocial, claveCertificado;
            int puntoVenta;

            if (unidad is { UsaEfc: true })
            {
                if (unidad.Cuit.Length == 0 || unidad.Pdv.Length == 0)
                    throw new InvalidOperationException($"La unidad de negocio '{uNegocio}' tiene factura electrónica activada pero le falta CUIT o punto de venta.");

                cuit = unidad.Cuit;
                razonSocial = unidad.RazonSocial;
                claveCertificado = uNegocio!.Trim();
                if (!int.TryParse(unidad.Pdv, out puntoVenta) || puntoVenta <= 0)
                    throw new InvalidOperationException($"El punto de venta electrónico configurado para la unidad '{uNegocio}' no es válido: '{unidad.Pdv}'.");
            }
            else
            {
                cuit = ReadValue(global, "WSFE_CUIT", string.Empty);
                razonSocial = ReadValue(global, "NOMBRE", string.Empty);
                claveCertificado = ClaveGlobal;
                var pvTexto = ReadValue(global, "PV_EFACTURA", string.Empty);

                if (cuit.Length == 0 || pvTexto.Length == 0)
                    return null;

                if (!int.TryParse(pvTexto, out puntoVenta) || puntoVenta <= 0)
                    throw new InvalidOperationException($"El punto de venta electrónico global (PV_EFACTURA) no es válido: '{pvTexto}'.");
            }

            var cuitDigits = new string(cuit.Where(char.IsDigit).ToArray());
            if (cuitDigits.Length != 11)
                throw new InvalidOperationException($"El CUIT del emisor debe tener 11 dígitos: '{cuit}'.");

            var certificado = await ReadCertificadoAsync(cn, claveCertificado, ct);
            if (certificado is null || certificado.Value.Crt is null || certificado.Value.Key is null)
                throw new InvalidOperationException(
                    unidad is { UsaEfc: true }
                        ? $"La unidad de negocio '{uNegocio}' tiene factura electrónica activada pero no tiene certificado subido (Configuración General > Ventas > Facturación electrónica)."
                        : "No hay certificado de facturación electrónica configurado (Configuración General > Ventas > Facturación electrónica).");

            var certificadoPem = Encoding.UTF8.GetString(certificado.Value.Crt);
            var clavePem = Encoding.UTF8.GetString(certificado.Value.Key);

            return new ArcaEmisorConfig(cuitDigits, razonSocial, ambiente, puntoVenta, certificadoPem, clavePem, condicionIva);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "ResolveEmisor", ex, "No se pudo resolver la configuración de facturación electrónica.", new { UNegocio = uNegocio }, ct: ct);
            throw new InvalidOperationException("No se pudo resolver la configuración de facturación electrónica.", ex);
        }
    }

    public async Task<ArcaModoFalloCae> ResolveModoFalloCaeAsync(SqlConnection cn, CancellationToken ct)
    {
        var values = await ReadConfigAsync(cn, ct);
        return string.Equals(ReadValue(values, "ARCA_MODO_FALLO_CAE", "ESTRICTO"), "DEGRADADO", StringComparison.OrdinalIgnoreCase)
            ? ArcaModoFalloCae.Degradado
            : ArcaModoFalloCae.Estricto;
    }

    public async Task<ArcaEmisorConfig?> ResolvePadronEmisorAsync(SqlConnection cn, CancellationToken ct)
    {
        var global = await ReadConfigAsync(cn, ct);
        var cuit = new string(ReadValue(global, "WSFE_CUIT", ReadValue(global, "CUIT", string.Empty))
            .Where(char.IsDigit).ToArray());
        if (cuit.Length != 11)
            throw new InvalidOperationException("No se configuró un CUIT válido para la consulta de padrón ARCA.");

        var certificadoConfigurado = await ReadCertificadoAsync(cn, "PADRON", ct);
        var certificado = certificadoConfigurado is not null
            ? (certificadoConfigurado.Value.Crt, certificadoConfigurado.Value.Key, Ambiente: string.Equals(ReadValue(global, "ARCA_PADRON_AMBIENTE", ReadValue(global, "ARCA_AMBIENTE", "HOMOLOGACION")), "PRODUCCION", StringComparison.OrdinalIgnoreCase) ? ArcaAmbiente.Produccion : ArcaAmbiente.Homologacion)
            : ReadCertificadoPadronPorDefecto();
        if (certificado is null || certificado.Value.Crt is null || certificado.Value.Key is null)
            throw new InvalidOperationException("No hay un certificado propio configurado para consultar el padrón ARCA.");

        return new ArcaEmisorConfig(
            cuit,
            ReadValue(global, "NOMBRE", string.Empty),
            certificado.Value.Ambiente,
            0,
            Encoding.UTF8.GetString(certificado.Value.Crt),
            Encoding.UTF8.GetString(certificado.Value.Key),
            ReadValue(global, "CONDIVA", ReadValue(global, "CONDIVAEMPRESA", string.Empty)));
    }

    private (byte[]? Crt, byte[]? Key, ArcaAmbiente Ambiente)? ReadCertificadoPadronPorDefecto()
    {
        var carpetas = new[]
        {
            configuration["ArcaPadron:DirectorioCertificados"],
            Path.Combine(Directory.GetCurrentDirectory(), "certificado"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "certificado")),
            @"C:\dev\AlfaCore\certificado"
        }.Where(x => !string.IsNullOrWhiteSpace(x))
         .Select(x => x!)
         .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var carpeta in carpetas)
        {
            if (!Directory.Exists(carpeta))
                continue;

            foreach (var crt in Directory.GetFiles(carpeta, "CONSULTA PADRON*.crt").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var key = Path.Combine(carpeta, "Cprivada_CONSULTAPADRON.key");
                if (File.Exists(key) && CertificadoVigente(crt))
                    return (File.ReadAllBytes(crt), File.ReadAllBytes(key), ArcaAmbiente.Homologacion);
            }

            // En instalaciones antiguas de VB6 el padrón se consultaba con el
            // certificado productivo general, separado del certificado de WSFE.
            var certificadoProductivo = Path.Combine(carpeta, "certificado.crt");
            var clavesProductivas = new[] { "CPrivada.key", "privada.key", "clave.key" };
            if (File.Exists(certificadoProductivo) && CertificadoVigente(certificadoProductivo))
            {
                foreach (var nombreKey in clavesProductivas)
                {
                    var key = Path.Combine(carpeta, nombreKey);
                    if (File.Exists(key))
                        return (File.ReadAllBytes(certificadoProductivo), File.ReadAllBytes(key), ArcaAmbiente.Produccion);
                }
            }
        }

        return null;
    }

    private static bool CertificadoVigente(string archivo)
    {
        try
        {
            using var certificado = new X509Certificate2(archivo);
            var ahora = DateTime.UtcNow;
            return certificado.NotBefore.ToUniversalTime() <= ahora
                && certificado.NotAfter.ToUniversalTime() > ahora;
        }
        catch
        {
            return false;
        }
    }

    // ---- Pantalla de configuración (Configuración General > Ventas > Facturación electrónica) ----

    public Task<ArcaConfiguracionGeneralDto> GetConfiguracionGeneralAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetConfiguracionGeneral", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var global = await ReadConfigAsync(cn, token);
            var certGlobal = await ReadCertificadoNombresAsync(cn, ClaveGlobal, token);
            var certPadron = await ReadCertificadoNombresAsync(cn, "PADRON", token);

            var dto = new ArcaConfiguracionGeneralDto
            {
                UsaFacturacionElectronica = string.Equals(ReadValue(global, "USAFCELECTRONICA", "NO"), "SI", StringComparison.OrdinalIgnoreCase),
                Ambiente = ReadValue(global, "ARCA_AMBIENTE", "HOMOLOGACION").ToUpperInvariant(),
                ModoFalloCae = ReadValue(global, "ARCA_MODO_FALLO_CAE", "ESTRICTO").ToUpperInvariant(),
                Cuit = ReadValue(global, "WSFE_CUIT", string.Empty),
                PuntoVenta = ReadValue(global, "PV_EFACTURA", string.Empty),
                TieneCertificado = certGlobal.TieneCrt && certGlobal.TieneKey,
                NombreArchivoCrt = certGlobal.NombreCrt,
                NombreArchivoKey = certGlobal.NombreKey,
                TieneCertificadoPadron = certPadron.TieneCrt && certPadron.TieneKey,
                NombreArchivoCrtPadron = certPadron.NombreCrt,
                NombreArchivoKeyPadron = certPadron.NombreKey
            };

            if (await ExistsAsync(cn, "dbo.V_TA_UnidadNegocio", token))
            {
                var unidades = new List<(string Codigo, string RazonSocial, bool UsaEfc, string Cuit, string Pdv)>();

                // El reader se cierra ANTES de seguir usando "cn" para leer el certificado de cada
                // unidad más abajo -- de lo contrario ADO.NET tira "ya hay un DataReader abierto"
                // (InvalidOperationException, que además queda swallowed por ExecuteLoggedAsync).
                await using (var cmd = new SqlCommand(
                    """
                    SELECT LTRIM(RTRIM(Codigo)) AS Codigo, ISNULL(LTRIM(RTRIM(RAZON_SOCIAL)), '') AS RazonSocial,
                           ISNULL(USAEFC, 0) AS UsaEfc, ISNULL(LTRIM(RTRIM(CUIT)), '') AS Cuit,
                           ISNULL(LTRIM(RTRIM(PDV)), '') AS Pdv
                    FROM dbo.V_TA_UnidadNegocio
                    WHERE ISNULL(USAEFC, 0) = 1 OR ISNULL(LTRIM(RTRIM(CUIT)), '') <> ''
                    ORDER BY Codigo;
                    """, cn))
                await using (var rd = await cmd.ExecuteReaderAsync(token))
                {
                    while (await rd.ReadAsync(token))
                        unidades.Add((GetString(rd, 0), GetString(rd, 1), Convert.ToBoolean(rd.GetValue(2)), GetString(rd, 3), GetString(rd, 4)));
                }

                foreach (var u in unidades)
                {
                    var certUnidad = await ReadCertificadoNombresAsync(cn, u.Codigo, token);
                    dto.Unidades.Add(new ArcaUnidadNegocioConfigDto
                    {
                        Codigo = u.Codigo,
                        RazonSocialUnidad = u.RazonSocial,
                        UsaFacturacionElectronica = u.UsaEfc,
                        Cuit = u.Cuit,
                        PuntoVenta = u.Pdv,
                        TieneCertificado = certUnidad.TieneCrt && certUnidad.TieneKey,
                        NombreArchivoCrt = certUnidad.NombreCrt,
                        NombreArchivoKey = certUnidad.NombreKey
                    });
                }
            }

            return dto;
        }, "No se pudo cargar la configuración de facturación electrónica.", ct);

    public Task GuardarConfiguracionGeneralAsync(ArcaConfiguracionGeneralDto dto, CancellationToken ct = default)
        => ExecuteLoggedAsync("GuardarConfiguracionGeneral", async token =>
        {
            ArgumentNullException.ThrowIfNull(dto);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);

            await SetConfigAsync(cn, detailColumn, "USAFCELECTRONICA", dto.UsaFacturacionElectronica ? "SI" : "NO", token);
            await SetConfigAsync(cn, detailColumn, "ARCA_AMBIENTE",
                string.Equals(dto.Ambiente, "PRODUCCION", StringComparison.OrdinalIgnoreCase) ? "PRODUCCION" : "HOMOLOGACION", token);
            await SetConfigAsync(cn, detailColumn, "ARCA_MODO_FALLO_CAE",
                string.Equals(dto.ModoFalloCae, "DEGRADADO", StringComparison.OrdinalIgnoreCase) ? "DEGRADADO" : "ESTRICTO", token);
            await SetConfigAsync(cn, detailColumn, "WSFE_CUIT", (dto.Cuit ?? string.Empty).Trim(), token);
            await SetConfigAsync(cn, detailColumn, "PV_EFACTURA", (dto.PuntoVenta ?? string.Empty).Trim(), token);

            await appEvents.LogAuditAsync(ModuleName, "GuardarConfiguracionGeneral", "TA_CONFIGURACION", "ARCA",
                "Se actualizó la configuración general de facturación electrónica.",
                new { dto.UsaFacturacionElectronica, dto.Ambiente, dto.ModoFalloCae, dto.Cuit, dto.PuntoVenta }, token);
        }, "No se pudo guardar la configuración de facturación electrónica.", ct);

    public Task GuardarUnidadAsync(ArcaUnidadNegocioConfigDto dto, CancellationToken ct = default)
        => ExecuteLoggedAsync("GuardarUnidad", async token =>
        {
            ArgumentNullException.ThrowIfNull(dto);
            var codigo = (dto.Codigo ?? string.Empty).Trim();
            if (codigo.Length == 0)
                throw new InvalidOperationException("Falta el código de la unidad de negocio.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await using var cmd = new SqlCommand(
                """
                UPDATE dbo.V_TA_UnidadNegocio
                SET USAEFC = @UsaEfc, CUIT = @Cuit, PDV = @Pdv
                WHERE LTRIM(RTRIM(Codigo)) = @Codigo;
                """, cn);
            cmd.Parameters.AddWithValue("@UsaEfc", dto.UsaFacturacionElectronica);
            cmd.Parameters.AddWithValue("@Cuit", DbNullable((dto.Cuit ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@Pdv", DbNullable((dto.PuntoVenta ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            var filas = await cmd.ExecuteNonQueryAsync(token);
            if (filas == 0)
                throw new InvalidOperationException($"No se encontró la unidad de negocio '{codigo}'.");

            await appEvents.LogAuditAsync(ModuleName, "GuardarUnidad", "V_TA_UnidadNegocio", codigo,
                "Se actualizó la configuración de facturación electrónica de la unidad de negocio.",
                new { dto.UsaFacturacionElectronica, dto.Cuit, dto.PuntoVenta }, token);
        }, "No se pudo guardar la unidad de negocio.", ct);

    /// <summary>uNegocioOGlobal: código de unidad (LTRIM/RTRIM) o "GLOBAL" para el certificado de
    /// fallback general. Cada archivo (crt/key) se actualiza solo si viene con contenido -- permite
    /// subir uno sin tocar el otro.</summary>
    public Task SubirCertificadoAsync(string uNegocioOGlobal, byte[]? crt, string? nombreCrt, byte[]? clave, string? nombreClave, CancellationToken ct = default)
        => ExecuteLoggedAsync("SubirCertificado", async token =>
        {
            var clavePk = NormalizarClaveCertificado(uNegocioOGlobal);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var existe = await ExisteCertificadoAsync(cn, clavePk, token);
            if (existe)
            {
                await using var cmd = new SqlCommand(
                    """
                    UPDATE dbo.ARCA_CERTIFICADO
                    SET ArchivoCrt = COALESCE(@Crt, ArchivoCrt), NombreArchivoCrt = COALESCE(@NombreCrt, NombreArchivoCrt),
                        ArchivoKey = COALESCE(@Key, ArchivoKey), NombreArchivoKey = COALESCE(@NombreKey, NombreArchivoKey),
                        FechaHoraModificacion = GETDATE()
                    WHERE UNegocio = @UNegocio;
                    """, cn);
                AddVarBinary(cmd, "@Crt", crt);
                cmd.Parameters.AddWithValue("@NombreCrt", DbNullable(nombreCrt));
                AddVarBinary(cmd, "@Key", clave);
                cmd.Parameters.AddWithValue("@NombreKey", DbNullable(nombreClave));
                cmd.Parameters.AddWithValue("@UNegocio", clavePk);
                await cmd.ExecuteNonQueryAsync(token);
            }
            else
            {
                await using var cmd = new SqlCommand(
                    """
                    INSERT INTO dbo.ARCA_CERTIFICADO (UNegocio, ArchivoCrt, NombreArchivoCrt, ArchivoKey, NombreArchivoKey, FechaHoraModificacion)
                    VALUES (@UNegocio, @Crt, @NombreCrt, @Key, @NombreKey, GETDATE());
                    """, cn);
                cmd.Parameters.AddWithValue("@UNegocio", clavePk);
                AddVarBinary(cmd, "@Crt", crt);
                cmd.Parameters.AddWithValue("@NombreCrt", DbNullable(nombreCrt));
                AddVarBinary(cmd, "@Key", clave);
                cmd.Parameters.AddWithValue("@NombreKey", DbNullable(nombreClave));
                await cmd.ExecuteNonQueryAsync(token);
            }

            await appEvents.LogAuditAsync(ModuleName, "SubirCertificado", "ARCA_CERTIFICADO", clavePk,
                "Se subió un certificado/clave de facturación electrónica.",
                new { UNegocio = clavePk, ArchivoCrt = nombreCrt, ArchivoKey = nombreClave }, token);
        }, "No se pudo subir el certificado.", ct);

    public Task EliminarCertificadoAsync(string uNegocioOGlobal, CancellationToken ct = default)
        => ExecuteLoggedAsync("EliminarCertificado", async token =>
        {
            var clavePk = NormalizarClaveCertificado(uNegocioOGlobal);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand("DELETE FROM dbo.ARCA_CERTIFICADO WHERE UNegocio = @UNegocio;", cn);
            cmd.Parameters.AddWithValue("@UNegocio", clavePk);
            await cmd.ExecuteNonQueryAsync(token);

            await appEvents.LogAuditAsync(ModuleName, "EliminarCertificado", "ARCA_CERTIFICADO", clavePk,
                "Se quitó un certificado de facturación electrónica.", null, token);
        }, "No se pudo quitar el certificado.", ct);

    private static string NormalizarClaveCertificado(string uNegocioOGlobal)
    {
        var valor = (uNegocioOGlobal ?? string.Empty).Trim();
        return valor.Length == 0 ? throw new InvalidOperationException("Falta indicar la unidad de negocio (o GLOBAL) del certificado.") : valor;
    }

    private static async Task<bool> ExisteCertificadoAsync(SqlConnection cn, string uNegocio, CancellationToken ct)
    {
        if (!await ExistsAsync(cn, "dbo.ARCA_CERTIFICADO", ct))
            return false;
        await using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.ARCA_CERTIFICADO WHERE UNegocio = @UNegocio;", cn);
        cmd.Parameters.AddWithValue("@UNegocio", uNegocio);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<(byte[]? Crt, byte[]? Key)?> ReadCertificadoAsync(SqlConnection cn, string uNegocio, CancellationToken ct)
    {
        if (!await ExistsAsync(cn, "dbo.ARCA_CERTIFICADO", ct))
            return null;

        // Lectura vía SqlDataReader (no Dapper): igual criterio que TA_LOGOS.IMAGEN para blobs grandes.
        await using var cmd = new SqlCommand("SELECT ArchivoCrt, ArchivoKey FROM dbo.ARCA_CERTIFICADO WHERE UNegocio = @UNegocio;", cn);
        cmd.Parameters.AddWithValue("@UNegocio", uNegocio);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        var crt = await rd.IsDBNullAsync(0, ct) ? null : rd.GetFieldValue<byte[]>(0);
        var key = await rd.IsDBNullAsync(1, ct) ? null : rd.GetFieldValue<byte[]>(1);
        return (crt, key);
    }

    private static async Task<(bool TieneCrt, string? NombreCrt, bool TieneKey, string? NombreKey)> ReadCertificadoNombresAsync(SqlConnection cn, string uNegocio, CancellationToken ct)
    {
        if (!await ExistsAsync(cn, "dbo.ARCA_CERTIFICADO", ct))
            return (false, null, false, null);

        await using var cmd = new SqlCommand(
            """
            SELECT CASE WHEN ArchivoCrt IS NULL THEN 0 ELSE 1 END, NombreArchivoCrt,
                   CASE WHEN ArchivoKey IS NULL THEN 0 ELSE 1 END, NombreArchivoKey
            FROM dbo.ARCA_CERTIFICADO WHERE UNegocio = @UNegocio;
            """, cn);
        cmd.Parameters.AddWithValue("@UNegocio", uNegocio);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return (false, null, false, null);

        return (
            rd.GetInt32(0) == 1, rd.IsDBNull(1) ? null : rd.GetString(1),
            rd.GetInt32(2) == 1, rd.IsDBNull(3) ? null : rd.GetString(3));
    }

    private async Task SetConfigAsync(SqlConnection cn, string detailColumn, string clave, string valor, CancellationToken ct)
    {
        var sql = $"""
            UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, {detailColumn} = NULL, FechaHora_Modificacion = GETDATE()
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;

            IF @@ROWCOUNT = 0
                INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion, FechaHora_Modificacion)
                VALUES (N'ARCA', @ClaveOriginal, @Valor, GETDATE(), GETDATE());
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Valor", DbNullable(valor));
        cmd.Parameters.AddWithValue("@Clave", clave.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@ClaveOriginal", clave);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static object DbNullable(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    /// <summary>AddWithValue con DBNull.Value no alcanza a inferir varbinary(max) -- SQL Server lo
    /// interpreta como nvarchar y el COALESCE contra la columna binaria falla ("no se permite la
    /// conversión implícita de nvarchar a varbinary(max)"). Hay que declarar el tipo explícito.</summary>
    private static void AddVarBinary(SqlCommand cmd, string name, byte[]? value)
    {
        var param = new SqlParameter(name, System.Data.SqlDbType.VarBinary, -1)
        {
            Value = (object?)value ?? DBNull.Value
        };
        cmd.Parameters.Add(param);
    }

    private static async Task<Dictionary<string, string>> ReadConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var detailColumn = await ResolveDetailColumnAsync(cn, ct);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sql = $"""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), ISNULL(VALOR, ''), ISNULL(CAST({detailColumn} AS nvarchar(max)), '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (
                N'USAFCELECTRONICA', N'ARCA_AMBIENTE', N'ARCA_MODO_FALLO_CAE',
                N'WSFE_CUIT', N'PV_EFACTURA', N'CONDIVA', N'CONDIVAEMPRESA', N'NOMBRE'
            )
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var key = GetString(rd, 0);
            var value = ResolveStoredValue(GetString(rd, 1), GetString(rd, 2));
            values[key] = value;
        }

        return values;
    }

    private static async Task<UnidadEmisorRow?> ReadUnidadEmisorAsync(SqlConnection cn, string uNegocio, CancellationToken ct)
    {
        const string sql = """
            SELECT ISNULL(USAEFC, 0) AS UsaEfc, LTRIM(RTRIM(ISNULL(RAZON_SOCIAL, ''))) AS RazonSocial,
                   LTRIM(RTRIM(ISNULL(CUIT, ''))) AS Cuit, LTRIM(RTRIM(ISNULL(PDV, ''))) AS Pdv
            FROM dbo.V_TA_UnidadNegocio
            WHERE LTRIM(RTRIM(Codigo)) = @UNegocio;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@UNegocio", uNegocio);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new UnidadEmisorRow(
            Convert.ToBoolean(rd.GetValue(0)),
            GetString(rd, 1),
            GetString(rd, 2),
            GetString(rd, 3));
    }

    private static async Task<string> ResolveDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux')
            ORDER BY name
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static async Task<bool> ExistsAsync(SqlConnection cn, string objeto, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT OBJECT_ID(N'{objeto}');", cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null and not DBNull;
    }

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static string ReadValue(Dictionary<string, string> values, string key, string fallback = "")
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private Task ExecuteLoggedAsync(string action, Func<CancellationToken, Task> operation, string userMessage, CancellationToken ct)
        => ExecuteLoggedAsync(action, async token => { await operation(token); return true; }, userMessage, ct);

    private sealed record UnidadEmisorRow(bool UsaEfc, string RazonSocial, string Cuit, string Pdv);
}
