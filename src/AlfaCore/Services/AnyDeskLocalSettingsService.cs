namespace AlfaCore.Services;

public sealed class AnyDeskLocalSettingsService : IAnyDeskLocalSettingsService
{
    public string BuildRegistryScript(string executablePath)
    {
        var trimmedPath = (executablePath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmedPath))
            throw new InvalidOperationException("La ruta del ejecutable de AnyDesk es obligatoria.");

        if (!string.Equals(Path.GetExtension(trimmedPath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La ruta debe apuntar a un archivo .exe.");

        var commandValue = EscapeRegValue($"\"{trimmedPath}\" \"%1\"");

        return $"""
            Windows Registry Editor Version 5.00

            [HKEY_CURRENT_USER\Software\Classes\anydesk]
            @="URL:AnyDesk Protocol"
            "URL Protocol"=""

            [HKEY_CURRENT_USER\Software\Classes\anydesk\shell\open\command]
            @="{commandValue}"

            """;
    }

    // Sintaxis .reg: dentro de un valor de cadena, la barra invertida y las comillas van escapadas.
    private static string EscapeRegValue(string raw)
        => raw.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
