using Xunit;
using AlfaCore.Models;
using AlfaCore.Services;

namespace AlfaCore.Tests;

/// <summary>Reglas de permisos web (2026-11-18): "ve todo" igual en menú y pantallas, permisos explícitos y perfiles.</summary>
public class PermisosWebTests
{
    private static (bool Ok, IReadOnlySet<string> Keys)? Estado(bool ok, params string[] keys)
        => (ok, keys.ToHashSet(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void SinPermisosCargados_VeTodo_IgualQueElMenu()
        => Assert.True(PermissionService.ResolveAccess(Estado(true), "D016004"));

    [Fact]
    public void ConPermisosCargados_VeSoloLoTildado()
    {
        var estado = Estado(true, "D010185-WEB-CONVERSACIONES");
        Assert.True(PermissionService.ResolveAccess(estado, "D010185-WEB-CONVERSACIONES"));
        Assert.False(PermissionService.ResolveAccess(estado, "D016004"));
    }

    [Fact]
    public void SiNoSePuedenLeerLosPermisos_SeNiega()
        => Assert.False(PermissionService.ResolveAccess(Estado(false), "D016004"));

    [Fact]
    public void SinSesionDeUsuario_NoBloquea()
        => Assert.True(PermissionService.ResolveAccess(null, "D016004"));

    [Fact]
    public void Catalogo_ConfigurarYVerTodosSonExplicitos_PlantillasNo()
    {
        Assert.True(PermisosWebCatalogo.EsExplicito(PermisosWebCatalogo.ConversacionesConfigurar));
        Assert.True(PermisosWebCatalogo.EsExplicito(PermisosWebCatalogo.ConversacionesVerTodos));
        Assert.False(PermisosWebCatalogo.EsExplicito(PermisosWebCatalogo.ConversacionesPlantillas));
        Assert.False(PermisosWebCatalogo.EsExplicito("D016004"));

        Assert.Equal(PermisoWebAlmacen.Explicitos, PermisosWebCatalogo.AlmacenDe(PermisosWebCatalogo.ConversacionesConfigurar));
        Assert.Equal(PermisoWebAlmacen.ConversacionesVerTodos, PermisosWebCatalogo.AlmacenDe(PermisosWebCatalogo.ConversacionesVerTodos));
        Assert.Equal(PermisoWebAlmacen.Tareas, PermisosWebCatalogo.AlmacenDe(PermisosWebCatalogo.ConversacionesPlantillas));
        Assert.Equal(PermisoWebAlmacen.Tareas, PermisosWebCatalogo.AlmacenDe("D016004"));
    }

    [Fact]
    public void Catalogo_TodasLasAccionesCuelganDeConversaciones_YEstanEnElScript()
    {
        // Solo el código del script, sin los comentarios (que sí nombran TA_MENU/TA_TAREAS para explicar).
        var updates = Path.Combine(RepoRoot(), "src", "AlfaCore", "App_Data", "updates");
        var script = string.Join(Environment.NewLine,
            new[] { "2026-11-18-001__seguridad_permisos_perfiles_menu_web.sql", "2026-11-18-002__seguridad_accesos_conversaciones_menu_web.sql" }
                .SelectMany(f => File.ReadAllLines(Path.Combine(updates, f)))
                .Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        foreach (var accion in PermisosWebCatalogo.Acciones)
        {
            Assert.Equal(PermisosWebCatalogo.Conversaciones, accion.PadreClave);
            Assert.Contains(accion.Clave, script);
            Assert.True(accion.Clave.Length <= 50, accion.Clave);
        }
        Assert.Contains(PermisosWebCatalogo.PerfilesMenu, script);
        Assert.Contains(PermisosWebCatalogo.AccesosConversacionesMenu, script);
        Assert.DoesNotContain("TA_TAREAS", script);
        Assert.DoesNotContain("TA_MENU", script);
    }

    [Fact]
    public void AlTildarSoloUnPermisoExplicito_NoSeSumaElModulo()
    {
        // Si se sumara "Conversaciones" como opción del menú, el usuario pasaría a ver solo eso.
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PermisosWebCatalogo.ConversacionesConfigurar] = PermisosWebCatalogo.Conversaciones,
            [PermisosWebCatalogo.ConversacionesPlantillas] = PermisosWebCatalogo.Conversaciones,
            [PermisosWebCatalogo.Conversaciones] = "D"
        };
        var managed = parents.Keys.ToArray();

        var soloConfig = AutorizacionTareasService.ExpandSelectionWithAncestors(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermisosWebCatalogo.ConversacionesConfigurar }, parents, managed);
        Assert.Equal([PermisosWebCatalogo.ConversacionesConfigurar], soloConfig);

        var plantillas = AutorizacionTareasService.ExpandSelectionWithAncestors(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermisosWebCatalogo.ConversacionesPlantillas }, parents, managed);
        Assert.Contains(PermisosWebCatalogo.Conversaciones, plantillas);
    }

    [Fact]
    public void Arbol_TildarElModulo_NoArrastraLosExplicitosNiTocaLoDelPerfil()
    {
        var tree = new List<MenuAutorizacionItemDto>
        {
            Nodo(PermisosWebCatalogo.Conversaciones, "/conversaciones",
                Nodo(PermisosWebCatalogo.ConversacionesPlantillas, null, accion: true),
                Nodo(PermisosWebCatalogo.ConversacionesConfigurar, null, accion: true, explicito: true),
                Nodo("D-DEL-PERFIL", "/x", porPerfil: true, autorizado: true))
        };

        MenuAutorizacionArbol.Toggle(tree, tree, PermisosWebCatalogo.Conversaciones, true);
        Assert.True(MenuAutorizacionArbol.Find(tree, PermisosWebCatalogo.ConversacionesPlantillas)!.Autorizado);
        Assert.False(MenuAutorizacionArbol.Find(tree, PermisosWebCatalogo.ConversacionesConfigurar)!.Autorizado);

        MenuAutorizacionArbol.Toggle(tree, tree, PermisosWebCatalogo.Conversaciones, false);
        Assert.True(MenuAutorizacionArbol.Find(tree, "D-DEL-PERFIL")!.Autorizado);

        // Tildado directo, el explícito sí se da.
        MenuAutorizacionArbol.Toggle(tree, tree, PermisosWebCatalogo.ConversacionesConfigurar, true);
        Assert.True(MenuAutorizacionArbol.Find(tree, PermisosWebCatalogo.ConversacionesConfigurar)!.Autorizado);
    }

    [Fact]
    public void Arbol_ElFiltroMuestraLosPermisosDeAccionAunqueNoTenganRuta()
    {
        var tree = new List<MenuAutorizacionItemDto>
        {
            Nodo(PermisosWebCatalogo.Conversaciones, "/conversaciones",
                Nodo(PermisosWebCatalogo.ConversacionesConfigurar, null, accion: true, explicito: true))
        };

        var visible = MenuAutorizacionArbol.Filter(tree, "CONV-CONFIG");
        Assert.NotNull(MenuAutorizacionArbol.Find(visible, PermisosWebCatalogo.ConversacionesConfigurar));
    }

    [Fact]
    public void Engranaje_SinPermiso_NoOfreceLaConfiguracionDeConversaciones()
    {
        var todas = ConfiguracionCatalogo.Entradas;
        var sin = ConfiguracionCatalogo.SegunPermisoConversaciones(todas, puedeConfigurarConversaciones: false);
        Assert.DoesNotContain(sin, e => e.RutaBase == "/conversaciones/configuracion");
        Assert.Contains(sin, e => e.RutaBase == "/seguridad/perfiles");
        Assert.Same(todas, ConfiguracionCatalogo.SegunPermisoConversaciones(todas, puedeConfigurarConversaciones: true));
    }

    [Fact]
    public void BuscadorDeConfiguracion_NoApuntaASeccionesQueSeMudaron()
    {
        // Operación y accesos pasó a Seguridad e Informes a su pestaña (2026-11-18).
        Assert.DoesNotContain(ConfiguracionCatalogo.Entradas, e => e.Ruta.Contains("seccion=operacion-accesos", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ConfiguracionCatalogo.Entradas, e => e.Ruta.Contains("seccion=informes", StringComparison.OrdinalIgnoreCase));
        var accesos = Assert.Single(ConfiguracionCatalogo.Entradas, e => e.RutaBase == "/seguridad/accesos-conversaciones");
        Assert.Equal(ConfiguracionCatalogo.AreaUsuarios, accesos.Area);
        Assert.Contains(ConfiguracionCatalogo.Entradas, e => e.Ruta == "/conversaciones/configuracion?seccion=respuestas-rapidas");
    }

    [Fact]
    public void RespuestasRapidas_EsUnPermisoComunDeConversaciones()
    {
        var accion = PermisosWebCatalogo.Buscar(PermisosWebCatalogo.ConversacionesRespuestas);
        Assert.NotNull(accion);
        Assert.False(accion!.Explicito);
        Assert.Equal(PermisoWebAlmacen.Tareas, accion.Almacen);
    }

    [Fact]
    public void ConfiguracionDeCadaModulo_EstaEnSuModulo_NoEnConfiguracionGeneral()
    {
        // 2026-11-18: Ventas, Artículos y Gestión contable tienen su pestaña Configuración.
        Assert.DoesNotContain(ConfiguracionCatalogo.Entradas, e => e.Ruta.StartsWith("/configuracion-general/ventas", StringComparison.OrdinalIgnoreCase)
            || e.Ruta.StartsWith("/configuracion-general/articulos", StringComparison.OrdinalIgnoreCase)
            || e.Ruta.StartsWith("/configuracion-general/contable", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ventas", Assert.Single(ConfiguracionCatalogo.Entradas, e => e.Clave == "ventas").Modulo);
        Assert.Equal("articulos", Assert.Single(ConfiguracionCatalogo.Entradas, e => e.Clave == "articulos").Modulo);
        Assert.Equal("contabilidad", Assert.Single(ConfiguracionCatalogo.Entradas, e => e.Clave == "gestion-contable").Modulo);

        var pages = Path.Combine(RepoRoot(), "src", "AlfaCore", "Components", "Pages");
        foreach (var (archivo, nueva) in new[] { ("ConfiguracionGeneralVentas.razor", "/ventas/configuracion"),
                     ("ConfiguracionGeneralArticulos.razor", "/articulos/configuracion"), ("ConfiguracionGeneralContable.razor", "/contabilidad/configuracion") })
        {
            var razor = File.ReadAllText(Path.Combine(pages, archivo));
            Assert.Contains($"@page \"{nueva}\"", razor);
            Assert.Contains("RedirigirRutaMudada", razor);
        }
    }

    [Fact]
    public void MenuArchivos_SigueLaEstructuraDeAlfa_SinTocarTablasLegacy()
    {
        var script = string.Join(Environment.NewLine,
            File.ReadAllLines(Path.Combine(RepoRoot(), "src", "AlfaCore", "App_Data", "updates", "2026-11-18-003__archivos_estructura_alfa_menu_web.sql"))
                .Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        Assert.DoesNotContain("TA_MENU", script);
        Assert.DoesNotContain("TA_TAREAS", script);
        // Tablas (D0103) y Precios (D0105) como en Alfa; Paquete y Estudio contable a Utilidades (D98).
        Assert.Contains("(N''D010301'', N''D0103''", script);
        Assert.Contains("SET PadreClave = N''D0103''", script);
        Assert.Contains("SET PadreClave = N''D0105''", script);
        Assert.Contains("Clave = N''D9832''", script);
        Assert.Contains("Clave = N''D016040''", script);
    }

    private static MenuAutorizacionItemDto Nodo(string clave, string? ruta, params MenuAutorizacionItemDto[] hijos)
        => Nodo(clave, ruta, accion: false, explicito: false, porPerfil: false, autorizado: false, hijos);

    private static MenuAutorizacionItemDto Nodo(string clave, string? ruta, bool accion = false, bool explicito = false,
        bool porPerfil = false, bool autorizado = false, params MenuAutorizacionItemDto[] hijos)
    {
        var nodo = new MenuAutorizacionItemDto
        {
            Clave = clave,
            Nombre = clave,
            Habilitado = true,
            HabilitadoWeb = true,
            RutaWeb = ruta,
            EsAccion = accion,
            Explicito = explicito,
            PorPerfil = porPerfil,
            Autorizado = autorizado
        };
        nodo.Hijos.AddRange(hijos);
        return nodo;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AlfaCore.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró AlfaCore.sln.");
    }
}
