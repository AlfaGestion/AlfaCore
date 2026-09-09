using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AlfaCore.Services;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot <c>--migrate-whatsapp-vault</c>: re-envuelve <c>ProtectedValue</c> de
/// <c>dbo.WhatsAppSecureVault</c> del key ring viejo al productivo, sin re-onboarding.
///
/// Reglas fijas:
///  - dry-run por defecto; sólo escribe con <c>--commit</c> explícito;
///  - scope obligatorio por <c>--id-base</c> (nunca toca otra base);
///  - no inicia Kestrel ni hosted services (se corta en Program.Main antes de CreateBuilder);
///  - transaccional, idempotente, con round-trip validado antes de cada UPDATE;
///  - no imprime plaintext, ProtectedValue, tokens ni PIN;
///  - <c>SecretReference</c>, <c>TokenReference</c>, ownership y tokens Meta quedan intactos
///    (sólo cambia la columna <c>ProtectedValue</c> y <c>ModifiedAtUtc</c>).
/// </summary>
internal static class WhatsAppVaultMigrationCommand
{
    public const string Verb = "--migrate-whatsapp-vault";
    private const string BackupTable = "dbo.WhatsAppSecureVaultReprotectBackup";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static IConfiguration BuildConfiguration()
    {
        var environmentName =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        IConfiguration configuration,
        TextWriter output,
        CancellationToken ct)
    {
        MigrationArgs parsed;
        try
        {
            parsed = MigrationArgs.Parse(args);
        }
        catch (Exception ex)
        {
            output.WriteLine($"ERROR (argumentos): {ex.Message}");
            output.WriteLine(Usage);
            return 2;
        }

        output.WriteLine("== migrate-whatsapp-vault ==");
        output.WriteLine($"modo            : {(parsed.Commit ? "COMMIT" : "DRY-RUN")}");
        output.WriteLine($"id-base         : {parsed.IdBase}");
        output.WriteLine($"old key ring    : {parsed.OldKeyRingPath}");
        output.WriteLine($"old protection  : {parsed.OldProtection}");
        output.WriteLine($"new key ring    : {parsed.NewKeyRingPath}");
        output.WriteLine($"new protection  : {parsed.NewProtection}");
        if (parsed.OldProtection == WhatsAppDataProtectionKeyProtection.Certificate)
            output.WriteLine($"old cert        : {parsed.OldCertificateThumbprint}");
        if (parsed.NewProtection == WhatsAppDataProtectionKeyProtection.Certificate)
            output.WriteLine($"new cert        : {parsed.NewCertificateThumbprint}");

        string connectionString;
        try
        {
            connectionString = ResolveCentralConnectionString(configuration, parsed.CentralConnectionName);
        }
        catch (Exception ex)
        {
            output.WriteLine($"ERROR (conexión central): {ex.Message}");
            return 3;
        }

        IDataProtectionProvider oldProvider;
        IDataProtectionProvider newProvider;
        try
        {
            oldProvider = WhatsAppEmbeddedSignupDataProtection.Create(
                parsed.OldKeyRingPath, parsed.OldProtection, parsed.OldCertificateThumbprint);
            newProvider = WhatsAppEmbeddedSignupDataProtection.Create(
                parsed.NewKeyRingPath, parsed.NewProtection, parsed.NewCertificateThumbprint);
        }
        catch (Exception ex)
        {
            output.WriteLine($"ERROR (Data Protection): {ex.Message}");
            return 4;
        }

        List<VaultRow> rows;
        int base106Selected;
        try
        {
            await using var readConnection = new SqlConnection(connectionString);
            await readConnection.OpenAsync(ct);
            rows = (await readConnection.QueryAsync<VaultRow>(new CommandDefinition(SelectActiveRowsSql,
                new { IdBase = parsed.IdBase }, cancellationToken: ct))).AsList();

            base106Selected = await readConnection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.WhatsAppSecureVault WHERE IdBase = 106 AND IdBase = @IdBase;",
                new { IdBase = parsed.IdBase }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            output.WriteLine($"ERROR (lectura SQL): {ex.Message}");
            return 5;
        }

        var credentialCount = rows.Count(r => IsCredential(r.SecretType));
        var pinCount = rows.Count(r => IsPin(r.SecretType));
        output.WriteLine($"filas activas   : CREDENTIAL={credentialCount} PHONE_PIN={pinCount} (total {rows.Count})");
        output.WriteLine($"BASE106 SELECCIONADA: {base106Selected}");

        if (rows.Count == 0)
        {
            output.WriteLine("Nada para migrar.");
            output.WriteLine("WRITES SQL: 0");
            return 0;
        }

        IReadOnlyList<VaultRowResult> results;
        try
        {
            results = Reprotect(
                rows,
                parsed.IdBase,
                secretType => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, secretType),
                secretType => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(newProvider, secretType));
        }
        catch (Exception ex)
        {
            output.WriteLine($"ERROR (re-protect / round-trip): {ex.Message}");
            output.WriteLine("WRITES SQL: 0");
            return 6;
        }

