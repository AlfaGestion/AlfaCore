using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Editor genérico para tablas de referencia chicas (forma Código/Descripción/Color opcional)
/// registradas en dbo.ALFACORE_TABLAS_REFERENCIA — pensado para Archivos &gt; Tablas, no exclusivo
/// de ningún módulo licenciable.
/// </summary>
public interface ITablasReferenciaService
{
    Task<IReadOnlyList<TablaReferenciaDto>> GetTablasAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TablaReferenciaFilaDto>> GetFilasAsync(string clave, CancellationToken ct = default);
    Task GuardarFilaAsync(string clave, GuardarFilaTablaReferenciaRequest request, CancellationToken ct = default);
    Task EliminarFilaAsync(string clave, string codigo, CancellationToken ct = default);
}
