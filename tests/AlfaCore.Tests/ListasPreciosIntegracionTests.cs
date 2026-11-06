using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Listas de precios contra una base de pruebas real (PRUEBAS02122025, Retail con listas 0001/0004).
/// Crea listas temporales, las usa y las borra al final. Requiere ALFACORE_TEST_RETAIL=1 y ALFACORE_TEST_RETAIL_SQL.
/// </summary>
[Collection("BasePruebasRetail")] // misma base de pruebas: sin correr en paralelo
public sealed class ListasPreciosIntegracionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CicloCompletoDeUnaListaDePrueba()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL_SQL");
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL") != "1" || string.IsNullOrWhiteSpace(cs)) return;
        var svc = Servicio(cs);
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        // Restos de una corrida anterior interrumpida.
        foreach (var resto in await cn.QueryAsync<string>("SELECT IdLista FROM dbo.V_MA_PRECIOSCAB WHERE TipoLista='V' AND Nombre LIKE 'PRUEBA%(borrar)'"))
            await svc.BorrarListaAsync(resto.Trim());
        // Rubro chico con artículos activos y con precio de venta en el maestro.
        var rubro = await cn.QueryFirstAsync<string>("""
            SELECT TOP (1) LTRIM(RTRIM(IDRUBRO)) FROM dbo.V_MA_ARTICULOS
            WHERE ISNULL(SUSPENDIDO,0)=0 AND ISNULL(SuspendidoV,0)=0 AND ISNULL(EnComodato,0)=0 AND ISNULL(IDRUBRO,'')<>''
            GROUP BY LTRIM(RTRIM(IDRUBRO)) HAVING COUNT(*) BETWEEN 3 AND 15 ORDER BY COUNT(*);
            """);
        var esperados = await cn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDRUBRO))=@Rubro
              AND ISNULL(SUSPENDIDO,0)=0 AND ISNULL(SuspendidoV,0)=0 AND ISNULL(EnComodato,0)=0;
            """, new { Rubro = rubro });
        string? lista = null, copia = null;
        try
        {
            var nueva = await svc.GuardarListaAsync(new ListaPrecioGuardarRequest { Nombre = "PRUEBA ALFACORE (borrar)", ConIva = true, Decimales = 2 });
            lista = nueva.IdLista;
            Assert.True(nueva.ConIva); Assert.Equal(0, nueva.Articulos);
            output.WriteLine($"Lista {lista}, rubro {rubro} ({esperados} artículos)");

            var agregado = await svc.AgregarAsync(new ListaPrecioAgregarRequest { IdLista = lista, Modo = ListaPrecioAgregarModo.Rubro, Valor = rubro });
            Assert.Equal(esperados, agregado.Filas);
            var otraVez = await svc.AgregarAsync(new ListaPrecioAgregarRequest { IdLista = lista, Modo = ListaPrecioAgregarModo.Rubro, Valor = rubro });
            Assert.Equal(0, otraVez.Filas); // los que ya están no se repiten

            var pagina = await svc.BuscarAsync(new ListaPrecioFiltro { IdLista = lista, PageSize = 100 });
            Assert.Equal(esperados, pagina.Total);
            Assert.True(pagina.Lista.ConIva);
            Assert.Equal("0123", pagina.Lista.ClasesDelMaestro); // Retail: costo y clases 1..3 vienen del maestro

            // Edición de una fila: el trigger redondea a 2 decimales y las clases del maestro no se tocan.
            var fila = pagina.Filas[0];
            var clase1Antes = fila.Precio1;
            fila.Precio4 = 1234.567m; fila.Precio1 = 99999m;
            fila.OfertaDesde = DateTime.Today; fila.OfertaHasta = DateTime.Today.AddDays(7); fila.Precio5 = 1000m;
            var guardada = await svc.GuardarFilaAsync(lista, fila);
            Assert.Equal(1234.57m, guardada.Precio4);
            Assert.Equal(clase1Antes, guardada.Precio1);
            Assert.Equal(DateTime.Today.AddDays(7), guardada.OfertaHasta);
            var ofertas = await svc.BuscarAsync(new ListaPrecioFiltro { IdLista = lista, SoloOfertas = true });
            Assert.Equal(1, ofertas.Total);

            var filtroUno = new ListaPrecioFiltro { IdLista = lista, Texto = fila.IdArticulo };
            var aumento = await svc.AumentarAsync(new ListaPrecioAumentoRequest { Filtro = filtroUno, Origen = 4, Destinos = [1, 4], Porcentaje = 10 });
            Assert.Contains("clases 1 no se tocaron", aumento.Mensaje);
            var aumentada = (await svc.BuscarAsync(filtroUno)).Filas.Single(x => x.Id == fila.Id);
            Assert.Equal(1358.03m, aumentada.Precio4); // 1234,57 × 1,10 = 1358,027
            Assert.Equal(clase1Antes, aumentada.Precio1);

            await svc.RedondearAsync(new ListaPrecioRedondeoRequest { Filtro = filtroUno, Clases = [4], Multiplo = 10 });
            Assert.Equal(1360m, (await svc.BuscarAsync(filtroUno)).Filas.Single(x => x.Id == fila.Id).Precio4);

            // Solapa "Listas" del artículo: aparece tildada con sus precios; destildarla la saca (en una transacción revertida).
            var listasArt = await ListasPreciosService.LeerListasArticuloAsync(cn, fila.IdArticulo, CancellationToken.None);
            var enLista = listasArt.Single(x => x.IdLista == lista);
            Assert.True(enLista.Incluido); Assert.Equal(1360m, enLista.Precio4);
            await using (var tx = (SqlTransaction)await cn.BeginTransactionAsync())
            {
                enLista.Incluido = false;
                var idArt = (await cn.ExecuteScalarAsync<string>("SELECT IDARTICULO FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO))=@Id", new { Id = fila.IdArticulo }, tx))!;
                await ListasPreciosService.GuardarListasArticuloAsync(cn, tx, idArt, new ArticuloSaveRequest(), [enLista], false, "PRUEBA", CancellationToken.None);
                Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MA_PRECIOS WHERE IdLista=@L AND TipoLista='V' AND IdArticulo=@A",
                    new { L = lista, A = idArt }, tx));
                // Volver a tildarla: alta con el precio cargado y las clases del maestro (1..3) tomadas del artículo.
                var maestro = new ArticuloSaveRequest { Precio1 = 11m, Precio2 = 22m, Precio3 = 33m };
                var alta = new ArticuloListaPrecioDto { IdLista = lista!, Incluido = true, Precio4 = 500m, Precio1 = 1m };
                await ListasPreciosService.GuardarListasArticuloAsync(cn, tx, idArt, maestro, [alta], false, "PRUEBA", CancellationToken.None);
                var (p1, p3, p4) = await cn.QuerySingleAsync<(decimal, decimal, decimal)>(
                    "SELECT Precio1, Precio3, Precio4 FROM dbo.V_MA_PRECIOS WHERE IdLista=@L AND TipoLista='V' AND IdArticulo=@A", new { L = lista, A = idArt }, tx);
                Assert.Equal((11m, 33m, 500m), (p1, p3, p4));
                await tx.RollbackAsync();
            }

            var nuevaCopia = await svc.GuardarListaAsync(new ListaPrecioGuardarRequest { Nombre = "PRUEBA COPIA (borrar)", ConIva = true, CopiarDeLista = lista });
            copia = nuevaCopia.IdLista;
            Assert.Equal(esperados, nuevaCopia.Articulos);

            var quitado = await svc.QuitarAsync(lista, [fila.Id]);
            Assert.Equal(1, quitado.Filas);
            Assert.Equal(esperados - 1, (await svc.BuscarAsync(new ListaPrecioFiltro { IdLista = lista })).Total);
            Assert.True(await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_PRECIOSHIS WHERE IdLista=@L AND LTRIM(RTRIM(IdArticulo))=@A AND Observaciones='BAJA'",
                new { L = lista, A = fila.IdArticulo }) >= 1);
            var historial = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_PRECIOSHIS WHERE IdLista=@L AND LTRIM(RTRIM(IdArticulo))=@A",
                new { L = lista, A = fila.IdArticulo });
            output.WriteLine($"Histórico del artículo {fila.IdArticulo} en la lista: {historial} filas");
            Assert.True(historial >= 3);
        }
        finally
        {
            if (copia is not null) await svc.BorrarListaAsync(copia);
            if (lista is not null) await svc.BorrarListaAsync(lista);
        }
        Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MA_PRECIOSCAB WHERE Nombre LIKE 'PRUEBA%(borrar)'"));
    }

    /// <summary>Abrir un artículo trae las 8 clases del maestro (antes venían en 0 y guardar las pisaba) y sus listas.</summary>
    [Fact]
    public async Task ArticuloAbiertoTraeSusOchoPreciosYSusListas()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL_SQL");
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL") != "1" || string.IsNullOrWhiteSpace(cs)) return;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        var esperado = await cn.QueryFirstAsync<(string Id, decimal P1, decimal P2, decimal P3, decimal P4)>("""
            SELECT TOP (1) LTRIM(RTRIM(IDARTICULO)), CONVERT(decimal(19,4),PRECIO1), CONVERT(decimal(19,4),PRECIO2), CONVERT(decimal(19,4),PRECIO3), CONVERT(decimal(19,4),PRECIO4)
            FROM dbo.V_MA_ARTICULOS a WHERE ISNULL(PRECIO1,0)>0 AND ISNULL(PRECIO2,0)>0 AND ISNULL(PRECIO3,0)>0
              AND EXISTS (SELECT 1 FROM dbo.V_MA_PRECIOS p WHERE p.IdArticulo=a.IDARTICULO AND p.TipoLista='V') ORDER BY IDARTICULO;
            """);
        var sesiones = Proxy<ISessionService>((m, _) => m.Name == "GetConnectionString" ? cs : null);
        var eventos = Proxy<IAppEventService>((m, _) => m.ReturnType == typeof(Task<string>) ? Task.FromResult("") : null);
        var svc = new ArticulosService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), sesiones, eventos,
            Proxy<IArticuloImagenFtpService>(), Proxy<ICentralBasesService>());
        var articulo = await svc.GetByIdAsync(esperado.Id);
        Assert.NotNull(articulo);
        Assert.Equal((esperado.P1, esperado.P2, esperado.P3, esperado.P4), (articulo!.Precio1, articulo.Precio2, articulo.Precio3, articulo.Precio4));
        var listas = await svc.GetListasPrecioAsync(esperado.Id);
        Assert.Contains(listas, l => l.Incluido && l.Existia && l.Precio4 > 0);
        var nuevas = await svc.GetListasPrecioAsync(null);
        Assert.All(nuevas, l => Assert.False(l.Incluido));
        output.WriteLine($"Artículo {esperado.Id}: en {listas.Count(l => l.Incluido)} de {listas.Count} listas");
    }

    private static ListasPreciosService Servicio(string cs)
    {
        var sesion = new SessionDto();
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch
        {
            "GetConnectionString" => cs,
            "GetActiveSession" => sesion,
            _ => null
        });
        var users = Proxy<IAppUserSessionService>((m, _) => m.Name switch
        {
            "get_IsAuthenticated" => true,
            "IsAuthorizedForSession" => true,
            "GetCurrentUserName" => "PRUEBA-ALFACORE",
            _ => null
        });
        var permisos = Proxy<IPermissionService>((m, _) => m.Name == "HasAccessAsync" ? Task.FromResult(true) : null);
        var eventos = Proxy<IAppEventService>((m, _) => m.ReturnType == typeof(Task<string>) ? Task.FromResult("") : null);
        return new ListasPreciosService(sessions, users, permisos, eventos);
    }

    private static T Proxy<T>(Func<System.Reflection.MethodInfo, object?[]?, object?>? handler = null) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, CargaComprobantesRetailTests.RetailProxy>();
        ((CargaComprobantesRetailTests.RetailProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
