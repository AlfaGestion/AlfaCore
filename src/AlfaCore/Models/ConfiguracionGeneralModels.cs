namespace AlfaCore.Models;

/// <summary>
/// Datos de la empresa y logo/estilo de impresión, equivalente web de FrmAsistenteConfig.frm
/// (solapas "Datos empresa" y "Logo y Estilo" únicamente -- el selector de skin Azul/Negro/Luna/
/// Royale/iTunes del form original no se porta: es una preferencia local por PC guardada en un
/// .ini de escritorio, no un dato de servidor, y no aplica al tema fijo de AlfaDesign).
/// Todo vive en TA_CONFIGURACION (claves reales tomadas del form original, ver comentarios en
/// ConfiguracionGeneralService) salvo el logo en sí, que vive en TA_LOGOS.
/// </summary>
public sealed class ConfiguracionEmpresaDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Calle { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Piso { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Provincia { get; set; } = string.Empty;
    public string Pais { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string CondicionIva { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    public string NroIngresosBrutos { get; set; } = string.Empty;
    public bool AgenteRetencionIibb { get; set; }
    public bool AgenteRetencionGanancias { get; set; }
    public DateTime? FechaInicioActividades { get; set; }
    public string PuntoVentaPrincipal { get; set; } = string.Empty;
    public string UnidadNegocio { get; set; } = string.Empty;
    public string CodigoFiscal { get; set; } = string.Empty;
    public string RegistroIgj { get; set; } = string.Empty;
    public string RegistroSagpya { get; set; } = string.Empty;
    public string Pagina { get; set; } = string.Empty;
}

public sealed class ConfiguracionCondIvaOptionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class ConfiguracionLogoDto
{
    public bool TieneLogo { get; set; }
    public bool IncluirEnFacturas { get; set; }
    public bool IncluirEnPresupuestos { get; set; }
    public bool IncluirEnOtrosComprobantes { get; set; }
    public bool PosicionIzquierda { get; set; }
}
