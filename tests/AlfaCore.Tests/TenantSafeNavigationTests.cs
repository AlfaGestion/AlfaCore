using System.Text.RegularExpressions;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Guardia estática de navegación tenant-safe (auditoría 2026-09-24). En SaaS toda página de
/// MainLayout vive bajo /{idweb}/{idbase}/...; un <c>href="/..."</c> o <c>NavigateTo("/...")</c>
/// escrito a mano navega a la ruta root, pierde la base activa y termina en el login roto
/// ("No hay una base activa lista para validar el acceso interno"). Los links internos deben pasar
/// por <c>IRouteContextService.BuildRoute(...)</c>.
/// Excepciones explícitas:
/// - páginas con PublicLayout (login, registro, landing, selección de base, públicas): sus rutas
///   son globales por diseño;
/// - destinos globales (<see cref="GlobalTargets"/>): login/registro/landing/assets/API;
/// - rutas ya prefijadas por interpolación (<c>$"/{idweb}/..."</c>);
/// - deuda conocida (<see cref="KnownDebt"/>): links root que ya existían fuera del alcance de este
///   fix. El tope por archivo sólo puede bajar; cualquier link root nuevo hace fallar el test.
/// </summary>
public sealed class TenantSafeNavigationTests
{
    private static readonly Regex RootNavigation = new(
        """href="(/[^"]*)"|href="@\(\$"(/[^"]*)"\)"|NavigateTo\(\$?"(/[^"]*)""",
        RegexOptions.Compiled);

    private static readonly string[] GlobalTargets =
    [
        "/login", "/registrarme", "/modulos", "/landing/", "/seleccionar-base", "/reuniones",
        "/css/", "/js/", "/lib/", "/img/", "/_content/", "/api/"
    ];

    // Deuda preexistente fuera del alcance de este fix (pendiente de migrar a BuildRoute).
    // Archivo relativo a Components/Pages -> cantidad máxima de links root permitidos.
    private static readonly Dictionary<string, int> KnownDebt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Actividad.razor"] = 1,              // /ayuda#...
        ["Articulos.razor"] = 1,              // /ayuda#...
        ["Ayuda.razor"] = 1,                  // /ayuda#{anchor}
        ["CalendarioReuniones.razor"] = 1,    // /calendario
        ["Comprobantes.razor"] = 1,           // /ayuda#...
        ["Contactos.razor"] = 3,              // /conversaciones?id=...
        ["Conversaciones.razor"] = 8,         // /crm, /contactos, /clientes
        ["ConversacionesEstadisticas.razor"] = 2,
        ["Crm.razor"] = 2,                    // /conversaciones?id=...
        ["Familias.razor"] = 4,               // /ayuda, /compras/...
        ["Home.razor"] = 1,                   // /ayuda#...
        ["InformesIAResultado.razor"] = 1,    // /compras/informesia
        ["Rubros.razor"] = 3,                 // /ayuda, /compras/...
    };

    [Fact]
    public void PagesDoNotAddNewRootAbsoluteInternalLinks()
    {
        var pagesRoot = Path.Combine(RepositoryRoot(), "src", "AlfaCore", "Components", "Pages");
        var problems = new List<string>();

        foreach (var file in Directory.EnumerateFiles(pagesRoot, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            if (source.Contains("@layout AlfaCore.Components.Layout.PublicLayout", StringComparison.Ordinal))
                continue;

            var violations = FindRootInternalTargets(source).ToList();
            if (violations.Count == 0)
                continue;

            var relative = Path.GetRelativePath(pagesRoot, file).Replace('\\', '/');
            var allowed = KnownDebt.TryGetValue(relative, out var max) ? max : 0;
            if (violations.Count > allowed)
                problems.Add($"{relative}: {violations.Count} link(s) root (permitidos {allowed}): {string.Join(", ", violations)}");
        }

        Assert.True(problems.Count == 0,
            "Links internos root sin tenant. Usá RouteContext.BuildRoute(...):\n" + string.Join("\n", problems));
    }

    [Theory]
    [InlineData("Auditoria.razor")]
    [InlineData("AuditoriaErrores.razor")]
    [InlineData("AuditoriaErrorDetalle.razor")]
    [InlineData("Costos.razor")]
    [InlineData("Interfaces.razor")]
    [InlineData("InterfacesEditor.razor")]
    [InlineData("Consultas.razor")]
    [InlineData("ConsultaDetalle.razor")]
    [InlineData("Tickets.razor")]
    public void AuditedPages_BuildInternalLinksThroughRouteContext(string page)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AlfaCore", "Components", "Pages", page));

        Assert.Empty(FindRootInternalTargets(source));
        Assert.Contains(".BuildRoute(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditoriaErrorDetailLink_PreservesTenant()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AlfaCore", "Components", "Pages", "AuditoriaErrores.razor"));

        Assert.Contains("RouteContext.BuildRoute($\"/auditoria/error/{item.Id}\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_DetectsHandWrittenRootLinks()
    {
        var sample = """
            <a href="/auditoria/errores">x</a>
            <a href="@($"/conversaciones?id={id}")">y</a>
            Nav.NavigateTo($"/interfaces/{id}");
            Nav.NavigateTo("/costos");
            <a href="@RouteContext.BuildRoute("/auditoria/errores")">ok</a>
            <a href="/login">ok</a>
            Nav.NavigateTo($"/{Uri.EscapeDataString(idweb)}/catalogo/{seg}");
            <link rel="stylesheet" href="/css/x.css" />
            """;

        Assert.Equal(
            ["/auditoria/errores", "/conversaciones?id={id}", "/interfaces/{id}", "/costos"],
            FindRootInternalTargets(sample).ToArray());
    }

    private static IEnumerable<string> FindRootInternalTargets(string source)
    {
        foreach (Match match in RootNavigation.Matches(source))
        {
            var target = match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
            if (target.StartsWith("/{", StringComparison.Ordinal))
                continue;
            if (GlobalTargets.Any(g => string.Equals(target, g.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                                       || target.StartsWith(g, StringComparison.OrdinalIgnoreCase)))
                continue;

            yield return target;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("No se pudo ubicar la raíz del repositorio.");
    }
}
