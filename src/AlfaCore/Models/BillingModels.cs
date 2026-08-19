namespace AlfaCore.Models;

/// <summary>Estados posibles de <c>dbo.Cargos.Estado</c> (CHECK constraint).</summary>
public static class CargoEstados
{
    public const string Borrador = "BORRADOR";
    public const string Pendiente = "PENDIENTE";
    public const string Pagado = "PAGADO";
    public const string PagoParcial = "PAGO_PARCIAL";
    public const string Vencido = "VENCIDO";
    public const string Anulado = "ANULADO";
}

/// <summary>Estados posibles de <c>dbo.Pagos.Estado</c> (CHECK constraint).</summary>
public static class PagoEstados
{
    public const string Creado = "CREADO";
    public const string Pendiente = "PENDIENTE";
    public const string Aprobado = "APROBADO";
    public const string Rechazado = "RECHAZADO";
    public const string Cancelado = "CANCELADO";
    public const string Reembolsado = "REEMBOLSADO";
}

/// <summary>Valores posibles de <c>dbo.Pagos.MedioPago</c> (CHECK constraint).</summary>
public static class MedioPagoValores
{
    public const string Efectivo = "EFECTIVO";
    public const string Transferencia = "TRANSFERENCIA";
    public const string MercadoPago = "MERCADO_PAGO";
    public const string Tarjeta = "TARJETA";
    public const string DebitoAutomatico = "DEBITO_AUTOMATICO";
    public const string Otro = "OTRO";

    public static readonly IReadOnlyList<string> Todos =
    [
        Efectivo, Transferencia, MercadoPago, Tarjeta, DebitoAutomatico, Otro
    ];
}

/// <summary>Un cargo de suscripción — ver <c>dbo.Cargos</c>.</summary>
public sealed class CargoDto
{
    public int Id { get; init; }
    public string IdCliente { get; init; } = string.Empty;
    public int IdClienteModulo { get; init; }
    public string Concepto { get; init; } = string.Empty;
    public DateTime? PeriodoDesde { get; init; }
    public DateTime? PeriodoHasta { get; init; }
    public decimal Importe { get; init; }
    public string Moneda { get; init; } = MonedaValores.Ars;
    public DateTime FechaEmision { get; init; }
    public DateTime FechaVencimiento { get; init; }
    public string Estado { get; init; } = CargoEstados.Pendiente;
    public DateTime CreadoUtc { get; init; }
    public DateTime? ModificadoUtc { get; init; }
    /// <summary>Enriquecido con <c>dbo.Clientes.nombre</c> solo por <see cref="IBillingService.SearchCargosAsync"/> — no es una columna de <c>dbo.Cargos</c>.</summary>
    public string? ClienteNombre { get; init; }
}

/// <summary>Un pago (manual en v1) — ver <c>dbo.Pagos</c>.</summary>
public sealed class PagoDto
{
    public int Id { get; init; }
    public string IdCliente { get; init; } = string.Empty;
    public int? IdCargo { get; init; }
    public DateTime Fecha { get; init; }
    public decimal Importe { get; init; }
    public string Moneda { get; init; } = MonedaValores.Ars;
    public string Estado { get; init; } = PagoEstados.Creado;
    public string MedioPago { get; init; } = MedioPagoValores.Transferencia;
    public string? Provider { get; init; }
    public string? ProviderPaymentId { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string? Referencia { get; init; }
    public string? Observaciones { get; init; }
    public string? RegistradoPor { get; init; }
    public DateTime? FechaAprobacion { get; init; }
    public DateTime CreadoUtc { get; init; }
    public DateTime? ModificadoUtc { get; init; }
    /// <summary>Enriquecido con <c>dbo.Clientes.nombre</c> solo por <see cref="IBillingService.SearchPagosAsync"/> — no es una columna de <c>dbo.Pagos</c>.</summary>
    public string? ClienteNombre { get; init; }
}

/// <summary>Filtros de listado de cargos — incluye paginación server-side (regla 27 de CODEX_RULES.md).</summary>
public sealed class CargosFilters
{
    public string? IdCliente { get; set; }
    public string? Estado { get; set; }
    public DateTime? FechaVencimientoDesde { get; set; }
    public DateTime? FechaVencimientoHasta { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>Filtros de listado de pagos — incluye paginación server-side (regla 27 de CODEX_RULES.md).</summary>
public sealed class PagosFilters
{
    public string? IdCliente { get; set; }
    public string? Estado { get; set; }
    public string? MedioPago { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>Configuración de vista del listado de Cargos (regla 26 de CODEX_RULES.md).</summary>
public sealed class CargosViewSettingsDto
{
    public string AgruparPor { get; set; } = CargosViewGroupKeys.None;
    public List<CargosViewColumnDto> Columnas { get; set; } = [];
}

public sealed class CargosViewColumnDto
{
    public string Key     { get; set; } = string.Empty;
    public string Label   { get; set; } = string.Empty;
    public bool   Visible { get; set; }
    public int    Order   { get; set; }
}

public static class CargosViewColumnKeys
{
    public const string Cliente      = "cliente";
    public const string Concepto     = "concepto";
    public const string Periodo      = "periodo";
    public const string Importe      = "importe";
    public const string Moneda       = "moneda";
    public const string Vencimiento  = "vencimiento";
    public const string Estado       = "estado";
}

public static class CargosViewGroupKeys
{
    public const string None   = "none";
    public const string Estado = "estado";
}

/// <summary>Configuración de vista del listado de Pagos (regla 26 de CODEX_RULES.md).</summary>
public sealed class PagosViewSettingsDto
{
    public string AgruparPor { get; set; } = PagosViewGroupKeys.None;
    public List<PagosViewColumnDto> Columnas { get; set; } = [];
}

public sealed class PagosViewColumnDto
{
    public string Key     { get; set; } = string.Empty;
    public string Label   { get; set; } = string.Empty;
    public bool   Visible { get; set; }
    public int    Order   { get; set; }
}

public static class PagosViewColumnKeys
{
    public const string Cliente     = "cliente";
    public const string Fecha       = "fecha";
    public const string Importe     = "importe";
    public const string Moneda      = "moneda";
    public const string MedioPago   = "mediopago";
    public const string Estado      = "estado";
    public const string Referencia  = "referencia";
}

public static class PagosViewGroupKeys
{
    public const string None   = "none";
    public const string Estado = "estado";
}

/// <summary>
/// Registrar un pago ya confirmado por fuera del sistema (transferencia recibida, efectivo
/// entregado) contra un Cargo puntual. Ver <see cref="IBillingService.RegistrarPagoManualAsync"/>.
/// </summary>
public sealed class RegistrarPagoManualRequest
{
    public string IdCliente { get; set; } = string.Empty;
    public int IdCargo { get; set; }
    public DateTime? Fecha { get; set; }
    public decimal Importe { get; set; }
    public string Moneda { get; set; } = MonedaValores.Ars;
    public string MedioPago { get; set; } = MedioPagoValores.Transferencia;
    public string? Referencia { get; set; }
    public string? Observaciones { get; set; }
}
