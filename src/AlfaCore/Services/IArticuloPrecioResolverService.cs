using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Resolución de lista/clase/precio de artículos para un cliente (o consumidor final cuando
/// no hay cliente), extraída de CrmCotizacionService para que la comparta cualquier módulo
/// que necesite cotizar artículos con la misma regla (hoy: CRM y Cotizaciones).
/// </summary>
public interface IArticuloPrecioResolverService
{
    Task<CrmCotizacionPricingContextDto> ResolveContextAsync(SqlConnection cn, string? clienteCodigo, CancellationToken ct, SqlTransaction? tx = null);

    Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosAsync(SqlConnection cn, CrmCotizacionPricingContextDto pricing, string texto, int take, CancellationToken ct);
}
