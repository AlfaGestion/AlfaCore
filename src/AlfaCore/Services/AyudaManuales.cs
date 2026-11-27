using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace AlfaCore.Services;

public sealed record AyudaTema(string Clave,string Nombre,string Recurso,string Grupo="General");
public sealed record AyudaArticulo(string Id,string Titulo,string Contenido);
public sealed record AyudaManual(AyudaTema Tema,string Contenido,IReadOnlyList<AyudaArticulo> Articulos);
public sealed record AyudaResultado(AyudaTema Tema,AyudaArticulo Articulo);

// Catálogo cerrado de documentación funcional. No lee rutas aportadas por el usuario ni datos contables.
// Un manual por módulo (Grupo = módulo del menú), con una sección (##) por pantalla y una (###) por acción.
public static class AyudaManuales
{
    public static IReadOnlyList<AyudaTema> Temas { get; }=Array.AsReadOnly(new[] {
        new AyudaTema("general","Uso general de AlfaCore","AlfaCore.Docs.manual_usuario.md","General"),
        new AyudaTema("archivos","Maestros, tablas y precios","AlfaCore.Docs.manual_archivos.md","Archivos"),
        new AyudaTema("seguridad","Seguridad y usuarios","AlfaCore.Docs.manual_seguridad.md","Archivos"),
        new AyudaTema("auditoria","Auditoría","AlfaCore.Docs.manual_auditoria_usuarios.md","Archivos"),
        new AyudaTema("compras","Compras","AlfaCore.Docs.manual_compras.md","Compras"),
        new AyudaTema("carga-compras","Carga de comprobantes de compras","AlfaCore.Docs.manual_carga_compras.md","Compras"),
        new AyudaTema("reportes","Reportes de compras y ventas","AlfaCore.Docs.manual_reportes_comerciales.md","Compras"),
        new AyudaTema("ventas-modulo","Ventas","AlfaCore.Docs.manual_ventas.md","Ventas"),
        new AyudaTema("ventas","Carga de comprobantes de ventas","AlfaCore.Docs.manual_carga_comprobantes.md","Ventas"),
        new AyudaTema("cobranzas","Cobranzas","AlfaCore.Docs.manual_cobranzas.md","Ventas"),
        new AyudaTema("mostrador","Punto de Venta: mostrador y delivery","AlfaCore.Docs.manual_punto_venta_mostrador.md","Ventas"),
        new AyudaTema("salon","Punto de Venta: salón y carta QR","AlfaCore.Docs.manual_punto_venta_salon.md","Ventas"),
        new AyudaTema("catalogos","Catálogos, carrito y recepción documental","AlfaCore.Docs.manual_catalogos_interfaces.md","Ventas"),
        new AyudaTema("caja","Caja y cierre de caja","AlfaCore.Docs.manual_cierre_caja.md","Caja y Bancos"),
        new AyudaTema("conciliacion","Conciliación bancaria","AlfaCore.Docs.manual_conciliacion_bancaria.md","Caja y Bancos"),
        new AyudaTema("tarjetas","Conciliación de tarjetas","AlfaCore.Docs.manual_conciliacion_tarjetas.md","Caja y Bancos"),
        new AyudaTema("stock","Stock","AlfaCore.Docs.manual_stock_movimientos.md","Stock"),
        new AyudaTema("contabilidad","Gestión contable","AlfaCore.Docs.manual_contabilidad.md","Gestión contable"),
        new AyudaTema("utilidades","Utilidades y configuración","AlfaCore.Docs.manual_utilidades.md","Utilidades"),
        new AyudaTema("consultas","Diseñador de Consultas","AlfaCore.Docs.manual_consultas.md","Utilidades"),
        new AyudaTema("conversaciones","Conversaciones","AlfaCore.Docs.manual_conversaciones.md","Conversaciones"),
        new AyudaTema("asistente","Asistente IA de Conversaciones","AlfaCore.Docs.manual_alfaknowledge_conversaciones.md","Conversaciones"),
        new AyudaTema("chat-web","Chat del sitio web","AlfaCore.Docs.manual_conversaciones_chat_web.md","Conversaciones"),
        new AyudaTema("crm","Oportunidades y cotizaciones","AlfaCore.Docs.manual_crm.md","CRM"),
        new AyudaTema("calendario","Calendario","AlfaCore.Docs.manual_calendario_eventos_repetidos.md","CRM"),
        new AyudaTema("tickets","Tickets y partes de horas","AlfaCore.Docs.manual_tickets_horas.md","Tickets y horas"),
        new AyudaTema("informes","Informes y comunicados","AlfaCore.Docs.manual_novedades.md","Informes"),
        new AyudaTema("logistica","Carga de viajes","AlfaCore.Docs.manual_logistica.md","Logística") });
    private static readonly Lazy<IReadOnlyDictionary<string,AyudaManual>> Manuales=new(()=>Temas.ToDictionary(t=>t.Clave,Cargar));
    private static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    public static AyudaManual Obtener(string tema)=>Manuales.Value.TryGetValue(tema,out var manual)
        ? manual : throw new ArgumentException("El tema de ayuda no está disponible.",nameof(tema));
    // Ayuda por pantalla: tema y artículo que se abre al pulsar "?". La ruta se busca contenida en la
    // dirección (o al final, si termina en $). El orden importa: la primera coincidencia gana, así que
    // lo más específico va antes (por ejemplo "/compras/articulos" antes que "/articulos").
    // Al documentar una pantalla nueva, agregarla acá; AyudaContextualTests verifica que el artículo exista.
    internal static readonly IReadOnlyList<(string Ruta,string Tema,string? Articulo)> Pantallas=Array.AsReadOnly(new (string,string,string?)[] {
        ("/ventas/punto-venta/salon/qr","salon","Imprimir los QR de las mesas"),
        ("/ventas/punto-venta/salon","salon","Trabajar con el salón"),
        ("/ventas/punto-venta/cocina","salon","Pantalla de cocina y barra"),
        ("/ventas/punto-venta/mostrador","mostrador","Buscar y cargar artículos"),
        ("/ventas/punto-venta/informe","mostrador","Informe del punto de venta"),
        ("/ventas/delivery","mostrador","Pedidos de delivery y take away"),
        ("/ventas/puntos-venta","mostrador","Antes de empezar"),
        ("/ventas/punto-venta","mostrador","Elegir el punto de venta"),
        ("conciliacion-bancaria","conciliacion",null),
        ("conciliacion-tarjetas","tarjetas",null),
        ("cobranzas","cobranzas",null),
        ("consultas","consultas",null),
        ("/caja-bancos/cierre","caja","Cierre de caja"),
        ("/caja-bancos","caja","Tablero de Caja y Bancos"),
        ("/contabilidad/plan-de-cuentas","contabilidad","Plan de cuentas"),
        ("/contabilidad/diario","contabilidad","Libro diario"),
        ("/contabilidad/subdiarios","contabilidad","Subdiarios"),
        ("/contabilidad/mayor","contabilidad","Mayor de cuentas"),
        ("/contabilidad/sumas-y-saldos","contabilidad","Sumas y saldos"),
        ("/contabilidad/balance-general","contabilidad","Balance general"),
        ("/contabilidad/asientos","contabilidad","Asientos"),
        ("/contabilidad/retenciones","contabilidad","Retenciones y percepciones"),
        ("/contabilidad/posicion-iva","contabilidad","Posición de IVA"),
        ("/contabilidad/control-arca","contabilidad","Control contra ARCA"),
        ("/contabilidad/fechas-de-cierre","contabilidad","Fechas de cierre"),
        ("/contabilidad/estudio-contable","contabilidad","Estudio contable"),
        ("/contabilidad/paquete-estudio","contabilidad","Paquete para el estudio: libros de IVA"),
        ("/contabilidad/configuracion","contabilidad","Configuración contable"),
        ("/libro-iva","contabilidad","Libros de IVA"),
        ("/contabilidad","contabilidad","Cómo usar los reportes contables"),
        ("/ventas/carga-comprobantes/visor","ventas","Ver comprobantes"),
        ("/portal-cliente/pedidos/nuevo","ventas-modulo","Pedidos del Portal Cliente"),
        ("/ventas/carga-comprobantes","ventas","Buscar pendientes y filtrar"),
        ("/ventas/reportes","reportes","Buscar y generar"),
        ("/ventas/configuracion","ventas-modulo","Configuración de ventas"),
        ("/ventas/reclamo-saldos","ventas-modulo","Reclamo de saldos"),
        ("/ventas/comparativo","ventas-modulo","Comparativo de ventas y compras"),
        ("/ventas/vendedores","ventas-modulo","Vendedores y unidades de negocio"),
        ("/ventas/unidades-negocio","ventas-modulo","Vendedores y unidades de negocio"),
        ("/ventas/clientes","ventas-modulo","Clientes del período"),
        ("/ventas/comprobantes","ventas-modulo","Comprobantes de ventas"),
        ("/ventas/rubros","ventas-modulo","Rubros, familias y artículos"),
        ("/ventas/familias","ventas-modulo","Rubros, familias y artículos"),
        ("/ventas/articulos","ventas-modulo","Rubros, familias y artículos"),
        ("/ventas-dashboard","ventas-modulo","Inicio de Ventas (tablero)"),
        ("carrito-compras","catalogos","Carrito de compras"),
        ("/catalogo","catalogos","Crear y publicar un catálogo"),
        ("/interfaces","catalogos","Recepción documental"),
        ("/compras/carga","carga-compras",null),
        ("/compras/reportes","reportes","Buscar y generar"),
        ("/compras/actividad","reportes","Actividad de compras"),
        ("/compras/pagos/ordenes","compras","Ver órdenes de pago"),
        ("/compras/pagos","compras","Registrar una orden de pago"),
        ("/compras/proveedores","compras","Proveedores del período"),
        ("/compras/comprobantes","compras","Comprobantes de compras"),
        ("/compras/rubros","compras","Rubros y familias"),
        ("/compras/familias","compras","Rubros y familias"),
        ("/compras/articulos","compras","Artículos comprados"),
        ("/compras/informesia","compras","InformesIA"),
        ("/compras","compras","Inicio de Compras (tablero)"),
        ("/stock/reportes","stock","Reportes de stock"),
        ("/stock/movimientos","stock","Cargar un movimiento"),
        ("/stock","stock","Tablero de stock"),
        ("/costos/perfiles","archivos","Perfiles de importación"),
        ("/costos/historial","archivos","Historial de actualizaciones"),
        ("/costos","archivos","Importar una lista de costos"),
        ("/tareas","general","Tareas"),
        ("/articulos/listas-precios","archivos","Listas de precios"),
        ("/articulos/configuracion","archivos","Configuración de artículos"),
        ("/articulos","archivos","Dar de alta un artículo"),
        ("/clientes","archivos","Dar de alta un cliente"),
        ("/proveedores","archivos","Dar de alta un proveedor"),
        ("/tecnicos","archivos","Dar de alta un técnico"),
        ("/contactos","archivos","Crear o editar un contacto"),
        ("/archivos/tablas","archivos","Tablas de referencia"),
        ("/archivos/chequeras","archivos","Chequeras"),
        ("/archivos/comprobantes","archivos","Tabla de comprobantes"),
        ("/archivos/bancos","archivos","Bancos"),
        ("/auditoria/error","auditoria","Errores del sistema"),
        ("/auditoria/comprobantes","auditoria","Auditoría de comprobantes"),
        ("/auditoria/usuarios","auditoria","Actividad de usuarios"),
        ("/auditoria","auditoria","Resumen de auditoría"),
        ("/seguridad/autorizacion-tareas","seguridad","Autorización de tareas"),
        ("/seguridad/perfiles","seguridad","Perfiles de permisos"),
        ("/seguridad/accesos-conversaciones","seguridad","Accesos a Conversaciones"),
        ("/usuarios","seguridad","Usuarios"),
        ("/conversaciones/plantillas","conversaciones","Plantillas"),
        ("/conversaciones/configuracion","conversaciones","Configuración de Conversaciones"),
        ("/conversaciones/estadisticas","conversaciones","Estadísticas"),
        ("/conversaciones/informes","conversaciones","Informes mensuales"),
        ("/conversaciones","conversaciones","La bandeja de conversaciones"),
        ("/cotizaciones/configuracion","crm","Configuración de cotizaciones"),
        ("/cotizaciones","crm","Armar una cotización"),
        ("partes-horas","tickets","Cargar un parte"),
        ("partes-de-horas","tickets","Cargar un parte"),
        ("/horas$","tickets","Cargar un parte"),
        ("/crm","crm","Crear y trabajar una oportunidad"),
        ("/calendario/reuniones","calendario","Reservas desde el link público (reuniones y capacitaciones)"),
        ("/calendario","calendario","Ver el calendario y crear un evento"),
        ("/tickets/seguimiento","tickets","Seguimiento de tickets"),
        ("/tickets/configuracion","tickets","Configuración de tickets"),
        ("/tickets","tickets","La mesa de ayuda"),
        ("/informes/novedades","informes","Escribir un comunicado (Informes → Comunicados)"),
        ("/novedades","informes","Dónde se ven"),
        ("/informes","informes","Artículos e informes"),
        ("/carga-viajes/tarifas","logistica","Tarifas"),
        ("/carga-viajes/reportes","logistica","Reportes de viajes"),
        ("/carga-viajes/liquidacion","logistica","Liquidaciones a choferes y fleteros"),
        ("/carga-viajes/choferes","logistica","Choferes, destinos y tipos de vehículo"),
        ("/carga-viajes/destinos","logistica","Choferes, destinos y tipos de vehículo"),
        ("/carga-viajes/tipo-vehiculo","logistica","Choferes, destinos y tipos de vehículo"),
        ("/carga-viajes/configuracion","logistica","Configuración"),
        ("/carga-viajes","logistica","Cargar un viaje"),
        ("/configuracion-general/logo","utilidades","Logo y estilo"),
        ("/configuracion-general/email","utilidades","Correo saliente"),
        ("/configuracion-general","utilidades","Datos de la empresa"),
        ("/configuracion-web-portal","utilidades","Portal web"),
        ("/documentos","utilidades","Diseñador de comprobantes"),
        ("/actualizaciones","utilidades","Actualizaciones"),
        ("/configuracion$","utilidades","Dónde está cada configuración") });

