using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Endpoints para el puesto de SOPORTE (Claude en NMC_CONT_DEV\Soporte, herramienta
/// herramientas\tickets.py). Cada caso atendido deja su informe en un ticket de la base de
/// Alfa Gestion (ConnectionStrings:AlfaGestion = ALFANET2007): como NOTA si el tecnico da el
/// numero de ticket, o como ticket nuevo si no hay.
///
/// Autenticacion: usuario y clave del tecnico en TA_USUARIOS de esa base (los mismos de AlfaCore),
/// en cada pedido. No hay sesion: la conexion se fija con SetWebhookOverride antes de tocar tablas.
/// Si falta la cadena AlfaGestion, las rutas responden 404 como si no existieran.
/// </summary>
public static class SoporteTicketsEndpoints
{
    private const int MaxInforme = 60000;

    public sealed class SoporteAuthRequest
    {
        public string Usuario { get; set; } = string.Empty;
        public string Clave { get; set; } = string.Empty;
    }

    public sealed class SoporteClientesRequest
    {
        public string Usuario { get; set; } = string.Empty;
        public string Clave { get; set; } = string.Empty;
        public string Texto { get; set; } = string.Empty;
    }

    public sealed class SoporteTicketRequest
    {
        public string Usuario { get; set; } = string.Empty;
        public string Clave { get; set; } = string.Empty;
        public int? Numero { get; set; }
        public string? ClienteCodigo { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Informe { get; set; } = string.Empty;
    }

    public static void MapSoporteTicketsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/soporte").AllowAnonymous().DisableAntiforgery();

        group.MapPost("/login", async (SoporteAuthRequest body, HttpContext http, IServiceProvider sp, CancellationToken ct) =>
        {
            var (error, usuario) = await AutenticarAsync(http, sp, body.Usuario, body.Clave, ct);
            return error ?? Results.Json(new { ok = true, usuario });
        });

        group.MapPost("/ticket", async (SoporteTicketRequest body, HttpContext http, IServiceProvider sp, CancellationToken ct) =>
        {
            var (error, usuario) = await AutenticarAsync(http, sp, body.Usuario, body.Clave, ct);
            if (error is not null)
                return error;

            var numero = body.Numero ?? 0;
            if (numero <= 0)
                return Results.Json(new { ok = false, error = "Falta el numero de ticket." }, statusCode: StatusCodes.Status400BadRequest);

            var tickets = sp.GetRequiredService<ITicketsService>();
            var t = await tickets.GetByNumeroAsync(numero, ct);
            if (t is null)
                return Results.Json(new { ok = false, error = $"No existe el ticket {numero}." }, statusCode: StatusCodes.Status404NotFound);

            return Results.Json(new
            {
                ok = true,
                t.IdTicket,
                t.Numero,
                t.Titulo,
                t.ClienteCodigo,
                t.ClienteNombre,
                Estado = t.EstadoNombre,
                Tecnico = t.TecnicoNombre,
                t.FechaHoraAlta
            });
        });

        group.MapPost("/clientes", async (SoporteClientesRequest body, HttpContext http, IServiceProvider sp, CancellationToken ct) =>
        {
            var (error, _) = await AutenticarAsync(http, sp, body.Usuario, body.Clave, ct);
            if (error is not null)
                return error;

            if (string.IsNullOrWhiteSpace(body.Texto) || body.Texto.Trim().Length < 2)
                return Results.Json(new { ok = false, error = "Escribir al menos 2 letras del cliente." }, statusCode: StatusCodes.Status400BadRequest);

            var rows = await sp.GetRequiredService<ITicketsService>().SearchClientesAsync(body.Texto.Trim(), ct);
            return Results.Json(new { ok = true, clientes = rows.Take(20) });
        });

        group.MapPost("/ticket-informe", async (SoporteTicketRequest body, HttpContext http, IServiceProvider sp, ILogger<SoporteTicketsLog> logger, CancellationToken ct) =>
        {
            var (error, usuario) = await AutenticarAsync(http, sp, body.Usuario, body.Clave, ct);
            if (error is not null)
                return error;

            var informe = (body.Informe ?? string.Empty).Replace("\r\n", "\n").Trim();
            if (informe.Length == 0)
                return Results.Json(new { ok = false, error = "El informe esta vacio." }, statusCode: StatusCodes.Status400BadRequest);
            if (informe.Length > MaxInforme)
                return Results.Json(new { ok = false, error = $"El informe supera {MaxInforme} caracteres." }, statusCode: StatusCodes.Status400BadRequest);

            var tickets = sp.GetRequiredService<ITicketsService>();
            try
            {
                if (body.Numero is > 0)
                {
                    var t = await tickets.GetByNumeroAsync(body.Numero.Value, ct);
                    if (t is null)
                        return Results.Json(new { ok = false, error = $"No existe el ticket {body.Numero}." }, statusCode: StatusCodes.Status404NotFound);

                    await tickets.AddNoteAsync(new TicketNotaRequest
                    {
                        IdTicket = t.IdTicket,
                        Texto = informe,
                        UsuarioAccion = usuario
                    }, ct);

                    logger.LogInformation("Soporte: informe agregado como nota al ticket {Numero} por {Usuario}.", t.Numero, usuario);
                    return Results.Json(new { ok = true, creado = false, t.IdTicket, t.Numero, t.Titulo });
                }

                var titulo = (body.Titulo ?? string.Empty).Trim();
                if (titulo.Length == 0)
                    return Results.Json(new { ok = false, error = "Para crear un ticket nuevo hace falta el titulo." }, statusCode: StatusCodes.Status400BadRequest);

                var clienteCodigo = string.IsNullOrWhiteSpace(body.ClienteCodigo) ? null : body.ClienteCodigo.Trim();
                var idTicket = await tickets.CreateAsync(new TicketCreateRequest
                {
                    Titulo = titulo,
                    Descripcion = informe,
                    ClienteCodigo = clienteCodigo,
                    UsuarioAccion = usuario
                }, ct);

                var nuevo = await tickets.GetByIdAsync(idTicket, ct);
                logger.LogInformation("Soporte: ticket {Numero} creado con el informe por {Usuario}.", nuevo?.Numero, usuario);
                return Results.Json(new { ok = true, creado = true, IdTicket = idTicket, nuevo?.Numero, Titulo = titulo });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Soporte: no se pudo registrar el informe (usuario {Usuario}).", usuario);
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }

    /// <summary>
    /// Fija la base de Alfa Gestion para este request y valida usuario/clave contra su TA_USUARIOS.
    /// Devuelve (resultado de error, null) o (null, nombre del usuario validado).
    /// </summary>
    private static async Task<(IResult? Error, string Usuario)> AutenticarAsync(
        HttpContext http, IServiceProvider sp, string? usuario, string? clave, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";

        var config = sp.GetRequiredService<IConfiguration>();
        var cs = config.GetConnectionString("AlfaGestion");
        if (string.IsNullOrWhiteSpace(cs))
            return (Results.NotFound(), string.Empty);

        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(clave))
            return (Results.Json(new { ok = false, error = "Faltan usuario y clave." }, statusCode: StatusCodes.Status401Unauthorized), string.Empty);

        var b = new SqlConnectionStringBuilder(cs);
        sp.GetRequiredService<ISessionService>().SetWebhookOverride(new SessionDto
        {
            Nombre = "Soporte",
            Servidor = b.DataSource,
            BaseDatos = b.InitialCatalog,
            Usuario = b.UserID,
            Password = b.Password,
            TrustServerCertificate = b.TrustServerCertificate,
            Activa = true
        });

        try
        {
            var info = await sp.GetRequiredService<ILegacyBaseUserSessionService>()
                .LoginAsync(usuario.Trim(), clave, cs, null, ct);
            return (null, string.IsNullOrWhiteSpace(info.UserName) ? usuario.Trim() : info.UserName.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // No distinguir "no existe" de "clave invalida".
            await Task.Delay(800, ct);
            return (Results.Json(new { ok = false, error = "Usuario o clave invalidos." }, statusCode: StatusCodes.Status401Unauthorized), string.Empty);
        }
    }
}

public sealed class SoporteTicketsLog;
