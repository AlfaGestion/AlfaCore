using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using AlfaCore.Services;
using AlfaCore.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

const string server = @"(localdb)\MSSQLLocalDB";
const string centralDatabase = "ALFA_CENTRAL_DEV";
const string tenantDatabase = "ALFACORE_ES_TENANT_DEV";
const int allowedBaseId = 84;
const int crossTenantBaseId = 1900000106;
const string esLocalLogin = "eslocal@alfacore.dev";
const string esLocalPassword = "AlfaCore-ES-84!";

try
{
    if (args.Length == 0)
        Fail("Uso: bootstrap | inspect-state | audit-existing-meta <WabaId> <PhoneNumberId> | prepare-simulation <PhoneNumberId>.");

    var root = FindRepositoryRoot();
    var command = args[0].Trim().ToLowerInvariant();
    switch (command)
    {
        case "bootstrap":
            EnsureEsLocalBootstrapGuards();
            await EnsureDatabaseAsync(centralDatabase);
            await EnsureDatabaseAsync(tenantDatabase);
            await ExecuteScriptAsync(Path.Combine(root, "docs", "base-datos", "sql-test", "bootstrap_alfa_central_dev_embedded_signup.sql"));
            await ExecuteScriptAsync(Path.Combine(root, "docs", "base-datos", "sql-test", "bootstrap_alfacore_es_tenant_dev_auth.sql"));
            await ValidateBootstrapAsync();
            Console.WriteLine($"BOOTSTRAP_OK Catalog=ALFA_CENTRAL_DEV Base=84 Tenant=ALFACORE_ES_TENANT_DEV Login={esLocalLogin}");
            break;

        case "prepare-simulation":
            if (args.Length != 2 || !IsDigits(args[1])) Fail("PhoneNumberId debe contener únicamente dígitos ficticios.");
            var result = await PrepareSimulationAsync(args[1]);
            Console.WriteLine($"BASE84_TOKEN={result.Base84Token}");
            Console.WriteLine($"CROSS_TENANT_TOKEN={result.CrossTenantToken}");
            break;

        case "inspect-state":
            EnsureEsLocalBootstrapGuards();
            await InspectStateAsync();
            break;

        case "audit-existing-meta":
            if (args.Length != 3 || !IsDigits(args[1]) || !IsDigits(args[2]))
                Fail("WabaId y PhoneNumberId deben contener únicamente dígitos.");
            EnsureEsLocalBootstrapGuards();
            await AuditExistingMetaAsync(args[1], args[2]);
            break;

        default:
            Fail("Comando ES local desconocido.");
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    Environment.ExitCode = 1;
}

string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln"))) directory = directory.Parent;
    return directory?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz de AlfaCore.");
}

string Connection(string database)
    => $"Server={server};Initial Catalog={database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10";

async Task EnsureDatabaseAsync(string database)
{
    if (!Regex.IsMatch(database, "^(?:ALFA_CENTRAL_DEV|ALFACORE_ES_TENANT_DEV)$", RegexOptions.CultureInvariant))
        throw new InvalidOperationException("Catálogo local no autorizado.");
    await using var cn = new SqlConnection(Connection("master"));
    await cn.OpenAsync();
    if (Convert.ToInt32(await cn.ExecuteScalarAsync("SELECT CAST(SERVERPROPERTY('IsLocalDB') AS int);")) != 1)
        throw new InvalidOperationException("El servidor SQL no es LocalDB.");
    await cn.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}];");
}

