using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace AlfaCore.Services;

public sealed record AyudaTema(string Clave,string Nombre,string Recurso);
public sealed record AyudaArticulo(string Id,string Titulo,string Contenido);
public sealed record AyudaManual(AyudaTema Tema,string Contenido,IReadOnlyList<AyudaArticulo> Articulos);

// Catálogo cerrado de documentación funcional. No lee rutas aportadas por el usuario ni datos contables.
public static class AyudaManuales
{
    public static IReadOnlyList<AyudaTema> Temas { get; }=Array.AsReadOnly(new[] {
        new AyudaTema("general","General","AlfaCore.Docs.manual_usuario.md"),
        new AyudaTema("contabilidad","Contabilidad","AlfaCore.Docs.manual_contabilidad.md"),
        new AyudaTema("ventas","Ventas: comprobantes","AlfaCore.Docs.manual_carga_comprobantes.md"),
        new AyudaTema("reportes","Reportes","AlfaCore.Docs.manual_reportes_comerciales.md"),
        new AyudaTema("seguridad","Seguridad","AlfaCore.Docs.manual_seguridad.md"),
        new AyudaTema("consultas","Consultas","AlfaCore.Docs.manual_consultas.md"),
        new AyudaTema("conciliacion","Conciliación","AlfaCore.Docs.manual_conciliacion_bancaria.md"),
        new AyudaTema("tarjetas","Conciliación de tarjetas asistida con IA","AlfaCore.Docs.manual_conciliacion_tarjetas.md"),
        new AyudaTema("cobranzas","Cobranzas","AlfaCore.Docs.manual_cobranzas.md") });
    private static readonly Lazy<IReadOnlyDictionary<string,AyudaManual>> Manuales=new(()=>Temas.ToDictionary(t=>t.Clave,Cargar));
    private static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    public static AyudaManual Obtener(string tema)=>Manuales.Value.TryGetValue(tema,out var manual)
        ? manual : throw new ArgumentException("El tema de ayuda no está disponible.",nameof(tema));
    // Ayuda por pantalla: tema (pestaña) y artículo que se abre al pulsar "?". El orden importa:
    // la primera coincidencia gana (por eso "visor" va antes que "carga-comprobantes").
    // Al documentar una pantalla nueva, agregarla acá; AyudaContextualTests verifica que el artículo exista.
    internal static readonly IReadOnlyList<(string Ruta,string Tema,string? Articulo)> Pantallas=Array.AsReadOnly(new (string,string,string?)[] {
        ("conciliacion-bancaria","conciliacion",null),
        ("conciliacion-tarjetas","tarjetas",null),
        ("cobranzas","cobranzas",null),
        ("consultas","consultas",null),
        ("/contabilidad/plan-de-cuentas","contabilidad","Plan de cuentas"),
        ("/contabilidad/diario","contabilidad","Libro diario"),
        ("/contabilidad/subdiarios","contabilidad","Subdiarios"),
        ("/contabilidad/mayor","contabilidad","Mayor de cuentas"),
        ("/contabilidad/sumas-y-saldos","contabilidad","Sumas y saldos"),
        ("/contabilidad/balance-general","contabilidad","Balance general"),
        ("/contabilidad/asientos","contabilidad","Asientos"),
        ("/contabilidad/retenciones","contabilidad","Retenciones y percepciones"),
        ("/libro-iva","contabilidad","Libros de IVA"),
        ("/contabilidad","contabilidad","Cómo usar los reportes contables"),
        ("/ventas/carga-comprobantes/visor","ventas","Ver comprobantes"),
        ("/ventas/carga-comprobantes","ventas","Buscar pendientes y filtrar"),
        ("/ventas/reportes","reportes","Buscar y generar"),
        ("/compras/reportes","reportes","Buscar y generar"),
        ("/compras/actividad","reportes","Actividad de compras"),
        ("/seguridad/autorizacion-tareas","seguridad","Autorización de tareas") });

    public static string TemaParaRuta(string ruta)=>Ubicar(ruta).Tema;
    /// <summary>Id del artículo de la pantalla (para abrir la ayuda directo ahí), o null.</summary>
    public static string? SeccionParaRuta(string ruta)=>Ubicar(ruta).Articulo is { } titulo ? IdArticulo(titulo) : null;
    private static (string Tema,string? Articulo) Ubicar(string ruta)
    {
        var path=ruta.Split('?','#')[0].ToLowerInvariant();
        foreach(var p in Pantallas)
            if(path.Contains(p.Ruta,StringComparison.Ordinal))return (p.Tema,p.Articulo);
        return ("general",null);
    }
    internal static string IdArticulo(string titulo)=>Regex.Replace(Normalizar(titulo),@"[^a-z0-9]+","-").Trim('-');
    public static string Html(string contenido)=>Markdown.ToHtml(contenido,Pipeline);
    public static IReadOnlyList<AyudaArticulo> Buscar(AyudaManual manual,string consulta)
    {
        var terminos=Regex.Matches(Normalizar(consulta),@"[\p{L}\p{N}]+")
            .Select(m=>m.Value).Where(t=>t.Length>1)
            .Select(t=>t.EndsWith("ciones",StringComparison.Ordinal) ? t[..^2] : t.Length>4 && t.EndsWith('s') ? t[..^1] : t).ToArray();
        return manual.Articulos.Where(a=>terminos.All(t=>Normalizar(a.Titulo+" "+a.Contenido).Contains(t,StringComparison.Ordinal))).ToArray();
    }
    private static AyudaManual Cargar(AyudaTema tema)
    {
        using var stream=typeof(AyudaManuales).Assembly.GetManifestResourceStream(tema.Recurso)
            ?? throw new InvalidOperationException($"Falta el manual de {tema.Nombre}.");
        using var reader=new StreamReader(stream,Encoding.UTF8);
        var contenido=reader.ReadToEnd();var articulos=new List<AyudaArticulo>();
        string? titulo=null;var texto=new StringBuilder();var ids=new HashSet<string>();
        void Guardar()
        {
            if(titulo is null)return;
            var id=IdArticulo(titulo);
            var unico=id;var n=2;while(!ids.Add(unico))unico=id+"-"+n++;
            articulos.Add(new(unico,titulo,texto.ToString().Trim()));texto.Clear();
        }
        foreach(var linea in contenido.Replace("\r\n","\n").Split('\n'))
        {
            var encabezado=Regex.Match(linea,@"^#{2,3}\s+(.+)$");
            if(encabezado.Success) { Guardar();titulo=encabezado.Groups[1].Value.Trim(); }
            else if(titulo is not null)texto.AppendLine(linea);
        }
        Guardar();return new(tema,contenido,articulos.AsReadOnly());
    }
    internal static string Normalizar(string texto)
    {
        var resultado=new StringBuilder();
        foreach(var c in texto.Normalize(NormalizationForm.FormD))
            if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)resultado.Append(char.ToLowerInvariant(c));
        return resultado.ToString().Normalize(NormalizationForm.FormC);
    }
}
