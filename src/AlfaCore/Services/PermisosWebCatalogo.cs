namespace AlfaCore.Services;

/// <summary>Dónde se guarda un permiso de acción.</summary>
public enum PermisoWebAlmacen
{
    /// <summary>dbo.ALFACORE_TAREAS_WEB, como cualquier opción del menú.</summary>
    Tareas,

    /// <summary>dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS: no cuenta como restricción del menú.</summary>
    Explicitos,

    /// <summary>dbo.CONV_ADMINISTRADORES: quién ve y responde por todos los números (la usa la bandeja).</summary>
    ConversacionesVerTodos
}

/// <summary>
/// Permiso que no es una pantalla del menú sino una parte de un módulo (por ejemplo "Configurar
/// Conversaciones"). Se muestra en Autorización de tareas y en Perfiles debajo de su módulo.
/// </summary>
/// <param name="Explicito">
/// true = solo lo tiene quien lo tenga tildado: no se da por "sin permisos cargados = ve todo" y tampoco
/// convierte al usuario en "restringido" (no le esconde el resto del menú).
/// </param>
public sealed record PermisoWebAccion(
    string Clave,
    string PadreClave,
    string Nombre,
    string Descripcion,
    bool Explicito,
    PermisoWebAlmacen Almacen);

/// <summary>
/// Catálogo único de permisos de acción (2026-11-18). Las filas de dbo.ALFACORE_MENU_WEB las crea el
/// script 2026-11-18-001; acá se define qué significa cada una. Para sumar uno nuevo: agregarlo acá y
/// en un script de actualización (fila en ALFACORE_MENU_WEB sin RutaWeb, con PadreClave del módulo).
/// </summary>
public static class PermisosWebCatalogo
{
    public const string Conversaciones = "D010185-WEB-CONVERSACIONES";
    public const string ConversacionesPlantillas = "D010185-WEB-CONV-PLANTILLAS";
    public const string ConversacionesConfigurar = "D010185-WEB-CONV-CONFIG";
    public const string ConversacionesVerTodos = "D010185-WEB-CONV-TODOS";
    public const string ConversacionesRespuestas = "D010185-WEB-CONV-RESPUESTAS";

    /// <summary>Seguridad &gt; Perfiles de permisos.</summary>
    public const string PerfilesMenu = "D015-WEB-PERFILES";

    /// <summary>Seguridad &gt; Accesos a Conversaciones (quién ve todos los números y quién atiende cada número).</summary>
    public const string AccesosConversacionesMenu = "D015-WEB-ACCESOS-CONV";

    public static IReadOnlyList<PermisoWebAccion> Acciones { get; } =
    [
        new(ConversacionesPlantillas, Conversaciones, "Plantillas de WhatsApp",
            "Crear, editar y mandar a aprobar plantillas de WhatsApp.",
            Explicito: false, PermisoWebAlmacen.Tareas),
        new(ConversacionesRespuestas, Conversaciones, "Respuestas rápidas",
            "Crear y editar las respuestas rápidas del chat (se usan escribiendo ::), sin poder cambiar el resto de la configuración.",
            Explicito: false, PermisoWebAlmacen.Tareas),
        new(ConversacionesConfigurar, Conversaciones, "Configurar Conversaciones",
            "Canales, números, asistente IA y reglas. Se da solo si se tilda (o si el usuario es administrador del sistema).",
            Explicito: true, PermisoWebAlmacen.Explicitos),
        new(ConversacionesVerTodos, Conversaciones, "Ver todos los números",
            "Ver y responder las conversaciones de todos los números de WhatsApp. Se da solo si se tilda.",
            Explicito: true, PermisoWebAlmacen.ConversacionesVerTodos)
    ];

    private static readonly Dictionary<string, PermisoWebAccion> PorClave =
        Acciones.ToDictionary(a => a.Clave, StringComparer.OrdinalIgnoreCase);

    public static PermisoWebAccion? Buscar(string? clave)
        => clave is not null && PorClave.TryGetValue(clave.Trim(), out var accion) ? accion : null;

    public static bool EsAccion(string? clave) => Buscar(clave) is not null;

    public static bool EsExplicito(string? clave) => Buscar(clave)?.Explicito == true;

    /// <summary>Dónde se guarda la clave: las que no son acciones del catálogo van a ALFACORE_TAREAS_WEB.</summary>
    public static PermisoWebAlmacen AlmacenDe(string? clave) => Buscar(clave)?.Almacen ?? PermisoWebAlmacen.Tareas;
}
