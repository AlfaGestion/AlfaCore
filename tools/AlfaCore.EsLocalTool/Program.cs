using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

const string server = @"(localdb)\MSSQLLocalDB";
const string centralDatabase = "ALFA_CENTRAL_DEV";
const string tenantDatabase = "ALFACORE_ES_TENANT_DEV";
const int allowedBaseId = 84;
const int crossTenantBaseId = 1900000106;

try
{
    if (args.Length == 0)
        Fail("Uso: bootstrap | prepare-simulation <PhoneNumberId>.");

    var root = FindRepositoryRoot();
    var command = args[0].Trim().ToLowerInvariant();
    switch (command)
    {
        case "bootstrap":
            await EnsureDatabaseAsync(centralDatabase);
            await EnsureDatabaseAsync(tenantDatabase);
            await ExecuteScriptAsync(Path.Combine(root, "docs", "base-datos", "sql-test", "bootstrap_alfa_central_dev_embedded_signup.sql"));
            await ValidateBootstrapAsync();
            Console.WriteLine("BOOTSTRAP_OK Catalog=ALFA_CENTRAL_DEV Base=84 Tenant=ALFACORE_ES_TENANT_DEV");
            break;

        case "prepare-simulation":
            if (args.Length != 2 || !IsDigits(args[1])) Fail("PhoneNumberId debe contener únicamente dígitos ficticios.");
            var result = await PrepareSimulationAsync(args[1]);
            Console.WriteLine($"BASE84_TOKEN={result.Base84Token}");
            Console.WriteLine($"CROSS_TENANT_TOKEN={result.CrossTenantToken}");
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
    script = Regex.Replace(script, @"(?im)^\s*:setvar\s+ExpectedDatabase\s+""ALFA_CENTRAL_DEV""\s*$", string.Empty)
        .Replace("$(ExpectedDatabase)", centralDatabase, StringComparison.Ordinal);
    await using var cn = new SqlConnection(Connection(centralDatabase));
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
    var result = await cn.QuerySingleAsync<(string Catalog, int IsLocal, int BaseCount, int TableCount)>("""
        SELECT DB_NAME(), CAST(SERVERPROPERTY('IsLocalDB') AS int),
          (SELECT COUNT(*) FROM dbo.bases WHERE id=84 AND nombre=N'ES_DEV_BASE_84'),
          (SELECT COUNT(*) FROM sys.tables WHERE name IN ('WhatsAppEmbeddedOnboarding','WhatsAppWabaOwnership','WhatsAppPhoneOwnership','WhatsAppSecureVault'));
        """);
    if (result.Catalog != centralDatabase || result.IsLocal != 1 || result.BaseCount != 1 || result.TableCount != 4)
        throw new InvalidOperationException("El bootstrap ES local no superó la validación final.");
}

async Task<(string Base84Token, string CrossTenantToken)> PrepareSimulationAsync(string phoneNumberId)
{
    await ValidateBootstrapAsync();
    await using var cn = new SqlConnection(Connection(centralDatabase));
    await cn.OpenAsync();
    var base84Token = await EnsureBaseTokenAsync(cn, allowedBaseId, "ES_DEV_BASE_84");
    var crossToken = await EnsureBaseTokenAsync(cn, crossTenantBaseId, "ES_LOCAL_CROSS_TENANT_CALLBACK");
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

async Task<string> EnsureBaseTokenAsync(SqlConnection cn, int idBase, string name)
{
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await cn.ExecuteAsync("""
        IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id=@IdBase)
          INSERT dbo.bases(id,idcliente,nombre,dbserver,dbname) VALUES(@IdBase,N'ES_LOCAL',@Name,N'(localdb)\MSSQLLocalDB',N'ALFACORE_ES_TENANT_DEV');
        UPDATE dbo.bases SET WebhookToken=COALESCE(WebhookToken,@Token) WHERE id=@IdBase;
        """, new { IdBase = idBase, Name = name, Token = token });
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
