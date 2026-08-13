namespace AlfaCore.Models;

public sealed class ConversacionWhatsAppConfigDto
{
    public string VerifyToken { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessAccountId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v22.0";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/api/conversaciones/whatsapp/webhook";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfiguredForSend =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(PhoneNumberId);

    public bool IsConfiguredForVerify =>
        !string.IsNullOrWhiteSpace(VerifyToken);

    public bool IsReadyForMetaSetup =>
        IsConfiguredForSend &&
        IsConfiguredForVerify &&
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string GetWebhookUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(WebhookPath) ? "/api/conversaciones/whatsapp/webhook" : WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }
}

public sealed class ConversacionInstagramConfigDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string InstagramAccountId { get; set; } = string.Empty;
    public string FacebookPageId { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v22.0";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/api/conversaciones/instagram/webhook";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfiguredForSend =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(InstagramAccountId);

    public bool IsConfiguredForVerify =>
        !string.IsNullOrWhiteSpace(VerifyToken);

    public bool IsReadyForMetaSetup =>
        IsConfiguredForSend &&
        IsConfiguredForVerify &&
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string GetWebhookUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(WebhookPath) ? "/api/conversaciones/instagram/webhook" : WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }
}

public sealed class ConversacionFacebookConfigDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string PageId { get; set; } = string.Empty;
    public string PageUsername { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v22.0";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/api/conversaciones/facebook/webhook";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfiguredForSend =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(PageId);

    public bool IsConfiguredForVerify =>
        !string.IsNullOrWhiteSpace(VerifyToken);

    public bool IsReadyForMetaSetup =>
        IsConfiguredForSend &&
        IsConfiguredForVerify &&
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string GetWebhookUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(WebhookPath) ? "/api/conversaciones/facebook/webhook" : WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }
}

public sealed class ConversacionMercadoLibreConfigDto
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string SellerId { get; set; } = string.Empty;
    public string SiteId { get; set; } = "MLA";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/api/conversaciones/mercadolibre/webhook";
    public string OAuthCallbackPath { get; set; } = "/api/conversaciones/mercadolibre/oauth/callback";
    public string ApiBaseUrl { get; set; } = "https://api.mercadolibre.com";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfiguredForAuth =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public bool IsConfiguredForApi =>
        IsConfiguredForAuth &&
        !string.IsNullOrWhiteSpace(AccessToken);

    public bool IsReadyForWebhook =>
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string GetWebhookUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(WebhookPath) ? "/api/conversaciones/mercadolibre/webhook" : WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }

    public string GetOAuthCallbackUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(OAuthCallbackPath) ? "/api/conversaciones/mercadolibre/oauth/callback" : OAuthCallbackPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }
}

public sealed class ConversacionAlfaKnowledgeConfigDto
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    /// <summary>
    /// Instrucciones curadas de cómo debe actuar/responder el asistente (persona, tono, reglas,
    /// qué escalar). Se anteponen a la sugerencia de respuesta. Editable por el admin.
    /// </summary>
    public string Instrucciones { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey);

    public string FullChatUrl
        => string.IsNullOrWhiteSpace(BaseUrl)
            ? string.Empty
            : BaseUrl.Trim().TrimEnd('/') + "/";
}

/// <summary>
/// Automatizaciones "Nivel 0" (sin IA, sin aprobación de operador): respuesta fija cuando llega
/// un mensaje de WhatsApp fuera del horario de atención configurado.
/// </summary>
public sealed class ConversacionAutomatizacionesConfigDto
{
    public bool Activo { get; set; }
    public string MensajeFueraHorario { get; set; } = string.Empty;
    public bool Lunes { get; set; } = true;
    public bool Martes { get; set; } = true;
    public bool Miercoles { get; set; } = true;
    public bool Jueves { get; set; } = true;
    public bool Viernes { get; set; } = true;
    public bool Sabado { get; set; }
    public bool Domingo { get; set; }
    public string HoraDesde { get; set; } = "09:00";
    public string HoraHasta { get; set; } = "18:00";

    // Bienvenida al primer contacto (Nivel 0, regla fija). Se manda una sola vez, cuando llega el
    // primer mensaje de una conversación nueva de WhatsApp y todavía no le respondimos nada.
    public bool BienvenidaActivo { get; set; }
    public string BienvenidaMensaje { get; set; } = "¡Hola! Gracias por escribirnos. En un momento te atendemos. 🙂";

