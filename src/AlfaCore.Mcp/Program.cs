using AlfaCore.Mcp.Infra;
using AlfaCore.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Cadena de conexion: variable de entorno ALFACORE_MCP_CONNECTION o user-secrets (ConnectionStrings:AlfaMcp).
// NUNCA va en el repo ni en appsettings. Debe ser la del login de solo lectura creado por 005_login_solo_lectura.sql.
var connectionString = builder.Configuration["ALFACORE_MCP_CONNECTION"]
                       ?? builder.Configuration.GetConnectionString("AlfaMcp");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Falta la cadena de conexión. Definir la variable de entorno ALFACORE_MCP_CONNECTION " +
        "o user-secrets 'ConnectionStrings:AlfaMcp' (ver src/AlfaCore.Mcp/README.md).");

// Etapa 5: reemplazar estas dos implementaciones por las basadas en el token OAuth (usuario y base por tenant).
builder.Services.AddSingleton<IConexionResolver>(new ConexionUnicaResolver(connectionString));
builder.Services.AddSingleton<IMcpUserContext>(new UsuarioFijoContext(builder.Configuration["Mcp:UsuarioFijo"] ?? "mcp-local"));

builder.Services.AddSingleton<IMcpConfigProvider, McpConfigProvider>();
builder.Services.AddSingleton<IAuditLog, SqlAuditLog>();
builder.Services.AddSingleton<SqlExecutor>();
builder.Services.AddSingleton<ToolRunner>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "alfacore-dir", Version = "0.1.0" };
        options.ServerInstructions =
            "Consulta de solo lectura sobre la base de gestión de la empresa (ventas, artículos, clientes, cuenta corriente y stock). " +
            "Los importes de ventas son netos de IVA, en pesos, con las notas de crédito restando. " +
            "Para fechas relativas ('este mes', 'el mes pasado') calcular primero el rango AAAA-MM-DD. " +
            "Preferir las herramientas de negocio; usar consultar_sql solo si ninguna alcanza (antes llamar a describir_esquema). " +
            "Si una respuesta trae 'advertencias', comunicarlas al usuario: pueden afectar la confiabilidad del margen.";
    })
    .WithHttpTransport(options => options.Stateless = true)   // sin sesiones: cada request es independiente (escala y se publica detras de un proxy)
    .WithToolsFromAssembly();

var app = builder.Build();

// Etapa 5: aqui se agrega app.UseAuthentication()/UseAuthorization() y MapMcp(...).RequireAuthorization().
// Mientras tanto el servidor escucha SOLO en loopback (appsettings.json) y no debe publicarse.
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { estado = "ok" }));

app.Run();

public partial class Program;
