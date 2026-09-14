namespace AlfaCore.Models;

public sealed record CierreCajaFiltros
{
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Caja { get; set; } = string.Empty;
    public bool SaldoInicial { get; set; }
    public bool Diario { get; set; }
    public bool Mensual { get; set; }
    public bool Cancelados { get; set; }
    public bool Productos { get; set; }
    public bool Rubros { get; set; }
    public bool Ventas { get; set; }
    public bool Cobranzas { get; set; }
}
public sealed record CierreCajaOpcion(string Codigo, string Descripcion);
public sealed record CierreCajaColumna(string Campo, string Nombre, bool Numero, bool Totaliza);
public sealed record CierreCajaSeccion(string Clave, string Nombre, string Consulta, string Orden, IReadOnlyList<CierreCajaColumna> Columnas);
public sealed record CierreCajaPagina(int TotalCount, IReadOnlyList<IReadOnlyDictionary<string, object?>> Filas, IReadOnlyDictionary<string, object?> Totales);

public static class CierreCajaSecciones
{
    public const string MenuKey = "D75-CIERRE";
    public static readonly IReadOnlyList<CierreCajaSeccion> Todas =
    [
        new("consolidado", "Saldo consolidado de caja", "consolidado", "_orden", [
        new("idcajas", "Caja", false, false),
        new("cuenta", "Cuenta", false, false),
        new("descripcion", "Descripción", false, false),
        new("saldoant", "Inicial + Ant.", true, true),
        new("cobranzas", "Cobranzas +", true, true),
        new("ingresos", "Ingresos +", true, true),
        new("egresos", "Egresos -", true, true),
        new("transferencias", "Transf -", true, true),
        new("saldo", "Saldo Actual", true, true),
        new("moneda", "Mon.", false, false),
        new("cotizacion", "Cotiz.", false, false),
        new("saldomoneda", "Saldo mon.", true, false)]),
        new("tarjetas", "Resumen de cobranzas con tarjetas", "tarjetas", "tarjeta,idcajas", [
        new("tarjeta", "Tarjeta", false, false),
        new("importe", "Importe", true, true),
        new("idcajas", "Caja", false, false)]),
        new("cobranzas-ctacte", "Cobranzas en cuenta corriente", "ctacte", "fecha,tc,idcomprobante,cuenta", [
        new("fecha", "Fecha", false, false),
        new("tc", "TC", false, false),
        new("idcomprobante", "IdComprobante", false, false),
        new("cuenta", "Cuenta", false, false),
        new("nombre", "Nombre", false, false),
        new("importe", "Importe", true, true)]),
        new("ventas-ctacte", "Ventas en cuenta corriente", "ctacte", "fecha,tc,idcomprobante,cuenta", [
        new("fecha", "Fecha", false, false),
        new("tc", "TC", false, false),
        new("idcomprobante", "IdComprobante", false, false),
        new("cuenta", "Cuenta", false, false),
        new("nombre", "Nombre", false, false),
        new("importe", "Importe", true, true)]),
        new("efectivo", "Detalle efectivo", "efectivo", "_orden", [
        new("nombre", "Detalle Efectivo", false, false),
        new("importe", "Importe", true, false)]),
        new("acumulado", "Total venta por comprobante", "acumulado", "nombre", [
        new("nombre", "Nombre", false, false),
        new("importe_venta_total", "Importe", true, true),
        new("total_iva", "IVA", true, false),
        new("cantidad_cptes", "Cant. Cptes.", true, false)]),
        new("transferencias", "Transferencias realizadas", "transferencias", "fecha,tc,sucursal,numero,letra,cuenta,origen,destino", [
        new("fecha", "Fecha", false, false),
        new("cuenta", "Cuenta", false, false),
        new("descripcion", "Descripción", false, false),
        new("egreso", "Egreso", true, true),
        new("ingreso", "Ingreso", true, true),
        new("origen", "Origen", false, false),
        new("destino", "Destino", false, false),
        new("moneda", "Moneda", false, false),
        new("cotizacion", "Cotización", false, false),
        new("tc", "TC", false, false),
        new("sucursal", "Sucursal", false, false),
        new("numero", "Numero", false, false),
        new("letra", "Letra", false, false)]),
        new("ingresos", "Detalle de ingresos de caja", "movimientos", "_orden", [
        new("cuenta", "Cuenta", false, false),
        new("descripcion", "Descripción", false, false),
        new("detalle", "Detalle", false, false),
        new("fecha", "Fecha", false, false),
        new("tc", "TC", false, false),
        new("idcomprobante", "IdComprobante", false, false),
        new("importe", "Importe", true, true),
        new("usuario", "Usuario", false, false)]),
        new("egresos", "Detalle de egresos de caja", "movimientos", "_orden", [
        new("cuenta", "Cuenta", false, false),
        new("descripcion", "Descripción", false, false),
        new("detalle", "Detalle", false, false),
        new("fecha", "Fecha", false, false),
        new("tc", "TC", false, false),
        new("idcomprobante", "IdComprobante", false, false),
        new("importe", "Importe", true, true),
        new("usuario", "Usuario", false, false)]),
        new("cancelados", "Comprobantes cancelados", "cancelados", "fechahora,tc,idcomprobante,usuario,pc", [
        new("tc", "TC", false, false),
        new("idcomprobante", "IdComprobante", false, false),
        new("fechahora", "Fecha", false, false),
        new("usuario", "Usuario", false, false),
        new("pc", "PC", false, false),
        new("detalle", "Detalle", false, false)]),
        new("rubros", "Detalle de venta por rubro", "rubros", "rubro,aliciva", [
        new("rubro", "Rubro", false, false),
        new("cantidad", "Cantidad", true, true),
        new("aliciva", "Alicuota", false, false),
        new("valorventa", "Valor venta s/iva", true, true),
        new("valorventa_civa", "Valor venta", true, true)]),
        new("productos", "Detalle de venta por artículo", "productos", "articulo,aliciva", [
        new("articulo", "Articulo", false, false),
        new("cantidad", "Cantidad", true, true),
        new("valorventa", "Valor venta s/iva", true, true),
        new("valorventa_civa", "Valor venta", true, true)]),
        new("ventas", "Detalle de ventas", "ventas", "fechahora,tc,idcomprobante,cuenta", [
        new("fecha", "Fecha", false, false),
        new("tc", "TC", false, false),
        new("idcomprobante", "IdComprobante", false, false),
        new("cuenta", "Cuenta", false, false),
        new("nombre", "Nombre", false, false),
        new("importe", "Importe", true, true),
        new("vendedor", "Vendedor", false, false),
        new("unegocio", "U. Neg.", false, false),
        new("usuario", "Usuario", false, false),
        new("fechahora", "Fecha/Hora", false, false)]),
        new("cobranzas", "Detalle de cobranzas", "ventas", "fechahora,tc,idcomprobante,cuenta", [
        new("fecha", "Fecha", false, false),
        new("tc", "TC", false, false),
        new("idcomprobante", "IdComprobante", false, false),
        new("cuenta", "Cuenta", false, false),
        new("nombre", "Nombre", false, false),
        new("importe", "Importe", true, true),
        new("vendedor", "Vendedor", false, false),
        new("unegocio", "U. Neg.", false, false),
        new("usuario", "Usuario", false, false),
        new("fechahora", "Fecha/Hora", false, false)]),
        new("diario", "Detalle diario", "diario", "_orden", [
        new("fecha", "Fecha", false, false),
        new("rubro", "Rubro", false, false),
        new("descripcion", "Articulo", false, false),
        new("importe", "Importe venta", true, true),
        new("efectivo", "Efectivo", true, true),
        new("tarjeta", "Tarjeta", true, true),
        new("debito", "Débito", true, true),
        new("saldo", "Saldo Cpte", true, true),
        new("nombre", "Cliente", false, false),
        new("formapago", "Forma de pago", false, false),
        new("factura", "Factura", false, false)]),
        new("mensual", "Detalle mensual", "diario", "_orden", [
        new("fecha", "Fecha", false, false),
        new("rubro", "Rubro", false, false),
        new("descripcion", "Articulo", false, false),
        new("importe", "Importe venta", true, true),
        new("efectivo", "Efectivo", true, true),
        new("tarjeta", "Tarjeta", true, true),
        new("debito", "Débito", true, true),
        new("saldo", "Saldo Cpte", true, true),
        new("nombre", "Cliente", false, false),
        new("formapago", "Forma de pago", false, false),
        new("factura", "Factura", false, false)]),
    ];
    public static IReadOnlyList<CierreCajaSeccion> Activas(CierreCajaFiltros filtros)
        => Todas.Where(s => s.Clave switch
        {
            "cancelados" => filtros.Cancelados, "rubros" => filtros.Rubros,
            "productos" => filtros.Productos, "ventas" => filtros.Ventas,
            "cobranzas" => filtros.Cobranzas, "diario" => filtros.Diario,
            "mensual" => filtros.Mensual, _ => true
        }).ToArray();
}
