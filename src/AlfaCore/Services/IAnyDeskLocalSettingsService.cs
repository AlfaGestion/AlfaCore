namespace AlfaCore.Services;

/// <summary>
/// AlfaCore corre en un servidor compartido (no en la PC de cada técnico), así que este
/// servicio no puede escribir directamente en el registro de Windows del técnico: solo genera
/// el contenido de un archivo .reg descargable, que cada técnico ejecuta una vez en su propia
/// PC para que los links "anydesk:&lt;id&gt;" abran su cliente local.
/// </summary>
public interface IAnyDeskLocalSettingsService
{
    string BuildRegistryScript(string executablePath);
}
