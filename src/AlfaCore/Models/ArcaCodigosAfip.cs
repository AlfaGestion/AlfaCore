namespace AlfaCore.Models;

/// <summary>Mapeos entre los códigos locales de AlfaCore (texto libre en TA_TIPODOCUMENTO/TA_CONDIVA,
/// sin relación con AFIP) y los códigos numéricos que exige WSFEv1. TiposDocumentoCore.Fiscal(...) ya
/// resuelve CbteTipo -- no se repite acá.
///
/// Los switches de DocTipo/CondicionIvaReceptor se arman por coincidencia de texto sobre la
/// DESCRIPCION local, porque esas tablas no tienen columna de código AFIP. Antes de habilitar en
/// producción hay que confirmar contra una base cliente real que cubren todos los valores que existen
/// (ver plan, sección de verificación) -- no asumir que esta lista es exhaustiva.</summary>
public static class ArcaCodigosAfip
{
    public const int DocTipoCuit = 80;
    public const int DocTipoCuil = 86;
    public const int DocTipoDni = 96;
    public const int DocTipoPasaporte = 94;
    public const int DocTipoConsumidorFinal = 99;

    public static (int DocTipo, string DocNro) ResolverDocumento(string? descripcionLocal, string? documentoNumero)
    {
        var descripcion = (descripcionLocal ?? string.Empty).Trim().ToUpperInvariant();
        // Las tablas legacy suelen guardar los tipos como "C.U.I.T", "C.U.I.L" o
        // "D.N.I.". Se compacta la descripción para que esos formatos no terminen
        // interpretándose como DNI por el caso default.
        var descripcionCompacta = new string(descripcion.Where(char.IsLetterOrDigit).ToArray());
        var numero = (documentoNumero ?? string.Empty).Trim();

        var docTipo = descripcionCompacta switch
        {
            var d when d.Contains("CUIT") => DocTipoCuit,
            var d when d.Contains("CUIL") => DocTipoCuil,
            var d when d.Contains("PASAPORTE") => DocTipoPasaporte,
            var d when d.Contains("DNI") || d.Contains("LE") || d.Contains("LC") => DocTipoDni,
            var d when d.Contains("CONSUMIDOR") => DocTipoConsumidorFinal,
            _ => numero.Length == 0 ? DocTipoConsumidorFinal : DocTipoDni
        };

        return docTipo == DocTipoConsumidorFinal ? (docTipo, "0") : (docTipo, numero);
    }

    /// <summary>CondicionIVAReceptorId (RG 5616) -- campo que la referencia Python nunca envía pero que
    /// WSFEv1 hoy exige para Factura A/B. Default seguro: Consumidor Final (5).</summary>
    public static int ResolverCondicionIvaReceptor(string? descripcionCondIvaLocal)
    {
        var descripcion = (descripcionCondIvaLocal ?? string.Empty).Trim().ToUpperInvariant();
        return descripcion switch
        {
            var d when d.Contains("RESPONSABLE INSCRIPTO") || d.Contains("RESP. INSCRIPTO") => 1,
            var d when d.Contains("EXENTO") => 4,
            var d when d.Contains("CONSUMIDOR FINAL") => 5,
            var d when d.Contains("MONOTRIBUT") => 6,
            var d when d.Contains("NO CATEGORIZAD") => 7,
            var d when d.Contains("PROVEEDOR DEL EXTERIOR") => 8,
            var d when d.Contains("CLIENTE DEL EXTERIOR") => 9,
            var d when d.Contains("NO ALCANZADO") => 15,
            _ => 5
        };
    }

    /// <summary>Id AFIP de alícuota de IVA -- tabla completa vigente, no solo las 4 que conocía la
    /// referencia Python (que además categorizaba mal el 0%: acá la comparación es exacta por valor,
    /// nunca "if (tasa)").</summary>
    public static int ResolverIdAlicuotaIva(decimal tasaPorcentual) => tasaPorcentual switch
    {
        0m => 3,
        2.5m => 9,
        5m => 8,
        10.5m => 4,
        21m => 5,
        27m => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(tasaPorcentual), tasaPorcentual, "Alícuota de IVA sin código AFIP asignado.")
    };

    /// <summary>Código de barras impreso del CAE (RG 2926/3749): CUIT(11)+CbteTipo(3)+PtoVta(4)+
    /// CAE(14)+VtoCAE yyyyMMdd(8) = 40 dígitos + 1 dígito verificador módulo 10.</summary>
    public static string CalcularCodigoBarraCae(string cuit, int cbteTipo, int ptoVta, string cae, DateTime vencimientoCae)
    {
        var cuitDigits = new string((cuit ?? string.Empty).Where(char.IsDigit).ToArray()).PadLeft(11, '0');
        if (cuitDigits.Length != 11)
            throw new ArgumentException("El CUIT del emisor debe tener 11 dígitos para calcular el código de barras.", nameof(cuit));

        var caeDigits = new string((cae ?? string.Empty).Where(char.IsDigit).ToArray()).PadLeft(14, '0');
        if (caeDigits.Length != 14)
            throw new ArgumentException("El CAE debe tener 14 dígitos para calcular el código de barras.", nameof(cae));

        var cuerpo = cuitDigits
            + cbteTipo.ToString("D3")
            + ptoVta.ToString("D4")
            + caeDigits
            + vencimientoCae.ToString("yyyyMMdd");

        return cuerpo + CalcularDigitoVerificadorMod10(cuerpo).ToString();
    }

    private static int CalcularDigitoVerificadorMod10(string cuerpo)
    {
        var suma = 0;
        for (var i = 0; i < cuerpo.Length; i++)
        {
            var digito = cuerpo[i] - '0';
            var posicionDesdeLaDerecha = cuerpo.Length - i;
            suma += posicionDesdeLaDerecha % 2 == 1 ? digito * 3 : digito;
        }

        var resto = suma % 10;
        return resto == 0 ? 0 : 10 - resto;
    }
}
