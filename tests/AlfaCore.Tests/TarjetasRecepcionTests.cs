using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasRecepcionTests
{
    [Theory]
    [InlineData("liquidacion.pdf","%PDF-1.7\n")]
    [InlineData("VISA.PDF","%PDF-2.0\n")]
    public void RecibePdfSinDependerDelNombreDelBanco(string nombre,string contenido)
        =>TarjetasRecepcionService.ValidarArchivo(new(nombre,System.Text.Encoding.ASCII.GetBytes(contenido)));

    [Theory]
    [InlineData("liquidacion.xls","%PDF-1.7\n")]
    [InlineData("liquidacion.pdf","NO ES PDF")]
    [InlineData("liquidacion.pdf","%PD")]
    public void RechazaArchivosNoPdfOConExtensionEngañosa(string nombre,string contenido)
        =>Assert.Throws<InvalidOperationException>(()=>TarjetasRecepcionService.ValidarArchivo(new(nombre,System.Text.Encoding.ASCII.GetBytes(contenido))));

    [Fact]
    public void RechazaPdfQueSuperaElLimiteAunqueElNombreSeaValido()
    {
        var bytes=new byte[20971521];"%PDF-"u8.CopyTo(bytes);
        Assert.Throws<InvalidOperationException>(()=>TarjetasRecepcionService.ValidarArchivo(new("liquidacion.pdf",bytes)));
    }
    [Fact]
    public void IdentidadDelWorkerQuedaEncerradaEnLaBaseYNoPermiteLoginNiElevarPrivilegios()
    {
        var sesion=new SessionDto();var otra=new SessionDto();
        var identidad=new TarjetasUsuarioWorker(new(){UserName="Operador",SystemCode="CN000PR"},sesion.Id);
        Assert.True(identidad.IsAuthorizedForSession(sesion.Id));Assert.False(identidad.IsAuthorizedForSession(otra.Id));
        Assert.False(identidad.CurrentUser.SuperAdmin);Assert.Null(identidad.CurrentToken);
        Assert.Throws<NotSupportedException>(()=>identidad.AdoptInternalUser(new(){SuperAdmin=true}));
        Assert.Throws<OperationCanceledException>(()=>identidad.EnsureAuthorizedForSession(otra.Id));
    }
}
