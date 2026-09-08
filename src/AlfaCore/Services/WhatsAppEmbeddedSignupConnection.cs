namespace AlfaCore.Services;

public static class WhatsAppEmbeddedSignupConnection
{
    public static string Resolve(IConfiguration configuration, IHostEnvironment? environment = null)
    {
        var options = configuration
            .GetSection(Configuration.WhatsAppEmbeddedSignupOptions.SectionName)
            .Get<Configuration.WhatsAppEmbeddedSignupOptions>() ?? new();
        var hasDedicatedConnection = !string.IsNullOrWhiteSpace(options.CentralConnectionString);
        if (options.UseApplicationCentralConnection && hasDedicatedConnection)
            throw new InvalidOperationException("WhatsApp Embedded Signup tiene dos conexiones centrales configuradas; elegí UseApplicationCentralConnection o CentralConnectionString.");

        if (options.UseApplicationCentralConnection)
            return configuration.GetConnectionString("AlfaCentral")
                ?? throw new InvalidOperationException("WhatsApp Embedded Signup requiere ConnectionStrings:AlfaCentral porque UseApplicationCentralConnection=true.");

        if (hasDedicatedConnection)
            return options.CentralConnectionString.Trim();

        throw new InvalidOperationException(
            "WhatsApp Embedded Signup requiere una conexión central explícita: UseApplicationCentralConnection=true o WhatsAppEmbeddedSignup:CentralConnectionString.");
    }
}
