using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Abstracción mínima del medio que efectivamente registra un pago en <c>dbo.Pagos</c>. V1 solo
/// tiene una implementación real (<see cref="ManualPaymentProvider"/>): alguien de Alfa confirma la
/// transferencia/efectivo por fuera del sistema y lo carga a mano, ya aprobado. Mercado Pago queda
/// preparado como una futura segunda implementación (el pago nacería en <see cref="PagoEstados.Pendiente"/>
/// y un webhook lo confirmaría después) — no se construye todavía porque no hay integración real
/// (ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md, decisión de producto 6). Recibe la
/// conexión/transacción ya abiertas por <see cref="IBillingService.RegistrarPagoManualAsync"/> para
/// que la creación del pago y la actualización de Cargo/ClienteModulos sean atómicas.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Valor que se graba en <c>Pagos.Provider</c> (ej. "MANUAL").</summary>
    string Codigo { get; }

    /// <summary>Inserta el Pago y devuelve su Id. No actualiza Cargo ni ClienteModulos — eso es responsabilidad del llamador.</summary>
    Task<int> RegistrarPagoAsync(
        RegistrarPagoManualRequest request,
        string registradoPor,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken ct = default);
}
