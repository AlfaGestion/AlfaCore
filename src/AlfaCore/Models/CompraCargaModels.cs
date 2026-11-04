namespace AlfaCore.Models;

public sealed class CompraCargaOpcion
{
    public string Codigo {get;set;}="";
    public string Descripcion {get;set;}="";
    public bool RequiereCentroCosto {get;set;}
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
    public bool ExentoArticulos {get;set;}
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
    public string ImpuestoInterno {get;set;}="";
}
public sealed record CompraCargaContexto(IReadOnlyList<CompraCargaOpcion> Unidades,IReadOnlyList<CompraCargaOpcion> Motivos,
    IReadOnlyList<CompraCargaOpcion> Cuentas,IReadOnlyList<decimal> Tasas,IReadOnlyList<CompraCargaRegla> Reglas,
    string Unidad,string Motivo,DateTime? Desde,DateTime? Hasta,DateTime? Cierre)
{
    public IReadOnlyList<CompraCargaTipo> Tipos {get;init;}=[];
    public IReadOnlyList<decimal> TasasFiscales {get;init;}=[];
    public IReadOnlyList<CompraCargaOpcion> CentrosCosto {get;init;}=[];
    public IReadOnlyList<CompraCargaOpcion> Depositos {get;init;}=[];
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
    public bool EsComercial=>Codigo is "OC" or "RMC";
    public bool VistaPreviaDisponible=>Codigo is "FCC" or "NCC" or "NDC" or "OC" or "RMC";
    public string Alcance=>Codigo switch
    {
        "FCC"=>"Factura de compra A/C en pesos: artículos y otros conceptos. Revisá el total y el asiento antes de registrar.",
        "OC"=>"Orden de compra de artículos: se graba aprobada y numerada por el sistema. No genera asiento, libro de IVA ni stock; queda pendiente para recibirla o facturarla.",
        "RMC"=>"Remito de compra de artículos: ingresa stock con el número del proveedor. No genera asiento ni libro de IVA; al facturarlo no se repite el stock. Sin orden de compra solo para proveedores habilitados.",
        "NCC"=>"Nota de crédito de artículos y otros conceptos. Ingresá importes positivos: se invierte el asiento y el signo del libro. No aplica automáticamente contra facturas.",
        "NCCP"=>"Nota de crédito proforma: no habilitada en este circuito fiscal.",
        "NDC"=>"Nota de débito de artículos y otros conceptos: deuda e IVA según la configuración del comprobante.",
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
    public string Articulo {get;set;}="";
    public string UnidadArticulo {get;set;}="";
    public string ArticuloHuella {get;set;}="";
    public decimal? CostoUnitario {get;set;}
    public decimal Cantidad {get;set;}
}
public sealed class CompraCargaBorrador
{
    public string Tipo {get;set;}="FCC";
    public int Talonario {get;set;}
    public long? DocumentoRecibido {get;set;}
    public int? LecturaCompraId {get;set;}
    public string LecturaCompraHuella {get;set;}="";
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
    public decimal ImpuestoInterno {get;set;}
    public decimal? TotalDocumento {get;set;}
    public string AutorizacionFiscal {get;set;}="";
    public DateTime? VencimientoAutorizacion {get;set;}
    public List<CompraCentroCosto> CentrosCosto {get;set;}=[];
    public int? CompraOrigenId {get;set;}
    public string CompraOrigenHuella {get;set;}="";
    /// <summary>Equivale a responder Sí a "dar por finalizada la Orden de Compra" cuando quedan artículos pendientes.</summary>
    public bool CerrarSaldoOrigen {get;set;}
    public string CompraOrigenTipo {get;set;}=""; // OC o RMC; solo informativo para la pantalla, el servicio lo relee
    public string Deposito {get;set;}="";
    public int? NotaFacturaId {get;set;}
    public decimal NotaAplicar {get;set;}
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
public sealed record CompraDocumentoConsulta(ComprobanteViewerDto Comprobante,IReadOnlyList<ComprobanteAsientoDto> Asientos);
public sealed record CompraCentroCosto(string Cuenta,string Centro,decimal Porcentaje);
public sealed record CompraBorradorGuardado(Guid Id,int Version,string Nombre,string Json,DateTime Modificado);
public class CompraConsultaFila
{
    public int Id {get;set;}
    public string Tipo {get;set;}="";
    public string Comprobante {get;set;}="";
    public string Cuenta {get;set;}="";
    public string Nombre {get;set;}="";
    public string Unidad {get;set;}="";
    public DateTime Fecha {get;set;}
    public decimal Total {get;set;}
    public bool Anulada {get;set;}
    public bool Web {get;set;}
}

public sealed class CompraArticulo
{
    public string Codigo{get;set;}="";public string Descripcion{get;set;}="";public string Unidad{get;set;}="";
    public decimal Costo{get;set;}public decimal Tasa{get;set;}public bool Exento{get;set;}public bool NoStock{get;set;}public bool Especial{get;set;}
    public string Cuenta{get;set;}="";public decimal Stock{get;set;}public string Huella{get;set;}="";
}
