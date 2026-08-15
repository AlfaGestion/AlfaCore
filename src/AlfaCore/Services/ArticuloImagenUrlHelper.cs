namespace AlfaCore.Services;

public static class ArticuloImagenUrlHelper
{
    public static string BuildPublicImageUrl(string? ftpCodigoCta, string? idArticulo)
    {
        var cta = (ftpCodigoCta ?? string.Empty).Trim();
        var codigo = (idArticulo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cta) || string.IsNullOrWhiteSpace(codigo))
            return string.Empty;

        return $"/api/catalogos/imagen-articulo/{Uri.EscapeDataString(cta)}/{Uri.EscapeDataString(codigo)}";
    }
}
