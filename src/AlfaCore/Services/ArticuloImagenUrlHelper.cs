namespace AlfaCore.Services;

public static class ArticuloImagenUrlHelper
{
    public static string BuildPublicImageUrl(string? ftpCodigoCta, string? idArticulo, int? idBase = null, bool thumbnail = false)
    {
        var cta = (ftpCodigoCta ?? string.Empty).Trim();
        var codigo = (idArticulo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cta) || string.IsNullOrWhiteSpace(codigo))
            return string.Empty;

        var url = $"/api/catalogos/imagen-articulo/{Uri.EscapeDataString(cta)}/{Uri.EscapeDataString(codigo)}";

        var query = new List<string>();
        if (idBase is > 0)
            query.Add($"idbase={idBase.Value}");
        if (thumbnail)
            query.Add("thumb=true");

        return query.Count > 0 ? $"{url}?{string.Join("&", query)}" : url;
    }
}
