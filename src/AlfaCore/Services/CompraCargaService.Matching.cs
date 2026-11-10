using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Emparejamiento de los renglones leídos por la IA con el maestro de artículos. Réplica del motor con puntaje del legacy
// (BuscarArticuloAutomaticoCompra de modBusquedaAutomaticaArticulos.bas): código del proveedor / barras = 100, código dentro de la
// descripción = 70, descripción hasta 80, costo hasta 20, proveedor válido 20. El matcher externo de IA del legacy no se porta.
public sealed partial class CompraCargaService
{
    internal const int PuntajeMinimoPorDefecto=60;
    private const int PuntajeCodigo=100,PuntajeCodigoEnDescripcion=70,PuntajeDescripcionMaximo=80,PuntajeCostoMaximo=20,PuntajeDescripcionMinimo=35,MaximoCandidatos=20;

    private sealed class FilaCandidato
    {
        public string IdArticulo{get;set;}="";
        public decimal ScoreCodProv{get;set;}
        public decimal ScoreProv{get;set;}
        public decimal ScoreDesc{get;set;}
        public decimal ScoreCosto{get;set;}
        public decimal ScoreTotal{get;set;}
        public int MatchClave{get;set;}
        public int PorCodigo{get;set;}
        public int PorLista{get;set;}
        public int PorBarras{get;set;}
    }

    /// <summary>Regla de aceptación del legacy (CandidatoAceptableBusquedaAuto), evaluada en el orden de la consulta.</summary>
    internal static bool CandidatoAceptable(decimal scoreCodProv,decimal scoreDesc,decimal scoreTotal,bool matchClave,string palabraClave,int puntajeMinimo)
    {
        if(scoreCodProv>=PuntajeCodigo)return scoreTotal>=puntajeMinimo;
        if(palabraClave.Length==0||!matchClave||scoreDesc<PuntajeDescripcionMinimo)return false;
        return scoreTotal>=puntajeMinimo;
    }

    private static string Normalizado(string campo)
        =>$"REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(ISNULL({campo},'')),' ',''),'-',''),'.',''),'/',''),'\\',''),'_','')";

