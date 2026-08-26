namespace AlfaCore.Services;

internal static class WhatsAppEmbeddedSignupConnection
{
    public static string Resolve(IConfiguration configuration, IHostEnvironment? environment = null)
    {
        var options = configuration
            .GetSection(Configuration.WhatsAppEmbeddedSignupOptions.SectionName)
            .Get<Configuration.WhatsAppEmbeddedSignupOptions>() ?? new();
        if (!string.IsNullOrWhiteSpace(options.CentralConnectionString))
            return options.CentralConnectionString.Trim();

        if (environment?.IsDevelopment() == true)
        {
            throw new InvalidOperationException(
                "WhatsApp Embedded Signup requiere una conexión central explícita en Development; no se permite usar ConnectionStrings:AlfaCentral como fallback.");
        }

        return configuration.GetConnectionString("AlfaCentral")
            ?? throw new InvalidOperationException("No se configuró la conexión central de WhatsApp Embedded Signup.");
    }
}
