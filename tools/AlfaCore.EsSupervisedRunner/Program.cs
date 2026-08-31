using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

if (args.Length != 2 || args[0] != "--confirm-local-dev" || !Guid.TryParse(args[1], out var onboardingId))
    throw new InvalidOperationException("Uso: --confirm-local-dev <IdOnboarding>.");

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var configuration = new ConfigurationBuilder()
    .SetBasePath(root)
    .AddJsonFile("src/AlfaCore/appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["WhatsAppEmbeddedSignup:CentralConnectionString"] = @"Server=(localdb)\MSSQLLocalDB;Initial Catalog=ALFA_CENTRAL_DEV;Integrated Security=True;TrustServerCertificate=True",
        ["WhatsAppEmbeddedSignup:WorkerEnabled"] = "false"
    })
    .Build();
var options = configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName).Get<WhatsAppEmbeddedSignupOptions>() ?? new();
if (options.WorkerEnabled)
    throw new InvalidOperationException("El runner se niega a operar con WorkerEnabled=true.");

var connection = new SqlConnectionStringBuilder(options.CentralConnectionString);
if (!string.Equals(connection.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase)
    || !string.Equals(connection.InitialCatalog, "ALFA_CENTRAL_DEV", StringComparison.Ordinal))
    throw new InvalidOperationException("El runner solo admite (localdb)\\MSSQLLocalDB / ALFA_CENTRAL_DEV.");
await using (var guard = new SqlConnection(connection.ConnectionString))
{
    await guard.OpenAsync();
    await using var command = guard.CreateCommand();
    command.CommandText = "SELECT DB_NAME(), CAST(SERVERPROPERTY('IsLocalDB') AS int)";
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    if (reader.GetString(0) != "ALFA_CENTRAL_DEV" || reader.GetInt32(1) != 1)
        throw new InvalidOperationException("La conexión no corresponde al catálogo DEV LocalDB autorizado.");
}

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddOptions<WhatsAppEmbeddedSignupOptions>().Bind(configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName));
var dataProtection = services.AddDataProtection().SetApplicationName("AlfaCore.WhatsAppEmbeddedSignup");
if (!string.IsNullOrWhiteSpace(options.DataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeysPath));
    if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
}
services.AddHttpClient("MetaEmbeddedSignupOAuth").RemoveAllLoggers();
services.AddHttpClient("MetaEmbeddedSignupManagement").RemoveAllLoggers();
services.AddScoped<IWhatsAppEmbeddedSignupStore, WhatsAppEmbeddedSignupStore>();
services.AddScoped<IWhatsAppAssetOwnershipStore, WhatsAppAssetOwnershipStore>();
services.AddScoped<WhatsAppSecureVault>();
services.AddScoped<IWhatsAppCredentialVault>(p => p.GetRequiredService<WhatsAppSecureVault>());
services.AddScoped<IWhatsAppPhonePinVault>(p => p.GetRequiredService<WhatsAppSecureVault>());
services.AddScoped<IMetaOAuthClient, MetaOAuthClient>();
services.AddScoped<IMetaWhatsAppManagementClient, MetaWhatsAppManagementClient>();
services.AddSingleton<IWhatsAppEmbeddedSignupStateProtector, WhatsAppEmbeddedSignupStateProtector>();
services.AddScoped<IWhatsAppEmbeddedSignupErrorLogger, SupervisedErrorLogger>();
services.AddScoped<IWhatsAppEmbeddedSignupOrchestrator, WhatsAppEmbeddedSignupOrchestrator>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var orchestrator = scope.ServiceProvider.GetRequiredService<IWhatsAppEmbeddedSignupOrchestrator>();
var store = scope.ServiceProvider.GetRequiredService<IWhatsAppEmbeddedSignupStore>();
var management = scope.ServiceProvider.GetRequiredService<IMetaWhatsAppManagementClient>();
var vault = scope.ServiceProvider.GetRequiredService<IWhatsAppCredentialVault>();

const string expectedWabaId = "1547539197385596";
const string expectedPhoneNumberId = "1195619520311268";
var preflight = await store.GetAsync(onboardingId) ?? throw new InvalidOperationException("Onboarding inexistente.");
if (onboardingId != Guid.Parse("5bad6682-238b-4230-888f-c7b112fa9edd")
    || preflight.IdBase != 84
    || preflight.OnboardingMode != WhatsAppEmbeddedOnboardingMode.Standard
    || preflight.Status != WhatsAppEmbeddedOnboardingStatus.RegisteringPhones
    || preflight.CurrentStep != "REGISTRATION_REQUIRED")
    throw new InvalidOperationException("El onboarding no coincide exactamente con la autorización supervisada ES-3B.");
var preflightToken = new WhatsAppCredentialReference(preflight.TokenReference);
_ = await vault.GetAsync(preflightToken);
var preflightContext = await vault.GetContextAsync(preflightToken) ?? throw new InvalidOperationException("La credencial segura no tiene contexto vigente.");
if (preflightContext.IdBase != 84 || preflightContext.WabaId != expectedWabaId || preflightContext.PhoneNumberId != expectedPhoneNumberId)
    throw new InvalidOperationException("El contexto seguro no coincide con el activo autorizado.");
