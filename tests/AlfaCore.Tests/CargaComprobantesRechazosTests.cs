using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Rechazos reales de ARCA (homologación, 02/11/2026) traducidos a qué tiene que hacer el usuario.</summary>
public sealed class CargaComprobantesRechazosTests
{
    private static ArcaCaeResultadoDto Rechazo(int codigo, string mensaje)
        => new(true, "R", 1, 1, null, null, [new ArcaObservacionDto(codigo, mensaje)], null);

    [Fact]
    public void CuitFueraDelPadron_ExplicaHomologacionYQueRevisar()
    {
        var texto = CargaComprobantesRechazos.Explicar(Rechazo(10015,
            "Comprobante C (CbteDesde igual a CbteHasta), DocTipo: 80, DocNro 30718648617 no se encuentra registrado en los padrones de AFIP y no corresponde a una cuit pais."),
            "30718648617");
        Assert.StartsWith("ARCA no encuentra el CUIT 30718648617 en su padrón.", texto);
        Assert.Contains("homologación", texto);
        Assert.Contains(CargaComprobantesRechazos.NadaGrabado, texto);
        Assert.Contains("(ARCA: 10015:", texto);
    }

    [Fact]
    public void DocumentoEnCero_PideCargarElDocumento()
        => Assert.StartsWith("El cliente no tiene número de documento.", CargaComprobantesRechazos.Explicar(Rechazo(10015,
            "Para facturas C menor a $10000000, si DocTipo es distinto a 99, DocNro debe ser mayor a 0.")));

    [Fact]
    public void SinObservaciones_IgualIndicaQueNoSeGraboNada()
        => Assert.Contains(CargaComprobantesRechazos.NadaGrabado, CargaComprobantesRechazos.Explicar(new(true, "R", 1, 1, null, null, [], null)));
}
