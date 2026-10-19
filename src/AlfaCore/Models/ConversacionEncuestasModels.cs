namespace AlfaCore.Models;

/// <summary>Encuesta de satisfacción al cerrar una conversación de WhatsApp (2026-10-19).</summary>
public static class ConversacionEncuestaTextos
{
    public const string PreguntaPorDefecto = "¡Gracias por escribirnos! ¿Cómo calificarías nuestra atención?";
    public const string AgradecimientoBueno = "¡Gracias por tu calificación! 🙌 Si querés dejarnos un comentario, escribilo acá.";
    public const string AgradecimientoMejorable = "Gracias por avisarnos. ¿Nos contás qué podríamos mejorar? Escribilo acá.";
    public const string AgradecimientoComentario = "¡Gracias por tu comentario! Lo tenemos en cuenta. 🙂";

    /// <summary>Prefijo del id de los botones: "csat:{IdEncuesta}:{Calificacion}".</summary>
    public const string PrefijoBoton = "csat:";
}

/// <summary>Calificación del cliente: 3 = Muy bien, 2 = Regular, 1 = Mal.</summary>
public static class ConversacionEncuestaCalificaciones
{
    public const int Mal = 1;
    public const int Regular = 2;
    public const int MuyBien = 3;

    public static string Etiqueta(int? calificacion) => calificacion switch
    {
        MuyBien => "Muy bien",
        Regular => "Regular",
        Mal => "Mal",
        _ => "Sin respuesta"
    };

    public static string Emoji(int? calificacion) => calificacion switch
    {
        MuyBien => "😀",
        Regular => "😐",
        Mal => "😞",
        _ => "—"
    };

    /// <summary>Botones de WhatsApp (máximo 3, título de hasta 20 caracteres).</summary>
    public static IReadOnlyList<(int Valor, string Titulo)> Botones { get; } =
    [
        (MuyBien, "😀 Muy bien"),
        (Regular, "😐 Regular"),
        (Mal, "😞 Mal")
    ];
}

public static class ConversacionEncuestaEstados
{
    public const string Enviando = "ENVIANDO";
    public const string Enviada = "ENVIADA";
    public const string Respondida = "RESPONDIDA";
    public const string Error = "ERROR";
}

/// <summary>Botón de respuesta rápida de WhatsApp (mensaje interactivo).</summary>
public sealed record ConversacionBotonRespuesta(string Id, string Titulo);
