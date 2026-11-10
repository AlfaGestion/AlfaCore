using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Alta de artículos nuevos desde la factura leída (CmdAlta_Click / "Alta todo lo nuevo" de FrmPersonalizarCompra) y asignación manual de un artículo.
public sealed partial class CompraCargaService
{
    private sealed class ClasificacionArticulo{public string Rubro{get;set;}="";public string Tipo{get;set;}="";public string Unidad{get;set;}="";}

    /// <summary>
    /// Datos para dar de alta el artículo del renglón. Rubro, tipo y unidad se proponen, en este orden, del artículo más parecido,
    /// de lo que más usa el proveedor en sus artículos anteriores y de la configuración COMPRAS-ALTA-*; el operador puede cambiarlos.
    /// </summary>
    public Task<CompraAltaPropuesta> PropuestaAltaAsync(CompraRenglonResuelto renglon,string proveedor,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"PropuestaAlta",async(c,t)=>
        {
            var contexto=await Contexto(c,t);
            var articulos=articulosMaestro??throw new InvalidOperationException("El alta de artículos no está disponible en esta instalación.");
            var propuesta=new CompraAltaPropuesta{Codigo=await articulos.GetNextCodigoAsync(t),CodigoProveedor=renglon.CodigoProveedor.Length>25?renglon.CodigoProveedor[..25]:renglon.CodigoProveedor};
            propuesta.Descripcion=renglon.Descripcion.Length>100?renglon.Descripcion[..100].TrimEnd():renglon.Descripcion;
            propuesta.Tasa=renglon.IvaLeido is >0?renglon.IvaLeido.Value:contexto.TasasFiscales.FirstOrDefault();
            // Sin artículo todavía: las unidades por bulto salen de la lectura y de la descripción.
            var copia=new CompraRenglonResuelto{CantidadLeida=renglon.CantidadLeida,Total=renglon.Total,CostoLeido=renglon.CostoLeido,Descripcion=renglon.Descripcion};
            CalcularBulto(copia,new());
            propuesta.Costo=copia.Costo>0?copia.Costo:renglon.CostoLeido;
            var clasificacion=await ClasificacionSugeridaAsync(c,renglon,proveedor,t);
            propuesta.Rubro=clasificacion.Datos.Rubro;propuesta.Tipo=clasificacion.Datos.Tipo;propuesta.Unidad=clasificacion.Datos.Unidad;propuesta.Origen=clasificacion.Origen;
            return propuesta;
        },ct);

    private static async Task<(ClasificacionArticulo Datos,string Origen)> ClasificacionSugeridaAsync(SqlConnection c,CompraRenglonResuelto renglon,string proveedor,CancellationToken t)
    {
        const string columnas="LTRIM(RTRIM(ISNULL(a.IDRUBRO,''))) Rubro,LTRIM(RTRIM(ISNULL(a.IDTIPO,''))) Tipo,LTRIM(RTRIM(ISNULL(a.IDUNIDAD,''))) Unidad";
        // 1) El artículo más parecido de los candidatos (misma familia de producto).
        if(renglon.Candidatos.FirstOrDefault() is {} parecido)
        {
            var d=await c.QueryFirstOrDefaultAsync<ClasificacionArticulo>(Cmd($"SELECT TOP(1) {columnas} FROM dbo.V_MA_ARTICULOS a WHERE LTRIM(RTRIM(a.IDARTICULO))=@id AND LTRIM(RTRIM(ISNULL(a.IDRUBRO,'')))<>'';",new{id=parecido.Articulo.Codigo},t));
            if(d is not null)return (d,$"artículo parecido {parecido.Articulo.Codigo}");
        }
        // 2) Lo más usado por el proveedor.
        var delProveedor=await c.QueryFirstOrDefaultAsync<ClasificacionArticulo>(Cmd($"""
            SELECT TOP(1) {columnas} FROM dbo.V_MA_ARTICULOS a
            WHERE LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR,'')))=@proveedor AND ISNULL(a.SUSPENDIDO,0)=0 AND LTRIM(RTRIM(ISNULL(a.IDRUBRO,'')))<>'' AND LTRIM(RTRIM(ISNULL(a.IDTIPO,'')))<>'' AND LTRIM(RTRIM(ISNULL(a.IDUNIDAD,'')))<>''
            GROUP BY LTRIM(RTRIM(ISNULL(a.IDRUBRO,''))),LTRIM(RTRIM(ISNULL(a.IDTIPO,''))),LTRIM(RTRIM(ISNULL(a.IDUNIDAD,'')))
            ORDER BY COUNT(*) DESC;
            """,new{proveedor=(proveedor??"").Trim()},t));
        if(delProveedor is not null)return (delProveedor,"artículos anteriores del proveedor");
        // 3) Configuración y, si falta, el primer valor existente de cada maestro.
        var config=await ConfiguracionAsync(c,["COMPRAS-ALTA-RUBRO","COMPRAS-ALTA-TIPO","COMPRAS-ALTA-UNIDAD"],t);
        async Task<string> Primero(string clave,string tabla,string campo)
            =>config.GetValueOrDefault(clave) is {Length:>0} v?v:await c.QueryFirstOrDefaultAsync<string>(Cmd($"SELECT TOP(1) LTRIM(RTRIM({campo})) FROM dbo.{tabla} ORDER BY {campo};",null,t))??"";
        return (new ClasificacionArticulo{Rubro=await Primero("COMPRAS-ALTA-RUBRO","v_ta_rubros","IdRubro"),Tipo=await Primero("COMPRAS-ALTA-TIPO","v_ta_tipoArticulo","IdTipo"),Unidad=await Primero("COMPRAS-ALTA-UNIDAD","v_ta_unidad","IdUnidad")},"configuración (COMPRAS-ALTA-*)");
    }

