using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CentralAuthService(
    IConfiguration configuration,
    ICentralClientesService clientesService,
    ICentralBasesService basesService,
    IPasswordVerifier passwordVerifier,
    IAppEventService appEvents) : ICentralAuthService
{
    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral")))
            {
                return Fail("Falta configurar la conexión central (ConnectionStrings:AlfaCentral).");
            }

            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Fail("Usuario o contraseña incorrectos.");
            }

            var user = await GetUserAsync(request.UserName.Trim(), ct).ConfigureAwait(false);
            if (user is null)
            {
                return Fail("Usuario o contraseña incorrectos.");
            }

            if (!passwordVerifier.Verify(request.Password, user.Password))
            {
                return Fail("Usuario o contraseña incorrectos.");
            }

            var cliente = await clientesService.GetByIdClienteAsync(user.IdCliente, ct).ConfigureAwait(false);
            if (cliente is null)
            {
                return Fail("Usuario o contraseña incorrectos.");
            }

            if (string.IsNullOrWhiteSpace(cliente.IdWeb))
            {
                var idWeb = await clientesService.GenerateAndSaveIdWebAsync(cliente.IdCliente, cliente.RazonSocial, ct).ConfigureAwait(false);
                cliente = new ClienteCentralDto
                {
                    IdCliente = cliente.IdCliente,
                    RazonSocial = cliente.RazonSocial,
                    IdWeb = idWeb,
                    SuperAdmin = cliente.SuperAdmin,
                    Tipo = cliente.Tipo
                };
            }

            var bases = cliente.SuperAdmin
                ? await basesService.GetAllAsync(ct).ConfigureAwait(false)
                : await basesService.GetByClienteAsync(user.IdCliente, false, ct).ConfigureAwait(false);
            var concedidas = cliente.SuperAdmin ? [] : await basesService.GetConcedidasAsync(user.IdCliente, ct).ConfigureAwait(false);
            var esEstudio = !cliente.SuperAdmin && string.Equals(cliente.Tipo, "E", StringComparison.OrdinalIgnoreCase);

            // Un estudio contable entra aunque todavía no tenga empresas: va a su portal.
            if (bases.Count == 0 && concedidas.Count == 0 && !esEstudio)
            {
                return Fail("Usuario o contraseña incorrectos.");
            }

            if (esEstudio || (bases.Count == 0 && concedidas.Count > 0))
            {
                return new LoginResult
                {
                    Success = true,
                    Session = new SesionCentralDto { Usuario = user, Cliente = cliente, Bases = bases.Concat(concedidas).ToArray() },
                    RequiresBaseSelection = false,
                    RedirectUrl = "/estudio"
                };
            }

            var session = new SesionCentralDto
            {
                Usuario = user,
                Cliente = cliente,
                Bases = bases
            };

            return new LoginResult
            {
                Success = true,
                Session = session,
                RequiresBaseSelection = bases.Count > 1,
                RedirectUrl = bases.Count == 1
                    ? (string.IsNullOrWhiteSpace(cliente.IdWeb) ? "/seleccionar-base" : $"/{cliente.IdWeb}/{bases[0].IdBase}")
                    : (string.IsNullOrWhiteSpace(cliente.IdWeb) ? "/seleccionar-base" : $"/{cliente.IdWeb}")
            };
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                "Central",
                "Login",
                ex,
                "No se pudo validar el acceso central.",
                new { request.UserName, HasPassword = !string.IsNullOrWhiteSpace(request.Password) },
                AppEventSeverity.Error,
                ct);

            return Fail("No se pudo validar el acceso central.");
        }
    }

    private async Task<UsuarioCentralDto?> GetUserAsync(string userName, CancellationToken ct)
    {
        const string sql = """
            SELECT [user] AS UserName, ISNULL(password, '') AS Password, idcliente AS IdCliente
            FROM dbo.users
            WHERE UPPER(LTRIM(RTRIM([user]))) = @UserName;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        return await cn.QuerySingleOrDefaultAsync<UsuarioCentralDto>(new CommandDefinition(sql, new { UserName = userName.Trim().ToUpperInvariant() }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static LoginResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };
}
