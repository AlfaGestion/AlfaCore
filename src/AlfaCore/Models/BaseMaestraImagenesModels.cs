namespace AlfaCore.Models;

public class BaseMaestraImagenOrigenDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string DescripcionArticulo { get; set; } = string.Empty;
    public string CodigoBarra { get; set; } = string.Empty;
    public string RutaImagen { get; set; } = string.Empty;
    public bool TieneImagenActual { get; set; }
}

public sealed class BaseMaestraImagenArticuloDto : BaseMaestraImagenOrigenDto
{
    public string CodigoConsulta { get; set; } = string.Empty;
    public string Fuente { get; set; } = string.Empty;
    public string Confianza { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public bool ImagenEncontrada { get; set; }
    public bool PermiteReemplazo { get; set; }
    public bool Seleccionado { get; set; }
    public bool PuedeSeleccionarse { get; set; } = true;
    public string Estado { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class BaseMaestraImagenResultadoDto
{
    public int Total { get; set; }
    public int ImagenesEncontradas { get; set; }
    public int Seleccionadas { get; set; }
    public int Asignadas { get; set; }
    public int OmitidasPorExistir { get; set; }
    public int SinImagen { get; set; }
    public int ConError { get; set; }
}