    /// <summary>Rubros, tipos y unidades del maestro de artículos para el formulario de alta.</summary>
    public Task<ArticuloLookupDataDto> AltaOpcionesAsync(int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"AltaOpciones",(c,t)=>(articulosMaestro??throw new InvalidOperationException("El alta de artículos no está disponible en esta instalación.")).GetLookupDataAsync(t),ct);

    /// <summary>Da de alta el artículo (reutiliza el maestro de artículos: formato de códigos, IVA general, listas) y lo devuelve listo para la compra.</summary>
    public Task<CompraArticulo> AltaArticuloAsync(CompraAltaPropuesta p,string proveedor,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"AltaArticulo",async(c,t)=>
        {
            var articulos=articulosMaestro??throw new InvalidOperationException("El alta de artículos no está disponible en esta instalación.");
            var contexto=await Contexto(c,t);var retail=(await ConfiguracionAsync(c,["RETAIL","PIVA"],t));
            var esRetail=string.Equals(retail.GetValueOrDefault("RETAIL"),"SI",StringComparison.OrdinalIgnoreCase);
            var cuenta=(proveedor??"").Trim();
            var cfg=await ConfiguracionAsync(c,[cuenta+"_UTILIDAD",cuenta+"_REDONDEO",cuenta+"_TODASLASLISTAS",cuenta+"_SEGURO"],t);
            decimal Dec(string clave)=>decimal.TryParse(cfg.GetValueOrDefault(clave.ToUpperInvariant())?.Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out var v)?v:0m;
            var costo=decimal.Round(p.Costo,4);
            var seguro=Dec(cuenta+"_Seguro");if(seguro>0)costo=decimal.Round(costo*(1+seguro/100m),4);
            var utilidad=Dec(cuenta+"_Utilidad");var tasa=p.Exento?0:p.Tasa>0?p.Tasa:contexto.TasasFiscales.FirstOrDefault();
            // Retail: la clase 1 es el costo del proveedor y la clase 4 (venta) = costo + utilidad + IVA, redondeada (AltaEnTodasLasListasVenta del legacy).
            var precioVenta=esRetail&&utilidad>0?AjustarRedondeoPrecio(decimal.Round(costo*(1+utilidad/100m)*(1+tasa/100m),2),Dec(cuenta+"_Redondeo")):0m;
            var solicitud=new ArticuloSaveRequest{Codigo=p.Codigo.Trim(),CodigoBarra=p.CodigoBarra.Trim(),Descripcion=p.Descripcion.Trim(),RubroCodigo=p.Rubro.Trim(),MarcaCodigo=p.Tipo.Trim(),UnidadCodigo=p.Unidad.Trim(),
                CuentaProveedor=cuenta,CodigoArtProveedor=p.CodigoProveedor.Trim(),Costo=costo,Precio1=esRetail?costo:0m,Precio4=precioVenta,Precio=precioVenta,Utilidad=utilidad,TasaIva=tasa,Exento=p.Exento,
                IncluirEnTodasLasListas=esRetail&&precioVenta>0&&cfg.GetValueOrDefault((cuenta+"_TODASLASLISTAS").ToUpperInvariant()) is "SI" or "1" or "S"};
            var codigo=await articulos.SaveAsync(solicitud,t);
            var creado=(await LeerArticulosAsync(c,"",contexto.Unidad,contexto.Depositos.FirstOrDefault()?.Codigo??"",[codigo.Trim()],t,null,contexto.TasasFiscales.FirstOrDefault(),conStock:false)).FirstOrDefault()
                ??throw new InvalidOperationException("El artículo se creó pero no está disponible para la compra. Buscalo en el maestro de artículos.");
            return creado;
        },ct);

