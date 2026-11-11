using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Conexión guiada de Mercado Libre con la app central de ALFA NET. Mismo flujo que
/// <see cref="MetaCanalesSocialesService"/>: "Conectar" → el vendedor entra con su cuenta en Mercado Libre →
/// vuelve a /api/conversaciones/mercadolibre/central/oauth/callback (canje en memoria) → la pantalla confirma
/// y recién ahí se graba: fila en ALFA_CENTRAL.dbo.CONV_CANALES_SOCIALES (canal MERCADOLIBRE, cuenta =
/// seller_id, para repartir las notificaciones) y tokens en TA_CONFIGURACION de la base (CONV_MELI_*,
/// sin el Client Secret, que queda solo en el servidor).
/// </summary>
public sealed class MercadoLibreConexionService(
    MercadoLibreCentralClient client,
    MetaCanalesSocialesStore store,
    MetaConexionesPendientes pendientes,
    IConversacionesConfigService configService,
    ICentralBasesService centralBases,
    IAppEventService appEvents,
    IAppModeService appMode,
    IOptions<MercadoLibreCentralOptions> options)
{
    public const string CallbackPath = "/api/conversaciones/mercadolibre/central/oauth/callback";
    private const string Canal = MetaCanalSocial.MercadoLibre;

    private readonly MercadoLibreCentralOptions _options = options.Value;

    public string MotivoNoDisponible()
    {
        if (!_options.Enabled)
            return "La conexión guiada todavía no está habilitada en este servidor.";
        if (!appMode.IsSaaSMode || !store.CentralDisponible)
            return "La conexión guiada solo está disponible en la versión en la nube de AlfaCore.";
        if (!_options.Listo)
            return "Falta cargar los datos de la app de Mercado Libre de ALFA NET en el servidor.";
        return string.Empty;
    }

    public async Task<MetaCanalConexionVista> GetVistaAsync(int idBase, string? pendienteId, string usuario, CancellationToken ct)
    {
        var motivo = MotivoNoDisponible();
        if (motivo.Length > 0 || idBase <= 0)
            return new MetaCanalConexionVista { Disponible = false, MotivoNoDisponible = motivo };

        var pendiente = pendientes.Get(pendienteId);
        if (pendiente is not null && (pendiente.Canal != Canal || pendiente.IdBase != idBase || !MismoUsuario(pendiente.Usuario, usuario)))
            pendiente = null;

        return new MetaCanalConexionVista
        {
            Disponible = true,
            Conexion = await store.GetActivaPorBaseAsync(idBase, Canal, ct),
            Pendiente = pendiente
        };
    }

    public string IniciarConexion(int idBase, string usuario, string hostBaseUri, string returnPath)
    {
        var motivo = MotivoNoDisponible();
        if (motivo.Length > 0)
            throw new MetaCanalSocialException(motivo);
        if (idBase <= 0)
            throw new MetaCanalSocialException("No se pudo identificar la empresa. Volvé a entrar a la pantalla.");

        var redirectUri = BuildRedirectUri(hostBaseUri);
        var pendiente = pendientes.Crear(Canal, idBase, usuario, redirectUri, MetaCanalesSocialesService.SanitizeReturnPath(returnPath));
        return client.BuildAuthorizeUrl(redirectUri, pendiente.Id);
    }

    public async Task<string?> ProcesarCallbackAsync(string? state, string? code, string? error, string? errorDescription, CancellationToken ct)
    {
        var pendiente = pendientes.Get(state);
        if (pendiente is null || pendiente.Canal != Canal)
            return null;

        if (pendiente.Estado != MetaConexionPendienteEstado.EsperandoMeta)
            return MetaCanalesSocialesService.BuildReturnUrl(pendiente);

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            pendiente.Estado = MetaConexionPendienteEstado.Error;
            pendiente.Error = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                ? "Se canceló el permiso en Mercado Libre. Cuando quieras, volvé a tocar Conectar."
                : $"Mercado Libre no completó la conexión{(string.IsNullOrWhiteSpace(errorDescription) ? "." : ": " + errorDescription.Trim())}";
            return MetaCanalesSocialesService.BuildReturnUrl(pendiente);
        }

        try
        {
            pendiente.MercadoLibre = await client.AutorizarAsync(code.Trim(), pendiente.RedirectUri, ct);
            pendiente.Estado = MetaConexionPendienteEstado.Lista;
        }
        catch (MetaCanalSocialException ex)
        {
            pendiente.Estado = MetaConexionPendienteEstado.Error;
            pendiente.Error = ex.Message;
            try
            {
                await appEvents.LogErrorAsync("Conversaciones", "MercadoLibreCallback", ex, ex.Message,
                    new { pendiente.IdBase }, AppEventSeverity.Warning, CancellationToken.None);
            }
            catch
            {
                // El callback no corre en la sesión de la base: si no se puede registrar, no frena la conexión.
            }
        }

        return MetaCanalesSocialesService.BuildReturnUrl(pendiente);
    }

    /// <summary>Confirma la conexión en la sesión del usuario (graba TA_CONFIGURACION con sus permisos).</summary>
    public async Task<MetaCanalConexionDto> ConfirmarAsync(string pendienteId, int idBase, string usuario, CancellationToken ct)
    {
        var pendiente = pendientes.Get(pendienteId);
        if (pendiente is null || pendiente.Canal != Canal || pendiente.IdBase != idBase || !MismoUsuario(pendiente.Usuario, usuario))
            throw new MetaCanalSocialException("La conexión venció. Tocá Conectar de nuevo.");
        if (pendiente.Estado != MetaConexionPendienteEstado.Lista || pendiente.MercadoLibre is not { } cuenta)
            throw new MetaCanalSocialException(string.IsNullOrWhiteSpace(pendiente.Error) ? "La conexión no terminó en Mercado Libre." : pendiente.Error);

        var conexion = new MetaCanalConexionDto
        {
            IdBase = idBase,
            Canal = Canal,
            CuentaId = cuenta.SellerId,
            CuentaNombre = string.IsNullOrWhiteSpace(cuenta.Nombre) ? cuenta.Nickname : cuenta.Nombre,
            CuentaUsuario = cuenta.Nickname,
            FotoUrl = cuenta.FotoUrl,
            TokenVenceUtc = cuenta.TokenVenceUtc
        };

        var (ok, otraBase) = await store.ConectarAsync(conexion, usuario, ct);
        if (!ok)
        {
            var nombre = otraBase is > 0 ? (await centralBases.GetByIdAsync(otraBase.Value, ct))?.Nombre : null;
            throw new MetaCanalSocialException(
                $"Esta cuenta de Mercado Libre ya está conectada a otra empresa{(string.IsNullOrWhiteSpace(nombre) ? string.Empty : $" ({nombre.Trim()})")}. " +
                "Desconectala primero ahí y después volvé a conectar acá.");
        }

        var config = await configService.GetMercadoLibreConfigAsync(idBase, ct);
        config.ClientId = _options.ClientId.Trim();
        config.ClientSecret = string.Empty;
        config.CredencialesCentrales = true;
        config.AccessToken = cuenta.AccessToken;
        config.RefreshToken = cuenta.RefreshToken;
        config.SellerId = cuenta.SellerId;
        config.SiteId = string.IsNullOrWhiteSpace(cuenta.SiteId) ? (string.IsNullOrWhiteSpace(_options.SiteId) ? "MLA" : _options.SiteId.Trim()) : cuenta.SiteId;
        config.ApiBaseUrl = _options.ApiBase;
        await configService.SaveMercadoLibreConfigAsync(config, ct);

        pendientes.Quitar(pendiente.Id);
        await appEvents.LogAuditAsync("Conversaciones", "MercadoLibreConectar", "CONV_CANALES_SOCIALES", conexion.CuentaId,
            $"Mercado Libre conectado con la conexión guiada: {conexion.Titulo}.",
            new { conexion.IdBase, conexion.CuentaId, conexion.CuentaUsuario, Usuario = usuario }, ct);

        return await store.GetActivaPorBaseAsync(idBase, Canal, ct) ?? conexion;
    }

    public void DescartarPendiente(string? pendienteId)
    {
        if (!string.IsNullOrWhiteSpace(pendienteId))
            pendientes.Quitar(pendienteId);
    }

    public async Task DesconectarAsync(int idBase, string usuario, CancellationToken ct)
    {
        var conexion = await store.GetActivaPorBaseAsync(idBase, Canal, ct);
        var config = await configService.GetMercadoLibreConfigAsync(idBase, ct);
        if (!string.IsNullOrWhiteSpace(config.AccessToken) && !string.IsNullOrWhiteSpace(config.SellerId) && config.CredencialesCentrales)
        {
            try { await client.DesvincularAsync(config.SellerId.Trim(), config.AccessToken.Trim(), ct); }
            catch (MetaCanalSocialException) { /* Si el acceso ya no sirve, igual desconectamos de nuestro lado. */ }
        }

        config.AccessToken = string.Empty;
        config.RefreshToken = string.Empty;
        await configService.SaveMercadoLibreConfigAsync(config, ct);
        await store.DesconectarAsync(idBase, Canal, usuario, ct);
        await appEvents.LogAuditAsync("Conversaciones", "MercadoLibreDesconectar", "CONV_CANALES_SOCIALES",
            conexion?.CuentaId ?? string.Empty, "Mercado Libre desconectado.", new { IdBase = idBase, Usuario = usuario }, ct);
    }

    internal string BuildRedirectUri(string hostBaseUri)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.CallbackBaseUrl) ? hostBaseUri : _options.CallbackBaseUrl;
        baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new MetaCanalSocialException("Para conectar con Mercado Libre hay que entrar a AlfaCore por su dirección segura (https).");
        return baseUrl + CallbackPath;
    }

    private static bool MismoUsuario(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