    public static string TemaParaRuta(string ruta)=>Ubicar(ruta).Tema;
    /// <summary>Id del artículo de la pantalla (para abrir la ayuda directo ahí), o null.</summary>
    public static string? SeccionParaRuta(string ruta)=>Ubicar(ruta).Articulo is { } titulo ? IdArticulo(titulo) : null;
    private static (string Tema,string? Articulo) Ubicar(string ruta)
    {
        var path=ruta.Split('?','#')[0].ToLowerInvariant().TrimEnd('/');
        foreach(var p in Pantallas)
        {
            var coincide=p.Ruta.EndsWith('$')
                ? path.EndsWith(p.Ruta[..^1],StringComparison.Ordinal)
                : path.Contains(p.Ruta,StringComparison.Ordinal);
            if(coincide)return (p.Tema,p.Articulo);
        }
        return ("general",null);
    }
    internal static string IdArticulo(string titulo)=>Regex.Replace(Normalizar(titulo),@"[^a-z0-9]+","-").Trim('-');
    /// <summary>
    /// Markdown a HTML. "[[bi:ticket-perforated]]" se dibuja como el ícono real del botón (Bootstrap Icons), así el
    /// manual y las respuestas de la IA muestran qué hay que tocar (2026-11-27). Sólo nombres de ícono válidos:
    /// el HTML del Markdown sigue deshabilitado.
    /// </summary>
    public static string Html(string contenido)=>MarcaIcono.Replace(Markdown.ToHtml(contenido,Pipeline),
        m=>$"<i class=\"bi bi-{m.Groups[1].Value} ayuda-icono\" aria-hidden=\"true\"></i>");
    internal static readonly Regex MarcaIcono=new(@"\[\[bi:([a-z0-9-]{1,60})\]\]",RegexOptions.Compiled);
    /// <summary>El texto sin las marcas de ícono (para buscar y para contar el largo).</summary>
    internal static string SinIconos(string texto)=>MarcaIcono.Replace(texto,"");
    public static IReadOnlyList<AyudaArticulo> Buscar(AyudaManual manual,string consulta)
    {
        var terminos=TerminosBusqueda(consulta);
        return manual.Articulos.Select(a=>(a,puntos:Puntaje(a,terminos))).Where(x=>x.puntos>0)
            .OrderByDescending(x=>x.puntos).Select(x=>x.a).ToArray();
    }
    /// <summary>
    /// Busca en todos los manuales, ordenado por cuántas palabras coinciden (más en el título); a igual puntaje,
    /// primero el manual actual. Antes exigía TODAS las palabras: "de dónde doy de alta los…" no encontraba nada
    /// porque "dónde", "doy" y "los" no están en ningún artículo (2026-11-26).
    /// </summary>
    public static IReadOnlyList<AyudaResultado> BuscarEnTodo(string consulta,string? temaActual=null,int maximo=40)
    {
        var terminos=TerminosBusqueda(consulta);
        if(terminos.Length==0)return [];
        // El manual en el que se está tiene 2 puntos de ventaja: lo que se busca desde una pantalla suele ser de ella.
        const int ventajaManualActual=2;
        return Temas.SelectMany(t=>Obtener(t.Clave).Articulos.Select(a=>(r:new AyudaResultado(t,a),puntos:Puntaje(a,terminos))))
            .Where(x=>x.puntos>0)
            .OrderByDescending(x=>x.puntos+(x.r.Tema.Clave==temaActual?ventajaManualActual:0))
            .ThenByDescending(x=>x.r.Tema.Clave==temaActual)
            .Take(maximo).Select(x=>x.r).ToArray();
    }

