using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Alta de clientes por chat y dirección de entrega del pedido (2026-11-17). Se prueba la lógica pura
/// (coincidencias con clientes existentes, datos faltantes); lo que graba en SQL requiere una base de tenant.
/// </summary>
public class ConversacionBotClientesTests
{
    private static ConversacionBotClientesService.DatosAlta Datos(
        string nombre = "Juan Pérez", string dni = "", string calle = "San Martín", string numero = "123",
        string localidad = "Localidad", string referencia = "")
        => new(nombre, dni, calle, numero, localidad, referencia);

    private static ConversacionBotClientesService.ClienteExistente Cliente(
        string razon = "PEREZ JUAN", string calle = "AV SAN MARTIN", string numero = "123",
        string telefono = "", string documento = "")
        => new() { Codigo = "112000001", RazonSocial = razon, Calle = calle, Numero = numero, Telefono = telefono, Documento = documento };

    [Theory]
    [InlineData("2944534818", "+5492944534818", true)]       // celular con 54 9 en el chat
    [InlineData("2944-534818", "5492944534818", true)]       // con guion
    [InlineData("0294415534818", "5492944534818", false)]    // otro formato que no termina igual: no adivina
    [InlineData("534818", "5492944534818", true)]            // número local sin característica
    [InlineData("4444444 / 2944534818", "+5492944534818", true)] // varios teléfonos en el campo
    [InlineData("2944534819", "+5492944534818", false)]
    [InlineData("", "+5492944534818", false)]
    [InlineData("12345", "+5492944534818", false)]           // muy corto para compararlo
    public void TelefonoCoincide_ComparaLosUltimosDigitos(string telefonoCliente, string telefonoChat, bool esperado)
        => Assert.Equal(esperado, ConversacionBotClientesService.TelefonoCoincide(telefonoCliente, telefonoChat));

    [Fact]
    public void TelefonoNacional_SacaElPrefijoInternacional()
    {
        Assert.Equal("2944534818", ConversacionBotClientesService.TelefonoNacional("+54 9 2944 53-4818"));
        Assert.Equal("534818", ConversacionBotClientesService.TelefonoNacional("534818"));
    }

    [Theory]
    [InlineData("Juan Pérez", "PEREZ JUAN", true)]
    [InlineData("juan perez", "Pérez, Juan Carlos", true)]
    [InlineData("Juan Pérez", "PEREZ MARIA", false)]
    [InlineData("Pérez", "PEREZ JUAN", false)] // con una sola palabra no alcanza
    public void NombreCoincide_PideTodasLasPalabras(string nombre, string razonSocial, bool esperado)
        => Assert.Equal(esperado, ConversacionBotClientesService.NombreCoincide(nombre, razonSocial));

    [Theory]
    [InlineData("San Martín", "123", "AV SAN MARTIN", "123", true)]
    [InlineData("calle Belgrano", "45", "BELGRANO", "45", true)]
    [InlineData("San Martín", "123", "SAN MARTIN", "124", false)]
    [InlineData("San Martín", "", "SAN MARTIN", "", false)] // sin altura no se compara
    public void DireccionCoincide_MismaCalleYAltura(string calle, string numero, string calleCliente, string numeroCliente, bool esperado)
        => Assert.Equal(esperado, ConversacionBotClientesService.DireccionCoincide(calle, numero, calleCliente, numeroCliente));

    [Fact]
    public void Evaluar_NombreYDireccionEsFuerte_SoloUnoEsPosible()
    {
        Assert.Equal(ConversacionBotClientesService.Coincidencia.Fuerte,
            ConversacionBotClientesService.Evaluar(Datos(), "+5491100000000", Cliente()));
        Assert.Equal(ConversacionBotClientesService.Coincidencia.Posible,
            ConversacionBotClientesService.Evaluar(Datos(), "+5491100000000", Cliente(numero: "999")));
        Assert.Equal(ConversacionBotClientesService.Coincidencia.Posible,
            ConversacionBotClientesService.Evaluar(Datos(), "+5491100000000", Cliente(razon: "OTRA PERSONA")));
        Assert.Equal(ConversacionBotClientesService.Coincidencia.Ninguna,
            ConversacionBotClientesService.Evaluar(Datos(), "+5491100000000", Cliente(razon: "OTRA PERSONA", calle: "MITRE")));
    }