    // Nivel 3 - bot autónomo con guardarraíles. Apagado por defecto: responde solo cuando está
    // explícitamente activo y con contexto suficiente; si no, deja la conversación para un humano.
    public bool BotActivo { get; set; }
    /// <summary>El bot solo responde conversaciones sin técnico asignado (si un humano la tomó, calla).</summary>
    public bool BotSoloSinAsignar { get; set; } = true;
    /// <summary>Palabras (separadas por coma) en el mensaje del cliente que fuerzan escalar a un humano.</summary>
    public string BotPalabrasEscalado { get; set; } = "humano, persona, reclamo, operador, hablar con alguien";
    /// <summary>Tope de respuestas automáticas por conversación antes de escalar (evita loops).</summary>
    public int BotMaxRespuestas { get; set; } = 5;

    // Auto-cierre por inactividad. Cierra conversaciones donde estamos esperando al cliente
    // (último mensaje nuestro) y no responde. Manda un aviso previo antes de cerrar. Apagado por
    // defecto. Aplica a todos los canales; el aviso/cierre solo se envían si el canal lo permite.
    public bool AutoCierreActivo { get; set; }
    /// <summary>Horas de inactividad (esperando al cliente) para mandar el aviso previo. 23 deja
    /// la ventana de 24h de WhatsApp todavía abierta para poder escribirle.</summary>
    public int AutoCierreHorasAviso { get; set; } = 23;
    /// <summary>Horas tras el aviso, sin respuesta del cliente, para cerrar la conversación.</summary>
    public int AutoCierreHorasCierre { get; set; } = 24;
    public string AutoCierreMensajeAviso { get; set; } = "¿Seguís ahí? Si no tenemos novedades, vamos a cerrar esta conversación. Escribinos cuando quieras. 🙂";
    /// <summary>Mensaje al cerrar (opcional; vacío = cierra sin mensaje).</summary>
    public string AutoCierreMensajeCierre { get; set; } = "Cerramos esta conversación por inactividad. Cuando lo necesites, escribinos de nuevo. ¡Gracias!";

    // Seguimientos / SLA. Cuando el cliente está esperando respuesta (último mensaje entrante) hace
    // más de X horas, deja una nota interna de recordatorio; si sigue sin respuesta hace Y horas y
    // estaba asignada, la desasigna para que otro la tome. No manda nada al cliente (todos los canales).
    public bool SlaActivo { get; set; }
    public int SlaHorasRecordatorio { get; set; } = 2;
    public int SlaHorasReasignar { get; set; } = 4;

    // Asistente IA (cerebro del bot). Se persiste en dbo.CONV_ASISTENTE (texto largo).
    /// <summary>Comportamiento / persona / instrucciones del asistente (system prompt).</summary>
    public string AsistenteComportamiento { get; set; } = string.Empty;

    /// <summary>Información del negocio pegada: fuente de verdad que se inyecta en cada respuesta.</summary>
    public string AsistenteInformacion { get; set; } = string.Empty;

    /// <summary>SOLO_INFO | GENERAL | GENERAL_AVISA (qué hace cuando la info no alcanza).</summary>
    public string AsistentePolitica { get; set; } = "GENERAL_AVISA";

    /// <summary>Fuera de horario, que atienda el asistente IA (en vez del mensaje fijo).</summary>
    public bool AsistenteFueraHorario { get; set; }

    /// <summary>Palabras que marcan urgencia (se atienden en cualquier horario): sube prioridad y avisa al operador.</summary>
    public string AsistenteUrgenciaPalabras { get; set; } = string.Empty;

    /// <summary>Usar AlfaKnowledge (documentos/archivos) como fuente adicional para responder.</summary>
    public bool AsistenteUsaKnowledge { get; set; } = true;

    /// <summary>
    /// Tono/persona que usa la IA al redactar el resumen mensual por cliente (Informes). Solo define
    /// el estilo: el formato (viñetas, tope de palabras, no inventar, sin saludo/firma) queda fijo en
    /// código. Si está vacío, se usa <see cref="DefaultInformeInstrucciones"/>.
    /// </summary>
    public string InformeInstrucciones { get; set; } = DefaultInformeInstrucciones;

