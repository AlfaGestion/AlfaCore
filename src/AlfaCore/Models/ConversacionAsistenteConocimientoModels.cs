namespace AlfaCore.Models;

/// <summary>Bloque temático de "Información del negocio" del asistente.</summary>
public sealed class AsistenteBloqueDto
{
    public int IdBloque { get; set; }
    public string Categoria { get; set; } = AsistenteBloqueCategorias.General;
    public string Titulo { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public int Orden { get; set; } = 100;
}

public static class AsistenteBloqueCategorias
{
    public const string General = "GENERAL";
    public const string Empresa = "EMPRESA";
    public const string Horarios = "HORARIOS";
    public const string Envios = "ENVIOS";
    public const string Pagos = "PAGOS";
    public const string Garantias = "GARANTIAS";
    public const string Faq = "FAQ";
    public const string Productos = "PRODUCTOS";
    public const string NoResponder = "NO_RESPONDER";

    public static IReadOnlyList<(string Clave, string Nombre)> Todas { get; } =
    [
        (Empresa, "Empresa y contacto"),
        (Horarios, "Horarios y sucursales"),
        (Envios, "Envíos y entregas"),
        (Pagos, "Medios de pago"),
        (Garantias, "Garantías y devoluciones"),
        (Faq, "Preguntas frecuentes"),
        (Productos, "Productos y servicios"),
        (NoResponder, "Lo que el asistente no debe responder"),
        (General, "Otros")
    ];

    public static string Nombre(string? clave)
        => Todas.FirstOrDefault(c => string.Equals(c.Clave, clave, StringComparison.OrdinalIgnoreCase)).Nombre ?? "Otros";
}

/// <summary>Ficha de un archivo de conocimiento subido al vector store de la base.</summary>
public sealed class AsistenteArchivoDto
{
    public int IdArchivo { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public long Tamano { get; set; }
    public string Estado { get; set; } = AsistenteArchivoEstados.Procesando;
    public string UltimoError { get; set; } = string.Empty;
    public DateTime FechaAlta { get; set; }
    public string UsuarioAlta { get; set; } = string.Empty;
    public string OpenAiFileId { get; set; } = string.Empty;
}

public static class AsistenteArchivoEstados
{
    public const string Procesando = "PROCESANDO";
    public const string Listo = "LISTO";
    public const string Error = "ERROR";
}

/// <summary>Fragmento encontrado en los archivos para una consulta.</summary>
public sealed record AsistenteFragmentoDto(string Archivo, double Puntaje, string Texto);
