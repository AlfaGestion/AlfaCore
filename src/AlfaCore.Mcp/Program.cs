using System.Threading.RateLimiting;
using AlfaCore.Mcp.Auth;
using AlfaCore.Mcp.Infra;
using AlfaCore.Mcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

var builder = WebApplication.CreateBuilder(args);
var authOpt = AuthOptions.Load(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(authOpt);

// Filtro de host: ademas de los de appsettings (localhost), se acepta SOLO el host publico del Issuer.
// Sin esto, detras de IIS todo pedido a alfacentral.ddns.net volveria con 400 (Invalid Hostname).
if (authOpt.Enabled && Uri.TryCreate(authOpt.Issuer, UriKind.Absolute, out var issuerUri))
{
    var permitidos = (builder.Configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    if (!permitidos.Contains("*") && !permitidos.Contains(issuerUri.Host, StringComparer.OrdinalIgnoreCase))
        builder.Configuration["AllowedHosts"] = string.Join(';', permitidos.Append(issuerUri.Host));
}

// Cadena de conexion de DATOS: variable de entorno ALFACORE_MCP_CONNECTION o user-secrets (ConnectionStrings:AlfaMcp).
// NUNCA en el repo ni en appsettings. Debe ser la del login de solo lectura (005_login_solo_lectura.sql).
var connectionString = builder.Configuration["ALFACORE_MCP_CONNECTION"]
                       ?? builder.Configuration.GetConnectionString("AlfaMcp");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Falta la cadena de conexión. Definir la variable de entorno ALFACORE_MCP_CONNECTION " +
        "o user-secrets 'ConnectionStrings:AlfaMcp' (ver src/AlfaCore.Mcp/README.md).");

// Base por defecto: la de ALFACORE_MCP_CONNECTION (usuarios con clave sin base asignada y configuracion OAuth).
// Con login (multi-base) cada consulta usa la base del token; ver ConexionPorBaseResolver.
if (!authOpt.Enabled) builder.Services.AddSingleton<IConexionResolver>(new ConexionUnicaResolver(connectionString));
builder.Services.AddSingleton<IMcpConfigProvider, McpConfigProvider>();
builder.Services.AddSingleton<IAuditLog, SqlAuditLog>();
builder.Services.AddSingleton<SqlExecutor>();
builder.Services.AddScoped<ToolRunner>();
builder.Services.AddHttpContextAccessor();

if (authOpt.Enabled)
{
    // ---- Almacen de usuarios / clientes / tokens (login SEPARADO del de datos) ----------------------------------------
    if (authOpt.Store.Equals("Memory", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddSingleton<IAuthStore, MemoryAuthStore>();
    else
    {
        var authCs = builder.Configuration["ALFACORE_MCP_AUTH_CONNECTION"] ?? builder.Configuration.GetConnectionString("AlfaMcpAuth");
        if (string.IsNullOrWhiteSpace(authCs))
            throw new InvalidOperationException(
                "Falta la cadena de conexión del almacén OAuth: variable ALFACORE_MCP_AUTH_CONNECTION o user-secrets 'ConnectionStrings:AlfaMcpAuth' " +
                "(login con permisos solo sobre las tablas MCP_*; ver docs/base-datos/sql-referencia/dir-mcp/010_oauth_tablas.sql).");
        builder.Services.AddSingleton<IAuthStore>(sp => new SqlAuthStore(authCs, sp.GetRequiredService<ILogger<SqlAuthStore>>()));
    }

    // ---- Multi-base: accesos por mail (ALFA_CENTRAL) y conexion segun la base del token ---------------------------------
    if (authOpt.Store.Equals("Memory", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddSingleton<MemoryAccesoStore>().AddSingleton<IAccesoStore>(sp => sp.GetRequiredService<MemoryAccesoStore>());
    else
        builder.Services.AddSingleton<IAccesoStore>(new SqlAccesoStore(
            builder.Configuration["ALFACORE_MCP_AUTH_CONNECTION"] ?? builder.Configuration.GetConnectionString("AlfaMcpAuth")!));
    builder.Services.AddSingleton<IConexionResolver>(sp => new ConexionPorBaseResolver(
        sp.GetRequiredService<IHttpContextAccessor>(), sp.GetRequiredService<IAccesoStore>(), sp.GetRequiredService<SecretProtector>(),
        connectionString, sp.GetRequiredService<ILogger<ConexionPorBaseResolver>>()));

    // ---- Mails (codigos de ingreso, avisos, invitaciones). Sin SMTP: en Development se escriben en el log; en produccion solo usuario y clave.
    var emailOpt = EmailOptions.Load(builder.Configuration);
    builder.Services.AddSingleton(emailOpt);
    if (!emailOpt.Configurado && builder.Environment.IsDevelopment())
        builder.Services.AddSingleton<IEmailSender, ConsolaEmailSender>();
    else
        builder.Services.AddSingleton<IEmailSender>(new SmtpEmailSender(emailOpt));

    builder.Services.AddSingleton<SigningKeyProvider>();
    builder.Services.AddSingleton<TokenService>();
    builder.Services.AddSingleton<SecretProtector>();
    builder.Services.AddScoped<IMcpUserContext, HttpContextUserContext>();

    builder.Services.AddAntiforgery(o =>
    {
        o.Cookie.Name = "mcp-af";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    });

    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        void Politica(string nombre, int limite) => o.AddPolicy(nombre, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        Politica("oauth-login", authOpt.LimiteLoginPorMin);
        Politica("oauth-token", authOpt.LimiteTokenPorMin);
        Politica("oauth-registro", authOpt.LimiteRegistroPorMin);
    });

    builder.Services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(o => { o.MapInboundClaims = false; o.RequireHttpsMetadata = false; })
        .AddMcp(o =>
        {
            // 401 => WWW-Authenticate: Bearer resource_metadata="<url publica>" (el cliente descubre asi el servidor de autorizacion).
            o.ResourceMetadataUri = new Uri(authOpt.Issuer + "/.well-known/oauth-protected-resource" + authOpt.EndpointPath);
            o.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = authOpt.Resource,
                AuthorizationServers = { authOpt.Issuer },
                ScopesSupported = { AuthOptions.Scope },
                ResourceName = "AlfaCore - consulta de gestion"
            };
        });
    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<TokenService>((o, ts) => o.TokenValidationParameters = ts.ParametrosValidacion());
    builder.Services.AddAuthorization(o => o.AddPolicy("mcp", p => p
        .RequireAuthenticatedUser()
        .RequireAssertion(c => (c.User.FindFirst("scope")?.Value ?? "").Split(' ').Contains(AuthOptions.Scope))));
}
else
{
    // Solo Development (AuthOptions lo impide en otros entornos): sin login, usuario fijo, escucha en loopback.
    builder.Services.AddSingleton<IMcpUserContext>(new UsuarioFijoContext(builder.Configuration["Mcp:UsuarioFijo"] ?? "mcp-local"));
}

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "alfacore-dir", Version = "0.2.0" };
        options.ServerInstructions =
            "Consulta de solo lectura sobre la base de gestión de la empresa (ventas, compras, artículos, clientes, proveedores, cuenta corriente y stock). " +
            "Para un cliente, proveedor o artículo puntual, buscar primero su código con buscar_clientes / buscar_proveedores / buscar_articulos. " +
            "Los importes de ventas son netos de IVA, en pesos, con las notas de crédito restando. " +
            "Para fechas relativas ('este mes', 'el mes pasado') calcular primero el rango AAAA-MM-DD. " +
            "Preferir las herramientas de negocio; usar consultar_sql solo si ninguna alcanza (antes llamar a describir_esquema). " +
            "Si una respuesta trae 'advertencias', comunicarlas al usuario: pueden afectar la confiabilidad del margen.";
    })
    .WithHttpTransport(options => options.Stateless = true)   // sin sesiones: cada request es independiente (escala detras de un proxy)
    .WithToolsFromAssembly();