    // Palabras que no dicen de qué trata la consulta: con ellas un artículo no es más relevante.
    private static readonly HashSet<string> PalabrasVacias=new(("a al alfacore como con cual cuales cuando de del desde donde doy el en es esta estan este esto favor hacer hago hay la las lo los mas me mi mis necesito o para por porque puede puedo que quiero saber se ser seria si sistema su sus tengo un una unas uno unos usar y ya").Split(' '));

    /// <summary>Palabras de la consulta que sirven para buscar: sin acentos, sin palabras vacías y sin plural.
    /// La usan el buscador de la ayuda y la ayuda con IA.</summary>
    internal static string[] TerminosBusqueda(string consulta)=>Regex.Matches(Normalizar(consulta),@"[\p{L}\p{N}]+")
        .Select(m=>m.Value).Where(t=>t.Length>1 && !PalabrasVacias.Contains(t))
        .Select(t=>t.EndsWith("ciones",StringComparison.Ordinal) ? t[..^2] : t.Length>4 && t.EndsWith('s') ? t[..^1] : t)
        .Distinct().ToArray();

    /// <summary>3 puntos por palabra en el título, 1 si sólo está en el texto.</summary>
    internal static int Puntaje(AyudaArticulo articulo,IReadOnlyCollection<string> terminos)
    {
        var (titulo,texto)=TextoNormalizado(articulo);
        return terminos.Sum(t=>titulo.Contains(t,StringComparison.Ordinal) ? 3 : texto.Contains(t,StringComparison.Ordinal) ? 1 : 0);
    }

