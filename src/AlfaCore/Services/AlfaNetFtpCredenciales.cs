namespace AlfaCore.Services;

/// <summary>
/// Credenciales del FTP de ALFA NET (imágenes de artículos y destino por defecto de Interfaces).
/// Nunca van en el código ni en el repo: se leen de variables de entorno (en desarrollo, del archivo
/// <c>.env</c> que carga <c>DotEnvLoader</c>; en el servidor, variables de entorno del sitio o del sistema).
/// </summary>
public static class AlfaNetFtpCredenciales
{
    public const string VariableHost = "ALFANET_FTP_HOST";
    public const string VariableUsuario = "ALFANET_FTP_USUARIO";
    public const string VariableClave = "ALFANET_FTP_CLAVE";

    public static string Host => Leer(VariableHost);
    public static string Usuario => Leer(VariableUsuario);
    public static string Clave => Leer(VariableClave);

    /// <summary>True si están las tres variables; si falta alguna, el FTP no se usa.</summary>
    public static bool Configuradas => Host.Length > 0 && Usuario.Length > 0 && Clave.Length > 0;

    public static string Faltantes => string.Join(", ",
        new[] { VariableHost, VariableUsuario, VariableClave }.Where(v => Leer(v).Length == 0));

    private static string Leer(string variable) => (Environment.GetEnvironmentVariable(variable) ?? string.Empty).Trim();
}
