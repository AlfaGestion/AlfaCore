namespace AlfaCore.Models;

/// <summary>Qué tendría que haber hecho el asistente ante una consulta (2026-10-15).</summary>
public static class AsistenteCorreccionAcciones
{
    /// <summary>Responder con el texto corregido.</summary>
    public const string Responder = "RESPONDER";

    /// <summary>No responder por su cuenta: pasárselo a una persona del equipo.</summary>
    public const string Derivar = "DERIVAR";

    /// <summary>Responder normalmente pero sin abrir un ticket automático.</summary>
    public const string SinTicket = "SIN_TICKET";

    public static string Normalizar(string? accion)
    {
        var a = (accion ?? string.Empty).Trim().ToUpperInvariant();
        return a is Derivar or SinTicket ? a : Responder;
    }

    public static string Descripcion(string? accion) => Normalizar(accion) switch
    {
        Derivar => "Pasarlo a una persona",
        SinTicket => "No abrir ticket",
        _ => "Responder esto"
    };
}

public static class AsistenteCorreccionEstados
{
    public const string Pendiente = "PENDIENTE";
    public const string Aprobada = "APROBADA";
    public const string Rechazada = "RECHAZADA";
}

public sealed class AsistenteCorreccionDto
{
    public int IdCorreccion { get; set; }
    public long? IdConversacion { get; set; }
    public long? IdMensaje { get; set; }
    public string PreguntaCliente { get; set; } = string.Empty;
    public string RespuestaAsistente { get; set; } = string.Empty;
    public string Accion { get; set; } = AsistenteCorreccionAcciones.Responder;
    public string RespuestaCorrecta { get; set; } = string.Empty;
    public string Estado { get; set; } = AsistenteCorreccionEstados.Pendiente;
    public string UsuarioPropone { get; set; } = string.Empty;
    public DateTime FechaPropuesta { get; set; }
    public string UsuarioRevisa { get; set; } = string.Empty;
    public DateTime? FechaRevision { get; set; }
    public int VecesUsada { get; set; }
    public DateTime? UltimoUso { get; set; }
}

/// <summary>Lo que el equipo propone desde una respuesta del asistente en la conversación.</summary>
public sealed class AsistenteCorreccionPropuesta
{
    public long? IdConversacion { get; set; }
    public long? IdMensaje { get; set; }
    public string PreguntaCliente { get; set; } = string.Empty;
    public string RespuestaAsistente { get; set; } = string.Empty;
    public string Accion { get; set; } = AsistenteCorreccionAcciones.Responder;
    public string RespuestaCorrecta { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
}

/// <summary>Corrección aprobada que se parece a la consulta actual, con qué tanto se parece (0 a 1).</summary>
public sealed record AsistenteCorreccionCoincidencia(AsistenteCorreccionDto Correccion, double Parecido);
