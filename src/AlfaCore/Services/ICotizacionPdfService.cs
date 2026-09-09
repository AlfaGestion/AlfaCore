using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICotizacionPdfService
{
    byte[] GenerarPdf(
        CotizacionVersionDetailDto detail,
        string nombreEmpresa,
        byte[]? logoBytes = null,
        byte[]? portadaBytes = null,
        byte[]? firmaBytes = null,
        string? firmanteNombre = null);
}
