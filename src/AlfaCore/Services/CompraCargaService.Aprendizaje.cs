using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Lo que el sistema aprende de una compra confirmada: el código que el proveedor le da a cada artículo (lista de compra del proveedor,
// como altaListaCompraRapida del legacy) y las unidades por bulto confirmadas (IA_ProveedorArticuloUdsXBl). La próxima factura del
// mismo proveedor encuentra esos artículos por código sin pasar por la descripción.
public sealed partial class CompraCargaService
{
    private sealed class PrecioExistente{public int Id{get;set;}public string Codigo{get;set;}="";}
    private sealed class ListaProveedor{public string? Lista{get;set;}public string? Nombre{get;set;}}

    /// <summary>Mejor esfuerzo, ya confirmada la compra: un error acá nunca debe deshacer ni ocultar el registro.</summary>
    internal async Task AprenderEquivalenciasAsync(SqlConnection c,CompraCargaBorrador d,CancellationToken t,SqlTransaction? tx=null)
    {
        try
        {
            var lineas=d.Lineas.Where(l=>l.Articulo.Length>0&&l.CodigoProveedor.Trim().Length>0).ToList();
            if(d.Proveedor.Trim().Length==0||lineas.Count==0&&!d.Lineas.Any(l=>l.Articulo.Length>0&&l.UnidadesPorBulto>1))return;
            var cuenta=d.Proveedor.Trim();var usuario=usuarios.GetCurrentUserName();
            var lista=await ListaComprasProveedorAsync(c,cuenta,t,tx);
            foreach(var l in lineas)
            {
                var codigoProveedor=CodigoPk.Format(l.CodigoProveedor.Trim().Length>25?l.CodigoProveedor.Trim()[..25]:l.CodigoProveedor.Trim(),25);
                var existente=await c.QueryFirstOrDefaultAsync<PrecioExistente>(Cmd("""
                    SELECT TOP(1) ID Id,LTRIM(RTRIM(ISNULL(IDARTICULOPROVEEDOR,''))) Codigo FROM dbo.V_MA_PRECIOS
                    WHERE TipoLista='C' AND LTRIM(RTRIM(IDARTICULO))=@articulo AND LTRIM(RTRIM(ISNULL(CUENTAPROVEEDOR,'')))=@cuenta ORDER BY ID;
                    """,new{articulo=l.Articulo.Trim(),cuenta},t,tx));
                if(existente is null)
                    await c.ExecuteAsync(Cmd("""
                        INSERT dbo.V_MA_PRECIOS(IDLISTA,NOMBRE,IDARTICULO,CONIVA,TIPOLISTA,COSTO,IDMONEDA,CUENTAPROVEEDOR,IDARTICULOPROVEEDOR,USUARIO)
                        VALUES(@lista,@nombre,@articulo,0,'C',@costo,@moneda,@cuenta,@codigo,@usuario);
                        """,new{lista=lista.Id,nombre=lista.Nombre,articulo=CodigoPk.Format(l.Articulo.Trim(),25),costo=l.CostoUnitario??0m,moneda=CodigoPk.Format("1",4),cuenta,codigo=codigoProveedor,usuario},t,tx));
                else if(existente.Codigo.Length==0)
                    await c.ExecuteAsync(Cmd("UPDATE dbo.V_MA_PRECIOS SET IDARTICULOPROVEEDOR=@codigo WHERE ID=@id;",new{codigo=codigoProveedor,id=existente.Id},t,tx));
            }
            foreach(var l in d.Lineas.Where(l=>l.Articulo.Length>0&&l.UnidadesPorBulto>1))
                await c.ExecuteAsync(Cmd("""
                    IF OBJECT_ID('dbo.IA_ProveedorArticuloUdsXBl') IS NOT NULL
                    BEGIN
                      UPDATE dbo.IA_ProveedorArticuloUdsXBl SET UdsXBl=@uds,FechaModificacion=GETDATE(),Usuario=@usuario WHERE LTRIM(RTRIM(CuentaProveedor))=@cuenta AND LTRIM(RTRIM(IdArticulo))=@articulo;
                      IF @@ROWCOUNT=0 INSERT dbo.IA_ProveedorArticuloUdsXBl(CuentaProveedor,IdArticulo,UdsXBl,Usuario) VALUES(@cuenta,@articuloFmt,@uds,@usuario);
                    END
                    """,new{cuenta,articulo=l.Articulo.Trim(),articuloFmt=CodigoPk.Format(l.Articulo.Trim(),25),uds=l.UnidadesPorBulto,usuario},t,tx));
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){await eventos.LogErrorAsync("CargaCompras","AprenderEquivalencias",ex,"No se pudieron guardar las equivalencias del proveedor.",ct:t);}
    }

    /// <summary>Lista de compra del proveedor: la asignada en su ficha, la que ya usa en V_MA_PRECIOS o una nueva con su nombre.</summary>
    private static async Task<(string Id,string Nombre)> ListaComprasProveedorAsync(SqlConnection c,string cuenta,CancellationToken t,SqlTransaction? tx=null)
    {
        var prov=await c.QueryFirstOrDefaultAsync<ListaProveedor>(Cmd("SELECT TOP(1) LTRIM(RTRIM(ISNULL(IdLista,''))) Lista,LTRIM(RTRIM(ISNULL(Razon_Social,''))) Nombre FROM dbo.Vt_Proveedores WHERE LTRIM(RTRIM(Codigo))=@cuenta;",new{cuenta},t,tx));
        var nombre=prov?.Nombre is {Length:>0} n?n:cuenta;if(nombre.Length>100)nombre=nombre[..100];
        var id=prov?.Lista??"";
        if(id.Length==0)id=await c.QueryFirstOrDefaultAsync<string>(Cmd("SELECT TOP(1) LTRIM(RTRIM(IdLista)) FROM dbo.V_MA_PRECIOS WHERE TipoLista='C' AND LTRIM(RTRIM(ISNULL(CUENTAPROVEEDOR,'')))=@cuenta ORDER BY ID;",new{cuenta},t,tx))??"";
        if(id.Length>0)return (CodigoPk.Format(id,4),nombre);
        // Sin lista: se crea con los últimos 4 dígitos de la cuenta; si ya está tomada, "LC" + minutos/segundos como el legacy.
        var cola=cuenta.Length>4?cuenta[^4..]:cuenta;var nueva=CodigoPk.Format(cola,4);
        for(var intento=0;intento<5;intento++)
        {
            var existe=await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.V_MA_PRECIOSCAB WHERE TipoLista='C' AND LTRIM(RTRIM(IdLista))=@id;",new{id=nueva.Trim()},t,tx));
            if(existe==0)break;
            nueva=CodigoPk.Format("LC"+(DateTime.Now.Minute+DateTime.Now.Second+intento).ToString("00"),4);
        }
        await c.ExecuteAsync(Cmd("INSERT dbo.V_MA_PRECIOSCAB(IDLISTA,NOMBRE,TIPOLISTA) VALUES(@id,@nombre,'C');",new{id=nueva,nombre},t,tx));
        return (nueva,nombre);
    }
}