async Task ExecuteScriptAsync(string scriptPath)
{
    var script = ExpandIncludes(scriptPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    var database = scriptPath.EndsWith("bootstrap_alfacore_es_tenant_dev_auth.sql", StringComparison.OrdinalIgnoreCase)
        ? tenantDatabase
        : centralDatabase;
    script = Regex.Replace(script, @"(?im)^\s*:setvar\s+\w+\s+""[^""]*""\s*$", string.Empty)
        .Replace("$(ExpectedDatabase)", database, StringComparison.Ordinal)
        .Replace("$(EsLocalLogin)", esLocalLogin, StringComparison.Ordinal)
        .Replace("$(EsLocalPassword)", esLocalPassword, StringComparison.Ordinal)
        .Replace("$(EsLocalTenantPasswordEncoded)", EncodeLegacyPassword(esLocalPassword), StringComparison.Ordinal);
    await using var cn = new SqlConnection(Connection(database));
    await cn.OpenAsync();
    foreach (var batch in Regex.Split(script, @"(?im)^\s*GO\s*(?:--.*)?$").Where(static x => !string.IsNullOrWhiteSpace(x)))
        await cn.ExecuteAsync(batch, commandTimeout: 120);
}

string ExpandIncludes(string path, HashSet<string> visited)
{
    var fullPath = Path.GetFullPath(path);
    if (!visited.Add(fullPath)) throw new InvalidOperationException("Include SQL circular.");
    var directory = Path.GetDirectoryName(fullPath)!;
    var lines = File.ReadAllLines(fullPath);
    return string.Join(Environment.NewLine, lines.Select(line =>
    {
        var match = Regex.Match(line, @"^\s*:r\s+(.+?)\s*$", RegexOptions.IgnoreCase);
        return match.Success ? ExpandIncludes(Path.Combine(directory, match.Groups[1].Value.Trim()), visited) : line;
    }));
}

async Task ValidateBootstrapAsync()
{
    await using var cn = new SqlConnection(Connection(centralDatabase));
    await cn.OpenAsync();
    var result = await cn.QuerySingleAsync<(string Catalog, int IsLocal, int BaseCount, int TableCount, int LoginCount)>("""
        SELECT DB_NAME(), CAST(SERVERPROPERTY('IsLocalDB') AS int),
          (SELECT COUNT(*) FROM dbo.bases WHERE id=84 AND nombre=N'ES_DEV_BASE_84'),
          (SELECT COUNT(*) FROM sys.tables WHERE name IN ('WhatsAppEmbeddedOnboarding','WhatsAppWabaOwnership','WhatsAppPhoneOwnership','WhatsAppSecureVault')),
          (SELECT COUNT(*) FROM dbo.users u INNER JOIN dbo.Clientes c ON c.idcliente=u.idcliente
             WHERE LOWER(LTRIM(RTRIM(u.[user])))=LOWER(@Login) AND c.idcliente=N'ES_LOCAL' AND c.idweb=N'ALFANET');
        """, new { Login = esLocalLogin });
    if (result.Catalog != centralDatabase || result.IsLocal != 1 || result.BaseCount != 1 || result.TableCount != 4 || result.LoginCount != 1)
        throw new InvalidOperationException("El bootstrap ES local no superó la validación final.");

    var storedCentralPassword = await cn.ExecuteScalarAsync<string?>(
        "SELECT password FROM dbo.users WHERE LOWER(LTRIM(RTRIM([user])))=LOWER(@Login);",
        new { Login = esLocalLogin });
    if (storedCentralPassword is null || !new PlainTextPasswordVerifier().Verify(esLocalPassword, storedCentralPassword))
        throw new InvalidOperationException("La credencial central ES Local no usa el verificador real de AlfaCore.");

    await using var tenant = new SqlConnection(Connection(tenantDatabase));
    await tenant.OpenAsync();
    var tenantUserCount = await tenant.ExecuteScalarAsync<int>("""
        SELECT COUNT(*) FROM dbo.TA_USUARIOS
        WHERE NOMBRE=N'Administrador ES Local' AND SISTEMA=N'CN000PR'
          AND email_de=@Login AND Activo=1 AND Administrador=1;
        """, new { Login = esLocalLogin });
    if (tenantUserCount != 1)
        throw new InvalidOperationException("El usuario interno ES Local no superó la validación final.");
    var storedTenantPassword = await tenant.ExecuteScalarAsync<string?>("""
        SELECT PASSWORD FROM dbo.TA_USUARIOS
        WHERE NOMBRE=N'Administrador ES Local' AND SISTEMA=N'CN000PR';
        """);
    if (storedTenantPassword is null
        || !string.Equals(new UsuariosPasswordCodec().Decode(storedTenantPassword), esLocalPassword, StringComparison.Ordinal))
        throw new InvalidOperationException("La credencial interna ES Local no usa el codec real de AlfaCore.");

    var requiredTenantTables = await tenant.ExecuteScalarAsync<int>("""
        SELECT COUNT(*) FROM sys.tables
        WHERE name IN ('TA_USUARIOS','TA_CONFIGURACION','CONV_WHATSAPP_NUMEROS',
                       'CONV_WHATSAPP_NUMERO_USUARIOS','CONV_ADMINISTRADORES');
        """);
    if (requiredTenantTables != 5)
        throw new InvalidOperationException("El tenant ES Local no tiene el esquema mínimo de Login y Configuración.");
}

void EnsureEsLocalBootstrapGuards()
{
    if (!string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Environment.GetEnvironmentVariable("AlfaCoreEsLocal__Enabled"), "true", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Environment.GetEnvironmentVariable("WhatsAppEmbeddedSignup__AllowedBaseIds__0"), "84", StringComparison.Ordinal)
        || !string.Equals(Environment.GetEnvironmentVariable("WhatsAppEmbeddedSignup__WorkerEnabled"), "false", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Las guardas de Development ES Local no están activas.");

    EnsureExactLocalConnection("ConnectionStrings__AlfaCentral", centralDatabase);
    EnsureExactLocalConnection("ConnectionStrings__AlfaGestion", tenantDatabase);
}

void EnsureExactLocalConnection(string variable, string expectedDatabase)
{
    var value = Environment.GetEnvironmentVariable(variable);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"Falta la conexión local {variable}.");
    var builder = new SqlConnectionStringBuilder(value);
    if (!string.Equals(builder.DataSource, server, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(builder.InitialCatalog, expectedDatabase, StringComparison.Ordinal)
        || !builder.IntegratedSecurity)
        throw new InvalidOperationException($"La conexión {variable} no apunta al catálogo LocalDB autorizado.");
}

string EncodeLegacyPassword(string plainText)
{
    var length = plainText.Length;
    return string.Concat(plainText.Reverse().Select(ch =>
        (ch + length).ToString("000", System.Globalization.CultureInfo.InvariantCulture)));
}

async Task<(string Base84Token, string CrossTenantToken)> PrepareSimulationAsync(string phoneNumberId)
{
    await ValidateBootstrapAsync();
    await using var cn = new SqlConnection(Connection(centralDatabase));
    await cn.OpenAsync();
    var base84Token = await EnsureBaseTokenAsync(cn, allowedBaseId, "ES_DEV_BASE_84", "ES_LOCAL");
    var crossToken = await EnsureBaseTokenAsync(cn, crossTenantBaseId, "ES_LOCAL_CROSS_TENANT_CALLBACK", "ES_LOCAL_CROSS");
    const string wabaId = "900000000000084";
    await cn.ExecuteAsync("""
        IF NOT EXISTS (SELECT 1 FROM dbo.WhatsAppWabaOwnership WHERE WabaId=@WabaId)
          INSERT dbo.WhatsAppWabaOwnership(WabaId,IdBase,MetaBusinessId,FechaAltaUtc,FechaModificacionUtc)
          VALUES(@WabaId,84,'900000000000001',SYSUTCDATETIME(),SYSUTCDATETIME());
        IF NOT EXISTS (SELECT 1 FROM dbo.WhatsAppPhoneOwnership WHERE PhoneNumberId=@PhoneId)
          INSERT dbo.WhatsAppPhoneOwnership(PhoneNumberId,WabaId,IdBase,FechaAltaUtc,FechaModificacionUtc)
          VALUES(@PhoneId,@WabaId,84,SYSUTCDATETIME(),SYSUTCDATETIME());
        """, new { WabaId = wabaId, PhoneId = phoneNumberId });
    var ownership = await cn.QuerySingleAsync<(int WabaBase, int PhoneBase, string PhoneWaba)>("""
        SELECT
          (SELECT IdBase FROM dbo.WhatsAppWabaOwnership WHERE WabaId=@WabaId),
          (SELECT IdBase FROM dbo.WhatsAppPhoneOwnership WHERE PhoneNumberId=@PhoneId),
          (SELECT WabaId FROM dbo.WhatsAppPhoneOwnership WHERE PhoneNumberId=@PhoneId);
        """, new { WabaId = wabaId, PhoneId = phoneNumberId });
    if (ownership.WabaBase != allowedBaseId || ownership.PhoneBase != allowedBaseId || ownership.PhoneWaba != wabaId)
        throw new InvalidOperationException("La fixture local ya existe con ownership incompatible.");
    return (base84Token, crossToken);
}

async Task InspectStateAsync()
{
    await ValidateBootstrapAsync();
    await using var central = new SqlConnection(Connection(centralDatabase));
    await central.OpenAsync();
    var centralState = await central.QuerySingleAsync<(int Onboardings, string? LatestStatus, string? LatestStep, int WabaOwnerships, int PhoneOwnerships, int VaultCredentials)>("""
        SELECT
          (SELECT COUNT(*) FROM dbo.WhatsAppEmbeddedOnboarding WHERE IdBase=@IdBase),
          (SELECT TOP (1) Estado FROM dbo.WhatsAppEmbeddedOnboarding WHERE IdBase=@IdBase ORDER BY FechaModificacionUtc DESC),
          (SELECT TOP (1) PasoActual FROM dbo.WhatsAppEmbeddedOnboarding WHERE IdBase=@IdBase ORDER BY FechaModificacionUtc DESC),
          (SELECT COUNT(*) FROM dbo.WhatsAppWabaOwnership WHERE IdBase=@IdBase),
          (SELECT COUNT(*) FROM dbo.WhatsAppPhoneOwnership WHERE IdBase=@IdBase),
          (SELECT COUNT(*) FROM dbo.WhatsAppSecureVault WHERE IdBase=@IdBase);
        """, new { IdBase = allowedBaseId });

    await using var tenant = new SqlConnection(Connection(tenantDatabase));
    await tenant.OpenAsync();
    var tenantState = await tenant.QuerySingleAsync<(int ActiveNumbers, int ActiveApiNumbers)>("""
        SELECT
          COUNT(*),
          SUM(CASE WHEN Activo=1 AND ISNULL(PhoneNumberId,N'') <> N''
                    AND ISNULL(PhoneNumberId,N'') NOT LIKE N'WEBPENDING-%' THEN 1 ELSE 0 END)
        FROM dbo.CONV_WHATSAPP_NUMEROS
        WHERE Activo=1;
        """);
    var activeApiRows = (await tenant.QueryAsync<(int IdNumero, string PhoneNumberId, string Nombre)>("""
        SELECT IdNumero,PhoneNumberId,Nombre
        FROM dbo.CONV_WHATSAPP_NUMEROS
        WHERE Activo=1 AND ISNULL(PhoneNumberId,N'') <> N''
          AND ISNULL(PhoneNumberId,N'') NOT LIKE N'WEBPENDING-%'
        ORDER BY IdNumero;
        """)).ToArray();

    Console.WriteLine(
        $"STATE_OK Central={centralDatabase} Tenant={tenantDatabase} Base={allowedBaseId} " +
        $"Onboardings={centralState.Onboardings} LatestStatus={centralState.LatestStatus ?? "NONE"} LatestStep={centralState.LatestStep ?? "NONE"} " +
        $"WabaOwnerships={centralState.WabaOwnerships} PhoneOwnerships={centralState.PhoneOwnerships} VaultCredentials={centralState.VaultCredentials} " +
        $"ActiveNumbers={tenantState.ActiveNumbers} ActiveApiNumbers={tenantState.ActiveApiNumbers}");
    foreach (var row in activeApiRows)
        Console.WriteLine($"OPERATIONAL IdNumero={row.IdNumero} PhoneNumberId={row.PhoneNumberId} Nombre={row.Nombre}");
}

async Task AuditExistingMetaAsync(string wabaId, string phoneNumberId)
{
    await ValidateBootstrapAsync();
    var keyRingPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlfaCore", "DataProtectionKeys", "WhatsAppEmbeddedSignup");
    if (!Path.IsPathRooted(keyRingPath) || !Directory.Exists(keyRingPath))
        throw new InvalidOperationException("El key ring local de Embedded Signup no está disponible.");

    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsAppEmbeddedSignup:CentralConnectionString"] = Connection(centralDatabase),
            ["WhatsAppEmbeddedSignup:GraphApiVersion"] = "v26.0",
            ["WhatsAppEmbeddedSignup:GraphBaseUrl"] = "https://graph.facebook.com",
            ["WhatsAppEmbeddedSignup:DataProtectionKeysPath"] = keyRingPath,
            ["WhatsAppEmbeddedSignup:WorkerEnabled"] = "false"
        })
        .Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddOptions<WhatsAppEmbeddedSignupOptions>()
        .Bind(configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName));
    var dataProtection = services.AddDataProtection().SetApplicationName("AlfaCore.WhatsAppEmbeddedSignup");
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
    if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
    services.AddHttpClient("MetaEmbeddedSignupManagement").RemoveAllLoggers();
    services.AddScoped<WhatsAppSecureVault>();
    services.AddScoped<IWhatsAppCredentialVault>(provider => provider.GetRequiredService<WhatsAppSecureVault>());
    services.AddScoped<IWhatsAppPhonePinVault>(provider => provider.GetRequiredService<WhatsAppSecureVault>());
    services.AddSingleton<IWhatsAppWabaRoutingProvider, ReadOnlyAuditRoutingProvider>();
    services.AddScoped<IMetaWhatsAppManagementClient, MetaWhatsAppManagementClient>();

    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var vault = scope.ServiceProvider.GetRequiredService<IWhatsAppCredentialVault>();
    var credential = await vault.FindActiveCredentialAsync(allowedBaseId, wabaId, phoneNumberId)
        ?? throw new InvalidOperationException("No existe una credencial Vault activa para el activo solicitado.");
    var context = await vault.GetContextAsync(credential)
        ?? throw new InvalidOperationException("La credencial Vault no tiene contexto vigente.");
    if (context.IdBase != allowedBaseId || context.WabaId != wabaId || context.PhoneNumberId != phoneNumberId)
        throw new InvalidOperationException("El contexto Vault no coincide exactamente con Base 84 y el activo solicitado.");

    await using var central = new SqlConnection(Connection(centralDatabase));
    var sqlState = await central.QuerySingleAsync<(int WabaOwnership, int PhoneOwnership, int MatchingOnboarding, string? OnboardingStatus, string? OnboardingStep)>("""
        SELECT
          (SELECT COUNT(*) FROM dbo.WhatsAppWabaOwnership WHERE WabaId=@WabaId AND IdBase=@IdBase),
          (SELECT COUNT(*) FROM dbo.WhatsAppPhoneOwnership WHERE PhoneNumberId=@PhoneNumberId AND WabaId=@WabaId AND IdBase=@IdBase),
          (SELECT COUNT(*) FROM dbo.WhatsAppEmbeddedOnboarding WHERE IdOnboarding=@IdOnboarding AND IdBase=@IdBase AND TokenReference=@TokenReference),
          (SELECT Estado FROM dbo.WhatsAppEmbeddedOnboarding WHERE IdOnboarding=@IdOnboarding AND IdBase=@IdBase AND TokenReference=@TokenReference),
          (SELECT PasoActual FROM dbo.WhatsAppEmbeddedOnboarding WHERE IdOnboarding=@IdOnboarding AND IdBase=@IdBase AND TokenReference=@TokenReference);
        """, new
    {
        IdBase = allowedBaseId,
        WabaId = wabaId,
        PhoneNumberId = phoneNumberId,
        context.IdOnboarding,
        TokenReference = credential.Value
    });
    if (sqlState.WabaOwnership != 1 || sqlState.PhoneOwnership != 1 || sqlState.MatchingOnboarding != 1)
        throw new InvalidOperationException("Ownership, onboarding y credencial no forman una relación inequívoca para Base 84.");

    await using var tenant = new SqlConnection(Connection(tenantDatabase));
    var operationalCount = await tenant.ExecuteScalarAsync<int>("""
        SELECT COUNT(*) FROM dbo.CONV_WHATSAPP_NUMEROS
        WHERE PhoneNumberId=@PhoneNumberId AND Activo=1;
        """, new { PhoneNumberId = phoneNumberId });

    var management = scope.ServiceProvider.GetRequiredService<IMetaWhatsAppManagementClient>();
    var phones = await management.DiscoverPhoneNumbersAsync(wabaId, credential);
    var phone = phones.SingleOrDefault(item => item.PhoneNumberId == phoneNumberId);
    Console.WriteLine(phone is null
        ? $"META_AUDIT_OK Credential=Present Context=Valid Ownership=Valid OnboardingStatus={sqlState.OnboardingStatus} OnboardingStep={sqlState.OnboardingStep} OperationalCount={operationalCount} Waba={wabaId} WabaAccessible=True PhoneFound=False"
        : $"META_AUDIT_OK Credential=Present Context=Valid Ownership=Valid OnboardingStatus={sqlState.OnboardingStatus} OnboardingStep={sqlState.OnboardingStep} OperationalCount={operationalCount} Waba={wabaId} WabaAccessible=True PhoneFound=True PhoneNumberId={phone.PhoneNumberId} DisplayPhoneNumber={phone.DisplayPhoneNumber} VerifiedName={phone.VerifiedName} RegistrationStatus={phone.RegistrationStatus}");
}

