using System.Text;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Link personal de catálogo (2026-10-01): credencial opaca (AES-GCM) con clave derivada por base del
/// WebhookToken de ALFA_CENTRAL. Alcance obligatorio base + idweb + catálogo + cliente + vencimiento;
/// cualquier falla = anónimo (sin precios si la base no los muestra a leads).
/// </summary>
public sealed class CatalogoClienteLinkTests
{
    private const string SecretoBase4264 = "B6F534AA11BB22CC33DD44EE55FF66007711882299330044AA55BB66CC77DD88";
    private const string SecretoBase84 = "86EE44AA11BB22CC33DD44EE55FF66007711882299330044AA55BB66CC77DD99";
    private static readonly DateTimeOffset Ahora = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);

    private static CatalogoClienteLinkPayload Payload(string cliente = "112010002", int idBase = 4264, int idReferencia = 1, string idWeb = "ALFANET", DateTimeOffset? expira = null)
        => new(idBase, idWeb, idReferencia, cliente, (expira ?? Ahora.AddDays(7)).ToUnixTimeSeconds());

    private static byte[] Clave(string secreto, int idBase) => CatalogoClienteLinkService.DerivarClave(secreto, idBase);

    [Fact]
    public void Cliente_LinkFirmado_SeLeeYEsValidoEnSuAlcance()
    {
        var clave = Clave(SecretoBase4264, 4264);
        var credencial = CatalogoClienteLinkService.Crear(clave, Payload());

        var leido = CatalogoClienteLinkService.Leer(clave, credencial);

        Assert.NotNull(leido);
        Assert.Equal("112010002", leido!.CodigoCliente);
        Assert.True(CatalogoClienteLinkService.EsValidoParaAlcance(leido, "alfanet", 4264, 1, Ahora));
    }

    [Fact]
    public void Credencial_NoExponeCodigoDeClienteNiDatosEnClaro()
    {
        var credencial = CatalogoClienteLinkService.Crear(Clave(SecretoBase4264, 4264), Payload());

        Assert.DoesNotContain("112010002", credencial);
        Assert.DoesNotContain("ALFANET", credencial, StringComparison.OrdinalIgnoreCase);
        var bytes = Convert.FromBase64String(credencial.Replace('-', '+').Replace('_', '/') + new string('=', (4 - credencial.Length % 4) % 4));
        Assert.DoesNotContain("112010002", Encoding.UTF8.GetString(bytes));
        Assert.True(Uri.EscapeDataString(credencial) == credencial, "La credencial debe ser segura para URL (base64url).");
    }

    [Fact]
    public void TokenVencido_NoEsValido()
    {
        var clave = Clave(SecretoBase4264, 4264);
        var leido = CatalogoClienteLinkService.Leer(clave, CatalogoClienteLinkService.Crear(clave, Payload(expira: Ahora.AddSeconds(-1))));

        Assert.NotNull(leido);
        Assert.False(CatalogoClienteLinkService.EsValidoParaAlcance(leido, "ALFANET", 4264, 1, Ahora));
    }

    [Fact]
    public void TokenAlterado_NoSePuedeLeer()
    {
        var clave = Clave(SecretoBase4264, 4264);
        var credencial = CatalogoClienteLinkService.Crear(clave, Payload());
        var medio = credencial.Length / 2;
        var alterado = credencial[..medio] + (credencial[medio] == 'A' ? 'B' : 'A') + credencial[(medio + 1)..];

        Assert.Null(CatalogoClienteLinkService.Leer(clave, alterado));
        Assert.Null(CatalogoClienteLinkService.Leer(clave, credencial[..^2]));
        Assert.Null(CatalogoClienteLinkService.Leer(clave, "no-es-una-credencial"));
        Assert.Null(CatalogoClienteLinkService.Leer(clave, string.Empty));
    }

    [Fact]
    public void TokenDeOtraBase_NoSeDescifraNiValida()
    {
        var credencial4264 = CatalogoClienteLinkService.Crear(Clave(SecretoBase4264, 4264), Payload());

        // Con la clave de otra base no se puede ni leer.
        Assert.Null(CatalogoClienteLinkService.Leer(Clave(SecretoBase84, 84), credencial4264));
        // Mismo secreto pero otro IdBase en la derivación: tampoco.
        Assert.Null(CatalogoClienteLinkService.Leer(Clave(SecretoBase4264, 84), credencial4264));
        // Y aunque se leyera, el alcance exige la misma base.
        var leido = CatalogoClienteLinkService.Leer(Clave(SecretoBase4264, 4264), credencial4264);
        Assert.False(CatalogoClienteLinkService.EsValidoParaAlcance(leido, "ALFANET", 84, 1, Ahora));
    }

    [Fact]
    public void TokenDeOtroCatalogoOIdWeb_NoValida()
    {
        var clave = Clave(SecretoBase4264, 4264);
        var leido = CatalogoClienteLinkService.Leer(clave, CatalogoClienteLinkService.Crear(clave, Payload(idReferencia: 1)));

        Assert.False(CatalogoClienteLinkService.EsValidoParaAlcance(leido, "ALFANET", 4264, 2, Ahora));
        Assert.False(CatalogoClienteLinkService.EsValidoParaAlcance(leido, "OTRAEMPRESA", 4264, 1, Ahora));
    }

    [Fact]
    public void ClienteA_NoPuedeConvertirseEnClienteB()
    {
        var clave = Clave(SecretoBase4264, 4264);
        var credencialA = CatalogoClienteLinkService.Crear(clave, Payload(cliente: "112010002"));

        var leido = CatalogoClienteLinkService.Leer(clave, credencialA);

        Assert.Equal("112010002", leido!.CodigoCliente);
        Assert.NotEqual("112010001", leido.CodigoCliente);
        // Cada credencial es distinta (nonce aleatorio): no hay forma de derivar la de B desde la de A.
        Assert.NotEqual(credencialA, CatalogoClienteLinkService.Crear(clave, Payload(cliente: "112010002")));
    }

    [Fact]
    public async Task SinWebhookTokenEnLaBase_NoSeEmiteLinkPersonal()
    {
        var service = new CatalogoClienteLinkService(new FakeCentralBases(webhookToken: null), new ThrowingRouteGuard(), NullLogger<CatalogoClienteLinkService>.Instance);

        Assert.Null(await service.CrearCredencialAsync("ALFANET", 4264, 1, "112010002"));
    }

    [Fact]
    public async Task Validar_TokenInvalido_DevuelveNullSinTocarLaBaseDelCliente()
    {
        // ThrowingRouteGuard: si se intentara buscar al cliente en la base, el test lo detectaría.
        var service = new CatalogoClienteLinkService(new FakeCentralBases(SecretoBase4264), new ThrowingRouteGuard(), NullLogger<CatalogoClienteLinkService>.Instance);

        Assert.Null(await service.ValidarCredencialAsync("alterado", "ALFANET", 4264, 1));
        Assert.Null(await service.ValidarCredencialAsync(null, "ALFANET", 4264, 1));
        var vencida = CatalogoClienteLinkService.Crear(Clave(SecretoBase4264, 4264), Payload(expira: DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Null(await service.ValidarCredencialAsync(vencida, "ALFANET", 4264, 1));
    }

    [Fact]
    public async Task Crear_UsaLaClaveDeLaBase_YSeLeeConEsaMismaClave()
    {
        var service = new CatalogoClienteLinkService(new FakeCentralBases(SecretoBase4264), new ThrowingRouteGuard(), NullLogger<CatalogoClienteLinkService>.Instance);

        var credencial = await service.CrearCredencialAsync("ALFANET", 4264, 1, "112010002");

        var leido = CatalogoClienteLinkService.Leer(Clave(SecretoBase4264, 4264), credencial!);
        Assert.True(CatalogoClienteLinkService.EsValidoParaAlcance(leido, "ALFANET", 4264, 1, DateTimeOffset.UtcNow));
        Assert.InRange(DateTimeOffset.FromUnixTimeSeconds(leido!.ExpiraUnix), DateTimeOffset.UtcNow.AddDays(6.9), DateTimeOffset.UtcNow.AddDays(7.1));
    }

    /// <summary>Página y PDF aplican la misma identidad; el botón "Soy cliente" usa el login existente.</summary>
    [Fact]
    public void Source_PaginaYPdfUsanLaMismaIdentidad_YLoginExistente()
    {
        var root = FindRepositoryRoot();
        var pagina = File.ReadAllText(Path.Combine(root, "src", "AlfaCore", "Components", "Pages", "CatalogoPublico.razor"));
        var program = File.ReadAllText(Path.Combine(root, "src", "AlfaCore", "Program.cs"));

        Assert.Contains("ClienteLinkSvc.ValidarCredencialAsync(ClienteCredencial, idweb ?? string.Empty, link.IdBase, link.IdReferencia)", pagina);
        Assert.Contains("if (ClienteIdentificadoParaCatalogo)", pagina);
        Assert.Contains("CatalogoClienteSession.LoginAsync(", pagina);
        Assert.Contains("<CatalogoClienteLoginPanel", pagina);
        Assert.Contains("Soy cliente", pagina);

        var endpoint = program.IndexOf("app.MapGet(\"/api/catalogos/public/{idweb}/{token}/pdf\"", StringComparison.Ordinal);
        var cuerpo = program[endpoint..program.IndexOf(".AllowAnonymous();", endpoint, StringComparison.Ordinal)];
        Assert.Contains("clienteLinkSvc.ValidarCredencialAsync(credencial, idweb, link.IdBase, link.IdReferencia, ct)", cuerpo);
        Assert.Contains("visitanteIdentificado: clienteIdentificado is not null", cuerpo);
    }

    [Theory]
    [InlineData(false, false, false)]  // PDF anónimo, precios a leads OFF → sin precios
    [InlineData(false, true, true)]    // PDF de cliente identificado → con precios
    [InlineData(true, false, true)]    // precios a leads ON → con precios
    public void Pdf_MismaReglaQueLaPagina(bool muestraPreciosLeads, bool identificado, bool conPrecios)
    {
        var catalogo = new CatalogosCatalogoDetalleDto
        {
            IdInsert = 1,
            Articulos = [new CatalogosCatalogoItemDto { IdArticulo = "01", DescripcionArticulo = "PILA DURACELL - AA", Precio = 2500m }]
        };

        var paraPdf = CatalogosPublicPriceVisibility.ParaVisitante(catalogo, muestraPreciosLeads, visitanteAutenticado: identificado);

        Assert.Equal(conPrecios, paraPdf.PreciosVisibles);
        Assert.Equal(conPrecios ? 2500m : null, paraPdf.Articulos[0].Precio);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }

    private sealed class FakeCentralBases(string? webhookToken) : ICentralBasesService
    {
        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult<BaseCentralDto?>(new BaseCentralDto { IdBase = idBase, IdCliente = "112010001", WebhookToken = webhookToken });
        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingRouteGuard : ISaaSTenantRouteGuard
    {
        public Task<SaaSTenantRouteContext> ResolveAsync(int? expectedBaseId, string? idWeb = null, CancellationToken ct = default) => throw new InvalidOperationException("No debería consultarse la base.");
        public SaaSTenantConnectionSnapshot GetRequiredConnection(int? expectedBaseId, string operationName) => throw new InvalidOperationException("No debería consultarse la base.");
        public long Invalidate() => throw new InvalidOperationException();
        public bool IsCurrent(SaaSTenantRouteLease lease, int? routeBaseId, int? sessionBaseId) => throw new InvalidOperationException();
    }
}
