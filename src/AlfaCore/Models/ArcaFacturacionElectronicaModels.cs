namespace AlfaCore.Models;

/// <summary>Ambiente de AFIP/ARCA contra el que se autentica y factura. Nunca se asume producción por
/// omisión -- ver ArcaConfigService.</summary>
public enum ArcaAmbiente
{
    Homologacion,
    Produccion
}

/// <summary>Emisor resuelto (por unidad de negocio o, si no aplica, por configuración global) para
/// pedir un CAE. El certificado/clave viajan en memoria como texto PEM (subidos y guardados como blob
/// en dbo.ARCA_CERTIFICADO, ver ArcaConfigService) -- no hay ruta de archivo en el servidor.</summary>
public sealed record ArcaEmisorConfig(
    string Cuit,
    string RazonSocial,
    ArcaAmbiente Ambiente,
    int PuntoVentaElectronico,
    string CertificadoPem,
    string ClavePrivadaPem,
    string CondicionIvaPropiaCodigo);

/// <summary>Configuración global de facturación electrónica para la pantalla de Configuración General
/// (Ventas). No incluye el contenido del certificado -- solo si hay uno subido y su nombre.</summary>
public sealed class ArcaConfiguracionGeneralDto
{
    public bool UsaFacturacionElectronica { get; set; }
    public string Ambiente { get; set; } = "HOMOLOGACION";
    public string ModoFalloCae { get; set; } = "ESTRICTO";
    public string Cuit { get; set; } = string.Empty;
    public string PuntoVenta { get; set; } = string.Empty;
    public bool TieneCertificado { get; set; }
    public string? NombreArchivoCrt { get; set; }
    public string? NombreArchivoKey { get; set; }
    public List<ArcaUnidadNegocioConfigDto> Unidades { get; set; } = [];
}

/// <summary>Una fila de V_TA_UnidadNegocio para la tabla de "unidades con factura electrónica" de la
/// pantalla de configuración.</summary>
public sealed class ArcaUnidadNegocioConfigDto
{
    public string Codigo { get; set; } = string.Empty;
    public string RazonSocialUnidad { get; set; } = string.Empty;
    public bool UsaFacturacionElectronica { get; set; }
    public string Cuit { get; set; } = string.Empty;
    public string PuntoVenta { get; set; } = string.Empty;
    public bool TieneCertificado { get; set; }
    public string? NombreArchivoCrt { get; set; }
    public string? NombreArchivoKey { get; set; }
}

/// <summary>Una alícuota de IVA con su base imponible e importe, tal cual la espera WSFEv1.CAESolicitar
/// (una entrada por alícuota distinta presente en el comprobante, no una por línea).</summary>
public sealed record ArcaIvaAlicuotaDto(int Id, decimal BaseImponible, decimal Importe);

public sealed record ArcaCaeSolicitudDto(
    string Cuit,
    int PtoVta,
    int CbteTipo,
    int Concepto,
    int DocTipo,
    string DocNro,
    DateTime CbteFch,
    long CbteDesde,
    long CbteHasta,
    decimal ImpTotal,
    decimal ImpTotConc,
    decimal ImpNeto,
    decimal ImpOpEx,
    decimal ImpTrib,
    decimal ImpIva,
    int CondicionIvaReceptorId,
    string MonId,
    decimal MonCotiz,
    IReadOnlyList<ArcaIvaAlicuotaDto> Ivas);

public sealed record ArcaObservacionDto(int Codigo, string Mensaje);

public sealed record ArcaCaeResultadoDto(
    bool ExitoTecnico,
    string? Resultado,
    long CbteDesde,
    long CbteHasta,
    string? Cae,
    DateTime? CaeVto,
    IReadOnlyList<ArcaObservacionDto> Observaciones,
    string? ErrorTecnico)
{
    public bool Aprobado => ExitoTecnico && string.Equals(Resultado, "A", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Cae);

    public static ArcaCaeResultadoDto Fallo(string errorTecnico, long cbteDesde = 0, long cbteHasta = 0)
        => new(false, null, cbteDesde, cbteHasta, null, null, [], errorTecnico);
}

/// <summary>Ticket de Acceso vigente (token/sign) para un CUIT+ambiente, con su expiración real
/// (no una ventana fija) -- se cachea en dbo.ARCA_WSAA_TICKET, compartible entre workers.</summary>
public sealed record WsaaTicket(string Token, string Sign, DateTime ExpirationTimeUtc)
{
    public bool VigentePara(DateTime nowUtc, TimeSpan margen) => ExpirationTimeUtc - margen > nowUtc;
}

/// <summary>Numeración a usar para el próximo comprobante, resuelta contra AFIP ANTES de crear el
/// comprobante local (ver diseño en el plan: evita tener que renumerar V_MV_Cpte después).</summary>
public sealed record ArcaNumeracionPrevistaDto(ArcaEmisorConfig Emisor, int CbteTipo, long NumeroSugerido)
{
    public string NumeroFormateado => NumeroSugerido.ToString("D8");
}

/// <summary>Datos de un comprobante ya persistido (V_MV_Cpte) necesarios para pedir su CAE. El
/// desglose neto/IVA se recalcula desde <see cref="Items"/> (el carrito ya validado en
/// PuntoVentaService.CreateSaleAsync) y NO desde las columnas AlicIva1..4/ImporteIva1..4 de la
/// cabecera: sp_web_CpteInsumos no las completa para ventas de POS (confirmado leyendo el SP), solo
/// acumula ImporteIva total sin desglose por alícuota -- reusar esas columnas daría un desglose
/// vacío/incorrecto. Verificar en homologación (ver plan) antes de habilitar en producción.</summary>
public sealed record PuntoVentaCaeContextoDto(
    string Tc,
    string IdComprobante,
    string Sucursal,
    string Numero,
    string Letra,
    string? UNegocio,
    IReadOnlyList<PuntoVentaCartItemDto> Items,
    decimal ImpTotal);

public enum ArcaCaeEstado
{
    NoAplica,
    Aprobado,
    Rechazado,
    Pendiente
}

/// <summary>Resultado de un intento de solicitud de CAE -- se devuelve siempre, nunca se lanza para
/// errores de negocio/AFIP (solo para errores de programación). A diferencia de la referencia Python,
/// un rechazo también queda persistido en V_MV_CPTE_ELECTRONICOS para trazabilidad.</summary>
public sealed record ArcaCaeIntentoDto(
    ArcaCaeEstado Estado,
    string? Cae,
    DateTime? CaeVencimiento,
    string? Motivo)
{
    public bool Aplica => Estado != ArcaCaeEstado.NoAplica;
    public bool Aprobado => Estado == ArcaCaeEstado.Aprobado;

    public static readonly ArcaCaeIntentoDto NoAplica = new(ArcaCaeEstado.NoAplica, null, null, null);
}
