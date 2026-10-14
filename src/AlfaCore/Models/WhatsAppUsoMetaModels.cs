namespace AlfaCore.Models;

/// <summary>
/// Uso del mes de un número de WhatsApp frente a lo que cobra Meta (no AlfaCore). Desde el
/// 1/10/2026 Meta cobra también los mensajes de servicio (respuestas dentro de las 24 h) a partir
/// del 1.001 de cada mes por número; sin método de pago cargado en Meta, deja de entregarlos.
/// Los conteos salen de CONV_MENSAJES: lo enviado desde AlfaCore (API) y lo enviado desde la app
/// WhatsApp Business del celular (coexistence, Origen = WHATSAPP_BUSINESS_APP, que Meta no cobra).
/// Gratis/Cobrados usan la clasificación que Meta manda en cada aviso de estado ("pricing").
/// </summary>
public sealed class WhatsAppUsoMetaDto
{
    /// <summary>Mensajes de servicio sin cargo por mes y por número (regla de Meta desde 1/10/2026).</summary>
    public const int MensajesServicioGratisPorMes = 1000;

    /// <summary>Id del número (CONV_WHATSAPP_NUMEROS); 0 = conversaciones sin número asignado (número principal).</summary>
    public int IdNumero { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }

    /// <summary>Enviados desde AlfaCore por la API oficial (los que Meta puede cobrar).</summary>
    public int EnviadosDesdeAlfaCore { get; set; }

    /// <summary>Enviados desde la app WhatsApp Business del celular: Meta no los cobra.</summary>
    public int EnviadosDesdeCelular { get; set; }

    /// <summary>Mensajes de servicio (respuestas, no plantillas) que cuentan para los 1.000 gratis.</summary>
    public int MensajesServicio { get; set; }

    /// <summary>Mensajes que Meta informó como cobrados (servicio pasados los 1.000 y plantillas).</summary>
    public int CobradosPorMeta { get; set; }

    /// <summary>Plantillas (marketing, utilidad, autenticación) que Meta informó como cobradas.</summary>
    public int PlantillasCobradas { get; set; }

    public int ServicioGratisRestantes => Math.Max(0, MensajesServicioGratisPorMes - MensajesServicio);
    public int PorcentajeServicioUsado => (int)Math.Min(100, Math.Round(MensajesServicio * 100.0 / MensajesServicioGratisPorMes));
    public bool SuperoGratis => MensajesServicio > MensajesServicioGratisPorMes;
    public bool CercaDelLimite => MensajesServicio >= MensajesServicioGratisPorMes * 8 / 10;
}

public enum WhatsAppMetodoPagoMetaEstado
{
    /// <summary>No se pudo consultar a Meta (número sin WABA conocida, credencial no disponible, error).</summary>
    Desconocido,
    Cargado,
    NoCargado
}

public sealed class WhatsAppMetodoPagoMetaDto
{
    public WhatsAppMetodoPagoMetaEstado Estado { get; set; }

    /// <summary>Panel de WhatsApp Manager de la cuenta del cliente, donde se carga el método de pago.</summary>
    public string UrlCargarPago { get; set; } = string.Empty;
}