    /// <summary>Consulta de candidatos. Compatible con nivel de compatibilidad 100 (sin TRY_CONVERT ni STRING_SPLIT).</summary>
    internal static (string Sql,DynamicParameters Parametros) ConsultaCandidatos(string codigo,IReadOnlyList<string> palabras,string palabraClave,string[] proveedores,decimal costo,bool amplia,string[]? aliasIds=null,bool codigoEnDescripcion=true)
    {
        var p=new DynamicParameters();
        var cod=CompraArticuloTexto.NormalizarCodigo(codigo);if(cod=="*")cod="";
        var tieneCodigo=cod.Length>0;
        p.Add("cod",cod);p.Add("provs",proveedores.Length==0?new[]{"\u0001"}:proveedores);p.Add("costo",costo);
        p.Add("codNum",tieneCodigo&&CompraArticuloTexto.SoloDigitos(cod)&&cod.Length<=15?long.Parse(cod,CultureInfo.InvariantCulture):(long?)null,System.Data.DbType.Int64);
        var sinCeros=CompraArticuloTexto.QuitarCeros(cod);
        p.Add("codSinCeros",sinCeros.Length>=3&&sinCeros!=cod?sinCeros:"");
        var provIn="LTRIM(RTRIM(ISNULL(A.CUENTAPROVEEDOR,''))) IN @provs";
        var codProv=Normalizado("A.CODIGOARTPROVEEDOR");
        var condCodigo=tieneCodigo?$"(({codProv}=@cod) OR (@codNum IS NOT NULL AND CASE WHEN LTRIM(RTRIM(ISNULL(A.CODIGOARTPROVEEDOR,'')))<>'' AND LTRIM(RTRIM(A.CODIGOARTPROVEEDOR)) NOT LIKE '%[^0-9]%' AND LEN(LTRIM(RTRIM(A.CODIGOARTPROVEEDOR)))<=15 THEN CONVERT(bigint,LTRIM(RTRIM(A.CODIGOARTPROVEEDOR))) END=@codNum))":"0=1";
        var barras=tieneCodigo?"("+string.Join(" OR ",new[]{"CODIGOBARRA","CODIGOBARRA1","CODIGOBARRA2","CODIGOBARRA3","CODIGOBARRA4","CodigoBarraDun"}.Select(c=>$"{Normalizado("A."+c)}=@cod"))+")":"0=1";
        // Alias aprendido: artículos para los que este proveedor ya usó ese código en facturas anteriores (se resuelve antes, una sola vez, sobre la lista de compra).
        p.Add("aliasIds",aliasIds is {Length:>0}?aliasIds:new[]{""});
        var lista=tieneCodigo&&aliasIds is {Length:>0}?"LTRIM(RTRIM(A.IDARTICULO)) IN @aliasIds":"0=1";
        var descNorm="REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(ISNULL(A.DESCRIPCION,'')),' ',''),'-',''),'.',''),'/',''),'\\',''),'_',''),'(',''),')','')";
        p.Add("codLike","%"+CompraArticuloTexto.LikeLiteral(cod)+"%");p.Add("codSinCerosLike","%"+CompraArticuloTexto.LikeLiteral(sinCeros)+"%");
        var codEnDesc=tieneCodigo&&codigoEnDescripcion?$"({descNorm} LIKE @codLike ESCAPE '\\' OR (@codSinCeros<>'' AND {descNorm} LIKE @codSinCerosLike ESCAPE '\\'))":"0=1";
        var sumaPalabras=palabras.Count==0?"0":string.Join("+",palabras.Select((w,i)=>
        {
            p.Add("w"+i,"%"+CompraArticuloTexto.LikeLiteral(w)+"%");
            return $"CASE WHEN UPPER(ISNULL(A.DESCRIPCION,'')) COLLATE Latin1_General_CI_AI LIKE @w{i} ESCAPE '\\' THEN 1 ELSE 0 END";
        }));
        var tienePalabraClave=palabraClave.Length>0;
        if(tienePalabraClave)p.Add("clave","%"+CompraArticuloTexto.LikeLiteral(palabraClave)+"%");
        p.Add("umbral",palabras.Count==0?int.MaxValue:CompraArticuloTexto.Umbral(palabras.Count));
        var matchClave=tienePalabraClave?"CASE WHEN UPPER(ISNULL(A.DESCRIPCION,'')) COLLATE Latin1_General_CI_AI LIKE @clave ESCAPE '\\' THEN 1 ELSE 0 END":"0";
        var scoreDesc=palabras.Count==0?"0":$"CONVERT(decimal(9,2),({sumaPalabras})*{PuntajeDescripcionMaximo}.0/{palabras.Count})";
        var scoreCosto=costo>0?$"CONVERT(decimal(9,2),CASE WHEN ISNULL(A.COSTO,0)>0 THEN CASE WHEN (1-ABS(A.COSTO-@costo)/CASE WHEN A.COSTO>@costo THEN A.COSTO ELSE @costo END)*{PuntajeCostoMaximo}<0 THEN 0 ELSE (1-ABS(A.COSTO-@costo)/CASE WHEN A.COSTO>@costo THEN A.COSTO ELSE @costo END)*{PuntajeCostoMaximo} END ELSE 0 END)":"CONVERT(decimal(9,2),0)";
        var condicionCodigo=tieneCodigo?$"(({condCodigo} AND {provIn}) OR {lista} OR {barras} OR ({codEnDesc} AND {provIn}))":"0=1";
        var condicionDescripcion=palabras.Count==0?"0=1":amplia
            ?"("+string.Join(" OR ",palabras.Select((_,i)=>$"UPPER(ISNULL(A.DESCRIPCION,'')) COLLATE Latin1_General_CI_AI LIKE @w{i} ESCAPE '\\'"))+")"
            :$"({matchClave}=1 AND ({sumaPalabras})>=@umbral)";
        var sql=$"""
            SELECT TOP({MaximoCandidatos}) X.* FROM (
              SELECT LTRIM(RTRIM(A.IDARTICULO)) IdArticulo,
                CONVERT(decimal(9,2),CASE WHEN (({condCodigo} AND {provIn}) OR {lista}) THEN {PuntajeCodigo} WHEN {barras} THEN {PuntajeCodigo} WHEN ({codEnDesc} AND {provIn}) THEN {PuntajeCodigoEnDescripcion} ELSE 0 END) ScoreCodProv,
                CONVERT(decimal(9,2),CASE WHEN {provIn} THEN 20 ELSE 0 END) ScoreProv,
                {scoreDesc} ScoreDesc,{scoreCosto} ScoreCosto,{matchClave} MatchClave,
                CASE WHEN {condCodigo} AND {provIn} THEN 1 ELSE 0 END PorCodigo,CASE WHEN {lista} THEN 1 ELSE 0 END PorLista,CASE WHEN {barras} THEN 1 ELSE 0 END PorBarras,
                A.DESCRIPCION
              FROM dbo.V_MA_ARTICULOS A WITH(NOLOCK)
              WHERE ISNULL(A.SUSPENDIDO,0)=0 AND ({condicionCodigo} OR {condicionDescripcion})
            ) X
            ORDER BY X.ScoreCodProv DESC,X.MatchClave DESC,X.ScoreDesc DESC,(X.ScoreCodProv+X.ScoreProv+X.ScoreDesc+X.ScoreCosto) DESC,X.ScoreProv DESC,X.DESCRIPCION;
            """;
        return (sql,p);
    }