    [Fact]
    public void Evaluar_MismoDniOTelefonoEsFuerte_IncluidoElDniDentroDelCuit()
    {
        Assert.Equal(ConversacionBotClientesService.Coincidencia.Fuerte,
            ConversacionBotClientesService.Evaluar(Datos(dni: "23.456.789"), "", Cliente(razon: "X Y", calle: "Z", numero: "1", documento: "23456789")));
        Assert.Equal(ConversacionBotClientesService.Coincidencia.Fuerte,
            ConversacionBotClientesService.Evaluar(Datos(dni: "23456789"), "", Cliente(razon: "X Y", calle: "Z", numero: "1", documento: "20-23456789-3")));
        Assert.Equal(ConversacionBotClientesService.Coincidencia.Fuerte,
            ConversacionBotClientesService.Evaluar(Datos(), "+5492944534818", Cliente(razon: "X Y", calle: "Z", numero: "1", telefono: "2944534818")));
    }

    [Fact]
    public void LeerDatos_YDatosFaltantes()
    {
        var datos = ConversacionBotClientesService.LeerDatos(
            """{"nombre_apellido":"  Ana   Gómez ","dni":"30111222","calle":"Mitre","numero":12,"localidad":"Lago"}""");
        Assert.Equal("Ana Gómez", datos.Nombre);
        Assert.Equal("12", datos.Numero);
        Assert.Empty(ConversacionBotClientesService.DatosFaltantes(datos));

        var incompletos = ConversacionBotClientesService.LeerDatos("""{"nombre_apellido":"Ana","calle":"Mitre"}""");
        var faltan = ConversacionBotClientesService.DatosFaltantes(incompletos);
        Assert.Contains("nombre y apellido", faltan);
        Assert.Contains("localidad", faltan);
        Assert.Contains(faltan, f => f.StartsWith("número de la casa", StringComparison.Ordinal));

        // Sin número pero con referencia (casa de campo, paraje) alcanza.
        var conReferencia = ConversacionBotClientesService.LeerDatos(
            """{"nombre_apellido":"Ana Gómez","calle":"Paraje Los Repollos","localidad":"Lago","referencia":"tranquera verde"}""");
        Assert.Empty(ConversacionBotClientesService.DatosFaltantes(conReferencia));

        Assert.Empty(ConversacionBotClientesService.LeerDatos("no es json").Nombre);
    }

    [Fact]
    public void DireccionTexto_NoMuestraLocalidadVacia()
    {
        Assert.Equal("Mitre 12, Lago", ConversacionBotClientesService.DireccionTexto("Mitre", "12", "Lago"));
        Assert.Equal("Mitre 12", ConversacionBotClientesService.DireccionTexto("Mitre ", "12", "."));
    }

    [Fact]
    public void Pedido_DireccionDeEntrega_SeLeeYSeMuestraEnElResumen()
    {
        Assert.Equal("Mitre 12, Lago", ConversacionBotPedidosService.LeerDireccionEntrega("""{"items":[],"direccion_entrega":" Mitre   12, Lago "}"""));
        Assert.Equal(string.Empty, ConversacionBotPedidosService.LeerDireccionEntrega("""{"items":[]}"""));

        var resumen = ConversacionBotPedidosService.FormatearResumen(
            [new ConversacionBotPedidosService.LineaBorrador("10", "BIDON 10 L", 2m, 4000m)], 8000m, "Mitre 12, Lago");
        Assert.Contains("Entrega en: Mitre 12, Lago", resumen);
        Assert.Contains("SIN registrar", resumen);
    }
}
