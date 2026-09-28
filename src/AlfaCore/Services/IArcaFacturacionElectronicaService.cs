using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Orquesta WSAA+WSFEv1 para el flujo de venta del POS. Reutiliza la conexión ya abierta por
/// el llamador (PuntoVentaService), nunca abre una propia.</summary>
public interface IArcaFacturacionElectronicaService
{
    /// <summary>Valida certificado y conectividad WSAA sin pedir CAE ni reservar numeración.</summary>
    Task ValidarDisponibilidadAsync(SqlConnection cn, string? uNegocio, CancellationToken ct);

    /// <summary>Llamado ANTES de crear el comprobante local: resuelve el emisor y pide a AFIP el
    /// próximo número autorizado, para que V_MV_Cpte nazca ya alineado. Null si la facturación
    /// electrónica no aplica (apagada o sin configurar) -- el llamador sigue el flujo normal de
    /// auto-numeración local. Si aplica pero AFIP no responde, la excepción se propaga: el llamador
    /// decide (según ARCA_MODO_FALLO_CAE) si la deja pasar o corta la venta antes de grabar nada.</summary>
    Task<ArcaNumeracionPrevistaDto?> ResolverNumeracionAsync(SqlConnection cn, string? uNegocio, string letra, CancellationToken ct, Func<string, Task>? progreso = null);

    /// <summary>Llamado DESPUÉS de que los ítems ya están grabados: pide el CAE y siempre persiste un
    /// intento en V_MV_CPTE_ELECTRONICOS (éxito o rechazo) -- nunca lanza para errores de negocio/AFIP,
    /// solo para errores de programación. Devuelve NoAplica si la facturación electrónica no aplica.</summary>
    Task<ArcaCaeIntentoDto> SolicitarCaeYPersistirAsync(SqlConnection cn, PuntoVentaCaeContextoDto contexto, CancellationToken ct, Func<string, Task>? progreso = null);
}
