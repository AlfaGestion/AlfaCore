using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Adapta la configuración existente de TA_CONFIGURACION/TA_LOGOS a una identidad pública única.</summary>
public sealed class CompanyBrandingService(IConfiguracionGeneralService configuracionGeneralService) : ICompanyBrandingService
{
    private const string DefaultName = "Alfa Gestión";
    private const string DefaultLogo = "/logos/Logo.png";

    public async Task<CompanyBrandingDto> GetAsync(
        string? baseUrl = null,
        string? idWeb = null,
        int? idBase = null,
        CancellationToken ct = default)
    {
        var empresa = await configuracionGeneralService.GetEmpresaAsync(ct);
        var logo = await configuracionGeneralService.GetLogoInfoAsync(ct);
        var nombre = empresa.Nombre.Trim();
        var logoUrl = logo.TieneLogo && !string.IsNullOrWhiteSpace(idWeb) && idBase is > 0
            ? $"/api/configuracion-web-portal/logo/{Uri.EscapeDataString(idWeb.Trim())}/{idBase.Value}"
            : logo.TieneLogo ? "/api/configuracion-general/logo" : DefaultLogo;

        if (Uri.TryCreate(baseUrl?.TrimEnd('/'), UriKind.Absolute, out var absoluteBase))
            logoUrl = new Uri(absoluteBase, logoUrl.TrimStart('/')).ToString();

        var direccion = string.Join(" ", new[] { empresa.Calle, empresa.Numero, empresa.Piso, empresa.Departamento }
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        return new CompanyBrandingDto
        {
            Nombre = string.IsNullOrWhiteSpace(nombre) ? DefaultName : nombre,
            Direccion = direccion,
            Telefono = empresa.Telefono.Trim(),
            Email = empresa.EmailWeb.Trim(),
            SitioWeb = empresa.SitioWeb.Trim(),
            LogoUrl = logoUrl,
            TieneLogo = logo.TieneLogo
        };
    }
}