    private sealed class ClaveValor{public string Clave{get;set;}="";public string Valor{get;set;}="";}
    private sealed class UdsRecordadas{public string Id{get;set;}="";public decimal Uds{get;set;}}
    private static decimal? NumeroIa(string? t)
        =>decimal.TryParse((t??"").Trim().Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)?n:null;

    private static async Task<Dictionary<string,string>> ConfiguracionAsync(SqlConnection c,IEnumerable<string> claves,CancellationToken t,SqlTransaction? tx=null)
    {
        var lista=claves.Select(x=>x.ToUpperInvariant()).Distinct().ToArray();
        var filas=await c.QueryAsync<ClaveValor>(Cmd("SELECT UPPER(LTRIM(RTRIM(CLAVE))) Clave,LTRIM(RTRIM(ISNULL(VALOR,''))) Valor FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @lista;",new{lista},t,tx));
        return filas.GroupBy(f=>f.Clave).ToDictionary(g=>g.Key,g=>g.First().Valor);
    }

    private sealed class AliasProveedor{public string Id{get;set;}="";public string Cod{get;set;}="";}

    /// <summary>
    /// Resuelve cada renglón de la lectura: artículo, puntaje, unidades por bulto, cantidad y costo unitario. Pocos viajes a la base por factura
    /// (alias del proveedor en una consulta, una por renglón para los candidatos y una sola lectura de todos los artículos), para que
    /// siga siendo ágil aunque el servidor SQL esté lejos.
    /// </summary>
    internal static async Task<List<CompraRenglonResuelto>> ResolverRenglonesAsync(SqlConnection c,IReadOnlyList<InterfacesCompraIaDetalleItemDto> items,string proveedor,string unidad,string deposito,decimal tasaPorDefecto,CancellationToken t)
    {
        proveedor=(proveedor??"").Trim();
        var config=await ConfiguracionAsync(c,["SCOREBUSQUEDAARTICULO","PROVEEDORESASOCIADOS_"+proveedor],t);
        var minimo=int.TryParse(config.GetValueOrDefault("SCOREBUSQUEDAARTICULO"),out var m)&&m>0?m:PuntajeMinimoPorDefecto;
        var proveedores=new List<string>();
        if(proveedor.Length>0)proveedores.Add(proveedor);
        proveedores.AddRange((config.GetValueOrDefault("PROVEEDORESASOCIADOS_"+proveedor)??"").Split(',',';',' ').Select(x=>x.Trim()).Where(x=>x.Length>0));
        var provs=proveedores.Distinct().ToArray();
        var memoria=proveedor.Length==0?new Dictionary<string,decimal>():await UdsRecordadasAsync(c,proveedor,t);
        // Alias aprendidos: el código que este proveedor usó en facturas anteriores para cada artículo (lista de compra), en una sola consulta.
        var alias=provs.Length==0?Enumerable.Empty<AliasProveedor>().ToLookup(x=>x.Cod,x=>x.Id)
            :(await c.QueryAsync<AliasProveedor>(Cmd($"SELECT LTRIM(RTRIM(P.IDARTICULO)) Id,{Normalizado("P.IDARTICULOPROVEEDOR")} Cod FROM dbo.V_MA_PRECIOS P WITH(NOLOCK) WHERE P.TipoLista='C' AND LTRIM(RTRIM(ISNULL(P.CUENTAPROVEEDOR,''))) IN @provs AND LTRIM(RTRIM(ISNULL(P.IDARTICULOPROVEEDOR,'')))<>'';",new{provs},t)))
                .ToLookup(x=>x.Cod,x=>x.Id,StringComparer.Ordinal);
        var resultado=new List<CompraRenglonResuelto>();var trabajo=new List<(CompraRenglonResuelto R,List<FilaCandidato> Filas)>();
        foreach(var it in items.OrderBy(i=>i.NroRenglon))
        {
            t.ThrowIfCancellationRequested();
            var cantidad=NumeroIa(it.Cantidad) is {} q&&q>0?q:1m;
            // Costo por unidad de factura: el neto informado; si falta, la lista con sus descuentos; si no, el total del renglón sobre la cantidad.
            var unitario=it.ImporteNeto is >0?it.ImporteNeto.Value
                :it.ImporteLista is >0?it.ImporteLista.Value*(1-(decimal)(it.Dto1??0)/100m)*(1-(decimal)(it.Dto2??0)/100m)
                :it.Total is >0?it.Total.Value/cantidad:0m;
            var r=new CompraRenglonResuelto{Numero=it.NroRenglon,CodigoProveedor=(it.CodigoArticulo??"").Trim(),Descripcion=(it.Descripcion??"").Trim(),CantidadLeida=cantidad,Total=it.Total,
                IvaLeido=it.Iva is {} iva?(decimal)iva:null,CostoLeido=decimal.Round(unitario,4)};
            resultado.Add(r);
            var palabras=CompraArticuloTexto.Palabras(r.Descripcion);var cod=CompraArticuloTexto.NormalizarCodigo(r.CodigoProveedor);
            if(cod.Length==0&&palabras.Count==0){r.Motivo="El renglón no tiene código ni descripción utilizables.";continue;}
            var aliasIds=cod.Length==0||cod=="*"?[]:alias[cod].Distinct().ToArray();
            var filas=new List<FilaCandidato>();
            // Primero lo normal; si no aparece ningún candidato, vale cualquiera de las palabras (FiltroLikeDescripcionArticuloAuto del legacy).
            foreach(var amplia in new[]{false,true})
            {
                var (sql,p)=ConsultaCandidatos(r.CodigoProveedor,palabras,CompraArticuloTexto.PalabraClave(r.Descripcion),provs,r.CostoLeido,amplia,aliasIds,true);
                filas=(await c.QueryAsync<FilaCandidato>(Cmd(sql,p,t))).AsList();
                if(filas.Count>0)break;
            }
            if(filas.Count==0){r.Motivo="No hay artículos parecidos en el maestro.";continue;}
            trabajo.Add((r,filas));
        }
        var articulos=new Dictionary<string,CompraArticulo>(StringComparer.OrdinalIgnoreCase);
        foreach(var tanda in trabajo.SelectMany(x=>x.Filas.Select(f=>f.IdArticulo)).Distinct().Chunk(100))
            foreach(var a in await LeerArticulosAsync(c,"",unidad,deposito,tanda,t,null,tasaPorDefecto,conStock:false))articulos[a.Codigo]=a;
        foreach(var (r,filas) in trabajo)SeleccionarCandidato(r,filas,articulos,minimo);
        // Un mismo artículo en dos renglones: se queda el que tiene mejor evidencia (código antes que descripción, luego puntaje) y el otro vuelve a revisión.
        foreach(var grupo in resultado.Where(x=>x.Articulo is not null).GroupBy(x=>x.Articulo!.Codigo).Where(g=>g.Count()>1))
            foreach(var perdedor in grupo.OrderByDescending(x=>RangoMetodo(x.Metodo)).ThenByDescending(x=>x.Puntaje).Skip(1).Where(x=>RangoMetodo(x.Metodo)<2).ToList())
            {
                perdedor.Motivo="El artículo propuesto ya está en otro renglón con mejor coincidencia; confirmá si es el mismo.";
                perdedor.Candidatos.Insert(0,new(perdedor.Articulo!,perdedor.Puntaje,perdedor.Metodo));perdedor.Articulo=null;perdedor.Puntaje=0;perdedor.Metodo="";
            }
        foreach(var r in resultado.Where(x=>x.Articulo is not null))
        {
            CalcularBulto(r,memoria);
            var calculado=CompraCargaPlanificador.Moneda(r.Cantidad*r.Costo);
            if(r.Total is >0&&Math.Abs(r.Total.Value-calculado)>Math.Max(1m,r.Total.Value*0.001m))
                r.Aviso=$"El renglón informa {r.Total:N2} y cantidad × costo da {calculado:N2}; revisalo contra el original.";
        }
        return resultado;
    }
    private static int RangoMetodo(string metodo)=>metodo switch{"Código de proveedor" or "Alias aprendido" or "Código de barras"=>2,"Código en la descripción"=>1,_=>0};

