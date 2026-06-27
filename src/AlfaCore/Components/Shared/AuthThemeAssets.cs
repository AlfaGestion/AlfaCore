namespace AlfaCore.Components.Shared;

public static class AuthThemeAssets
{
    public static string GetLoginLogo(bool isDarkTheme)
        => isDarkTheme
            ? "/logos/LogoGrandeAzul.png"
            : "/logos/LogoGrandeNegro.png";
}
