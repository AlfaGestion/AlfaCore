namespace AlfaCore.Models;

/// <summary>
/// Definición tipada de una variable de plantilla de WhatsApp seleccionable desde
/// "Insertar variable" en el editor de plantillas (BODY únicamente).
/// </summary>
/// <param name="Key">
/// Key estable persistida en <c>dbo.CONV_PLANTILLAS_VARIABLES.VariableKey</c>. Nunca cambia una vez
/// publicada: es lo que vincula una posición <c>{{N}}</c> del cuerpo con su significado.
/// </param>
/// <param name="Label">Etiqueta corta mostrada en el selector y debajo del cuerpo.</param>
/// <param name="Description">Descripción visible en el selector de variables.</param>
/// <param name="Group">Agrupador visual del selector (Contacto, Cobranza, Tareas, Guardia).</param>
/// <param name="RequiredContext">
/// Texto secundario que explica qué contexto hace falta para resolver la variable (ej: "Requiere una
/// tarea asociada"). Null cuando no hace falta contexto adicional más allá de la conversación activa.
/// </param>
/// <param name="CanResolveAutomaticallyInManualSend">
/// true solo si existe HOY un resolver real invocable desde el flujo de envío manual genérico
/// (Conversaciones → enviar plantilla). Tareas y Guardia se catalogan con su contexto pero quedan en
/// false acá: sus resolvers viven dentro de TareasService/CalendarioService, que no se tocan en esta
/// función y no se les construye un puente hacia el envío manual.
/// </param>
public sealed record WhatsAppTemplateVariableDefinition(
    string Key,
    string Label,
    string Description,
    string Group,
    string? RequiredContext,
    bool CanResolveAutomaticallyInManualSend);

/// <summary>
/// Catálogo centralizado y tipado de variables de plantillas de WhatsApp. Única fuente de verdad de
/// las VariableKey válidas para <c>dbo.CONV_PLANTILLAS_VARIABLES</c>; no debe duplicarse en Razor ni
/// en servicios. Solo incluye variables con un resolver real confirmado (nunca variables cosméticas
/// sin fuente de datos, como las de Facturación: no existe ningún resolver real para ellas hoy).
/// </summary>
public static class WhatsAppTemplateVariableCatalog
{
    public const string ContactName = "contact.name";
    public const string CobranzaDetalleDeuda = "cobranza.detalleDeuda";
    public const string PagoFormaPago = "pago.formaPago";
    public const string TareaTitulo = "tarea.titulo";
    public const string TareaTecnicoAsignado = "tarea.tecnicoAsignado";
    public const string TareaAutorAccion = "tarea.autorAccion";
    public const string TareaFechaHoraRegistro = "tarea.fechaHoraRegistro";
    public const string GuardiaTecnico = "guardia.tecnico";
    public const string GuardiaInicio = "guardia.inicio";
    public const string GuardiaFin = "guardia.fin";

    public const string GroupContacto = "Contacto";
    public const string GroupCobranza = "Cobranza";
    public const string GroupTareas = "Tareas";
    public const string GroupGuardia = "Guardia";

    /// <summary>Único componente de plantilla soportado por la lógica de negocio actual.</summary>
    public const string ComponenteBody = "BODY";

    public static IReadOnlyList<WhatsAppTemplateVariableDefinition> All { get; } =
    [
        new(ContactName,
            "Nombre del contacto",
            "Nombre visible del contacto de la conversación (cliente, contacto o, en su defecto, el teléfono).",
            GroupContacto,
            RequiredContext: null,
            CanResolveAutomaticallyInManualSend: true),

        new(CobranzaDetalleDeuda,
            "Detalle de deuda",
            "Resumen de saldo, comprobantes pendientes y atraso, calculado desde la cuenta corriente del cliente.",
            GroupCobranza,
            RequiredContext: "Requiere que el contacto tenga un cliente vinculado con cuenta corriente resoluble.",
            CanResolveAutomaticallyInManualSend: true),

        new(PagoFormaPago,
            "Forma de pago",
            "Datos de transferencia/pago configurados para cobranza (CONV_COBRANZA_FORMA_PAGO).",
            GroupCobranza,
            RequiredContext: null,
            CanResolveAutomaticallyInManualSend: true),

        new(TareaTitulo,
            "Título de la tarea",
            "Título de la tarea asociada al envío.",
            GroupTareas,
            RequiredContext: "Requiere una tarea asociada. Solo se resuelve automáticamente en las notificaciones propias de Tareas.",
            CanResolveAutomaticallyInManualSend: false),

        new(TareaTecnicoAsignado,
            "Técnico asignado",
            "Técnico asignado a la tarea.",
            GroupTareas,
            RequiredContext: "Requiere una tarea asociada. Solo se resuelve automáticamente en las notificaciones propias de Tareas.",
            CanResolveAutomaticallyInManualSend: false),

        new(TareaAutorAccion,
            "Autor de la acción",
            "Usuario que asignó o finalizó la tarea.",
            GroupTareas,
            RequiredContext: "Requiere una tarea asociada. Solo se resuelve automáticamente en las notificaciones propias de Tareas.",
            CanResolveAutomaticallyInManualSend: false),

        new(TareaFechaHoraRegistro,
            "Fecha y hora de registro",
            "Fecha y hora de la asignación o el cierre de la tarea.",
            GroupTareas,
            RequiredContext: "Requiere una tarea asociada. Solo se resuelve automáticamente en las notificaciones propias de Tareas.",
            CanResolveAutomaticallyInManualSend: false),

        new(GuardiaTecnico,
            "Técnico de guardia",
            "Técnico asignado a la guardia del calendario.",
            GroupGuardia,
            RequiredContext: "Disponible cuando el envío tiene contexto de guardia/calendario. Solo se resuelve automáticamente en los recordatorios propios de Calendario.",
            CanResolveAutomaticallyInManualSend: false),

        new(GuardiaInicio,
            "Inicio de guardia",
            "Fecha y hora de inicio de la guardia.",
            GroupGuardia,
            RequiredContext: "Disponible cuando el envío tiene contexto de guardia/calendario. Solo se resuelve automáticamente en los recordatorios propios de Calendario.",
            CanResolveAutomaticallyInManualSend: false),

        new(GuardiaFin,
            "Fin de guardia",
            "Fecha y hora de fin de la guardia.",
            GroupGuardia,
            RequiredContext: "Disponible cuando el envío tiene contexto de guardia/calendario. Solo se resuelve automáticamente en los recordatorios propios de Calendario.",
            CanResolveAutomaticallyInManualSend: false),
    ];

    private static readonly Dictionary<string, WhatsAppTemplateVariableDefinition> ByKey =
        All.ToDictionary(x => x.Key, StringComparer.Ordinal);

    public static WhatsAppTemplateVariableDefinition? Find(string? key)
        => !string.IsNullOrWhiteSpace(key) && ByKey.TryGetValue(key.Trim(), out var definition) ? definition : null;

    public static bool IsValidKey(string? key) => Find(key) is not null;

    public static IReadOnlyList<IGrouping<string, WhatsAppTemplateVariableDefinition>> GroupedForSelector()
        => All.GroupBy(x => x.Group).ToList();
}
