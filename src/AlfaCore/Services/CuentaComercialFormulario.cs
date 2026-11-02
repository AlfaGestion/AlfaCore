using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Arma el pedido de guardado de una cuenta a partir de su ficha. Lo comparten la ficha completa de
/// clientes/proveedores y la ficha rápida (carga directa), para que las dos guarden exactamente igual.
/// </summary>
public static class CuentaComercialFormulario
{
    public static CuentaComercialSaveRequest DesdeDetalle(CuentaComercialDetailDto detail, CuentaComercialTipo tipo) => new()
    {
        CodigoOriginal = detail.CodigoOriginal,
        Codigo = detail.Codigo,
        RazonSocial = detail.RazonSocial,
        Contacto = detail.Contacto,
        Calle = detail.Calle,
        Numero = detail.Numero,
        Piso = detail.Piso,
        Departamento = detail.Departamento,
        CodigoPostal = detail.CodigoPostal,
        Localidad = detail.Localidad,
        ProvinciaCodigo = detail.ProvinciaCodigo,
        PaisCodigo = detail.PaisCodigo,
        Telefono = detail.Telefono,
        Fax = detail.Fax,
        Mail = detail.Mail,
        Web = detail.Web,
        DocumentoTipoCodigo = detail.DocumentoTipoCodigo,
        NumeroDocumento = detail.NumeroDocumento,
        IvaCodigo = detail.IvaCodigo,
        Observaciones = TextoEnriquecidoCompat.NormalizarComoTextoPlano(detail.Observaciones),
        LimiteCredito = detail.LimiteCredito,
        CondicionComercialCodigo = detail.CondicionComercialCodigo,
        ListaPrecioCodigo = detail.ListaPrecioCodigo,
        Clase = detail.Clase,
        VendedorCodigo = detail.VendedorCodigo,
        CuentaPrincipal = detail.CuentaPrincipal,
        CodigoImputacion = detail.CodigoImputacion,
        CodigoOpcional = detail.CodigoOpcional,
        MonedaCodigo = detail.MonedaCodigo,
        ClasificacionCodigo = detail.ClasificacionCodigo,
        LugarEntrega = detail.LugarEntrega,
        DatosPago = detail.DatosPago,
        Manual = TextoEnriquecidoCompat.NormalizarParaEditor(detail.Manual),
        NotaCuenta = TextoEnriquecidoCompat.NormalizarParaEditor(detail.NotaCuenta),
        SituacionIb = detail.SituacionIb,
        ConvenioIb = detail.ConvenioIb,
        ConceptoIb = detail.ConceptoIb,
        NroConstanciaIb = detail.NroConstanciaIb,
        Bloqueado = detail.Bloqueado,
        ExentoIvaServicios = detail.ExentoIvaServicios,
        ExentoIvaArticulos = detail.ExentoIvaArticulos,
        ExentoIvaOtros = detail.ExentoIvaOtros,
        ProveedorLocal = detail.ProveedorLocal,
        ProveedorCompartido = detail.ProveedorCompartido,
        DescuentosCondicion = detail.DescuentosCondicion
            .Select(item => new CuentaComercialCondicionDescuentoDto
            {
                CondicionCodigo = item.CondicionCodigo,
                CondicionDescripcion = item.CondicionDescripcion,
                Descuento1 = item.Descuento1,
                Descuento2 = item.Descuento2,
                Descuento3 = item.Descuento3,
                Descuento4 = item.Descuento4,
                Descuento5 = item.Descuento5
            })
            .ToList(),
        FacturacionAutomatica = tipo == CuentaComercialTipo.Cliente
            ? detail.FacturacionAutomatica ?? new CuentaComercialFacturacionAutomaticaDto()
            : null
    };
}