async Task<string> EnsureBaseTokenAsync(SqlConnection cn, int idBase, string name, string idCliente)
{
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await cn.ExecuteAsync("""
        IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id=@IdBase)
          INSERT dbo.bases(id,idcliente,nombre,dbserver,dbname) VALUES(@IdBase,@IdCliente,@Name,N'(localdb)\MSSQLLocalDB',N'ALFACORE_ES_TENANT_DEV');
        UPDATE dbo.bases SET idcliente=@IdCliente, WebhookToken=COALESCE(WebhookToken,@Token) WHERE id=@IdBase AND nombre=@Name;
        """, new { IdBase = idBase, IdCliente = idCliente, Name = name, Token = token });
    var row = await cn.QuerySingleAsync<(string Name, string Server, string Database, string Token)>("""
        SELECT nombre AS Name, ISNULL(dbserver,'') AS Server, ISNULL(dbname,'') AS [Database], WebhookToken AS Token
        FROM dbo.bases WHERE id=@IdBase;
        """, new { IdBase = idBase });
    if (row.Name != name || row.Server != server || row.Database != tenantDatabase)
        throw new InvalidOperationException("La base ficticia local existe con una identidad incompatible.");
    return row.Token;
}

bool IsDigits(string value) => value.Length > 0 && value.All(static c => c is >= '0' and <= '9');
void Fail(string message) => throw new InvalidOperationException(message);

sealed class ReadOnlyAuditRoutingProvider : IWhatsAppWabaRoutingProvider
{
    public Task<WhatsAppWabaRoutingConfiguration> GetAsync(int idBase, CancellationToken ct = default)
        => throw new InvalidOperationException("La auditoría read-only no permite configurar routing ni webhooks.");
}
