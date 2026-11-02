using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ArcaPadronSoapTests
{
    private const string Inscripto = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
        <ns2:getPersona_v2Response xmlns:ns2="http://a5.soap.ws.server.puc.sr/"><personaReturn>
          <datosGenerales><razonSocial>MADERAS SAN BLAS SRL</razonSocial>
            <domicilioFiscal><direccion>ALVAREZ JONTE AV. 4230</direccion><codPostal>1407</codPostal>
              <descripcionProvincia>CIUDAD AUTONOMA BUENOS AIRES</descripcionProvincia></domicilioFiscal></datosGenerales>
          <datosRegimenGeneral><impuesto><idImpuesto>30</idImpuesto><descripcionImpuesto>IVA</descripcionImpuesto></impuesto></datosRegimenGeneral>
        </personaReturn></ns2:getPersona_v2Response></soap:Body></soap:Envelope>
        """;

    private const string Monotributo = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
        <ns2:getPersona_v2Response xmlns:ns2="http://a5.soap.ws.server.puc.sr/"><personaReturn>
          <datosGenerales><apellido>PEREZ</apellido><nombre>JUAN</nombre></datosGenerales>
          <datosMonotributo><categoriaMonotributo><descripcionCategoria>A LOCACIONES</descripcionCategoria></categoriaMonotributo></datosMonotributo>
        </personaReturn></ns2:getPersona_v2Response></soap:Body></soap:Envelope>
        """;

    [Fact]
    public void InscriptoEnIva_EsResponsableInscriptoConDomicilio()
    {
        var p = ArcaPadronSoap.ParsePersona(Inscripto, "30688774930");
        Assert.Equal("MADERAS SAN BLAS SRL", p.RazonSocial);
        Assert.Equal("1", p.CondicionIvaCodigo);
        Assert.Equal("1407", p.CodigoPostal);
    }

    [Fact]
    public void ConDatosMonotributo_EsMonotributoYNombreCompuesto()
    {
        var p = ArcaPadronSoap.ParsePersona(Monotributo, "20111111112");
        Assert.Equal("5", p.CondicionIvaCodigo);
        Assert.Equal("PEREZ JUAN", p.RazonSocial);
    }

    [Fact]
    public void FaultDeArca_SeInformaComoError()
    {
        var xml = """<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><soap:Fault><faultstring>No existe persona con ese Id</faultstring></soap:Fault></soap:Body></soap:Envelope>""";
        var ex = Assert.Throws<InvalidOperationException>(() => ArcaPadronSoap.ParsePersona(xml, "20111111112"));
        Assert.Contains("No existe persona", ex.Message);
    }
}
