namespace AlfaCore.Services;

/// <summary>Una pantalla (o sección) de configuración del sistema.</summary>
/// <param name="Ruta">Ruta interna sin base; puede incluir query para abrir una sección puntual.</param>
/// <param name="ModulosRelacionados">Módulos (primer segmento de ruta) en los que esta entrada se ofrece como "Este módulo".</param>
public sealed record ConfiguracionEntrada(
    string Clave,
    string Titulo,
    string Descripcion,
    string Area,
    string Ruta,
    string Icono,
    IReadOnlyList<string> ModulosRelacionados,
    string PalabrasClave = "",
    bool Principal = false)
{
    /// <summary>Módulo dueño de la pantalla (primer segmento de la ruta), usado para permisos.</summary>
    public string Modulo => ConfiguracionCatalogo.PrimerSegmento(Ruta);

    public string RutaBase => Ruta.Split('?', '#')[0].TrimEnd('/');
}

/// <summary>
/// Lista única de configuraciones (2026-10-05). La usan el menú del engranaje de la barra superior y
/// la página /configuracion con su buscador. Cada módulo sigue siendo dueño de su pantalla; acá solo
/// se registra dónde está y cómo encontrarla. Para sumar una configuración nueva, agregarla acá.
/// </summary>
public static class ConfiguracionCatalogo
{
    public const string AreaEmpresa = "Empresa y sistema";
    public const string AreaUsuarios = "Usuarios y seguridad";
    public const string AreaComercial = "Comercial";
    public const string AreaConversaciones = "Conversaciones";
    public const string AreaAgenda = "Agenda";
    public const string AreaIntegraciones = "Integraciones";

    public static IReadOnlyList<string> Areas { get; } =
        [AreaEmpresa, AreaUsuarios, AreaComercial, AreaConversaciones, AreaAgenda, AreaIntegraciones];

    public static IReadOnlyList<ConfiguracionEntrada> Entradas { get; } =
    [
        new("empresa", "Datos de la empresa", "Razón social, CUIT, domicilio y datos fiscales.", AreaEmpresa,
            "/configuracion-general", "bi-building", ["configuracion-general"], "razon social cuit domicilio fiscal empresa", Principal: true),
        new("logo", "Logo", "Logo que se usa en comprobantes, catálogos y portal.", AreaEmpresa,
            "/configuracion-general/logo", "bi-image", ["configuracion-general"], "imagen marca"),
        new("email", "Correo saliente", "Cuenta SMTP desde la que el sistema envía emails.", AreaEmpresa,
            "/configuracion-general/email", "bi-envelope", ["configuracion-general"], "smtp mail correo envio"),
        new("portal-web", "Portal web", "Portal de autogestión de clientes: acceso, contenidos y apariencia.", AreaEmpresa,
            "/configuracion-web-portal", "bi-globe", ["configuracion-web-portal"], "autogestion portal clientes cuenta corriente", Principal: true),
        new("documentos", "Documentos", "Diseño de comprobantes y documentos impresos.", AreaEmpresa,
            "/documentos/configuracion", "bi-file-earmark-text", ["documentos"], "comprobantes impresion pdf plantilla", Principal: true),

        new("usuarios", "Usuarios", "Altas, bajas y datos de los usuarios del sistema.", AreaUsuarios,
            "/usuarios", "bi-people", ["usuarios"], "usuario login acceso", Principal: true),
        new("autorizacion-tareas", "Permisos por tarea", "Qué puede hacer cada usuario en cada módulo.", AreaUsuarios,
            "/seguridad/autorizacion-tareas", "bi-shield-lock", ["seguridad", "usuarios"], "permisos tareas roles autorizacion", Principal: true),

        new("ventas", "Ventas", "Parámetros de facturación y ventas.", AreaComercial,
            "/configuracion-general/ventas", "bi-receipt", ["ventas"], "facturacion comprobantes precios", Principal: true),
        new("puntos-venta", "Puntos de venta", "Puntos de venta, cajas y comprobantes habilitados.", AreaComercial,
            "/ventas/puntos-venta", "bi-shop", ["ventas"], "pos mostrador caja arca afip"),
        new("articulos", "Artículos", "Parámetros de artículos y stock.", AreaComercial,
            "/configuracion-general/articulos", "bi-box-seam", ["articulos", "stock"], "productos stock codigos", Principal: true),
        new("cotizaciones", "Cotizaciones", "Formato, textos y condiciones de las cotizaciones.", AreaComercial,
            "/cotizaciones/configuracion", "bi-file-earmark-ruled", ["cotizaciones", "crm"], "presupuesto cotizacion", Principal: true),
        new("catalogo", "Catálogo", "Catálogos públicos, links y visibilidad de precios.", AreaComercial,
            "/catalogo/configuracion", "bi-grid-3x3-gap", ["catalogo", "catalogos"], "catalogo publico precios link", Principal: true),

        new("conversaciones", "Conversaciones", "Canales, asistente IA, automatizaciones y accesos.", AreaConversaciones,
            "/conversaciones/configuracion", "bi-chat-left-text", ["conversaciones"], "whatsapp instagram facebook mercado libre", Principal: true),
        new("conv-whatsapp", "WhatsApp", "Conectar números de WhatsApp Business o la API de Meta.", AreaConversaciones,
            "/conversaciones/configuracion?seccion=canales&subseccion=whatsapp-business", "bi-whatsapp", ["conversaciones"], "qr numero meta cloud api"),
        new("conv-asistente", "Asistente IA", "Personalidad, información del negocio y herramientas del bot.", AreaConversaciones,
            "/conversaciones/configuracion?seccion=asistente-ia&subseccion=general", "bi-robot", ["conversaciones"], "bot ia openai chatgpt respuestas automaticas"),
        new("conv-informacion", "Información del negocio para el asistente", "Bloques por tema y archivos (PDF, Word, manuales) que el asistente usa para responder.", AreaConversaciones,
            "/conversaciones/configuracion?seccion=asistente-ia&subseccion=informacion", "bi-journal-text", ["conversaciones"], "conocimiento faq preguntas frecuentes archivos pdf documentos manuales subir"),
        new("conv-horario", "Horario de atención", "Días y horario, y el mensaje fuera de horario.", AreaConversaciones,
            "/conversaciones/configuracion?seccion=automatizacion&subseccion=horario", "bi-clock", ["conversaciones"], "horario fuera de horario mensaje automatico"),
        new("conv-reglas", "Reglas por palabras clave", "Respuestas, derivaciones y prioridad sin IA.", AreaConversaciones,
            "/conversaciones/configuracion?seccion=automatizacion&subseccion=reglas", "bi-signpost-split", ["conversaciones"], "palabras clave derivar"),
        new("conv-administradores", "Administradores de conversaciones", "Quién ve y responde por todos los números.", AreaConversaciones,
            "/conversaciones/configuracion?seccion=operacion-accesos&subseccion=administradores", "bi-person-gear", ["conversaciones"], "accesos supervisores"),

        new("reuniones-publicas", "Reuniones públicas", "Tipos de reunión y capacitación que los clientes pueden reservar.", AreaAgenda,
            "/calendario/reuniones", "bi-calendar-plus", ["calendario"], "capacitacion reserva turno agenda", Principal: true),

        new("interfaces", "Interfaces", "Conexiones con sistemas externos.", AreaIntegraciones,
            "/interfaces/configuracion", "bi-plug", ["interfaces"], "integracion api externa", Principal: true)
    ];