    /// <summary>Redondeo de precios de venta del legacy (AjustarRedondeoPrecioVenta): sube al próximo múltiplo del límite.</summary>
    internal static decimal AjustarRedondeoPrecio(decimal valor,decimal limite)
    {
        if(limite<=0)return valor;
        var resto=valor-decimal.Truncate(valor/limite)*limite;
        if(valor-decimal.Round(valor,0)==limite)return valor;
        var ajuste=limite-resto;if(ajuste==limite)ajuste=0;
        return valor+ajuste;
    }

    /// <summary>Asigna a mano un artículo a un renglón leído (elegido de los candidatos o de la búsqueda) y recalcula bulto, cantidad y costo.</summary>
    public Task<CompraRenglonResuelto> AsignarArticuloAsync(CompraRenglonResuelto renglon,string codigoArticulo,string proveedor,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"AsignarArticulo",async(c,t)=>
        {
            var contexto=await Contexto(c,t);
            var a=(await LeerArticulosAsync(c,"",contexto.Unidad,contexto.Depositos.FirstOrDefault()?.Codigo??"",[(codigoArticulo??"").Trim()],t,null,contexto.TasasFiscales.FirstOrDefault(),conStock:false)).FirstOrDefault()
                ??throw new InvalidOperationException("El artículo elegido ya no está disponible.");
            if(a.Especial||a.Unidad.Length==0)throw new InvalidOperationException("Ese artículo requiere series, lotes, equivalencias u otros impuestos. Cargalo en su circuito específico.");
            var memoria=await UdsRecordadasAsync(c,proveedor,t);
            renglon.Articulo=a;renglon.Metodo="Elegido por el operador";renglon.Motivo="";renglon.Aviso="";renglon.Puntaje=renglon.Candidatos.FirstOrDefault(x=>x.Articulo.Codigo==a.Codigo)?.Puntaje??0;
            CalcularBulto(renglon,memoria);
            var calculado=CompraCargaPlanificador.Moneda(renglon.Cantidad*renglon.Costo);
            if(renglon.Total is >0&&Math.Abs(renglon.Total.Value-calculado)>Math.Max(1m,renglon.Total.Value*0.001m))
                renglon.Aviso=$"El renglón informa {renglon.Total:N2} y cantidad × costo da {calculado:N2}; revisalo contra el original.";
            return renglon;
        },ct);

    private static async Task<Dictionary<string,decimal>> UdsRecordadasAsync(SqlConnection c,string proveedor,CancellationToken t)
        =>(await c.QueryAsync<UdsRecordadas>(Cmd("""
            IF OBJECT_ID('dbo.IA_ProveedorArticuloUdsXBl') IS NULL BEGIN SELECT CONVERT(nvarchar(50),'') Id,CONVERT(decimal(18,4),0) Uds WHERE 1=0; END
            ELSE BEGIN SELECT LTRIM(RTRIM(IdArticulo)) Id,CONVERT(decimal(18,4),UdsXBl) Uds FROM dbo.IA_ProveedorArticuloUdsXBl WHERE LTRIM(RTRIM(CuentaProveedor))=@proveedor AND UdsXBl>0; END
            """,new{proveedor=(proveedor??"").Trim()},t))).GroupBy(x=>x.Id).ToDictionary(g=>g.Key,g=>g.First().Uds);
}
