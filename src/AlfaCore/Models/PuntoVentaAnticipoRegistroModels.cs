using AlfaCore.Services;

namespace AlfaCore.Models;

public sealed record PuntoVentaAnticipoRegistroSolicitud(
    Guid Operacion,
    string IdComprobante,
    string Cuenta,
    decimal Importe,
    IReadOnlyList<CobranzaPago> Pagos);

public sealed record PuntoVentaAnticipoRegistroResultado(
    bool Habilitado,
    string Mensaje,
    string? Comprobante = null,
    int? CobranzaId = null,
    int? NumeroAsiento = null,
    decimal? Importe = null);

public static class PuntoVentaAnticipoRegistroValidaciones
{
    public static void ValidarSolicitud(PuntoVentaAnticipoRegistroSolicitud solicitud)
    {
        if (solicitud.Operacion == Guid.Empty)
            throw Error("La operación de registro no es válida.", "POS_ANTICIPO_OPERACION");
        if (string.IsNullOrWhiteSpace(solicitud.IdComprobante))
            throw Error("La nota de pedido es obligatoria.", "POS_ANTICIPO_NP");
        if (string.IsNullOrWhiteSpace(solicitud.Cuenta))
            throw Error("La cuenta de la nota de pedido es obligatoria.", "POS_ANTICIPO_CUENTA");
        if (solicitud.Importe <= 0 || solicitud.Importe != decimal.Round(solicitud.Importe, 2))
            throw Error("La seña debe ser mayor a cero y tener hasta dos decimales.", "POS_ANTICIPO_IMPORTE");
        if (solicitud.Pagos is null || solicitud.Pagos.Count == 0 || solicitud.Pagos.Count > 20)
            throw Error("Seleccioná al menos un medio de pago válido.", "POS_ANTICIPO_MEDIOS");
        if (solicitud.Pagos.Any(p => string.IsNullOrWhiteSpace(p.Cuenta) || p.Importe <= 0 || p.Importe != decimal.Round(p.Importe, 2)))
            throw Error("Los medios de pago deben tener cuentas válidas e importes positivos.", "POS_ANTICIPO_MEDIOS");
        if (solicitud.Pagos.Sum(p => p.Importe) != solicitud.Importe)
            throw Error("Los medios de pago deben coincidir con el importe de la seña.", "POS_ANTICIPO_TOTAL");
    }

    private static AppValidationException Error(string message, string code)
    {
        var validation = new ValidationResult();
        validation.Add(code, message);
        return new AppValidationException(message, validation);
    }
}
