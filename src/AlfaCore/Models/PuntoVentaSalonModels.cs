namespace AlfaCore.Models;

/// <summary>Estados de una mesa abierta (POS_PEDIDO con MODALIDAD = SALON).</summary>
public static class PuntoVentaSalonMesaEstadoKeys
{
    public const string Libre = "LIBRE";       // solo de pantalla: la mesa no tiene pedido abierto
    public const string Abierta = "ABIERTA";
    public const string Cuenta = "CUENTA";     // pidio la cuenta / precuenta impresa
    public const string Cobrada = PuntoVentaPedidoEstadoKeys.Entregado;
    public const string Anulada = PuntoVentaPedidoEstadoKeys.Anulado;

    public static readonly string[] Abiertos = [Abierta, Cuenta];
}

/// <summary>Estado de cocina de cada linea (POS_PEDIDO_DET.ESTADO).</summary>
public static class PuntoVentaSalonLineaEstadoKeys
{
    public const string Pendiente = "PENDIENTE";
    public const string Enviado = "ENVIADO";
    public const string EnPreparacion = "EN_PREPARACION";
    public const string Listo = "LISTO";
    public const string Servido = "SERVIDO";
    public const string Anulado = "ANULADO";

    public static readonly string[] All = [Pendiente, Enviado, EnPreparacion, Listo, Servido, Anulado];
}

public static class PuntoVentaSalonDestinoKeys
{
    public const string Cocina = "COCINA";
    public const string Barra = "BARRA";
    public const string Barra2 = "BARRA2";
}

public static class PuntoVentaSalonOrigenKeys
{
    public const string Mozo = "MOZO";
    public const string Qr = "QR";
}

public sealed class PuntoVentaSalonDto
{
    public int IdPuntoVenta { get; set; }
    public string NombrePuntoVenta { get; set; } = string.Empty;
    public List<PuntoVentaSalonSectorDto> Sectores { get; set; } = [];
    public IReadOnlyList<PuntoVentaCatalogoOpcionDto> Mozos { get; set; } = [];
    public List<PuntoVentaSalonLlamadoDto> Llamados { get; set; } = [];
    public string QrModo { get; set; } = PuntoVentaQrModoKeys.No;
    public bool RequiereMozo { get; set; }
    public string ArticuloCubiertos { get; set; } = string.Empty;
}

public sealed class PuntoVentaSalonSectorDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public int Orden { get; set; }
    public List<PuntoVentaSalonMesaDto> Mesas { get; set; } = [];
}

