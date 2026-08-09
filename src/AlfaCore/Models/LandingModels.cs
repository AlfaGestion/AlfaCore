namespace AlfaCore.Models;

/// <summary>
/// Copy de una landing pública de módulo (/landing/{codigo}) — a mano por ahora, no sale del
/// catálogo de dbo.Modulos. Cuando haya un backend de landing pages de verdad (tipo Odoo Website),
/// esto se reemplaza por datos editables; mientras tanto vive acá para poder publicar rápido.
/// </summary>
public sealed class LandingContenido
{
    /// <summary>Código del módulo tal cual está en dbo.Modulos — para el día que esto se cruce con el catálogo real.</summary>
    public string Codigo { get; init; } = string.Empty;
    /// <summary>Identificador de URL (/landing/{slug}) — separado de Codigo porque algunos códigos (ej. "POS - PUNTO DE VENTA") no sirven como link.</summary>
    public string Slug { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string Icono { get; init; } = string.Empty;
    public string Tagline { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string MetaDescripcion { get; init; } = string.Empty;
    public decimal Precio { get; init; }
    public IReadOnlyList<string> Dependencias { get; init; } = [];
    public IReadOnlyList<LandingFeature> Features { get; init; } = [];
    /// <summary>Captura de pantalla del producto para el mockup debajo del hero — ruta bajo wwwroot (ej. "/img/landing/conversaciones/inbox.png"). Null = no se muestra esa sección.</summary>
    public string? ImagenHero { get; init; }
    public IReadOnlyList<LandingTestimonio> Testimonios { get; init; } = [];
}

public sealed record LandingFeature(string Icono, string Titulo, string Texto);

public sealed record LandingTestimonio(string Texto, string Autor, string Empresa);

/// <summary>Estado administrable (activa/suspendida) de una landing — cruza <see cref="LandingContenidoCatalogo"/> con dbo.LandingModulos.</summary>
public sealed class LandingEstadoDto
{
    public string Slug { get; init; } = string.Empty;
    public bool Activo { get; init; } = true;
    public DateTime? ModificadoUtc { get; init; }
    public string? ModificadoPor { get; init; }
}

public static class LandingContenidoCatalogo
{
    public static readonly IReadOnlyList<LandingContenido> Todos =
    [
        new LandingContenido
        {
            Codigo = "CONVERSACIONES",
            Slug = "conversaciones",
            Nombre = "Conversaciones",
            Icono = "bi-chat-dots-fill",
            Tagline = "Todos tus canales, en una sola bandeja.",
            Descripcion = "WhatsApp, Instagram, Facebook y MercadoLibre en un único inbox. Tu equipo atiende todo desde un mismo lugar, sin saltar entre apps ni perder mensajes de clientes.",
            MetaDescripcion = "Bandeja unificada de WhatsApp, Instagram, Facebook y MercadoLibre. Probalo gratis 30 días.",
            Precio = 150,
            ImagenHero = "/img/landing/conversaciones/inbox.png",
            Features =
            [
                new("bi-inboxes", "Bandeja multicanal", "WhatsApp, Instagram, Facebook y MercadoLibre en una sola pantalla."),
                new("bi-people", "Asignación de agentes", "Repartí las conversaciones entre tu equipo y hacé seguimiento de quién atiende qué."),
                new("bi-clock-history", "Historial completo", "Todo lo que hablaste con un cliente, ordenado y accesible en segundos."),
                new("bi-journal-text", "Notas internas", "Dejá contexto para el resto del equipo sin que el cliente lo vea."),
                new("bi-bell", "Notificaciones push", "Enterate al instante cuando llega un mensaje nuevo, aunque no tengas la pestaña abierta."),
            ],
            // Ejemplos ilustrativos — reemplazar por testimonios reales de clientes antes de difundir la landing.
            Testimonios =
            [
                new("Antes perdíamos pedidos porque quedaban perdidos entre WhatsApp Web e Instagram. Ahora todo entra a un solo lugar y nadie se queda sin respuesta.", "Nombre Apellido", "Ejemplo — comercio con venta por redes"),
                new("Repartir las conversaciones entre el equipo era un caos de capturas de pantalla. Con la asignación de agentes cada uno sabe qué le toca.", "Nombre Apellido", "Ejemplo — equipo de atención al cliente"),
                new("Lo que más noto es el historial: cuando un cliente vuelve después de meses, ya sé de qué hablamos la última vez sin preguntarle de nuevo.", "Nombre Apellido", "Ejemplo — servicio técnico"),
            ]
        },
        new LandingContenido
        {
            Codigo = "TICKETS",
            Slug = "tickets",
            Nombre = "Tickets",
            Icono = "bi-ticket-perforated-fill",
            Tagline = "De charla a solución, sin salir del chat.",
            Descripcion = "Convertí cualquier conversación en un ticket con seguimiento y estado, sin duplicar información entre sistemas distintos.",
            MetaDescripcion = "Convertí conversaciones en tickets con seguimiento, sin salir del chat. Probalo gratis 30 días.",
            Precio = 10,
            Dependencias = ["Conversaciones"],
            Features =
            [
                new("bi-cursor", "Un clic y listo", "Creá el ticket directo desde la conversación, sin cargar nada dos veces."),
                new("bi-kanban", "Estados personalizables", "Organizá el trabajo como mejor le sirva a tu equipo."),
                new("bi-clock", "Partes de horas incluidos", "Cada ticket puede generar su propio registro de horas trabajadas."),
                new("bi-graph-up", "Seguimiento de resolución", "Sabé cuánto tarda tu equipo en resolver cada caso."),
            ]
        },
        new LandingContenido
        {
            Codigo = "ALFAKNOWLEDGE",
            Slug = "alfaknowledge",
            Nombre = "AlfaKnowledge",
            Icono = "bi-stars",
            Tagline = "Un asistente con IA que ya sabe de tu negocio.",
            Descripcion = "Cargás tu propia base de conocimiento y la IA sugiere la respuesta ideal para cada conversación. Tu equipo revisa y envía — no escribe de cero.",
            MetaDescripcion = "Asistente con IA que sugiere respuestas usando tu propia base de conocimiento. Probalo gratis 30 días.",
            Precio = 10,
            Dependencias = ["Conversaciones"],
            Features =
            [
                new("bi-magic", "Respuestas sugeridas", "La IA propone la respuesta, tu equipo la aprueba o la ajusta antes de enviarla."),
                new("bi-book", "Base de conocimiento propia", "Cargás tus propios documentos y procedimientos — la IA responde con TU información."),
                new("bi-lightning-charge", "Menos tiempo de respuesta", "Tu equipo deja de escribir lo mismo una y otra vez."),
                new("bi-shield-check", "Siempre supervisado", "Nunca envía nada sola: propone, una persona decide."),
            ]
        },
        new LandingContenido
        {
            Codigo = "PARTES_HORAS",
            Slug = "partes-de-horas",
            Nombre = "Partes de horas",
            Icono = "bi-clock-history",
            Tagline = "Cada hora de trabajo, registrada y facturable.",
            Descripcion = "Registrá y controlá las horas que tu equipo dedica a cada cliente, con reportes listos para facturar a fin de mes.",
            MetaDescripcion = "Registro y control de horas trabajadas por cliente, listo para facturar. Probalo gratis 30 días.",
            Precio = 10,
            Dependencias = ["Conversaciones"],
            Features =
            [
                new("bi-stopwatch", "Registro rápido", "Se genera solo al resolver un ticket, o cargalo manualmente."),
                new("bi-bar-chart", "Reportes por cliente y técnico", "Sabé quién trabajó en qué y cuánto tiempo llevó."),
                new("bi-receipt", "Listo para facturar", "Separá horas facturables de las internas de un vistazo."),
            ]
        },
        new LandingContenido
        {
            Codigo = "AUTOMATIZACIONES",
            Slug = "automatizaciones",
            Nombre = "Automatizaciones",
            Icono = "bi-lightning-charge-fill",
            Tagline = "Nunca más un cliente sin respuesta.",
            Descripcion = "Respuestas automáticas fuera de horario y mensajes de bienvenida, para que ningún mensaje se quede sin una primera respuesta.",
            MetaDescripcion = "Respuestas automáticas fuera de horario y mensajes de bienvenida. Probalo gratis 30 días.",
            Precio = 10,
            Dependencias = ["Conversaciones"],
            Features =
            [
                new("bi-moon-stars", "Fuera de horario", "Un mensaje automático avisa cuándo vas a responder de verdad."),
                new("bi-hand-thumbs-up", "Bienvenida automática", "El primer contacto siempre recibe una respuesta inmediata."),
                new("bi-calendar-week", "Horarios configurables", "Definís días y horas de atención a tu medida."),
            ]
        },
        new LandingContenido
        {
            Codigo = "LOGISTICA",
            Slug = "logistica",
            Nombre = "Carga y control de Viajes",
            Icono = "bi-truck",
            Tagline = "Cada viaje, cada tarifa, bajo control.",
            Descripcion = "Carga y seguimiento de viajes, tarifas de clientes y fleteros, liquidaciones y reportes de rentabilidad en un solo módulo.",
            MetaDescripcion = "Carga de viajes, tarifas, liquidaciones a fleteros y reportes. Probalo gratis 30 días.",
            Precio = 10,
            Features =
            [
                new("bi-signpost-split", "Tarifas por destino y vehículo", "Definí precios por cliente, destino y tipo de vehículo, con ajustes automáticos."),
                new("bi-person-badge", "Liquidación de fleteros", "Calculá lo que le corresponde a cada chofer o fletero sin planillas aparte."),
                new("bi-graph-up-arrow", "Reportes de rentabilidad", "Vas a saber cuánto ganás por cliente, destino o chofer."),
                new("bi-file-earmark-excel", "Exportación a Excel", "Toda la información lista para compartir o analizar."),
            ]
        },
        new LandingContenido
        {
            Codigo = "POS - PUNTO DE VENTA",
            Slug = "punto-de-venta",
            Nombre = "Punto de Venta",
            Icono = "bi-shop",
            Tagline = "Mostrador, salón y delivery, en un mismo sistema.",
            Descripcion = "Punto de venta ágil con caja y control de stock integrados, pensado para mostrador, salón y delivery.",
            MetaDescripcion = "Punto de venta para mostrador, salón y delivery con caja y stock integrados. Probalo gratis 30 días.",
            Precio = 10,
            Features =
            [
                new("bi-cash-coin", "Venta rápida en mostrador", "Cobrá en segundos, con los medios de pago que uses todos los días."),
                new("bi-columns-gap", "Gestión de salón", "Mesas, pedidos y cuentas divididas sin planillas."),
                new("bi-bicycle", "Pedidos delivery", "Organizá los envíos sin depender de otra herramienta."),
                new("bi-box-seam", "Stock en tiempo real", "Cada venta descuenta stock automáticamente."),
            ]
        },
    ];
}
