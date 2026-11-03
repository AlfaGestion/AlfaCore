namespace AlfaCore.Models;

public sealed class CompraCargaOpcion
{
    public string Codigo {get;set;}="";
    public string Descripcion {get;set;}="";
}
public sealed class CompraCargaProveedor
{
    public string Codigo {get;set;}="";
    public string Nombre {get;set;}="";
    public string Documento {get;set;}="";
    public string CondicionIva {get;set;}="";
    public string Motivo {get;set;}="";
    public string Imputacion {get;set;}="";
    public bool ExentoServicios {get;set;}
    public bool ExentoOtros {get;set;}
    public bool SinOc {get;set;}
    public string Moneda {get;set;}="";
}
public sealed class CompraCargaRegla
{
    public string Motivo {get;set;}="";
    public string CondicionIva {get;set;}="";
    public string Compra {get;set;}="";
    public string Iva {get;set;}="";
    public string PercepcionIva {get;set;}="";
    public string PercepcionIibb {get;set;}="";
    public string PercepcionGanancias {get;set;}="";
}
public sealed record CompraCargaContexto(IReadOnlyList<CompraCargaOpcion> Unidades,IReadOnlyList<CompraCargaOpcion> Motivos,
    IReadOnlyList<CompraCargaOpcion> Cuentas,IReadOnlyList<decimal> Tasas,IReadOnlyList<CompraCargaRegla> Reglas,
    string Unidad,string Motivo,DateTime? Desde,DateTime? Hasta,DateTime? Cierre)
{
    public IReadOnlyList<CompraCargaTipo> Tipos {get;init;}=[];
    public IReadOnlyList<decimal> TasasFiscales {get;init;}=[];
}
public sealed class CompraCargaTipo
{
    public string Codigo {get;set;}="";
    public string Descripcion {get;set;}="";
    public string Unidad {get;set;}="";
    public int Talonario {get;set;}
    public string Letras {get;set;}="";
    public bool Externo {get;set;}
    public string Stock {get;set;}="";
    public string DebeHaber {get;set;}="";
    public string Clave=>$"{Codigo}|{Talonario}|{Unidad}";
    public bool VistaPreviaDisponible=>Codigo=="FCC";
    public string Alcance=>Codigo switch
    {
        "FCC"=>"Factura de compra: servicios y gastos en pesos, letras A/C. Revisá el asiento antes de registrar. No incluye mercadería ni pagos.",
        "OC"=>"Orden de compra: pedido y seguimiento de cantidades. Pendiente de implementar; no genera el asiento ni el IVA de una factura.",
        "RMC"=>"Remito de compra: recepción de mercadería y aplicación a órdenes. Pendiente de implementar; hay que evitar repetir stock al facturarlo.",
        "NCC" or "NCCP"=>"Nota de crédito de compra: requiere signos, aplicación y tratamiento fiscal propios. Pendiente de implementar.",
        "NDC"=>"Nota de débito de compra: requiere aplicación y tratamiento contable/fiscal propios. Pendiente de implementar.",
        "FPC"=>"Proforma de compra: requiere su tratamiento específico de cuenta corriente, stock e IVA. Pendiente de implementar.",
        "PG" or "PGFP" or "OPG"=>"Pago u orden de pago: corresponde al circuito de pagos a proveedores. No se procesa como factura en esta pantalla.",
        _=>"Comprobante configurado en Compras. Su circuito específico todavía no está implementado en esta pantalla."
    };
}
public sealed class CompraCargaLinea
{
    public string Descripcion {get;set;}="";
    public string Cuenta {get;set;}="";
    public string Tratamiento {get;set;}="Gravado";
    public decimal Neto {get;set;}
    public decimal Tasa {get;set;}
    public decimal? IvaInformado {get;set;}
}
public sealed class CompraCargaBorrador
{
    public string Tipo {get;set;}="FCC";
    public int Talonario {get;set;}
    public long? DocumentoRecibido {get;set;}
    public bool ServiciosGastosConfirmados {get;set;}=true;
    public string Proveedor {get;set;}="";
    public string Unidad {get;set;}="";
    public string Motivo {get;set;}="";
    public string Letra {get;set;}="A";
    public string Sucursal {get;set;}="";
    public string Numero {get;set;}="";
    public DateTime Fecha {get;set;}=DateTime.Today;
    public DateTime FechaContable {get;set;}=DateTime.Today;
    public DateTime Vencimiento {get;set;}=DateTime.Today;
    public decimal PercepcionIva {get;set;}
    public decimal PercepcionIibb {get;set;}
    public decimal PercepcionGanancias {get;set;}
    public decimal? TotalDocumento {get;set;}
    public string AutorizacionFiscal {get;set;}="";
    public DateTime? VencimientoAutorizacion {get;set;}
    public List<CompraCargaLinea> Lineas {get;set;}=[];
}
public sealed record CompraCargaAsiento(string Cuenta,string Descripcion,decimal Debe,decimal Haber,bool CabeceraIva=false);
public sealed record CompraCargaBaseIva(string Tratamiento,decimal Tasa,decimal Neto,decimal Iva);
public sealed record CompraCargaVista(string IdComprobante,decimal Neto,decimal Iva,decimal Total,decimal? DiferenciaDocumento,
    IReadOnlyList<CompraCargaBaseIva> Bases,IReadOnlyList<CompraCargaAsiento> Asiento,IReadOnlyList<string> Errores,IReadOnlyList<string> Avisos)
{
    public string Huella {get;init;}="";
    public decimal DiferenciaAsiento=>Asiento.Sum(x=>x.Debe-x.Haber);
}
public sealed record CompraCargaOrigen(InterfacesDetalleDto Documento,CompraCargaBorrador Borrador,IReadOnlyList<string> Avisos);
public sealed record CompraCargaRegistroSolicitud(Guid Operacion,Guid Sesion,CompraCargaBorrador Borrador,string Huella);
public sealed record CompraCargaRegistrada(int CompraId,string Comprobante,string Periodo,int Mes,int Asiento,string TipoRegistro,decimal Total);
