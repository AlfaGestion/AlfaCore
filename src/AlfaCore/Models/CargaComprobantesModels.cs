namespace AlfaCore.Models;

public sealed class CargaComprobantesFiltro
{
    public string TipoOrigen { get; set; } = "NP";
    public string Busqueda { get; set; } = string.Empty;
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string Vendedor { get; set; } = string.Empty;
    public string Zona { get; set; } = string.Empty;
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Deposito { get; set; } = string.Empty;
    /// <summary>Solo para el origen "FA" (facturación automática): fecha a facturar; vacía = hoy.</summary>
    public DateTime? FechaFacturacion { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>
/// Mail de comprobantes: mismas claves que v_FacturacionAutomatica.vbp (TP_V_factAutomatica_txtAsunto,
/// TP_V_factAutomatica_txtmensaje, TP_V_factAutomatica_chkemailauto), así el texto se comparte con el VB6.
/// </summary>
public sealed class ComprobanteMailConfig
{
    public string Asunto { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public bool EnviarAlEmitir { get; set; }
    /// <summary>Si tiene un mail, todos los envíos van ahí (para probar sin escribirle a los clientes).</summary>
    public string MailPrueba { get; set; } = string.Empty;
    public static readonly IReadOnlyList<(string Marca, string Descripcion)> Marcas =
    [
        ("<PERIODO>", "Mes y año del comprobante (ej. 11-2026)"),
        ("<NOMBRE>", "Razón social del cliente"),
        ("<FORMADEPAGO>", "Forma de pago de la unidad de negocio (configuración FormaDePago_UN_x)"),
        ("<DETALLEFACTURA>", "Fecha, número y total del comprobante")
    ];
}

/// <summary>Mail listo para revisar antes de enviar (visor de comprobantes).</summary>
public sealed class ComprobanteMailBorrador
{
    public string Tc { get; set; } = string.Empty;
    public string IdComprobante { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Para { get; set; } = string.Empty;
    public string Asunto { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? MailPrueba { get; set; }
    public string? UltimoEnvio { get; set; }
}

public sealed class ComprobanteMailResultado
{
    public bool Enviado { get; set; }
    public string Para { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}

/// <summary>Filtros del visor de comprobantes de ventas.</summary>
public sealed class ComprobantesVisorFiltro
{
    public string Tc { get; set; } = string.Empty;
    public string Busqueda { get; set; } = string.Empty;
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string UnidadNegocio { get; set; } = string.Empty;
    public bool IncluirAnulados { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    /// <summary>Columna de orden: fecha, tipo, numero, cliente, importe, unidad, usuario. Vacío = fecha.</summary>
    public string Orden { get; set; } = string.Empty;
    public bool Descendente { get; set; } = true;
}

/// <summary>Un comprobante de ventas en el visor (V_MV_CPTE).</summary>
public sealed class ComprobantesVisorFila
{
    public int Id { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string TcDescripcion { get; set; } = string.Empty;
    public string IdComprobante { get; set; } = string.Empty;
    public int IdComplemento { get; set; }
    public DateTime? Fecha { get; set; }
    public string Cuenta { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public bool Anulada { get; set; }
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string? Cae { get; set; }
    /// <summary>Último envío por mail (V_MA_EMAILMOV): "02/11/2026 18:30 · a@b.com · OK".</summary>
    public string? UltimoMail { get; set; }
    /// <summary>Formato de pantalla: 0010-00000519 C.</summary>
    public string Numero => IdComprobante.Length == 13 ? $"{IdComprobante[..4]}-{IdComprobante.Substring(4, 8)} {IdComprobante[12..]}" : IdComprobante;
}

public sealed class ComprobantesVisorResultado
{
    public IReadOnlyList<ComprobantesVisorFila> Items { get; set; } = [];
    public int Total { get; set; }
    public decimal ImporteTotal { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    /// <summary>Tipos de comprobante de ventas (V_TA_CPTE) para el filtro.</summary>
    public IReadOnlyList<CargaComprobantesOpcionFiltro> Tipos { get; set; } = [];
}

/// <summary>Resultado de validar o emitir la factura automática de un cliente.</summary>
public sealed class CargaComprobantesFacturacionAutomaticaResultado
{
    public string Cuenta { get; set; } = string.Empty;
    /// <summary>VALIDADA (vista previa, nada grabado), GRABADA, PENDIENTE (requiere recuperar) o ERROR.</summary>
    public string Estado { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public int Talonario { get; set; }
    public string? Tc { get; set; }
    public string? Comprobante { get; set; }
    public string? Cae { get; set; }
    public decimal Total { get; set; }
    public decimal Iva { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public Guid? Operacion { get; set; }
    public IReadOnlyList<CargaComprobantesFacturacionAutomaticaLinea> Lineas { get; set; } = [];
    /// <summary>Envío por mail de la factura emitida: null (no se pidió), ENVIADO o ERROR.</summary>
    public string? MailEstado { get; set; }
    public string? MailMensaje { get; set; }
}

public sealed record CargaComprobantesFacturacionAutomaticaLinea(string Texto, decimal Importe);

/// <summary>Cliente en condiciones de facturación automática a una fecha (consulta, como el VB6).</summary>
public sealed class CargaComprobantesFacturacionAutomaticaFila
{
    /// <summary>Código interno (MA_CUENTAS.CODIGO): abre la ficha del cliente.</summary>
    public string Cuenta { get; set; } = string.Empty;
    /// <summary>Código visible del cliente.</summary>
    public string CtaCodigo { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    /// <summary>Concepto tal como está en la ficha (puede tener la marca &lt;PERIODO&gt;).</summary>
    public string Concepto { get; set; } = string.Empty;
    /// <summary>Concepto con &lt;PERIODO&gt; reemplazado por el mes/año a facturar.</summary>
    public string ConceptoFactura { get; set; } = string.Empty;
    public string Tc { get; set; } = string.Empty;
    public string Periodicidad { get; set; } = string.Empty;
    /// <summary>Última facturación automática; null = nunca facturó.</summary>
    public DateTime? FechaUltimoMovimiento { get; set; }
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Vendedor { get; set; } = string.Empty;
    public string VendedorNombre { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public int Conceptos { get; set; }
}

public sealed class CargaComprobantesContexto
{
    /// <summary>HOMOLOGACION (base de prueba), PRODUCCION (emisión real) o DESHABILITADA (base real sin habilitar).</summary>
    public string ModoEmision { get; set; } = "HOMOLOGACION";
    public string ModoEmisionMotivo { get; set; } = string.Empty;
    public bool EmisionReal => ModoEmision == "PRODUCCION";
    public bool PuedeEmitir => ModoEmision != "DESHABILITADA";
    public Guid SesionId { get; set; }
    public string Sistema { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string UnidadNegocio { get; set; } = string.Empty;
    public bool AdminCentral { get; set; }
    public bool PuedeEditarPrecio { get; set; }
    public bool PuedeEditarDescuento { get; set; }
    public bool PuedeCambiarLista { get; set; }
    public bool PuedeCambiarClase { get; set; }
    public IReadOnlyList<CargaComprobantesOpcionFiltro> ListasPrecio { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> ClasesPrecio { get; set; } = [];
    public bool EntregaParcial { get; set; }
    public bool DescargaStockRemitos { get; set; }
    public int Decimales { get; set; } = 2;
    public bool PreciosFinales { get; set; }
    public bool PreciosFinalesNeto { get; set; }
    public bool RevalorizaRemitos { get; set; }
    public bool AjustaRedondeoTotal { get; set; }
    public bool UsaFacturaProforma { get; set; }
    public bool RobaIvaProforma { get; set; }
    public bool ProformaSinRegistracion { get; set; }
    public bool ArtExentoANoGrav { get; set; }
    public decimal AlicuotaGeneral { get; set; }
    public int PuntoVentaElectronico { get; set; }
    public IReadOnlyList<CargaComprobantesTalonario> Talonarios { get; set; } = [];
    public IReadOnlyList<CargaComprobantesTalonario> TalonariosDirecta { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> Vendedores { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> Zonas { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> UnidadesNegocio { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> Depositos { get; set; } = [];
}

public sealed class CargaComprobantesOpcionFiltro
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    /// <summary>Dato asociado opcional (ej. unidad de negocio de un depósito).</summary>
    public string Grupo { get; set; } = string.Empty;
}

public sealed class CargaComprobantesElectronicaEstado
{
    public string Ambiente { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public long UltimaFacturaA { get; set; }
    public long UltimaFacturaB { get; set; }
    public long UltimaFacturaC { get; set; }
}

public sealed class CargaComprobantesElectronicaPrueba
{
    public Guid Operacion { get; set; }
    public int OrigenId { get; set; }
    public string Estado { get; set; } = "";
    public int PuntoVenta { get; set; }
    public int TipoComprobante { get; set; }
    public long Numero { get; set; }
    public decimal Importe { get; set; }
    public string? Cae { get; set; }
    public DateTime? Vencimiento { get; set; }
}

public sealed class CargaComprobantesTalonario
{
    public string Tc { get; set; } = "FC";
    public int Talonario { get; set; }
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Letras { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    // Punto de venta por letra (V_TA_CPTE.x_SUC_DEFAULT), para mostrar el talonario como en Alfa.
    public int PuntoA { get; set; }
    public int PuntoB { get; set; }
    public int PuntoC { get; set; }
    public int PuntoX { get; set; }

    /// <summary>Puntos de venta del talonario por letra, agrupando las letras con el mismo punto: "A/B 0002 · C 0010".</summary>
    public string Puntos
    {
        get
        {
            var porLetra = new List<(string Letra, int Punto)>();
            foreach (var letra in (Letras ?? string.Empty).ToUpperInvariant().Distinct())
            {
                var punto = letra switch { 'A' => PuntoA, 'B' => PuntoB, 'C' => PuntoC, 'X' => PuntoX, _ => 0 };
                if (punto > 0) porLetra.Add((letra.ToString(), punto));
            }
            return string.Join(" · ", porLetra.GroupBy(x => x.Punto)
                .Select(g => $"{string.Join("/", g.Select(x => x.Letra))} {g.Key:0000}"));
        }
    }
}

public sealed class CargaComprobantesOrigen
{
    public int Id { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Comprobante { get; set; } = string.Empty;
    public DateTime? Fecha { get; set; }
    public string Cuenta { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Deposito { get; set; } = string.Empty;
    public string Lista { get; set; } = string.Empty;
    public string Moneda { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public bool TieneReservas { get; set; }
    public bool TienePreparacion { get; set; }
    public bool TieneStock { get; set; }
    public bool TieneDevolucion { get; set; }
    public bool TieneOtrosDetalles { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public bool DatosCalculoDisponibles { get; set; }
    public bool ExentoIvaArticulos { get; set; }
    public bool TieneAjustesGlobales { get; set; }
    public bool TienePercepciones { get; set; }
    public IReadOnlyList<CargaComprobantesLineaOrigen> Lineas { get; set; } = [];
}

public sealed class CargaComprobantesLineaOrigen
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PrecioReferenciaNeto { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int ReferenciaOrigenId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal CantidadReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal EquivalenciaReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CostoReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal BultosReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal KilosReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal MetrosCubicosReferencia { get; set; }
    /// <summary>Renglón de otros conceptos (sin artículo): se graba en V_MV_CPTE_OBSERV, sin stock.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool EsConcepto { get; set; }
    public int Id { get; set; }
    public int Secuencia { get; set; }
    public string Articulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public string Deposito { get; set; } = string.Empty;
    public decimal CantidadOriginal { get; set; }
    public decimal CoeficienteUnidad { get; set; }
    public decimal Pendiente { get; set; }
    public decimal Precio { get; set; }
    public decimal PrecioSinIva { get; set; }
    public decimal AlicuotaIva { get; set; }
    public decimal AlicuotaIvaRecargo { get; set; }
    public decimal ImpuestoInternoUnitario { get; set; }
    public decimal ItcUnitario { get; set; }
    public bool Exento { get; set; }
    public decimal Descuento { get; set; }
    public decimal TotalOriginal { get; set; }
    public int RenglonesArticulo { get; set; }
}

public sealed class CargaComprobantesCantidad
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OrigenId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PrecioUnitarioNeto { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Descuento { get; set; }
    public int LineaId { get; set; }
    public decimal Cantidad { get; set; }
}

public sealed class CargaComprobantesSimulacion
{
    public decimal ImporteReferencia { get; set; }
    public int LineasSeleccionadas { get; set; }
    public string EfectoStock { get; set; } = string.Empty;
    public IReadOnlyList<string> Avisos { get; set; } = [];
    public CargaComprobantesLiquidacion? Liquidacion { get; set; }
    public IReadOnlyList<string> PendientesCalculo { get; set; } = [];
}

/// <summary>Liquidación de artículos con precios históricos; no autoriza emisión.</summary>
public sealed class CargaComprobantesLiquidacion
{
    public decimal NetoGravado { get; set; }
    public decimal NetoNoGravado { get; set; }
    public decimal Iva { get; set; }
    public decimal ImpuestosInternos { get; set; }
    public decimal Total { get; set; }
    public IReadOnlyList<CargaComprobantesLiquidacionLinea> Lineas { get; set; } = [];
    public IReadOnlyList<CargaComprobantesLiquidacionIva> Alicuotas { get; set; } = [];
}

public sealed class CargaComprobantesLiquidacionLinea
{
    public int LineaId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Neto { get; set; }
    public decimal Iva { get; set; }
    public decimal ImpuestosInternos { get; set; }
    public decimal Total { get; set; }
    public bool Exento { get; set; }
    public decimal Alicuota { get; set; }
}

public sealed class CargaComprobantesLiquidacionIva
{
    public decimal Alicuota { get; set; }
    public decimal Neto { get; set; }
    public decimal Iva { get; set; }
}
