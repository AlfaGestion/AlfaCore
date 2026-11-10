using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    public Task<IReadOnlyList<CompraArticulo>> BuscarArticulosAsync(string texto,string unidad,string deposito,int? baseEsperada=null,CancellationToken ct=default,string proveedor="")
        =>Ejecutar<IReadOnlyList<CompraArticulo>>(baseEsperada,"BuscarArticulos",async(c,t)=>
        {
            var contexto=await Contexto(c,t);
            if(texto.Length>100||!contexto.Unidades.Any(u=>u.Codigo==unidad)||deposito.Length>0&&!contexto.Depositos.Any(d=>d.Codigo==deposito))
                throw new InvalidOperationException("Revisá el texto, la unidad y el depósito para buscar artículos.");
            return await LeerArticulosAsync(c,texto.Trim(),unidad,deposito,[],t,tasaPorDefecto:contexto.TasasFiscales.FirstOrDefault(),proveedor:proveedor);
        },ct);

    /// <summary>Código exacto como V_mv_Cpra: primero IDARTICULO, luego código de barras y por último el código del artículo en el proveedor elegido.</summary>
    public Task<CompraArticulo?> ResolverCodigoArticuloAsync(string codigo,string unidad,string deposito,string proveedor,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar<CompraArticulo?>(baseEsperada,"ResolverCodigoArticulo",async(c,t)=>
        {
            var contexto=await Contexto(c,t);
            codigo=(codigo??"").Trim();
            if(codigo.Length is 0 or >100||!contexto.Unidades.Any(u=>u.Codigo==unidad)||deposito.Length>0&&!contexto.Depositos.Any(d=>d.Codigo==deposito))
                throw new InvalidOperationException("Revisá el código, la unidad y el depósito para buscar artículos.");
            string[] criterios=[
                "LTRIM(RTRIM(a.IDARTICULO))=@codigo",
                "LTRIM(RTRIM(ISNULL(a.CODIGOBARRA,'')))=@codigo",
                "@proveedor<>'' AND LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR,'')))=@proveedor AND LTRIM(RTRIM(ISNULL(a.CodigoArtProveedor,'')))=@codigo"];
            foreach(var criterio in criterios)
            {
                var id=await c.QueryFirstOrDefaultAsync<string>(Cmd($"SELECT TOP(1) LTRIM(RTRIM(a.IDARTICULO)) FROM dbo.V_MA_ARTICULOS a WHERE ISNULL(a.SUSPENDIDO,0)=0 AND {criterio} ORDER BY a.IDARTICULO;",new{codigo,proveedor=(proveedor??"").Trim()},t,null));
                if(id is null)continue;
                return (await LeerArticulosAsync(c,"",unidad,deposito,[id],t,null,contexto.TasasFiscales.FirstOrDefault())).FirstOrDefault();
            }
            return null;
        },ct);

    private sealed class StockArticulo{public string Id{get;set;}="";public decimal Cantidad{get;set;}}

    internal static async Task<List<CompraArticulo>> LeerArticulosAsync(SqlConnection c,string texto,string unidad,string deposito,string[] codigos,CancellationToken ct,SqlTransaction? tx=null,decimal tasaPorDefecto=0,string proveedor="",bool conStock=true)
    {
        var literal=texto.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");
        var filas=(await c.QueryAsync<CompraArticulo>(Cmd($"""
            SELECT TOP(100) LTRIM(RTRIM(a.IDARTICULO)) Codigo,a.DESCRIPCION Descripcion,LTRIM(RTRIM(ISNULL(a.IDUNIDAD,''))) Unidad,
              CONVERT(decimal(18,4),ISNULL(a.COSTO,0)) Costo,CONVERT(decimal(9,4),ISNULL(a.TasaIVA,0)) Tasa,
              CONVERT(bit,ISNULL(a.EXENTO,0)) Exento,CONVERT(bit,ISNULL(a.NO_CONTROLA_STOCK,0)) NoStock,
              LTRIM(RTRIM(ISNULL(a.CuentaContable,''))) Cuenta,
              CONVERT(bit,CASE WHEN ISNULL(a.USASERIE,0)=1 OR ISNULL(a.USALOTE,0)=1 OR ISNULL(a.UsaTalle,0)=1
                OR ISNULL(a.PideEquivalencia,0)=1 OR ISNULL(a.PideMedidas,0)=1 OR ISNULL(a.EnComodato,0)=1
                OR ISNULL(a.IMPUESTOS,0)<>0 OR ISNULL(a.ITC,0)<>0 OR LTRIM(RTRIM(ISNULL(a.IdPercepcion,'')))<>''
                OR LTRIM(RTRIM(ISNULL(a.Moneda,''))) NOT IN ('','1')
                OR LTRIM(RTRIM(ISNULL(a.UD_STOCK,''))) NOT IN ('',LTRIM(RTRIM(a.IDUNIDAD))) THEN 1 ELSE 0 END) Especial,
              CONVERT(decimal(18,4),0) Stock,a.IDARTICULO IdCrudo
            FROM dbo.V_MA_ARTICULOS a WHERE ISNULL(a.SUSPENDIDO,0)=0 AND
              ((@porCodigo=1 AND LTRIM(RTRIM(a.IDARTICULO)) IN @codigos) OR (@porCodigo=0 AND
                (@texto='' OR LTRIM(RTRIM(a.IDARTICULO)) LIKE @patron ESCAPE '\'
                  OR a.DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @patron ESCAPE '\' OR a.CODIGOBARRA LIKE @patron ESCAPE '\')))
            ORDER BY CASE WHEN LTRIM(RTRIM(a.IDARTICULO))=@texto OR LTRIM(RTRIM(a.CODIGOBARRA))=@texto OR (@proveedor<>'' AND LTRIM(RTRIM(ISNULL(a.CodigoArtProveedor,'')))=@texto AND LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR,'')))=@proveedor) THEN 0 ELSE 1 END,a.DESCRIPCION,a.IDARTICULO;
            """,new{texto,unidad,deposito,proveedor=(proveedor??"").Trim(),porCodigo=codigos.Length>0,codigos,patron="%"+literal+"%"},ct,tx))).AsList();
        // El stock se calcula una sola vez para todos los resultados (una consulta agrupada); una subconsulta por artículo tardaba decenas de segundos.
        if(conStock&&filas.Count>0)
        {
            var stocks=(await c.QueryAsync<StockArticulo>(Cmd("""
                SELECT s.IDArticulo Id,CONVERT(decimal(18,4),SUM(ISNULL(s.Cantidad,0))) Cantidad FROM dbo.V_MV_STOCK s
                WHERE s.IDArticulo IN @ids AND ISNULL(s.Anulado,0)=0 AND LTRIM(RTRIM(s.UNEGOCIO))=@unidad AND (@deposito='' OR LTRIM(RTRIM(s.IdDeposito))=@deposito)
                GROUP BY s.IDArticulo;
                """,new{ids=filas.Select(f=>f.IdCrudo).ToArray(),unidad,deposito},ct,tx))).ToDictionary(x=>x.Id,x=>x.Cantidad);
            foreach(var f in filas)if(stocks.TryGetValue(f.IdCrudo,out var cantidad))f.Stock=cantidad;
        }
        // Como V_mv_Cpra.frm (~12093): un artículo gravado sin alícuota en el maestro usa la general (PIVA).
        foreach(var a in filas)if(!a.Exento&&a.Tasa==0&&tasaPorDefecto>0)a.Tasa=tasaPorDefecto;
        foreach(var a in filas)a.Huella=HashCompra(JsonSerializer.Serialize(new{a.Codigo,a.Descripcion,a.Unidad,a.Costo,a.Tasa,a.Exento,a.NoStock,a.Especial,a.Cuenta}));
        return filas;
    }

    private static async Task<List<CompraArticulo>> ValidarArticulosDirectosAsync(SqlConnection c,CompraCargaBorrador d,CompraCargaContexto contexto,CancellationToken ct,SqlTransaction? tx=null)
    {
        var lineas=d.Lineas.Where(l=>l.Articulo.Length>0).ToList();
        if(lineas.Count==0||d.CompraOrigenId is not null)return [];
        if(!contexto.Depositos.Any(dep=>dep.Codigo==d.Deposito))throw new InvalidOperationException("Elegí un depósito autorizado para registrar los artículos.");
        var articulos=await LeerArticulosAsync(c,"",d.Unidad,d.Deposito,lineas.Select(l=>l.Articulo.Trim()).Distinct().ToArray(),ct,tx,contexto.TasasFiscales.FirstOrDefault(),conStock:false);
        foreach(var l in lineas)
        {
            var a=articulos.SingleOrDefault(a=>a.Codigo==l.Articulo.Trim());
            if(a is null||a.Especial||a.Unidad.Length==0)throw new InvalidOperationException($"El artículo {l.Articulo} no está disponible o requiere series, lotes, equivalencias o impuestos especiales.");
            if(l.ArticuloHuella!=a.Huella||l.UnidadArticulo!=a.Unidad)throw new InvalidOperationException($"El artículo {l.Articulo} cambió. Volvé a buscarlo antes de registrar.");
            if(l.Cantidad<=0||l.Cantidad>9999999||decimal.Round(l.Cantidad,4)!=l.Cantidad||l.CostoUnitario is not {} costo||costo<=0||costo>999999999||decimal.Round(costo,4)!=costo||CompraCargaPlanificador.Moneda(costo*l.Cantidad)!=l.Neto)
                throw new InvalidOperationException($"Revisá cantidad, costo unitario y neto del artículo {l.Articulo}; se admiten hasta cuatro decimales en cantidad y costo.");
            if((a.Exento?"Exento":"Gravado")!=l.Tratamiento)throw new InvalidOperationException($"Revisá el tratamiento fiscal del artículo {l.Articulo}.");
            if(l.Cuenta.Length==0&&a.Cuenta.Length>0)l.Cuenta=a.Cuenta;
        }
        return articulos;
    }
}
