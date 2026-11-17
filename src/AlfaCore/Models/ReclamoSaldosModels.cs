namespace AlfaCore.Models;

/// <summary>Filtros del listado de clientes con saldo a reclamar.</summary>
public sealed class ReclamoFiltro
{
    public string Texto { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal SaldoMinimo { get; set; } = 1m;
    /// <summary>Solo clientes con al menos un comprobante vencido (más de N días de atraso). 0 = todos con saldo.</summary>
    public int DiasAtrasoMinimo { get; set; }
    public bool IncluirBajas { get; set; }
}

public sealed class ReclamoCliente
{
    public string Cuenta { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Saldo neto de la cuenta corriente (VE_SALDOSCTA): ya descuenta notas de crédito y anticipos.</summary>
    public decimal Saldo { get; set; }
    /// <summary>Suma de comprobantes pendientes con vencimiento anterior a hoy.</summary>
    public decimal Vencido { get; set; }
    public decimal SumaComprobantes { get; set; }
    public int CantidadComprobantes { get; set; }
    public int CantidadVencidos { get; set; }
    public DateTime? FechaMasAntigua { get; set; }
    /// <summary>Días de atraso del comprobante vencido más antiguo (0 si nada venció).</summary>
    public int DiasAtraso { get; set; }
    public string Unidades { get; set; } = string.Empty;
    public string Vendedor { get; set; } = string.Empty;
    public string Emails { get; set; } = string.Empty;
    public string Whatsapp { get; set; } = string.Empty;
    public DateTime? UltimoReclamo { get; set; }
    public string UltimoCanal { get; set; } = string.Empty;

    public bool TieneEmail => !string.IsNullOrWhiteSpace(Emails);
    public bool TieneWhatsapp => !string.IsNullOrWhiteSpace(Whatsapp);
    /// <summary>true si el saldo de la cuenta no coincide con la suma de comprobantes (hay NC/anticipos sin aplicar).</summary>
    public bool SaldoDifiere => Math.Abs(Saldo - SumaComprobantes) >= 0.01m;
}

public sealed class ReclamoComprobante
{
    public string Unidad { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? Vencimiento { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public decimal Pago { get; set; }
    public decimal Saldo { get; set; }
    public int DiasVencido { get; set; }
}

/// <summary>Comprobante cuyo saldo pendiente es solo una diferencia mínima (centavos): se excluye del reclamo y se lista para ajustarlo.</summary>
public sealed class ReclamoDiferenciaMinima
{
    public string Cuenta { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public decimal Pago { get; set; }
    public decimal Saldo { get; set; }
}

public sealed class ReclamoTramo
{
    public int Numero { get; set; }
    public string Asunto { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}

/// <summary>Configuración editable del reclamo (TA_CONFIGURACION, grupo COBRANZAS, claves RECLAMO-SALDOS-*).</summary>
public sealed class ReclamoConfig
{
    public List<ReclamoTramo> Tramos { get; set; } = [new() { Numero = 1 }, new() { Numero = 2 }, new() { Numero = 3 }];
    public int DiasHasta1 { get; set; } = 15;
    public int DiasHasta2 { get; set; } = 45;
    public string FormaPago { get; set; } = string.Empty;
    public decimal SaldoMinimo { get; set; } = 1m;
    /// <summary>Comprobantes con saldo menor o igual a este importe no se reclaman ni suman días de atraso (diferencias de centavos).</summary>
    public decimal Tolerancia { get; set; } = 1m;
    public int DiasEntreEnvios { get; set; } = 7;
    public long IdPlantillaWhatsApp { get; set; }
    /// <summary>Días de vigencia del enlace de <c>&lt;PORTAL&gt;</c> (acceso al Portal Cliente sin usuario ni clave).</summary>
    public int PortalDias { get; set; } = 15;
}

public enum ReclamoCanal { Email, WhatsApp, Ambos }

public sealed class ReclamoEnvioSolicitud
{
    public List<string> Cuentas { get; set; } = [];
    public ReclamoCanal Canal { get; set; } = ReclamoCanal.Email;
    /// <summary>Arma todo (destinatarios, asunto, cuerpo) pero no envía nada ni escribe la bitácora.</summary>
    public bool SoloSimular { get; set; } = true;
    /// <summary>Reenviar aunque el cliente haya recibido un reclamo dentro de los días mínimos configurados.</summary>
    public bool IgnorarEnviosRecientes { get; set; }
    /// <summary>Si viene, todos los mails van a esta dirección (envío de prueba): el cliente real no recibe nada.</summary>
    public string? MailPrueba { get; set; }
    /// <summary>Si viene, el email va a esta(s) dirección(es) en vez de a las del cliente (envío real, queda en la bitácora). Se usa desde "Ver mensaje".</summary>
    public string? MailDestino { get; set; }
    /// <summary>Si viene, todos los WhatsApp van a este teléfono (envío de prueba).</summary>
    public string? TelefonoPrueba { get; set; }
    /// <summary>Fuerza un tramo (1-3) en vez de elegirlo por días de atraso. 0 = automático.</summary>
    public int TramoForzado { get; set; }
}

public sealed class ReclamoVistaPrevia
{
    public string Cuenta { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Tramo { get; set; }
    public string Destinatarios { get; set; } = string.Empty;
    public string Asunto { get; set; } = string.Empty;
    public string CuerpoHtml { get; set; } = string.Empty;
    public string Whatsapp { get; set; } = string.Empty;
    public string WhatsappTexto { get; set; } = string.Empty;
    public List<string> Advertencias { get; set; } = [];
}

public sealed class ReclamoEnvioResultado
{
    public string Cuenta { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    /// <summary>Enviado | Simulado | Omitido | Error</summary>
    public string Estado { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}

/// <summary>Lo que se envió: cuerpo tal como salió y la foto de los comprobantes reclamados.</summary>
public sealed class ReclamoEnvioDetalle
{
    public string Cuerpo { get; set; } = string.Empty;
    public List<ReclamoComprobante> Comprobantes { get; set; } = [];
}

public sealed class ReclamoPlantillaWhatsApp
{
    public long IdPlantilla { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Cuerpo { get; set; } = string.Empty;
    public int Variables { get; set; }
}

public sealed class ReclamoUnidad
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

/// <summary>Filtros del historial de reclamos enviados (bitácora ALFACORE_RECLAMO_SALDO_ENVIO).</summary>
public sealed class ReclamoEnvioFiltro
{
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    /// <summary>Busca en cliente, código, destino y asunto.</summary>
    public string Texto { get; set; } = string.Empty;
    /// <summary>EMAIL | WHATSAPP | vacío = todos.</summary>
    public string Canal { get; set; } = string.Empty;
    /// <summary>ENVIADO | ERROR | vacío = todos.</summary>
    public string Estado { get; set; } = string.Empty;
    public bool IncluirPruebas { get; set; } = true;
}

public sealed class ReclamoEnvioFila
{
    public long IdEnvio { get; set; }
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public bool EsPrueba { get; set; }
    public int Tramo { get; set; }
    public decimal Saldo { get; set; }
    public decimal Vencido { get; set; }
    public int Comprobantes { get; set; }
    public string Asunto { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    /// <summary>Todos los envíos de una misma acción de "Enviar" comparten lote.</summary>
    public Guid? IdLote { get; set; }
    /// <summary>WhatsApp: conversación y mensaje en Conversaciones (null en email y en envíos anteriores a la trazabilidad).</summary>
    public long? IdConversacion { get; set; }
    public long? IdMensaje { get; set; }

    // ---- Resultado del reclamo (se calcula al consultar, no se guarda) ----
    /// <summary>WhatsApp: ENVIADO_META | ENTREGADO | LEIDO | ERROR_ENVIO (vacío si no aplica o no se encuentra).</summary>
    public string EstadoMensaje { get; set; } = string.Empty;
    /// <summary>Primera respuesta del cliente en la conversación después del envío (WhatsApp).</summary>
    public DateTime? Respondio { get; set; }
    /// <summary>Cuándo el cliente abrió el enlace al portal del mail (si el mensaje lo llevaba).</summary>
    public DateTime? AbrioPortal { get; set; }
    /// <summary>Cobranzas registradas para el cliente desde el envío.</summary>
    public decimal CobradoDesdeEnvio { get; set; }
    /// <summary>Saldo actual de la cuenta (para compararlo con el saldo reclamado).</summary>
    public decimal? SaldoActual { get; set; }
}

/// <summary>Un hecho en la línea de tiempo de un cliente: reclamo, respuesta, acceso al portal o cobranza.</summary>
public sealed class ReclamoEvento
{
    public DateTime Fecha { get; set; }
    /// <summary>RECLAMO | RESPUESTA | MENSAJE | PORTAL | COBRANZA</summary>
    public string Tipo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public decimal? Importe { get; set; }
    public bool Error { get; set; }
    public long? IdConversacion { get; set; }
}
