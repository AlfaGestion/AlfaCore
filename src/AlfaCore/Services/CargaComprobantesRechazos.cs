using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Traduce el rechazo de ARCA a qué tiene que hacer el usuario. Los códigos y textos de WSFE son
/// técnicos ("10015: DocNro ... no se encuentra registrado en los padrones"); acá se agrupan los más
/// comunes con una indicación concreta. El texto original de ARCA se conserva al final.
/// </summary>
public static class CargaComprobantesRechazos
{
    public const string NadaGrabado = "No se grabó nada: corregí el dato y volvé a emitir.";

    public static string Explicar(ArcaCaeResultadoDto? resultado, string? documento = null)
    {
        var observaciones = (resultado?.Observaciones ?? []).Where(o => !string.IsNullOrWhiteSpace(o.Mensaje)).ToList();
        if (observaciones.Count == 0)
            return $"ARCA rechazó el comprobante sin indicar el motivo. {NadaGrabado}";
        var original = string.Join(" / ", observaciones.Select(o => $"{o.Codigo}: {o.Mensaje.Trim()}"));
        return $"{Indicacion(observaciones, documento)} {NadaGrabado} (ARCA: {original})";
    }

    internal static string Indicacion(IReadOnlyList<ArcaObservacionDto> observaciones, string? documento)
    {
        bool Tiene(params string[] textos) => observaciones.Any(o => textos.Any(t => o.Mensaje.Contains(t, StringComparison.OrdinalIgnoreCase)));
        var doc = string.IsNullOrWhiteSpace(documento) ? "del cliente" : documento.Trim();

        if (Tiene("no se encuentra registrado en los padrones"))
            return $"ARCA no encuentra el CUIT {doc} en su padrón. Si estás probando en homologación es normal con CUIT nuevos " +
                   "(el padrón de prueba de ARCA es viejo): probá con otro cliente. En producción, revisá en la ficha del cliente que el CUIT esté bien escrito.";
        if (Tiene("DocNro debe ser mayor a 0", "DocNro debe ser mayor a cero"))
            return "El cliente no tiene número de documento. Cargá su CUIT o DNI en la ficha del cliente.";
        if (Tiene("CondicionIVAReceptor", "condicion frente al IVA", "condición frente al IVA", "Condicion IVA receptor"))
            return "La condición frente al IVA del cliente no corresponde a este comprobante. Revisala en la ficha del cliente (podés usar «Consultar ARCA» para traerla).";
        if (Tiene("DocTipo", "DocNro", "documento"))
            return "El tipo o el número de documento del cliente no es válido para ARCA. Revisalos en la ficha del cliente.";
        if (Tiene("CbteFch", "fecha"))
            return "La fecha no es válida para ARCA: no puede ser anterior a la del último comprobante autorizado en ese punto de venta ni estar a más de 5 días de hoy. Usá la fecha de hoy.";
        if (Tiene("Importe", "ImpTotal", "ImpNeto", "ImpIVA", "Iva"))
            return "ARCA encontró una diferencia en los importes o el IVA del comprobante. Revisá los renglones y avisá a soporte si se repite.";
        if (Tiene("PtoVta", "punto de venta"))
            return "El punto de venta no está habilitado en ARCA para factura electrónica. Revisá el talonario y la configuración de la unidad de negocio.";
        return "ARCA rechazó el comprobante.";
    }

    public const string Incierta = "ARCA no confirmó la respuesta (se cortó la comunicación). No emitas otra factura: " +
                                   "tocá «Recuperar operación» para consultar si quedó autorizada.";
}
