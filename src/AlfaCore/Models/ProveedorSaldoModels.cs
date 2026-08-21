namespace AlfaCore.Models;

/// <summary>
/// Saldo de cuenta corriente de un Proveedor. Análogo a <see cref="PortalClienteCuentaCorrienteResumenDto"/>
/// pero del lado compras (dbo.CO_CPTES_SALDOS / dbo.CO_CPTES_IMPAGOS, equivalentes de
/// dbo.VE_CPTES_SALDOS_VENTAS que ya usa el Portal Cliente). No es un portal propio, solo lo que
/// necesita el asistente de Conversaciones para responder saldo de un proveedor.
/// </summary>
public sealed class ProveedorSaldoResumenDto
{
    public decimal SaldoTotal { get; set; }
    public decimal Vencido { get; set; }
    public decimal AVencer { get; set; }
    public int CantidadPendientes { get; set; }
}

public sealed class ProveedorComprobantePendienteDto
{
    public string Tc { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime Vencimiento { get; set; }
    public decimal Saldo { get; set; }
    public bool EstaVencido { get; set; }
}