        foreach (var line in FormatRows(results))
            output.WriteLine(line);

        var pending = results.Where(r => r.Status == VaultRowMigrationStatus.Ready).ToArray();

        if (!parsed.Commit)
        {
            output.WriteLine($"DRY-RUN OK: {pending.Length} fila(s) re-envolvibles, {results.Count - pending.Length} ya migrada(s)/sin cambio.");
            output.WriteLine("WRITES SQL: 0");
            return 0;
        }

        int writes;
        try
        {
            writes = await CommitAsync(connectionString, parsed, results, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"ERROR (commit SQL, rollback aplicado): {ex.Message}");
            output.WriteLine("WRITES SQL: 0");
            return 7;
        }

        output.WriteLine($"COMMIT OK: {pending.Length} fila(s) re-envuelta(s).");
        output.WriteLine($"WRITES SQL: {writes}");
        return 0;
    }

    // --- núcleo puro (sin SQL, sin salida) -------------------------------------------------

    public static IReadOnlyList<VaultRowResult> Reprotect(
        IReadOnlyList<VaultRow> rows,
        int expectedIdBase,
        Func<string, IDataProtector> oldProtectorFor,
        Func<string, IDataProtector> newProtectorFor)
    {
        if (expectedIdBase <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedIdBase));

        var results = new List<VaultRowResult>(rows.Count);
        foreach (var row in rows)
        {
            if (row.IdBase != expectedIdBase)
                throw new InvalidOperationException(
                    $"Fila fuera de scope: SecretReference={row.SecretReference} IdBase={row.IdBase} (esperado {expectedIdBase}). Se abortó sin escribir.");

            var oldProtector = oldProtectorFor(row.SecretType);
            var newProtector = newProtectorFor(row.SecretType);

            if (TryUnprotect(newProtector, row.ProtectedValue, out _))
            {
                results.Add(new VaultRowResult(
                    row.SecretReference, row.SecretType, VaultRowMigrationStatus.AlreadyMigrated,
                    row.ProtectedValue.Length, row.ProtectedValue.Length, null));
                continue;
            }

            string plaintext = oldProtector.Unprotect(row.ProtectedValue);
            try
            {
                var rewrapped = newProtector.Protect(plaintext);
                var roundTrip = newProtector.Unprotect(rewrapped);
                if (!string.Equals(roundTrip, plaintext, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Round-trip falló para SecretReference={row.SecretReference} ({row.SecretType}). Se abortó sin escribir.");

                results.Add(new VaultRowResult(
                    row.SecretReference, row.SecretType, VaultRowMigrationStatus.Ready,
                    plaintext.Length, rewrapped.Length, rewrapped));
            }
            finally
            {
                plaintext = string.Empty;
            }
        }

        return results;
    }

    public static IEnumerable<string> FormatRows(IReadOnlyList<VaultRowResult> results)
    {
        foreach (var r in results)
            yield return $"  {r.SecretReference}  {r.SecretType,-10}  {r.Status,-16}  len {r.OldLength} -> {r.NewLength}";
    }

    // --- interno -------------------------------------------------------------------------

    private const string SelectActiveRowsSql = """
        SELECT SecretReference, SecretType, IdBase, ProtectedValue
        FROM dbo.WhatsAppSecureVault
        WHERE IdBase = @IdBase AND RevokedAtUtc IS NULL
        ORDER BY SecretType, CreatedAtUtc;
        """;

    private static async Task<int> CommitAsync(
        string connectionString,
        MigrationArgs parsed,
        IReadOnlyList<VaultRowResult> results,
        CancellationToken ct)
    {
        var pending = results.Where(r => r.Status == VaultRowMigrationStatus.Ready).ToArray();
        if (pending.Length == 0)
            return 0;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(CreateBackupTableSql, transaction: tx, cancellationToken: ct));

        var writes = 0;
        foreach (var row in pending)
        {
            // Respalda el ProtectedValue viejo leyéndolo de la propia fila dentro del tx, antes del UPDATE.
            await connection.ExecuteAsync(new CommandDefinition(InsertBackupSql, new
            {
                row.SecretReference,
                row.SecretType,
                OldKeyProtection = parsed.OldProtection.ToString(),
                NewKeyProtection = parsed.NewProtection.ToString()
            }, transaction: tx, cancellationToken: ct));

            var affected = await connection.ExecuteAsync(new CommandDefinition(UpdateProtectedValueSql, new
            {
                row.SecretReference,
                row.SecretType,
                IdBase = parsed.IdBase,
                ProtectedValue = row.ReprotectedValue
            }, transaction: tx, cancellationToken: ct));

            if (affected != 1)
                throw new InvalidOperationException(
                    $"UPDATE afectó {affected} filas para SecretReference={row.SecretReference} (esperado 1). Rollback.");

            writes += affected;
        }

        await tx.CommitAsync(ct);
        return writes;
    }

    private const string CreateBackupTableSql = $"""
        IF OBJECT_ID(N'{BackupTable}', N'U') IS NULL
        BEGIN
            CREATE TABLE {BackupTable}
            (
                SecretReference   char(32)       NOT NULL,
                SecretType        varchar(20)    NOT NULL,
                OldProtectedValue nvarchar(max)  NULL,
                OldKeyProtection  varchar(40)    NOT NULL,
                NewKeyProtection  varchar(40)    NOT NULL,
                TakenAtUtc        datetime2(3)   NOT NULL CONSTRAINT DF_WASVRB_TakenAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_WhatsAppSecureVaultReprotectBackup PRIMARY KEY (SecretReference, SecretType, TakenAtUtc)
            );
        END;
        """;

    // OldProtectedValue lo completa el propio backup leyendo la fila ANTES del UPDATE, en el mismo tx.
    private const string InsertBackupSql = $"""
        INSERT {BackupTable} (SecretReference, SecretType, OldProtectedValue, OldKeyProtection, NewKeyProtection, TakenAtUtc)
        SELECT v.SecretReference, v.SecretType, v.ProtectedValue, @OldKeyProtection, @NewKeyProtection, SYSUTCDATETIME()
        FROM dbo.WhatsAppSecureVault v
        WHERE v.SecretReference = @SecretReference AND v.SecretType = @SecretType;
        """;

    private const string UpdateProtectedValueSql = """
        UPDATE dbo.WhatsAppSecureVault
        SET ProtectedValue = @ProtectedValue, ModifiedAtUtc = SYSUTCDATETIME()
        WHERE SecretReference = @SecretReference AND SecretType = @SecretType
          AND IdBase = @IdBase AND RevokedAtUtc IS NULL;
        """;

    private static string ResolveCentralConnectionString(IConfiguration configuration, string connectionName)
    {
        try
        {
            var resolved = WhatsAppEmbeddedSignupConnection.Resolve(configuration, environment: null);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }
        catch (InvalidOperationException)
        {
            // Sin la sección ES completa (p. ej. corriendo desde el entorno de desarrollo): caemos
            // a la connection string nombrada. Nunca se imprime su contenido.
        }

        var named = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(named))
            throw new InvalidOperationException(
                $"No hay conexión central: ni WhatsAppEmbeddedSignup ni ConnectionStrings:{connectionName} están configuradas.");
        return named;
    }

    private static bool TryUnprotect(IDataProtector protector, string protectedValue, out string plaintext)
    {
        try
        {
            plaintext = protector.Unprotect(protectedValue);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = string.Empty;
            return false;
        }
        catch (FormatException)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    private static bool IsCredential(string secretType)
        => string.Equals(secretType, WhatsAppEmbeddedSignupDataProtection.CredentialSecretType, StringComparison.OrdinalIgnoreCase);

    private static bool IsPin(string secretType)
        => string.Equals(secretType, WhatsAppEmbeddedSignupDataProtection.PinSecretType, StringComparison.OrdinalIgnoreCase);

    public const string Usage = """
        Uso:
          AlfaCore --migrate-whatsapp-vault --id-base <n>
                   --old-keys <ruta> --old-protection <dpapi-current-user|dpapi-local-machine|certificate> [--old-cert-thumbprint <tp>]
                   --new-keys <ruta> --new-protection <dpapi-current-user|dpapi-local-machine|certificate> [--new-cert-thumbprint <tp>]
                   [--central-connection-name AlfaCentral] [--commit]
        Sin --commit: dry-run (no escribe nada).
        """;

    internal sealed record MigrationArgs(
        int IdBase,
        string OldKeyRingPath,
        WhatsAppDataProtectionKeyProtection OldProtection,
        string? OldCertificateThumbprint,
        string NewKeyRingPath,
        WhatsAppDataProtectionKeyProtection NewProtection,
        string? NewCertificateThumbprint,
        bool Commit,
        string CentralConnectionName)
    {
        public static MigrationArgs Parse(IReadOnlyList<string> args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < args.Count; i++)
            {
                var token = args[i];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                    continue;

                var eq = token.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0)
                {
                    map[token[2..eq]] = token[(eq + 1)..];
                    continue;
                }

                var key = token[2..];
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    map[key] = args[++i];
                }
                else
                {
                    flags.Add(key);
                }
            }

            var idBaseRaw = Required(map, "id-base");
            if (!int.TryParse(idBaseRaw, out var idBase) || idBase <= 0)
                throw new ArgumentException($"--id-base debe ser un entero positivo (recibido '{idBaseRaw}').");

            var oldKeys = Required(map, "old-keys");
            if (!Path.IsPathRooted(oldKeys))
                throw new ArgumentException("--old-keys debe ser una ruta absoluta.");
            if (!Directory.Exists(oldKeys))
                throw new ArgumentException($"--old-keys no existe: {oldKeys}");

            var newKeys = Required(map, "new-keys");
            if (!Path.IsPathRooted(newKeys))
                throw new ArgumentException("--new-keys debe ser una ruta absoluta.");
            if (string.Equals(Path.GetFullPath(oldKeys), Path.GetFullPath(newKeys), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("--old-keys y --new-keys no pueden ser el mismo directorio.");

            var oldProtection = ParseProtection(Required(map, "old-protection"), "--old-protection");
            var newProtection = ParseProtection(Required(map, "new-protection"), "--new-protection");

            var oldThumb = map.GetValueOrDefault("old-cert-thumbprint");
            var newThumb = map.GetValueOrDefault("new-cert-thumbprint");

            if (oldProtection == WhatsAppDataProtectionKeyProtection.Certificate && string.IsNullOrWhiteSpace(oldThumb))
                throw new ArgumentException("--old-protection certificate requiere --old-cert-thumbprint.");
            if (newProtection == WhatsAppDataProtectionKeyProtection.Certificate && string.IsNullOrWhiteSpace(newThumb))
                throw new ArgumentException("--new-protection certificate requiere --new-cert-thumbprint.");

            return new MigrationArgs(
                idBase,
                oldKeys,
                oldProtection,
                oldThumb,
                newKeys,
                newProtection,
                newThumb,
                flags.Contains("commit"),
                map.GetValueOrDefault("central-connection-name", "AlfaCentral"));
        }

        private static string Required(IReadOnlyDictionary<string, string> map, string key)
            => map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Falta el argumento obligatorio --{key}.");

        private static WhatsAppDataProtectionKeyProtection ParseProtection(string raw, string argName)
            => raw.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
            {
                "dpapicurrentuser" => WhatsAppDataProtectionKeyProtection.DpapiCurrentUser,
                "dpapilocalmachine" => WhatsAppDataProtectionKeyProtection.DpapiLocalMachine,
                "certificate" => WhatsAppDataProtectionKeyProtection.Certificate,
                _ => throw new ArgumentException(
                    $"{argName} inválido: '{raw}' (dpapi-current-user | dpapi-local-machine | certificate).")
            };
    }
}

internal sealed record VaultRow(string SecretReference, string SecretType, int IdBase, string ProtectedValue);

internal enum VaultRowMigrationStatus
{
    Ready,
    Committed,
    AlreadyMigrated
}

internal sealed record VaultRowResult(
    string SecretReference,
    string SecretType,
    VaultRowMigrationStatus Status,
    int OldLength,
    int NewLength,
    string? ReprotectedValue);
