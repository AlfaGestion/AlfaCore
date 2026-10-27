using AlfaCore.Services;

namespace AlfaCore.Repositories;

/// <summary>
/// Base donde se graban AUX_ERR y la auditoría: la de la empresa activa (sesión del usuario, o el override
/// del webhook/proceso). Antes se buscaba un HttpContext.Items["AlfaCore.ConnectionString"] que nadie
/// cargaba y siempre caía a ConnectionStrings:AlfaGestion: en producción, los errores y auditorías de
/// TODAS las empresas terminaban en la base de Alfa Net (2026-10-22, incidentes de DW en ALFANET2007).
///
/// ISessionService se pide recién al grabar (no por constructor): ConexionClienteService depende de
/// servicios que a su vez registran errores, y por constructor se formaba una dependencia circular. Si
/// resolver la sesión vuelve a registrar un error, ese registro va a la base por defecto (no hay bucle).
/// </summary>
internal static class TenantLogConnection
{
    private static readonly AsyncLocal<bool> Resolving = new();

    public static string Resolve(IServiceProvider? services, IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        if (httpContextAccessor.HttpContext?.Items.TryGetValue("AlfaCore.ConnectionString", out var currentConnection) == true
            && currentConnection is string itemConnection
            && !string.IsNullOrWhiteSpace(itemConnection))
        {
            return itemConnection;
        }

        if (services is not null && !Resolving.Value)
        {
            Resolving.Value = true;
            try
            {
                var session = services.GetService<ISessionService>();
                var tenant = session?.GetConnectionString();
                if (!string.IsNullOrWhiteSpace(tenant))
                    return tenant;
            }
            catch
            {
                // Sin sesión resoluble (proceso de fondo, circuito cerrado): base por defecto, como antes.
            }
            finally
            {
                Resolving.Value = false;
            }
        }

        var configured = configuration.GetConnectionString("AlfaGestion");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");
    }
}