public sealed class PuntoVentaSalonMesaDto
{
    public int Id { get; set; }
    public int IdSector { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int? Capacidad { get; set; }
    public double? PosX { get; set; }
    public double? PosY { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string Formato { get; set; } = PuntoVentaMesaFormatoKeys.Cuadrada;
    public string Icono { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;

    // Pedido abierto (si lo hay)
    public int? IdPedido { get; set; }
    public string Estado { get; set; } = PuntoVentaSalonMesaEstadoKeys.Libre;
    public DateTime? FechaHoraApertura { get; set; }
    public string Mozo { get; set; } = string.Empty;
    public string MozoNombre { get; set; } = string.Empty;
    public int? Cubiertos { get; set; }
    public decimal Total { get; set; }
    public int LineasPendientes { get; set; }   // cargadas y sin enviar a cocina/barra
    public int LineasEnCocina { get; set; }     // enviadas y todavia no servidas
    public int LineasListas { get; set; }
    public int LineasQrPorConfirmar { get; set; }
    public bool LlamaMozo { get; set; }
    public bool PideCuenta { get; set; }
    public DateTime? UltimoEnvio { get; set; }

    public bool EstaLibre => IdPedido is null;
}

public sealed class PuntoVentaSalonLineaDto
{
    public int Id { get; set; }
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal Precio { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string Estado { get; set; } = PuntoVentaSalonLineaEstadoKeys.Pendiente;
    public string Destino { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime? FechaHoraAlta { get; set; }
    public DateTime? FechaHoraEnvio { get; set; }
    public string MotivoAnulacion { get; set; } = string.Empty;
    public decimal Subtotal => Math.Round(Cantidad * Precio, 2);
}

public sealed class PuntoVentaSalonMesaDetalleDto
{
    public int IdPedido { get; set; }
    public int IdPuntoVenta { get; set; }
    public int IdMesa { get; set; }
    public string NombreMesa { get; set; } = string.Empty;
    public string NombreSector { get; set; } = string.Empty;
    public string Estado { get; set; } = PuntoVentaSalonMesaEstadoKeys.Abierta;
    public DateTime FechaHoraApertura { get; set; }
    public string Mozo { get; set; } = string.Empty;
    public string MozoNombre { get; set; } = string.Empty;
    public int? Cubiertos { get; set; }
    public string Pin { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public List<PuntoVentaSalonLineaDto> Lineas { get; set; } = [];
    public decimal Total => Lineas.Where(l => l.Estado != PuntoVentaSalonLineaEstadoKeys.Anulado).Sum(l => l.Subtotal);
}

public sealed class PuntoVentaSalonAbrirMesaRequest
{
    public int IdMesa { get; set; }
    public string Mozo { get; set; } = string.Empty;
    public int? Cubiertos { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
}

public sealed class PuntoVentaSalonAbrirMesaResult
{
    public int IdPedido { get; set; }
    /// <summary>true si la mesa ya estaba abierta (otro mozo la abrio antes).</summary>
    public bool YaEstabaAbierta { get; set; }
}

// ====================================================================== etapa 2: cocina / comandas

public sealed class PuntoVentaCocinaComandaDto
{
    public int IdComanda { get; set; }
    public int IdPedido { get; set; }
    public string Destino { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public string NombreMesa { get; set; } = string.Empty;
    public string NombreSector { get; set; } = string.Empty;
    public string Mozo { get; set; } = string.Empty;
    public int? Cubiertos { get; set; }
    public bool Impresa { get; set; }
    public List<PuntoVentaCocinaLineaDto> Lineas { get; set; } = [];

    public bool TodoListo => Lineas.Count > 0 && Lineas.All(l => l.Estado is PuntoVentaSalonLineaEstadoKeys.Listo or PuntoVentaSalonLineaEstadoKeys.Servido);
    public bool EnMarcha => Lineas.Any(l => l.Estado == PuntoVentaSalonLineaEstadoKeys.EnPreparacion);
}

public sealed class PuntoVentaCocinaLineaDto
{
    public int Id { get; set; }
    public int IdComanda { get; set; }
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string Estado { get; set; } = PuntoVentaSalonLineaEstadoKeys.Enviado;
    public string Origen { get; set; } = string.Empty;
}

public sealed class PuntoVentaAgotadoDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
}

// ====================================================================== etapa 3: carta QR

public static class PuntoVentaQrModoKeys
{
    public const string No = "NO";
    public const string Carta = "CARTA";     // solo ver la carta
    public const string Pedido = "PEDIDO";   // ver y pedir

    public static readonly string[] All = [No, Carta, Pedido];
}

public static class PuntoVentaLlamadoTipoKeys
{
    public const string Mozo = "MOZO";
    public const string Cuenta = "CUENTA";
}

public sealed class PuntoVentaCartaConfigDto
{
    public int IdPuntoVenta { get; set; }
    public string QrModo { get; set; } = PuntoVentaQrModoKeys.Pedido;
    public bool AutoAcepta { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    /// <summary>Solo lectura: rubros del punto de venta que forman la carta (vacio = todos).</summary>
    public List<string> Rubros { get; set; } = [];
    /// <summary>Solo lectura: lista de precios del punto de venta (vacio = maestro de articulos).</summary>
    public string Lista { get; set; } = string.Empty;
}

public sealed class PuntoVentaCartaDto
{
    public string NombrePuntoVenta { get; set; } = string.Empty;
    public string QrModo { get; set; } = PuntoVentaQrModoKeys.Pedido;
    public string Mensaje { get; set; } = string.Empty;
    public List<PuntoVentaCartaFamiliaDto> Familias { get; set; } = [];
}

public sealed class PuntoVentaCartaFamiliaDto
{
    public string IdFamilia { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public List<PuntoVentaCartaArticuloDto> Articulos { get; set; } = [];
}

public sealed class PuntoVentaCartaArticuloDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public string ImagenUrl { get; set; } = string.Empty;
}

public sealed class PuntoVentaMesaPublicaDto
{
    public int IdMesa { get; set; }
    public string NombreMesa { get; set; } = string.Empty;
    public string NombreSector { get; set; } = string.Empty;
    public int IdPuntoVenta { get; set; }
    public string NombrePuntoVenta { get; set; } = string.Empty;
    public string QrModo { get; set; } = PuntoVentaQrModoKeys.No;
    public bool AutoAcepta { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    /// <summary>Pedido abierto de la mesa (ABIERTA o CUENTA); null = mesa libre.</summary>
    public int? IdPedidoAbierto { get; set; }
}

public sealed class PuntoVentaMesaSesionDto
{
    public int IdPedido { get; set; }
    public string Pin { get; set; } = string.Empty;
}

public sealed class PuntoVentaMesaEstadoPublicoDto
{
    public int IdPedido { get; set; }
    public string Estado { get; set; } = string.Empty;
    public List<PuntoVentaSalonLineaDto> Lineas { get; set; } = [];
    public bool LlamadoMozoPendiente { get; set; }
    public bool LlamadoMozoAtendido { get; set; }
    public bool CuentaPedida { get; set; }
    public decimal Total => Lineas.Where(l => l.Estado != PuntoVentaSalonLineaEstadoKeys.Anulado).Sum(l => l.Subtotal);
}

public sealed class PuntoVentaCartaItemRequest
{
    public string IdArticulo { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class PuntoVentaSalonLlamadoDto
{
    public int Id { get; set; }
    public int IdMesa { get; set; }
    public int? IdPedido { get; set; }
    public string NombreMesa { get; set; } = string.Empty;
    public string Tipo { get; set; } = PuntoVentaLlamadoTipoKeys.Mozo;
    public DateTime FechaHora { get; set; }
}