    private static void SeleccionarCandidato(CompraRenglonResuelto r,List<FilaCandidato> filas,Dictionary<string,CompraArticulo> articulos,int minimo)
    {
        var clave=CompraArticuloTexto.PalabraClave(r.Descripcion);
        static string Metodo(FilaCandidato f)=>f.PorCodigo==1?"Código de proveedor":f.PorLista==1?"Alias aprendido":f.PorBarras==1?"Código de barras":f.ScoreCodProv>=PuntajeCodigoEnDescripcion?"Código en la descripción":"Descripción";
        static decimal Total(FilaCandidato f)=>f.ScoreCodProv+f.ScoreProv+f.ScoreDesc+f.ScoreCosto;
        r.Candidatos=filas.Where(f=>articulos.ContainsKey(f.IdArticulo)).Take(8).Select(f=>new CompraCandidato(articulos[f.IdArticulo],Total(f),Metodo(f))).ToList();
        var elegido=filas.FirstOrDefault(f=>articulos.ContainsKey(f.IdArticulo)&&CandidatoAceptable(f.ScoreCodProv,f.ScoreDesc,Total(f),f.MatchClave==1,clave,minimo));
        if(elegido is null){r.Motivo=$"Ningún candidato alcanza el puntaje mínimo ({minimo}); elegí uno de la lista o dalo de alta.";return;}
        var art=articulos[elegido.IdArticulo];
        if(art.Especial||art.Unidad.Length==0)
        {r.Motivo=$"El artículo {art.Codigo} requiere series, lotes u otro tratamiento especial; cargalo en su circuito específico.";return;}
        r.Articulo=art;r.Puntaje=Total(elegido);r.Metodo=Metodo(elegido);
    }

