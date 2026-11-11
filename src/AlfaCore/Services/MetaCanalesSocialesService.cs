using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Conexión guiada de Instagram y de Facebook Messenger (cada una por separado) con la app central
/// de ALFA NET. Flujo:
///   1. La pantalla llama <see cref="IniciarConexion"/> y manda al usuario a Meta.
///   2. Meta vuelve a /api/conversaciones/meta/oauth/{canal}/callback → <see cref="ProcesarCallbackAsync"/>
///      canjea el código (en memoria, sin grabar nada) y redirige a la pantalla.
///   3. La pantalla muestra la cuenta (o las páginas, para elegir) y llama <see cref="ConfirmarAsync"/>,
///      que activa los avisos en Meta, registra la cuenta en ALFA_CENTRAL y graba el token en
///      TA_CONFIGURACION de la base con los permisos del usuario.
/// El envío y la recepción siguen usando las mismas claves CONV_INSTAGRAM_* / CONV_FACEBOOK_* de siempre.
/// </summary>
public sealed class MetaCanalesSocialesService(
    MetaCanalesSocialesClient client,
    MetaCanalesSocialesStore store,
    MetaConexionesPendientes pendientes,
    IConversacionesConfigService configService,
    ICentralBasesService centralBases,
    IAppEventService appEvents,
    IAppModeService appMode,
    IOptions<MetaCanalesSocialesOptions> options,
    IOptions<WhatsAppEmbeddedSignupOptions> whatsAppOptions)
{
    public const string CallbackPathTemplate = "/api/conversaciones/meta/oauth/{0}/callback";

    private readonly MetaCanalesSocialesOptions _options = options.Value;
    private readonly WhatsAppEmbeddedSignupOptions _whatsApp = whatsAppOptions.Value;

    public string MotivoNoDisponible(string canal)
    {
        if (!_options.Enabled)
            return "La conexión guiada todavía no está habilitada en este servidor.";
        if (!appMode.IsSaaSMode || !store.CentralDisponible)
            return "La conexión guiada solo está disponible en la versión en la nube de AlfaCore.";
        if (canal == MetaCanalSocial.Instagram && !_options.InstagramListo)
            return "Falta cargar los datos de la app de Instagram de ALFA NET en el servidor.";
        if (canal == MetaCanalSocial.Facebook && !_options.FacebookListo(_whatsApp))
            return "Falta cargar los datos de la app de Facebook de ALFA NET en el servidor.";
        return string.Empty;
    }

    public async Task<MetaCanalConexionVista> GetVistaAsync(string canal, int idBase, string? pendienteId, string usuario, CancellationToken ct)
    {
        canal = RequireCanal(canal);
        var motivo = MotivoNoDisponible(canal);
        if (motivo.Length > 0 || idBase <= 0)
            return new MetaCanalConexionVista { Disponible = false, MotivoNoDisponible = motivo };

        var pendiente = pendientes.Get(pendienteId);
        if (pendiente is not null && (pendiente.Canal != canal || pendiente.IdBase != idBase || !MismoUsuario(pendiente.Usuario, usuario)))
            pendiente = null;

        return new MetaCanalConexionVista
        {
            Disponible = true,
            Conexion = await store.GetActivaPorBaseAsync(idBase, canal, ct),
            Pendiente = pendiente
        };
    }

    /// <summary>Arma la URL de Meta a la que hay que mandar al usuario.</summary>
    /// <param name="hostBaseUri">Base del sitio desde el que se abrió la pantalla (NavigationManager.BaseUri).</param>
    /// <param name="returnPath">Ruta relativa de la pantalla, para volver al terminar.</param>
    public string IniciarConexion(string canal, int idBase, string usuario, string hostBaseUri, string returnPath)
    {
        canal = RequireCanal(canal);
        var motivo = MotivoNoDisponible(canal);
        if (motivo.Length > 0)
            throw new MetaCanalSocialException(motivo);
        if (idBase <= 0)
            throw new MetaCanalSocialException("No se pudo identificar la empresa. Volvé a entrar a la pantalla.");

        var redirectUri = BuildRedirectUri(canal, hostBaseUri);
        var pendiente = pendientes.Crear(canal, idBase, usuario, redirectUri, SanitizeReturnPath(returnPath));
        return canal == MetaCanalSocial.Instagram
            ? client.BuildInstagramAuthorizeUrl(redirectUri, pendiente.Id)
            : client.BuildFacebookAuthorizeUrl(redirectUri, pendiente.Id);
    }

    /// <summary>
    /// Vuelta de Meta. No graba nada: canjea el código, deja el resultado en memoria y devuelve a dónde
    /// redirigir el navegador. Null si el state no existe (vencido o inventado).
    /// </summary>
    public async Task<string?> ProcesarCallbackAsync(string canal, string? state, string? code, string? error, string? errorDescription, CancellationToken ct)
    {
        canal = RequireCanal(canal);
        var pendiente = pendientes.Get(state);
        if (pendiente is null || pendiente.Canal != canal)
            return null;

        if (pendiente.Estado != MetaConexionPendienteEstado.EsperandoMeta)
            return BuildReturnUrl(pendiente);

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            pendiente.Estado = MetaConexionPendienteEstado.Error;
            pendiente.Error = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                ? "Se canceló el permiso en Meta. Cuando quieras, volvé a tocar Conectar."
                : $"Meta no completó la conexión{(string.IsNullOrWhiteSpace(errorDescription) ? "." : ": " + errorDescription.Trim())}";
            return BuildReturnUrl(pendiente);
        }

        try
        {
            if (canal == MetaCanalSocial.Instagram)
            {
                pendiente.Instagram = await client.InstagramAutorizarAsync(code.Trim(), pendiente.RedirectUri, ct);
            }
            else
            {
                var paginas = await client.FacebookAutorizarAsync(code.Trim(), pendiente.RedirectUri, ct);
                if (paginas.Count == 0)
                    throw new MetaCanalSocialException("Tu usuario de Facebook no administra ninguna página, o no se marcó ninguna al dar el permiso. Volvé a conectar y elegí la página del negocio.");
                pendiente.PaginasFacebook = paginas;
            }

            pendiente.Estado = MetaConexionPendienteEstado.Lista;
        }
        catch (MetaCanalSocialException ex)
        {
            pendiente.Estado = MetaConexionPendienteEstado.Error;
            pendiente.Error = ex.Message;
            await LogErrorSeguroAsync("MetaCallback" + canal, ex, ex.Message, pendiente.IdBase);
        }

        return BuildReturnUrl(pendiente);
    }

    /// <summary>
    /// Confirma la conexión. Corre en la sesión del usuario en la base (graba TA_CONFIGURACION con sus permisos).
    /// Para Facebook, <paramref name="paginaId"/> es la página elegida.
    /// </summary>
    public async Task<MetaCanalConexionDto> ConfirmarAsync(string pendienteId, int idBase, string usuario, string? paginaId, CancellationToken ct)
    {
        var pendiente = pendientes.Get(pendienteId);
        if (pendiente is null || pendiente.IdBase != idBase || !MismoUsuario(pendiente.Usuario, usuario))
            throw new MetaCanalSocialException("La conexión venció. Tocá Conectar de nuevo.");
        if (pendiente.Estado != MetaConexionPendienteEstado.Lista)
            throw new MetaCanalSocialException(string.IsNullOrWhiteSpace(pendiente.Error) ? "La conexión no terminó en Meta." : pendiente.Error);

        MetaCanalConexionDto conexion;
        if (pendiente.Canal == MetaCanalSocial.Instagram)
        {
            var cuenta = pendiente.Instagram ?? throw new MetaCanalSocialException("Falta la cuenta de Instagram. Tocá Conectar de nuevo.");
            conexion = new MetaCanalConexionDto
            {
                IdBase = idBase,
                Canal = MetaCanalSocial.Instagram,
                CuentaId = cuenta.CuentaId,
                CuentaIdAlterno = cuenta.CuentaIdAlterno,
                CuentaNombre = cuenta.Nombre,
                CuentaUsuario = cuenta.Usuario,
                FotoUrl = cuenta.FotoUrl,
                TokenVenceUtc = cuenta.TokenVenceUtc
            };

            await client.InstagramSuscribirAsync(cuenta.Token, ct);
            await RegistrarCentralAsync(conexion, usuario, ct);

            var config = await configService.GetInstagramConfigAsync(idBase, ct);
            config.AccessToken = cuenta.Token;
            config.InstagramAccountId = cuenta.CuentaId;
            await configService.SaveInstagramConfigAsync(config, ct);
        }
        else
        {
            var pagina = pendiente.PaginasFacebook.FirstOrDefault(p => string.Equals(p.PaginaId, paginaId?.Trim(), StringComparison.Ordinal))
                ?? (pendiente.PaginasFacebook.Count == 1 ? pendiente.PaginasFacebook[0] : null)
                ?? throw new MetaCanalSocialException("Elegí la página que querés conectar.");
            if (!pagina.PuedeMensajes)
                throw new MetaCanalSocialException($"Tu usuario no tiene permiso para manejar los mensajes de \"{pagina.Nombre}\". Pedile a un administrador de la página que la conecte.");

            conexion = new MetaCanalConexionDto
            {
                IdBase = idBase,
                Canal = MetaCanalSocial.Facebook,
                CuentaId = pagina.PaginaId,
                CuentaNombre = pagina.Nombre,
                CuentaUsuario = pagina.Usuario,
                FotoUrl = pagina.FotoUrl
            };

            await client.FacebookSuscribirPaginaAsync(pagina.PaginaId, pagina.Token, ct);
            await RegistrarCentralAsync(conexion, usuario, ct);

            var config = await configService.GetFacebookConfigAsync(idBase, ct);
            config.AccessToken = pagina.Token;
            config.PageId = pagina.PaginaId;
            config.PageUsername = pagina.Usuario;
            await configService.SaveFacebookConfigAsync(config, ct);
        }

        pendientes.Quitar(pendiente.Id);
        await appEvents.LogAuditAsync("Conversaciones", "MetaConectar" + Capitalizar(conexion.Canal), "CONV_CANALES_SOCIALES",
            conexion.CuentaId, $"{Capitalizar(conexion.Canal)} conectado con la conexión guiada: {conexion.Titulo}.",
            new { conexion.IdBase, conexion.Canal, conexion.CuentaId, conexion.CuentaUsuario, Usuario = usuario }, ct);

        return await store.GetActivaPorBaseAsync(idBase, conexion.Canal, ct) ?? conexion;
    }

    public void DescartarPendiente(string? pendienteId)
    {
        if (!string.IsNullOrWhiteSpace(pendienteId))
            pendientes.Quitar(pendienteId);
    }

    /// <summary>Desconecta el canal de la base: deja de recibir mensajes y borra el token guardado.</summary>
    public async Task DesconectarAsync(string canal, int idBase, string usuario, CancellationToken ct)
    {
        canal = RequireCanal(canal);
        var conexion = await store.GetActivaPorBaseAsync(idBase, canal, ct);

        if (canal == MetaCanalSocial.Instagram)
        {
            var config = await configService.GetInstagramConfigAsync(idBase, ct);
            if (!string.IsNullOrWhiteSpace(config.AccessToken))
                await IntentarAsync(() => client.InstagramDesuscribirAsync(config.AccessToken.Trim(), ct));
            config.AccessToken = string.Empty;
            await configService.SaveInstagramConfigAsync(config, ct);
        }
        else
        {
            var config = await configService.GetFacebookConfigAsync(idBase, ct);
            if (!string.IsNullOrWhiteSpace(config.AccessToken) && !string.IsNullOrWhiteSpace(config.PageId))
                await IntentarAsync(() => client.FacebookDesuscribirPaginaAsync(config.PageId.Trim(), config.AccessToken.Trim(), ct));
            config.AccessToken = string.Empty;
            await configService.SaveFacebookConfigAsync(config, ct);
        }

        await store.DesconectarAsync(idBase, canal, usuario, ct);
        await appEvents.LogAuditAsync("Conversaciones", "MetaDesconectar" + Capitalizar(canal), "CONV_CANALES_SOCIALES",
            conexion?.CuentaId ?? string.Empty, $"{Capitalizar(canal)} desconectado.", new { IdBase = idBase, Canal = canal, Usuario = usuario }, ct);
    }

    private async Task RegistrarCentralAsync(MetaCanalConexionDto conexion, string usuario, CancellationToken ct)
    {
        var (ok, otraBase) = await store.ConectarAsync(conexion, usuario, ct);
        if (ok)
            return;

        var nombre = otraBase is > 0 ? (await centralBases.GetByIdAsync(otraBase.Value, ct))?.Nombre : null;
        throw new MetaCanalSocialException(
            $"Esta cuenta ya está conectada a otra empresa{(string.IsNullOrWhiteSpace(nombre) ? string.Empty : $" ({nombre.Trim()})")}. " +
            "Desconectala primero ahí y después volvé a conectar acá.");
    }

    internal string BuildRedirectUri(string canal, string hostBaseUri)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.CallbackBaseUrl) ? hostBaseUri : _options.CallbackBaseUrl;
        baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            throw new MetaCanalSocialException("Para conectar con Meta hay que entrar a AlfaCore por su dirección segura (https).");
        return baseUrl + string.Format(System.Globalization.CultureInfo.InvariantCulture, CallbackPathTemplate, canal.ToLowerInvariant());
    }

    /// <summary>Solo rutas relativas del propio sitio (evita redirecciones abiertas).</summary>
    internal static string SanitizeReturnPath(string? path)
    {
        var value = (path ?? string.Empty).Trim();
        if (value.Length == 0 || !value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\') || value.Contains("://", StringComparison.Ordinal))
            return "/conversaciones/configuracion";
        var corte = value.IndexOfAny(['?', '#']);
        return corte >= 0 ? value[..corte] : value;
    }

    internal static string BuildReturnUrl(MetaConexionPendiente pendiente)
        => $"{pendiente.ReturnPath}?seccion=canales&subseccion={pendiente.Canal.ToLowerInvariant()}&meta_conexion={Uri.EscapeDataString(pendiente.Id)}";

    private static string RequireCanal(string canal)
        => MetaCanalSocial.Normalizar(canal) is { } normalizado && normalizado != MetaCanalSocial.MercadoLibre
            ? normalizado
            : throw new MetaCanalSocialException("Canal no soportado para la conexión guiada de Meta.");

    private static bool MismoUsuario(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Capitalizar(string canal) => canal == MetaCanalSocial.Instagram ? "Instagram" : "Facebook";

    private static async Task IntentarAsync(Func<Task> accion)
    {
        try { await accion(); }
        catch (MetaCanalSocialException) { /* Si el token ya no sirve, igual desconectamos de nuestro lado. */ }
    }

    private async Task LogErrorSeguroAsync(string accion, Exception ex, string mensaje, int idBase)
    {
        try
        {
            await appEvents.LogErrorAsync("Conversaciones", accion, ex, mensaje, new { IdBase = idBase }, AppEventSeverity.Warning, CancellationToken.None);
        }
        catch
        {
            // El callback no corre en la sesión de la base: si no se puede registrar, no frena la conexión.
        }
    }
}
