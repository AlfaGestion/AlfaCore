using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Políticas de V_mv_vta para la carga directa; no altera el resolver compartido con POS/CRM.</summary>
public sealed class CargaComprobantesSeleccionPrecios
{
    private const string ConfigSql = """
        SELECT UPPER(LTRIM(RTRIM(CLAVE))) Clave,
            COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),VALORAUX),'') Valor
        FROM dbo.TA_CONFIGURACION
        WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('V_MODIFICACPRECIO','USALISTASDEPRECIOS','LISTAOBLIGATORIO',
            'FIJALISTASCONIVA','MAESTROARTICULOCONIVA',
            'NOM_PCLASE1','NOM_PCLASE2','NOM_PCLASE3','NOM_PCLASE4','NOM_PCLASE5','NOM_PCLASE6','NOM_PCLASE7','NOM_PCLASE8');
        """;

    private static bool Si(string? valor) => valor?.Trim().ToUpperInvariant() is "SI" or "SÍ" or "1" or "S" or "TRUE" or "T" or "Y";
    private static bool ClaseHabilitada(string configuracion) => configuracion.Trim().Length > 0 &&
        !configuracion.Trim().Equals("NO", StringComparison.OrdinalIgnoreCase);
    private static bool ClasePermitida(string clases, int clase) => string.IsNullOrWhiteSpace(clases) ||
        clases.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(clase.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static async Task<(Usuario Usuario, Dictionary<string,string> Config)> LeerAsync(
        SqlConnection cn, string usuario, CancellationToken ct, SqlTransaction? tx)
    {
        var user = await cn.QuerySingleOrDefaultAsync<Usuario>(new CommandDefinition("""
            SELECT CONVERT(bit,ISNULL(ModificaListaPrecios,0)) ModificaLista,
                ISNULL(LTRIM(RTRIM(CLASEPUSA)),'') Clases
            FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@Usuario);
            """, new { Usuario=usuario },tx,cancellationToken:ct))
            ?? throw new CargaComprobantesReglaException("El usuario no está definido en la base activa.");
        var config=(await cn.QueryAsync<Config>(new CommandDefinition(ConfigSql,transaction:tx,cancellationToken:ct)))
            .ToDictionary(x=>x.Clave,x=>x.Valor,StringComparer.OrdinalIgnoreCase);
        return (user,config);
    }

    public async Task CargarCatalogosAsync(SqlConnection cn,CargaComprobantesContexto contexto,CancellationToken ct,SqlTransaction? tx=null)
    {
        var (usuario,cfg)=await LeerAsync(cn,contexto.Usuario,ct,tx);
        contexto.PuedeCambiarLista=usuario.ModificaLista && Si(cfg.GetValueOrDefault("USALISTASDEPRECIOS"));
        contexto.PuedeCambiarClase=ClaseHabilitada(cfg.GetValueOrDefault("V_MODIFICACPRECIO",""));
        contexto.ClasesPrecio=Enumerable.Range(1,8)
            .Where(n=>cfg.ContainsKey($"NOM_PCLASE{n}") && ClasePermitida(usuario.Clases,n))
            .Select(n=>new CargaComprobantesOpcionFiltro { Codigo=n.ToString(),Descripcion=cfg[$"NOM_PCLASE{n}"] }).ToList();
        contexto.ListasPrecio=(await cn.QueryAsync<CargaComprobantesOpcionFiltro>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(IdLista)) Codigo,LTRIM(RTRIM(Nombre)) Descripcion
            FROM dbo.V_MA_PRECIOSCAB WHERE LTRIM(RTRIM(TipoLista))='V'
                AND (VigenciaDesde IS NULL OR CONVERT(date,VigenciaDesde)<=CONVERT(date,GETDATE()))
                AND (VigenciaHasta IS NULL OR CONVERT(date,VigenciaHasta)>=CONVERT(date,GETDATE()))
            ORDER BY Nombre,IdLista;
            """,transaction:tx,cancellationToken:ct))).ToList();
    }

    public async Task<CrmCotizacionPricingContextDto> ResolverAsync(SqlConnection cn,string usuario,
        CargaComprobantesDirecta draft,CancellationToken ct,SqlTransaction? tx=null)
    {
        if (draft.Tc is not ("FC" or "FP" or "ND" or "NC"))
            throw new CargaComprobantesReglaException("Tipo de comprobante no admitido.");
        if(draft.Tc=="NC" && (draft.ListaPrecio is not null || draft.ClasePrecio is not null))
            throw new CargaComprobantesReglaException("La nota de crédito conserva los precios de su factura asociada; no permite cambiar lista ni clase.");
        var pricing=await new ArticuloPrecioResolverService().ResolveContextAsync(cn,draft.Cliente,ct,tx);
        if(draft.Tc=="NC") return pricing;
        var (user,cfg)=await LeerAsync(cn,usuario,ct,tx);
        var usaListas=Si(cfg.GetValueOrDefault("USALISTASDEPRECIOS"));
        if(draft.ListaPrecio is not null)
        {
            if(!user.ModificaLista || !usaListas)
                throw new CargaComprobantesReglaException("Tu usuario no puede cambiar la lista de precios o el uso de listas está deshabilitado.");
            pricing.IdLista=draft.ListaPrecio.Trim();
        }
        if(draft.ClasePrecio is { } clase)
        {
            if(!ClaseHabilitada(cfg.GetValueOrDefault("V_MODIFICACPRECIO","")))
                throw new CargaComprobantesReglaException("La configuración V_ModificaCPrecio no permite cambiar la clase de precio.");
            if(clase is <1 or >8)
                throw new CargaComprobantesReglaException("Elegí una clase de precio entre 1 y 8; la venta al costo no está incorporada.");
            pricing.ClasePrecio=clase;
        }
        if(!ClasePermitida(user.Clases,pricing.ClasePrecio) || !cfg.ContainsKey($"NOM_PCLASE{pricing.ClasePrecio}"))
            throw new CargaComprobantesReglaException("La clase de precio no está definida o no está habilitada para tu usuario.");
        pricing.UsaListas=usaListas && !string.IsNullOrWhiteSpace(pricing.IdLista);
        if(!pricing.UsaListas && string.Equals(cfg.GetValueOrDefault("LISTAOBLIGATORIO"),"SI",StringComparison.OrdinalIgnoreCase))
            throw new CargaComprobantesReglaException("La configuración exige seleccionar una lista de precios.");
        if(pricing.UsaListas && await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.V_MA_PRECIOSCAB WHERE LTRIM(RTRIM(IdLista))=@Lista AND LTRIM(RTRIM(TipoLista))='V'
                AND (VigenciaDesde IS NULL OR CONVERT(date,VigenciaDesde)<=CONVERT(date,GETDATE()))
                AND (VigenciaHasta IS NULL OR CONVERT(date,VigenciaHasta)>=CONVERT(date,GETDATE()));
            """,new { Lista=pricing.IdLista.Trim() },tx,cancellationToken:ct))!=1)
            throw new CargaComprobantesReglaException("La lista de precios no existe, no es única o está fuera de vigencia.");
        pricing.PreciosConIva=Si(cfg.GetValueOrDefault(pricing.UsaListas?"FIJALISTASCONIVA":"MAESTROARTICULOCONIVA"));
        return pricing;
    }

    public async Task ValidarArticulosAsync(SqlConnection cn,string usuario,string tc,CrmCotizacionPricingContextDto pricing,
        IEnumerable<string> articulos,CancellationToken ct,SqlTransaction? tx=null)
    {
        if(!pricing.UsaListas || tc=="NC") return;
        var (user,_)=await LeerAsync(cn,usuario,ct,tx);
        // Utl.nin significa pertenencia: FC/ND exigen artículo en lista incluso con permiso para cambiarla.
        // En FP la exigencia depende de ModificaListaPrecios (V_mv_vta.ValidaArtLista).
        if(user.ModificaLista && tc=="FP") return;
        var codigos=articulos.Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var cantidad=await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(DISTINCT LTRIM(RTRIM(IdArticulo))) FROM dbo.V_MA_PRECIOS
            WHERE LTRIM(RTRIM(IdLista))=@Lista AND TipoLista='V' AND LTRIM(RTRIM(IdArticulo)) IN @Codigos;
            """,new { Lista=pricing.IdLista.Trim(),Codigos=codigos },tx,cancellationToken:ct));
        if(cantidad!=codigos.Length)
            throw new CargaComprobantesReglaException("Un artículo no pertenece a la lista de precios seleccionada.");
    }

    private sealed class Usuario { public bool ModificaLista {get;set;} public string Clases {get;set;}=""; }
    private sealed class Config { public string Clave {get;set;}=""; public string Valor {get;set;}=""; }
}
