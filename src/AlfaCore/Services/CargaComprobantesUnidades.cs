using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Conversiones directas/inversas de Rt_Stock.CantidadEquivalente2; no usa dispositivos ni altera maestros.</summary>
public sealed class CargaComprobantesUnidades
{
    public sealed record Equivalencia(string Articulo, string Unidad, string Equivalente, decimal Coeficiente);
    public sealed record Cantidades(decimal Unidades, decimal Bultos, decimal Kilos, decimal MetrosCubicos);
    private string unidadBl = "", unidadKg = "", unidadM3 = "";
    private List<Equivalencia> equivalencias = [];

    public static async Task<CargaComprobantesUnidades> LeerAsync(SqlConnection cn, IEnumerable<string> articulos,
        CancellationToken ct, SqlTransaction? tx = null)
    {
        using var datos = await cn.QueryMultipleAsync(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) Clave,
                COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux),'') Valor
            FROM dbo.TA_CONFIGURACION WHERE CLAVE IN ('UNIDADDEMEDIDA_BL','UNIDADDEMEDIDA_KG','UNIDADDEMEDIDA_M3');
            SELECT LTRIM(RTRIM(IDARTICULO)) Articulo,LTRIM(RTRIM(IDUNIDAD)) Unidad,
                LTRIM(RTRIM(IDUNIDAD_EQUIV)) Equivalente,CONVERT(decimal(28,8),COEFICIENTE) Coeficiente
            FROM dbo.S_TA_EQUIV WHERE LTRIM(RTRIM(IDARTICULO)) IN @Articulos;
            """,new { Articulos=articulos.Distinct().ToArray() },tx,cancellationToken:ct));
        var cfg=(await datos.ReadAsync<Config>()).ToDictionary(x=>x.Clave,x=>x.Valor);
        return new() { unidadBl=cfg.GetValueOrDefault("UNIDADDEMEDIDA_BL", ""),unidadKg=cfg.GetValueOrDefault("UNIDADDEMEDIDA_KG", ""),
            unidadM3=cfg.GetValueOrDefault("UNIDADDEMEDIDA_M3", ""),equivalencias=(await datos.ReadAsync<Equivalencia>()).ToList() };
    }

    public Cantidades Resolver(string articulo,string unidad,decimal cantidad,decimal? bultos)
    {
        if(bultos is { } bl)
        {
            if(bl<=0 || bl>9999999 || decimal.Round(bl,4)!=bl || unidadBl.Length==0)
                throw new CargaComprobantesReglaException("Ingresá bultos positivos con hasta cuatro decimales y configurá su unidad.");
            cantidad=Convertir(articulo,unidadBl,unidad,bl,equivalencias)
                ?? throw new CargaComprobantesReglaException($"El artículo {articulo} no tiene equivalencia entre bultos y su unidad de venta.");
        }
        if(cantidad<=0 || cantidad>9999999)
            throw new CargaComprobantesReglaException("La cantidad convertida está fuera del rango permitido.");
        return new(cantidad,Convertir(articulo,unidad,unidadBl,cantidad,equivalencias)??0,
            Convertir(articulo,unidad,unidadKg,cantidad,equivalencias)??0,Convertir(articulo,unidad,unidadM3,cantidad,equivalencias)??0);
    }

    public static decimal? Convertir(string articulo,string unidad,string destino,decimal cantidad,IReadOnlyList<Equivalencia> equivalencias)
    {
        static bool Igual(string a,string b)=>string.Equals(a.Trim(),b.Trim(),StringComparison.OrdinalIgnoreCase);
        if(string.IsNullOrWhiteSpace(unidad)||string.IsNullOrWhiteSpace(destino))return null;
        if(Igual(unidad,destino))return decimal.Round(cantidad,4,MidpointRounding.ToEven);
        var directas=equivalencias.Where(x=>Igual(x.Articulo,articulo)&&Igual(x.Unidad,unidad)&&Igual(x.Equivalente,destino)).ToList();
        var inversas=equivalencias.Where(x=>Igual(x.Articulo,articulo)&&Igual(x.Unidad,destino)&&Igual(x.Equivalente,unidad)).ToList();
        if(directas.Count>1||inversas.Count>1)
            throw new CargaComprobantesReglaException($"El artículo {articulo} tiene equivalencias duplicadas; revisá el maestro.");
        var e=directas.FirstOrDefault()??inversas.FirstOrDefault();
        if(e is null)return null;
        if(e.Coeficiente<=0)throw new CargaComprobantesReglaException($"El artículo {articulo} tiene un coeficiente de equivalencia inválido.");
        // La búsqueda directa tiene prioridad en el original. La operación se invierte para cantidades.
        return decimal.Round(directas.Count==1?cantidad*e.Coeficiente:cantidad/e.Coeficiente,4,MidpointRounding.ToEven);
    }
    private sealed class Config { public string Clave { get; set; }="";public string Valor { get; set; }=""; }
}
