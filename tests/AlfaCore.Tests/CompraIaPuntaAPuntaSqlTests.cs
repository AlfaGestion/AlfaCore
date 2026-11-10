using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class CompraIaPuntaAPuntaSqlTests(ITestOutputHelper salida)
{
    [CompraIaSqlFact]
    public async Task FacturaLeidaPorIaSeConvierteEnCompraSeRegistraYAprendeSinDejarRastro()
    {
        var cadena=Environment.GetEnvironmentVariable("COMPRA_IA_TEST_SQL")!;
        Assert.StartsWith("PRUEBAS",new SqlConnectionStringBuilder(cadena).InitialCatalog,StringComparison.OrdinalIgnoreCase); // nunca contra una base de producción
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> f) where T:class
        {var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=f;return p;}
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var servicio=new CompraCargaService(Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>cadena,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Administrador",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        await using var cn=new SqlConnection(cadena);await cn.OpenAsync();

        var ia=await cn.QuerySingleAsync<InterfacesCompraIaResultadoDto>("""
            SELECT ID Id,IdComprobanteRecibido,Estado,ISNULL(TipoComprobante,'') TipoComprobante,ISNULL(Letra,'') Letra,ISNULL(PuntoVenta,'') PuntoVenta,ISNULL(Numero,'') Numero,Fecha,Vencimiento,
                   ISNULL(Moneda,'') Moneda,NetoGravado,NetoNoGravado,Exento,IVA_21 Iva21,IVA_105 Iva105,IVA_27 Iva27,Percepcion_IVA PercepcionIva,Percepcion_IIBB PercepcionIibb,
                   Percepcion_Ganancias PercepcionGanancias,ImpuestosInternos,OtrosImpuestos,Total,ISNULL(Cuenta_Contable,'') CuentaContable,ISNULL(Proveedor_Nombre,'') ProveedorNombre,
                   ISNULL(Lector_Observaciones,'') LectorObservaciones,ISNULL(JsonResultado,'') JsonResultado
            FROM dbo.IA_Compras_CAB WHERE ID=7
            """);
        ia.Items=(await cn.QueryAsync<InterfacesCompraIaDetalleItemDto>("""
            SELECT NroRenglon,Cantidad,ISNULL(Codigo_Articulo,'') CodigoArticulo,ISNULL(Descripcion,'') Descripcion,Importe_Lista ImporteLista,Dto1,Dto2,Importe_Neto ImporteNeto,IVA Iva,Total
            FROM dbo.IA_Compras_DET WHERE ID_CAB=7 ORDER BY NroRenglon
            """)).AsList();
        Assert.Equal("211010014",ia.CuentaContable.Trim());

        var reloj=System.Diagnostics.Stopwatch.StartNew();
        var avisos=new List<string>();
        var d=CompraCargaOrigenLectura.Convertir(ia,18,avisos);
        var renglones=await servicio.AplicarRenglonesLeidosAsync(cn,ia,d,avisos,default);
        salida.WriteLine($"T matching {reloj.ElapsedMilliseconds} ms");
        foreach(var a in avisos)salida.WriteLine("AVISO: "+a);
        var pendientes=renglones.Where(r=>!r.Resuelto).ToList();
        salida.WriteLine($"Líneas con artículo: {d.Lineas.Count}; pendientes: {pendientes.Count}");
        Assert.Equal(renglones.Count-pendientes.Count,d.Lineas.Count);
        Assert.All(d.Lineas,l=>Assert.True(l.Articulo.Length>0&&l.Cantidad>0&&l.CostoUnitario>0));
        Assert.DoesNotContain(d.Lineas,l=>l.Descripcion.StartsWith("Servicios / gastos"));

        // Las pendientes tienen una propuesta de alta razonable (sin crear nada todavía).
        var contexto=await servicio.ContextoAsync();
        d.Unidad=contexto.Unidad;d.Motivo=contexto.Motivo;d.Deposito=contexto.Depositos[0].Codigo;
        var tipo=contexto.Tipos.First(t=>t.Codigo=="FCC"&&t.Externo&&t.Letras.Contains('A')&&(t.Unidad.Length==0||t.Unidad==d.Unidad));
        d.Tipo=tipo.Codigo;d.Talonario=tipo.Talonario;d.Letra="A";d.Sucursal="9999";d.Numero="90000077";
        d.Fecha=d.FechaContable=d.Vencimiento=new(2026,11,7);d.TotalDocumento=null;d.ServiciosGastosConfirmados=true;
        d.DocumentoRecibido=null;d.LecturaCompraId=null;d.PercepcionIibb=0;d.PercepcionIva=0;d.PercepcionGanancias=0;d.ImpuestoInterno=0;

        salida.WriteLine($"T antes de preparar {reloj.ElapsedMilliseconds} ms");
        // La pantalla informa el total calculado cuando el operador no lo corrige a mano; acá se hace igual.
        d.TotalDocumento=(await servicio.PrepararAsync(d)).Total;
        var vista=await servicio.PrepararAsync(d);
        salida.WriteLine($"T preparar {reloj.ElapsedMilliseconds} ms");
        foreach(var e in vista.Errores)salida.WriteLine("ERROR VISTA: "+e);
        Assert.Empty(vista.Errores);
        var neto=d.Lineas.Sum(l=>l.Neto);
        salida.WriteLine($"Neto de la compra {neto:N2}; total calculado {vista.Total:N2}");
        Assert.True(neto>0);

        const string foto="SELECT CONVERT(varchar(20),(SELECT COUNT(*) FROM C_MV_CPTE))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM MV_ASIENTOS))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM V_MV_STOCK))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM V_MA_PRECIOS))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM IA_ProveedorArticuloUdsXBl))";
        var antes=await cn.ExecuteScalarAsync<string>(foto);
        using(var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                var proveedor=await servicio.ProveedorAsync("211010014");
                var revision=CompraCargaPlanificador.Preparar(d,proveedor,contexto);Assert.Empty(revision.Errores);
                var registrada=await servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,d,CompraCargaService.HuellaRevision(d,proveedor,contexto,revision)));
                salida.WriteLine($"Registrada {registrada.Comprobante} asiento {registrada.Asiento} total {registrada.Total:N2}");
                Assert.Equal(neto,await cn.ExecuteScalarAsync<decimal>("SELECT ImporteInsumos FROM C_MV_CPTE WHERE ID=@CompraId",registrada,tx));
                var conStock=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK WHERE TC='FCC' AND IDCOMPROBANTE=@Comprobante",registrada,tx);
                salida.WriteLine($"Movimientos de stock: {conStock}");
                Assert.True(conStock>0);
                // Aprendizaje: cada artículo con código de proveedor queda en la lista de compra del proveedor y los bultos confirmados en la memoria.
                await servicio.AprenderEquivalenciasAsync(cn,d,default,tx);
                var alias=await cn.ExecuteScalarAsync<int>("""
                    SELECT COUNT(*) FROM V_MA_PRECIOS WHERE TipoLista='C' AND LTRIM(RTRIM(CUENTAPROVEEDOR))='211010014' AND LTRIM(RTRIM(ISNULL(IDARTICULOPROVEEDOR,'')))<>''
                    """,transaction:tx);
                salida.WriteLine($"Alias de compra del proveedor tras aprender: {alias}");
                Assert.True(alias>=d.Lineas.Count(l=>l.CodigoProveedor.Length>0)/2);
            }
            finally{tx.Rollback();}
        }
        Assert.Equal(antes,await cn.ExecuteScalarAsync<string>(foto));
    }

    [CompraIaSqlFact]
    public async Task PropuestaDeAltaUsaElArticuloParecidoYLosAnterioresDelProveedor()
    {
        var cadena=Environment.GetEnvironmentVariable("COMPRA_IA_TEST_SQL")!;
        Assert.StartsWith("PRUEBAS",new SqlConnectionStringBuilder(cadena).InitialCatalog,StringComparison.OrdinalIgnoreCase); // nunca contra una base de producción
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> f) where T:class
        {var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=f;return p;}
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var articulos=Proxy<IArticulosService>((m,_)=>m.Name switch{"GetNextCodigoAsync"=>Task.FromResult("999999"),_=>null});
        var servicio=new CompraCargaService(Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>cadena,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Administrador",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!,articulos);
        var renglon=new CompraRenglonResuelto{Numero=1,CodigoProveedor="99999",Descripcion="PIMENTON AHUMADO ALICANTE 25 GRS X 12",CantidadLeida=1,Total=9000,CostoLeido=750,IvaLeido=21};
        var p=await servicio.PropuestaAltaAsync(renglon,"211010014");
        salida.WriteLine($"Rubro {p.Rubro} Tipo {p.Tipo} Unidad {p.Unidad} IVA {p.Tasa} Costo {p.Costo} ({p.Origen})");
        Assert.Equal("999999",p.Codigo);Assert.True(p.Rubro.Length>0&&p.Tipo.Length>0&&p.Unidad.Length>0);
        Assert.Equal(21m,p.Tasa);Assert.Equal(750m,p.Costo);Assert.Equal("99999",p.CodigoProveedor);
        Assert.Contains("proveedor",p.Origen);
    }
}