await using (var guard = new SqlConnection(connection.ConnectionString))
{
    await guard.OpenAsync();
    await using var command = guard.CreateCommand();
    command.CommandText = """
        SELECT
          (SELECT COUNT(*) FROM dbo.WhatsAppWabaOwnership WHERE WabaId=@WabaId AND IdBase=84),
          (SELECT COUNT(*) FROM dbo.WhatsAppPhoneOwnership WHERE PhoneNumberId=@PhoneId AND WabaId=@WabaId AND IdBase=84),
          (SELECT COUNT(*) FROM dbo.WhatsAppSecureVault WHERE SecretReference=@CredentialReference AND SecretType='CREDENTIAL' AND IdBase=84 AND RevokedAtUtc IS NULL);
        """;
    command.Parameters.AddWithValue("@WabaId", expectedWabaId);
    command.Parameters.AddWithValue("@PhoneId", expectedPhoneNumberId);
    command.Parameters.AddWithValue("@CredentialReference", preflight.TokenReference);
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    if (reader.GetInt32(0) != 1 || reader.GetInt32(1) != 1 || reader.GetInt32(2) != 1)
        throw new InvalidOperationException("Ownership o credencial no coinciden exactamente con Base 84.");
}
var preflightPhones = await management.DiscoverPhoneNumbersAsync(expectedWabaId, preflightToken);
var preflightPhone = preflightPhones.SingleOrDefault(x => x.PhoneNumberId == expectedPhoneNumberId && x.WabaId == expectedWabaId)
    ?? throw new InvalidOperationException("Graph no devolvió exactamente el teléfono autorizado.");
if (preflightPhone.RegistrationStatus != MetaPhoneRegistrationStatus.RegistrationRequired
    || preflightPhone.VerifiedName != "AlfaNet Tester"
    || preflightPhone.DisplayPhoneNumber != "+1 555-482-7373")
    throw new InvalidOperationException("El estado o identidad actual de Meta difiere de la autorización supervisada.");
Console.WriteLine("Preflight=OK; Base=84; Mode=Standard; Ownership=OK; Credential=Vault; Registration=Required");

for (var index = 0; index < 10; index++)
{
    var current = await store.GetAsync(onboardingId) ?? throw new InvalidOperationException("Onboarding inexistente.");
    Console.WriteLine($"State={current.Status}; Step={current.CurrentStep}; Mode={current.OnboardingMode}");
    if (current.IdBase != 84 || current.OnboardingMode != WhatsAppEmbeddedOnboardingMode.Standard)
        throw new InvalidOperationException("El onboarding no corresponde a Base 84 / STANDARD.");
    if (current.Status is WhatsAppEmbeddedOnboardingStatus.Importing
        or WhatsAppEmbeddedOnboardingStatus.ActionRequired or WhatsAppEmbeddedOnboardingStatus.FailedFinal or WhatsAppEmbeddedOnboardingStatus.FailedRetryable)
        break;
    await orchestrator.ProcessNextStepAsync(onboardingId);
}

var final = await store.GetAsync(onboardingId) ?? throw new InvalidOperationException("Onboarding inexistente.");
Console.WriteLine($"FinalState={final.Status}; FinalStep={final.CurrentStep}; ErrorCodePresent={!string.IsNullOrWhiteSpace(final.ErrorCode)}; IncidentPresent={!string.IsNullOrWhiteSpace(final.IncidentId)}");
if (final.Status is WhatsAppEmbeddedOnboardingStatus.Importing or WhatsAppEmbeddedOnboardingStatus.RegisteringPhones)
{
    var token = new WhatsAppCredentialReference(final.TokenReference);
    var hint = await vault.GetContextAsync(token) ?? throw new InvalidOperationException("No se encontró el contexto seguro del onboarding.");
    foreach (var phone in await management.DiscoverPhoneNumbersAsync(hint.WabaId, token))
        Console.WriteLine($"ImportPreview BusinessId={hint.MetaBusinessId}; WabaId={hint.WabaId}; PhoneNumberId={phone.PhoneNumberId}; DisplayPhoneNumber={phone.DisplayPhoneNumber}; VerifiedName={phone.VerifiedName}; RegistrationStatus={phone.RegistrationStatus}; QualityRating={phone.QualityRating}; Mode={final.OnboardingMode}");
}

internal sealed class SupervisedErrorLogger : IWhatsAppEmbeddedSignupErrorLogger
{
    public Task<string> LogAsync(Guid idOnboarding, int idBase, string step, string errorCode, string? wabaId, string? phoneNumberId, int retryCount, CancellationToken ct = default)
    {
        var incident = Guid.NewGuid().ToString("N");
        Console.Error.WriteLine($"Incident={incident}; Step={step}; ErrorCode={errorCode}; WabaPresent={!string.IsNullOrWhiteSpace(wabaId)}; PhonePresent={!string.IsNullOrWhiteSpace(phoneNumberId)}; Retry={retryCount}");
        return Task.FromResult(incident);
    }
}
