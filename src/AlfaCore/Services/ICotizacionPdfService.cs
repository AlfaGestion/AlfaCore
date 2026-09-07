using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICotizacionPdfService
{
    byte[] GenerarPdf(CotizacionVersionDetailDto detail, string nombreEmpresa);
}