    /// <summary>
    /// Unidades por bulto, en este orden: relación importe del renglón / (cantidad × neto) que informa la propia lectura; memoria del
    /// operador por proveedor y artículo; "N x M" o "(24)" en la descripción; razón entre el costo leído y el del maestro (≥1,8 veces).
    /// </summary>
    private static void CalcularBulto(CompraRenglonResuelto r,Dictionary<string,decimal> memoria)
    {
        var cantidad=r.CantidadLeida;var unitario=r.CostoLeido;var total=r.Total is >0?r.Total.Value:unitario*cantidad;
        r.UnidadesPorBulto=1;r.OrigenBulto="";
        if(unitario>0&&cantidad>0&&r.Total is >0)
        {
            var razon=total/(unitario*cantidad);var entero=decimal.Round(razon,0);
            if(entero>1&&Math.Abs(razon-entero)<=0.02m)
            {
                // El neto ya es por unidad y el renglón trae el total de las unidades del bulto: no se vuelve a dividir.
                r.UnidadesPorBulto=entero;r.OrigenBulto="importe del renglón ÷ neto";r.Cantidad=cantidad*entero;r.Costo=decimal.Round(total/r.Cantidad,4);return;
            }
        }
        decimal uds=1;string origen="";
        if(r.Articulo is not null&&memoria.TryGetValue(r.Articulo.Codigo,out var recordado)&&recordado>0){uds=recordado;origen="memoria del operador";}
        else if(CompraArticuloTexto.BultoDesdeDescripcion(r.Descripcion) is {} d){uds=d.Unidades;origen=d.Origen.ToLowerInvariant();}
        else if(r.Articulo is not null&&r.Articulo.Costo>0&&unitario>=r.Articulo.Costo*1.8m)
        {
            var razon=unitario/r.Articulo.Costo;var cand=decimal.Round(razon,0);
            if(cand>1&&cand<=200&&Math.Abs(razon-cand)<=0.2m){uds=cand;origen="costo leído vs. costo del artículo";}
        }
        r.UnidadesPorBulto=uds;r.OrigenBulto=origen;r.Cantidad=cantidad*uds;
        // Si el total del renglón coincide con cantidad × neto (±2 %), el costo sale del total para que el neto cierre con la factura.
        r.Costo=r.Total is >0&&Math.Abs(r.Total.Value-unitario*cantidad)<=r.Total.Value*0.02m?decimal.Round(r.Total.Value/r.Cantidad,4):decimal.Round(unitario/uds,4);
    }
}
