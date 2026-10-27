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
        new AyudaTema("consultas","Consultas","AlfaCore.Docs.manual_consultas.md"),
        new AyudaTema("conciliacion","Conciliación","AlfaCore.Docs.manual_conciliacion_bancaria.md"),
        new AyudaTema("tarjetas","Conciliación de tarjetas","AlfaCore.Docs.manual_conciliacion_tarjetas.md"),
        new AyudaTema("cobranzas","Cobranzas","AlfaCore.Docs.manual_cobranzas.md") });
    private static readonly Lazy<IReadOnlyDictionary<string,AyudaManual>> Manuales=new(()=>Temas.ToDictionary(t=>t.Clave,Cargar));
    private static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    public static AyudaManual Obtener(string tema)=>Manuales.Value.TryGetValue(tema,out var manual)
        ? manual : throw new ArgumentException("El tema de ayuda no está disponible.",nameof(tema));
    public static string TemaParaRuta(string ruta)
    {
        var path=ruta.Split('?')[0].ToLowerInvariant();
        if(path.Contains("conciliacion-bancaria"))return "conciliacion";
        if(path.Contains("conciliacion-tarjetas"))return "tarjetas";
        if(path.Contains("cobranzas"))return "cobranzas";
        if(path.Contains("consultas"))return "consultas";
        return "general";
    }
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
            var id=Regex.Replace(Normalizar(titulo),@"[^a-z0-9]+","-").Trim('-');
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