var app = builder.Build();

// Comandos de administracion (no levantan el servidor): dotnet run --project src/AlfaCore.Mcp -- crear-usuario <usuario> "<Nombre>" [base]
if (args.Length > 0 && AuthCommands.Comandos.Contains(args[0]))
    return await AuthCommands.EjecutarAsync(args, app.Services, authOpt);

if (authOpt.Enabled && app.Services.GetRequiredService<IAuthStore>() is MemoryAuthStore memoria
    && !string.IsNullOrWhiteSpace(authOpt.DevUser) && !string.IsNullOrWhiteSpace(authOpt.DevPassword))
{
    var idDev = await memoria.CrearUsuarioAsync(authOpt.DevUser, authOpt.DevUser, PasswordHasher.Hash(authOpt.DevPassword), null, CancellationToken.None);
    if (authOpt.DevTotp || !string.IsNullOrWhiteSpace(authOpt.DevTotpSecret))
    {
        // Solo Development: segundo factor de prueba. La clave se muestra por consola para cargarla a mano en la app autenticadora.
        var secretoDev = string.IsNullOrWhiteSpace(authOpt.DevTotpSecret) ? Totp.NuevoSecreto() : authOpt.DevTotpSecret.Trim();
        await memoria.SetTotpAsync(idDev, app.Services.GetRequiredService<SecretProtector>().Proteger(secretoDev), CancellationToken.None);
        app.Logger.LogWarning("TOTP de PRUEBA para {Usuario}. Clave para la app autenticadora (ingreso manual, tipo 'basado en tiempo'): {Secreto}", authOpt.DevUser, secretoDev);
    }
}

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'";
    await next();
});

if (authOpt.Enabled)
{
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapOAuth();
}

var mcp = app.MapMcp(authOpt.EndpointPath);
if (authOpt.Enabled) mcp.RequireAuthorization("mcp");
app.MapGet("/health", () => Results.Ok(new { estado = "ok" }));

app.Run();
return 0;

public partial class Program;