    /// <summary>
    /// Entradas que el usuario puede abrir: su pantalla está entre las rutas de su menú o su módulo
    /// entre sus módulos habilitados. Si el menú no informó nada (sin datos o fallo), se muestran todas
    /// y cada pantalla aplica su propio control de acceso.
    /// </summary>
    public static IReadOnlyList<ConfiguracionEntrada> Visibles(
        IEnumerable<string> modulosHabilitados,
        IEnumerable<string> rutasHabilitadas)
    {
        var modulos = new HashSet<string>(modulosHabilitados.Select(PrimerSegmento).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        var rutas = new HashSet<string>(rutasHabilitadas.Select(r => NormalizarRuta(r)).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        if (modulos.Count == 0 && rutas.Count == 0)
            return Entradas;

        return Entradas
            .Where(e => rutas.Contains(NormalizarRuta(e.RutaBase)) || modulos.Contains(e.Modulo))
            .ToList();
    }

    /// <summary>Entradas para "Este módulo", la principal primero.</summary>
    public static IReadOnlyList<ConfiguracionEntrada> DelModulo(IEnumerable<ConfiguracionEntrada> visibles, string? moduloActual)
    {
        var modulo = (moduloActual ?? string.Empty).Trim();
        if (modulo.Length == 0)
            return [];

        return visibles
            .Where(e => e.ModulosRelacionados.Contains(modulo, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Principal)
            .ToList();
    }

    /// <summary>Búsqueda sin acentos ni mayúsculas sobre título, descripción, área y palabras clave.</summary>
    public static IReadOnlyList<ConfiguracionEntrada> Buscar(IEnumerable<ConfiguracionEntrada> entradas, string? texto)
    {
        var terminos = Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terminos.Length == 0)
            return entradas.ToList();

        return entradas
            .Select(e => new { Entrada = e, Texto = Normalizar($"{e.Titulo} {e.Descripcion} {e.Area} {e.PalabrasClave}"), Titulo = Normalizar(e.Titulo) })
            .Where(x => terminos.All(t => x.Texto.Contains(t, StringComparison.Ordinal)))
            .OrderByDescending(x => terminos.Count(t => x.Titulo.Contains(t, StringComparison.Ordinal)))
            .ThenByDescending(x => x.Entrada.Principal)
            .Select(x => x.Entrada)
            .ToList();
    }

    public static string PrimerSegmento(string? ruta)
        => NormalizarRuta(ruta).Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private static string NormalizarRuta(string? ruta)
        => (ruta ?? string.Empty).Split('?', '#')[0].Trim().Trim('/').ToLowerInvariant();

    internal static string Normalizar(string? texto)
    {
        var descompuesto = (texto ?? string.Empty).ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
