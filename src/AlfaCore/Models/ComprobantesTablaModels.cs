namespace AlfaCore.Models;

/// <summary>Numeración de una letra de un talonario (columnas X_NRO_DESDE, X_NRO_HASTA, X_ULTIMO_NRO, VenceX, ObservacionesX, CpteDGIX de V_TA_CPTE).</summary>
public sealed class ComprobanteLetra
{
    public string Letra { get; set; } = string.Empty;
    public int Desde { get; set; }
    public int Hasta { get; set; }
    /// <summary>X_ULTIMO_NRO: en el legacy se muestra como «Próximo» (el número que se va a usar).</summary>
    public int Proximo { get; set; }
    public DateTime? Vence { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string CpteAfip { get; set; } = string.Empty;
}

/// <summary>Una fila de V_TA_CPTE: un comprobante (CODIGO) con uno de sus talonarios.</summary>
public sealed class ComprobanteTalonario
{
    public string Codigo { get; set; } = string.Empty;
    public int Talonario { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string Sistema { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public string Deposito { get; set; } = string.Empty;
    public bool Externo { get; set; }
    public bool ModificaNumeracion { get; set; }
    public bool PideVencimiento { get; set; }
    /// <summary>Letras habilitadas; la primera es la letra por defecto cuando hay más de una.</summary>
    public string Letras { get; set; } = string.Empty;
    /// <summary>Stock: E entrada, S salida, vacío ninguno.</summary>
    public string EntradaSalida { get; set; } = string.Empty;
    /// <summary>Cuenta: D debe, H haber, vacío ninguno.</summary>
    public string DebeHaber { get; set; } = string.Empty;
    public string? Signo { get; set; }
    public List<ComprobanteLetra> Numeracion { get; set; } = [];

    public ComprobanteLetra Letra(string letra) => Numeracion.First(n => n.Letra == letra);
}

/// <summary>Talonario listo para editar, con lo que la pantalla puede y no puede cambiar.</summary>
public sealed class ComprobanteEdicion
{
    public ComprobanteTalonario Datos { get; set; } = new();
    public bool EsNuevo { get; set; }
    /// <summary>Código ya existente (otro talonario): el sistema sale de ese código y no se elige.</summary>
    public bool CodigoExistente { get; set; }
    public int OtrosTalonarios { get; set; }
    public bool TieneMovimientos { get; set; }
    public bool SistemaEditable { get; set; }
    public bool EntradaSalidaEditable { get; set; }
    public bool DebeHaberEditable { get; set; }
    /// <summary>Por qué Debe/Haber o Entrada/Salida no se pueden cambiar (vacío si se puede).</summary>
    public string MotivoAtributos { get; set; } = string.Empty;
    public int AnchoCpteAfip { get; set; } = 2;
}

public sealed record ComprobantesOpcion(string Codigo, string Descripcion);

public sealed class ComprobantesTablaCatalogo
{
    public IReadOnlyList<ComprobantesOpcion> Unidades { get; init; } = [];
    public IReadOnlyList<ComprobantesOpcion> Depositos { get; init; } = [];
}
