using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class CompraArticuloTextoTests
{
    [Theory]
    [InlineData("ESP. TOMILLO S/TACC (x 25 gr)","ESP TOMILLO S TACC X 25 GR")]
    [InlineData("  Café   Molido  ","CAFE MOLIDO")]
    public void LimpiaTextoComoElLegacy(string entrada,string esperado)=>Assert.Equal(esperado,CompraArticuloTexto.Limpiar(entrada));

    [Fact]
    public void PalabrasDescartaCortasNumerosYVacias()
    {
        var palabras=CompraArticuloTexto.Palabras("SALSA DE TOMATE CON ALBAHACA 500 GR 12 UN");
        Assert.Equal(["SALSA","TOMATE","ALBAHACA"],palabras);
        Assert.Equal("SALSA",CompraArticuloTexto.PalabraClave("la SALSA de tomate"));
    }

    [Theory]
    [InlineData(1,1)]
    [InlineData(2,1)]
    [InlineData(3,2)]
    [InlineData(5,3)]
    public void UmbralEsLaMitadRedondeadHaciaArriba(int palabras,int esperado)=>Assert.Equal(esperado,CompraArticuloTexto.Umbral(palabras));

    [Theory]
    [InlineData("PIMENTON 25 GR X 12",12)]
    [InlineData("CAFE EN GRANO (24)",24)]
    [InlineData("YERBA 500 X 10",10)]
    [InlineData("ACEITE 900 CC",null)]
    public void BultoDesdeLaDescripcion(string descripcion,int? esperado)
    {
        var bulto=CompraArticuloTexto.BultoDesdeDescripcion(descripcion);
        if(esperado is null)Assert.Null(bulto);else Assert.Equal(esperado,(int?)bulto?.Unidades);
    }

    [Theory]
    [InlineData(100,30,70,true,"x",60,true)]   // código exacto: solo exige el puntaje total
    [InlineData(100,0,50,false,"",60,false)]
    [InlineData(0,40,70,true,"SALSA",60,true)]
    [InlineData(0,30,70,true,"SALSA",60,false)] // descripción por debajo de 35
    [InlineData(0,50,70,false,"SALSA",60,false)] // sin la palabra clave
    [InlineData(0,50,59,true,"SALSA",60,false)]
    public void ReglaDeAceptacion(decimal cod,decimal desc,decimal total,bool clave,string palabra,int minimo,bool esperado)
        =>Assert.Equal(esperado,CompraCargaService.CandidatoAceptable(cod,desc,total,clave,palabra,minimo));
}

public sealed class CompraArticuloMatchingSqlTests(ITestOutputHelper salida)
{
    [CompraIaSqlFact]
    public async Task EmparejaLosRenglonesDeUnaFacturaRealConElMaestro()
    {
        var conexion=Environment.GetEnvironmentVariable("COMPRA_IA_TEST_SQL")!;
        Assert.StartsWith("PRUEBAS",new SqlConnectionStringBuilder(conexion).InitialCatalog,StringComparison.OrdinalIgnoreCase); // nunca contra una base de producción
        await using var cn=new SqlConnection(conexion);await cn.OpenAsync();
        var items=(await cn.QueryAsync<InterfacesCompraIaDetalleItemDto>("""
            SELECT NroRenglon,Cantidad,Codigo_Articulo CodigoArticulo,Descripcion,Importe_Lista ImporteLista,Dto1,Dto2,Importe_Neto ImporteNeto,IVA Iva,Total FROM dbo.IA_Compras_DET WHERE ID_CAB=7 ORDER BY NroRenglon
            """)).AsList();
        Assert.True(items.Count>50);
        var r=await CompraCargaService.ResolverRenglonesAsync(cn,items,"211010014","1","",21m,default);
        Assert.Equal(items.Count,r.Count);
        foreach(var x in r)
            salida.WriteLine($"{x.Numero,3} {x.CodigoProveedor,-8} {Corto(x.Descripcion,34),-34} => {(x.Articulo is null?"-- SIN ARTICULO: "+x.Motivo:$"{x.Articulo.Codigo} {Corto(x.Articulo.Descripcion,30)} [{x.Puntaje:0}] {x.Metodo} x{x.UnidadesPorBulto:0.##} ({x.OrigenBulto}) cant {x.Cantidad:0.##} costo {x.Costo:0.####} (renglón {x.Total:N2} vs {x.Cantidad*x.Costo:N2})")}");
        var resueltos=r.Count(x=>x.Resuelto);
        salida.WriteLine($"Resueltos {resueltos} de {r.Count}");
        Assert.True(resueltos*100/r.Count>=60,$"Solo se resolvieron {resueltos} de {r.Count}.");
        // Los renglones con código de proveedor conocido tienen que ir por código, no por descripción.
        Assert.Contains(r,x=>x.Articulo?.Codigo=="1431"&&x.CodigoProveedor=="62116");
        static string Corto(string t,int n)=>t.Length<=n?t:t[..n];
    }
}

public sealed class CompraIaSqlFactAttribute:FactAttribute
{
    public CompraIaSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPRA_IA_TEST_SQL")))Skip="Requiere la variable COMPRA_IA_TEST_SQL con la cadena de una base de pruebas (nombre que empiece con PRUEBAS); los ensayos de escritura se revierten con ROLLBACK.";}
}