public sealed class CompraImportadaArcaSqlTests(ITestOutputHelper salida)
{
    [CompraIaSqlFact]
    public async Task CompletarUnaImportacionDeArcaExigeTotalesIgualesYReemplazaElAsientoSinDejarRastro()
    {
        var cadena=Environment.GetEnvironmentVariable("COMPRA_IA_TEST_SQL")!;
        Assert.StartsWith("PRUEBAS",new SqlConnectionStringBuilder(cadena).InitialCatalog,StringComparison.OrdinalIgnoreCase); // nunca contra una base de producción
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> f) where T:class
        {var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=f;return p;}
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var servicio=new CompraCargaService(Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>cadena,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Administrador",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        await using var cn=new SqlConnection(cadena);await cn.OpenAsync();

        var pendientes=await servicio.ImportadasPendientesAsync();
        salida.WriteLine("Importadas pendientes: "+string.Join(" | ",pendientes.Select(p=>$"{p.Id} {p.Tipo} {p.Comprobante} {p.Proveedor.Trim()} {p.Total:N2}")));
        var importada=pendientes.Single(p=>p.Id==23509);
        var existente=await servicio.CompraExistenteAsync(importada.Cuenta,importada.Tipo,importada.Comprobante[^1..],importada.Comprobante[..4],importada.Comprobante.Substring(4,8));
        Assert.True(existente is {ImportadaArca:true,Anulada:false});

        var d=await servicio.PrepararDesdeImportadaAsync(23509);
        Assert.Equal(23509,d.ReemplazaImportadaId);Assert.Equal(importada.Comprobante,CompraCargaService.IdComprobanteCompra(d.Sucursal,d.Numero,d.Letra));
        var contexto=await servicio.ContextoAsync();
        d.Deposito=contexto.Depositos[0].Codigo;
        var tipo=contexto.Tipos.First(t=>t.Codigo==d.Tipo&&t.Externo&&t.Letras.Contains(d.Letra[0])&&(t.Unidad.Length==0||t.Unidad==d.Unidad));d.Talonario=tipo.Talonario;
        var articulo=(await servicio.BuscarArticulosAsync("",d.Unidad,d.Deposito)).First(a=>!a.Especial&&a.Unidad.Length>0&&!a.NoStock);
        d.Lineas=[new(){Articulo=articulo.Codigo,ArticuloHuella=articulo.Huella,UnidadArticulo=articulo.Unidad,Descripcion=articulo.Descripcion,Cantidad=1,CostoUnitario=431632.48m,Neto=431632.48m,Tasa=21,Cuenta=articulo.Cuenta}];
        d.ServiciosGastosConfirmados=true;d.TotalDocumento=null;
        d.TotalDocumento=(await servicio.PrepararAsync(d)).Total;
        var vista=await servicio.PrepararAsync(d);
        foreach(var e in vista.Errores)salida.WriteLine("ERROR VISTA: "+e);
        Assert.Empty(vista.Errores);
        var proveedor=await servicio.ProveedorAsync(d.Proveedor);
        salida.WriteLine($"Nuevo total {vista.Total:N2} vs ARCA {importada.Total:N2}");

        const string foto="SELECT CONVERT(varchar(20),(SELECT COUNT(*) FROM C_MV_CPTE))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM MV_ASIENTOS))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM V_MV_STOCK))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM ALFACORE_COMPRA_ANULACION))";
        var antes=await cn.ExecuteScalarAsync<string>(foto);
        using(var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                // 1) Con importes distintos a los de ARCA no se puede reemplazar.
                var distinto=System.Text.Json.JsonSerializer.Deserialize<CompraCargaBorrador>(System.Text.Json.JsonSerializer.Serialize(d))!;
                distinto.Lineas[0].CostoUnitario=400000m;distinto.Lineas[0].Neto=400000m;
                var revisionD=CompraCargaPlanificador.Preparar(distinto,proveedor,contexto);distinto.TotalDocumento=revisionD.Total;revisionD=CompraCargaPlanificador.Preparar(distinto,proveedor,contexto);Assert.Empty(revisionD.Errores);
                var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,distinto,CompraCargaService.HuellaRevision(distinto,proveedor,contexto,revisionD))));
                salida.WriteLine("Rechazo esperado: "+error.Message);
                Assert.Contains("no coinciden con lo informado por ARCA",error.Message);
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM C_MV_CPTE WHERE ID=23509",transaction:tx));
                // 2) Con los mismos importes reemplaza la importación por la compra con su detalle.
                var revision=CompraCargaPlanificador.Preparar(d,proveedor,contexto);
                var registrada=await servicio.RegistrarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),sesion.Id,d,CompraCargaService.HuellaRevision(d,proveedor,contexto,revision)));
                salida.WriteLine($"Registrada {registrada.Comprobante} asiento {registrada.Asiento} total {registrada.Total:N2}");
                Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM C_MV_CPTE WHERE ID=23509",transaction:tx));
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM C_MV_CPTE WHERE ID=@CompraId AND LTRIM(RTRIM(USUARIO))<>'IMPORTACION'",registrada,tx));
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ALFACORE_COMPRA_ANULACION WHERE CompraId=23509",transaction:tx));
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK WHERE TC='FCC' AND IDCOMPROBANTE=@Comprobante",registrada,tx));
            }
            finally{tx.Rollback();}
        }
        Assert.Equal(antes,await cn.ExecuteScalarAsync<string>(foto));
    }
}

public sealed class CompraPendientesRecepcionSqlTests(ITestOutputHelper salida)
{
    [CompraIaSqlFact]
    public async Task ListaLosDocumentosDeCompraSinConfirmarConSuLecturaYEstado()
    {
        var cadena=Environment.GetEnvironmentVariable("COMPRA_IA_TEST_SQL")!;
        Assert.StartsWith("PRUEBAS",new SqlConnectionStringBuilder(cadena).InitialCatalog,StringComparison.OrdinalIgnoreCase); // nunca contra una base de producción
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> f) where T:class
        {var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=f;return p;}
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var servicio=new CompraCargaService(Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>cadena,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Administrador",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var pendientes=await servicio.PendientesRecepcionAsync();
        foreach(var p in pendientes)salida.WriteLine($"{p.Id} | {p.Estado} | {p.Proveedor} | {p.Tipo} {p.Comprobante} | {p.Archivo} | {p.Total:N2} | lectura={p.TieneLectura}");
        Assert.NotEmpty(pendientes);
        Assert.Contains(pendientes,p=>p.Id==18&&p.TieneLectura&&p.Total>0);
        Assert.All(pendientes,p=>Assert.NotEqual("Registrado",p.Estado));
    }
}