    // El texto sin acentos de cada artículo se calcula una vez: antes se normalizaban todos los manuales en cada
    // tecla del buscador, y la pantalla tardaba tanto en contestar que se perdían letras al escribir.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AyudaArticulo,Tuple<string,string>> Normalizados=new();
    private static (string Titulo,string Texto) TextoNormalizado(AyudaArticulo articulo)
    {
        var t=Normalizados.GetValue(articulo,a=>Tuple.Create(Normalizar(a.Titulo),Normalizar(a.Titulo+" "+SinIconos(a.Contenido))));
        return (t.Item1,t.Item2);
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
            // Un título sin texto propio (solo agrupa subsecciones) o el índice del documento no es una tarea.
            // Tampoco si sólo tiene una línea divisoria ("---"): "Alfa Gestión · Guía principal de uso" salía vacío.
            var propio=Regex.Replace(texto.ToString(),@"^\s*([-*_])(\s*\1){2,}\s*$","",RegexOptions.Multiline).Trim();
            if(titulo is null || propio.Length==0 || IdArticulo(titulo)=="indice") { texto.Clear();return; }
            var id=IdArticulo(titulo);
            var unico=id;var n=2;while(!ids.Add(unico))unico=id+"-"+n++;
            articulos.Add(new(unico,titulo,texto.ToString().Trim()));texto.Clear();
        }
        foreach(var linea in contenido.Replace("\r\n","\n").Split('\n'))
        {
            var encabezado=Regex.Match(linea,@"^#{2,3}\s+(.+)$");
            if(encabezado.Success) { Guardar();titulo=encabezado.Groups[1].Value.Trim(); }
            // La introducción (texto antes del primer ##) también es un artículo, con el título del manual: ahí
            // están los pasos de "Carga de comprobantes de compras" y la ayuda no los encontraba (2026-11-26).
            else if(titulo is null && Regex.Match(linea,@"^#\s+(.+)$") is { Success: true } principal) titulo=principal.Groups[1].Value.Trim();
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