    public const string DefaultInformeInstrucciones =
        "Sos parte del equipo de soporte técnico de software y le escribís al cliente un resumen de la " +
        "atención que le dimos durante el mes. Tono profesional y cercano, en español rioplatense: " +
        "tratalo de \"vos\" y escribí en primera persona del plural (trabajamos, resolvimos, quedamos). " +
        "Mostrate servicial y dejá la puerta abierta a seguir ayudándolo.";

    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfigured => Activo && !string.IsNullOrWhiteSpace(MensajeFueraHorario);
    public bool BotIsConfigured => BotActivo;
}

/// <summary>
/// Qué código de dbo.TA_CLASIFICACIONES cuenta como prioridad 1 (más importante), 2 o 3 para
/// ordenar la cola de espera. Un cliente sin clasificación, o con una que no está en ninguna de
/// las 3, cae en "el resto" (prioridad 4, la más baja). Se guarda en dbo.TA_CONFIGURACION con las
/// claves CLASIFICA1/2/3 — las mismas, sin prefijo, que ya usa Desktop, no son propias de este
/// módulo.
/// </summary>
public sealed record ConversacionClasificacionOptionDto(string Codigo, string Descripcion);

public sealed class ConversacionPrioridadConfigDto
{
    public string Clasifica1 { get; set; } = string.Empty;
    public string Clasifica2 { get; set; } = string.Empty;
    public string Clasifica3 { get; set; } = string.Empty;
}

/// <summary>
/// Un número de WhatsApp Business (Phone Number ID de Meta) con los usuarios del sistema
/// vinculados a él. Una conversación queda fijada a uno de estos números apenas se crea (por la
/// ventana de 24 h de Meta, que es por par número-cliente) y no vuelve a cambiar. Un usuario
/// normal solo ve/responde conversaciones de los números donde está vinculado; un administrador
/// de Conversaciones (dbo.CONV_ADMINISTRADORES) ve y responde por cualquiera.
/// </summary>
public sealed class ConversacionWhatsAppNumeroDto
{
    public int IdNumero { get; set; }
    public string PhoneNumberId { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public List<string> Usuarios { get; set; } = [];
}

public sealed class ConversacionAlfaKnowledgeConnectionTestResultDto
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string Service { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string DataSource { get; set; } = string.Empty;
    public string KnowledgeBase { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}


/// <summary>
/// Regla del motor de palabras clave de Conversaciones. Vigila los mensajes entrantes
/// de cualquier canal y, si coincide una palabra clave, aplica sus acciones (responder,
/// derivar a un técnico, fijar prioridad). Ver CONV_REGLAS.
/// </summary>
public sealed class ConversacionReglaDto
{
    public int IdRegla { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
    public int Orden { get; set; } = 100;

    /// <summary>CONTIENE | IGUAL | EMPIEZA.</summary>
    public string TipoCoincidencia { get; set; } = "CONTIENE";

    /// <summary>Palabras/frases separadas por coma o salto de línea (coincide con cualquiera).</summary>
    public string Palabras { get; set; } = string.Empty;

    /// <summary>'' = todos los canales; o WHATSAPP/INSTAGRAM/FACEBOOK/MERCADOLIBRE.</summary>
    public string Canal { get; set; } = string.Empty;

    /// <summary>Solo actúa si la conversación no tiene un técnico asignado.</summary>
    public bool SoloSinAsignar { get; set; } = true;

    public string RespuestaTexto { get; set; } = string.Empty;

    /// <summary>IdTecnico a asignar (derivar); vacío = no deriva.</summary>
    public string AsignarTecnico { get; set; } = string.Empty;

    /// <summary>BAJA | MEDIA | ALTA | URGENTE; vacío = no cambia la prioridad.</summary>
    public string Prioridad { get; set; } = string.Empty;

    /// <summary>Si coincide, detiene el resto de reglas y las auto-respuestas (bienvenida/bot).</summary>
    public bool Detener { get; set; } = true;

    /// <summary>SIEMPRE | DENTRO | FUERA del horario configurado en Automatizaciones.</summary>
    public string Horario { get; set; } = "SIEMPRE";

    /// <summary>La regla solo dispara en el primer mensaje entrante de la conversación.</summary>
    public bool SoloPrimerContacto { get; set; }
}
