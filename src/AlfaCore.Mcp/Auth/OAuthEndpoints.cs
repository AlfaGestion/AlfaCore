using System.Net;
using System.Text.Json;
using AlfaCore.Mcp.Infra;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.WebUtilities;

namespace AlfaCore.Mcp.Auth;

/// <summary>
/// Servidor de autorizacion OAuth 2.1 minimo para los conectores MCP (claude.ai, ChatGPT, Claude Code):
/// metadatos (RFC 8414 / OIDC discovery), registro dinamico de clientes publicos (RFC 7591), authorization code + PKCE S256,
/// refresh tokens con rotacion y deteccion de reuso. Un solo permiso: mcp:read. Sin OpenID, sin clientes confidenciales.
/// </summary>
public static class OAuthEndpoints
{
    private sealed record Solicitud(ClienteOAuth Cliente, string RedirectUri, string? State, string CodeChallenge, string Scope);

    public static void MapOAuth(this WebApplication app)
    {
        var opt = app.Services.GetRequiredService<AuthOptions>();
        var keys = app.Services.GetRequiredService<SigningKeyProvider>();

        IResult Metadatos() => Json(new Dictionary<string, object?>
        {
            ["issuer"] = opt.Issuer,
            ["authorization_endpoint"] = opt.Issuer + "/oauth/authorize",
            ["token_endpoint"] = opt.Issuer + "/oauth/token",
            ["registration_endpoint"] = opt.Issuer + "/oauth/register",
            ["jwks_uri"] = opt.Issuer + "/oauth/jwks",
            ["response_types_supported"] = new[] { "code" },
            ["response_modes_supported"] = new[] { "query" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            ["scopes_supported"] = new[] { AuthOptions.Scope },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
            ["authorization_response_iss_parameter_supported"] = true
        });

        // El cliente prueba varias rutas de descubrimiento (RFC 8414 con y sin path, y OIDC): se sirven todas.
        foreach (var ruta in new[] { "/.well-known/oauth-authorization-server", "/.well-known/oauth-authorization-server/{**resto}",
                                     "/.well-known/openid-configuration", "/.well-known/openid-configuration/{**resto}" })
            app.MapGet(ruta, Metadatos);

        app.MapGet("/oauth/jwks", () => Json(new Dictionary<string, object?>
        {
            ["keys"] = new object[] { new Dictionary<string, object?>
            {
                ["kty"] = keys.PublicJwk.Kty, ["use"] = "sig", ["alg"] = "RS256", ["kid"] = keys.PublicJwk.Kid,
                ["n"] = keys.PublicJwk.N, ["e"] = keys.PublicJwk.E
            } }
        }));

        app.MapPost("/oauth/register", Registrar).RequireRateLimiting("oauth-registro");
        app.MapGet("/oauth/authorize", AutorizarGet).RequireRateLimiting("oauth-token");
        app.MapPost("/oauth/authorize", AutorizarPost).RequireRateLimiting("oauth-login");
        app.MapPost("/oauth/token", Token).RequireRateLimiting("oauth-token");
    }

    // ---- /oauth/register -----------------------------------------------------------------------------------------------
    private static async Task<IResult> Registrar(HttpContext ctx, IAuthStore store, IMcpConfigProvider config, ILogger<TokenService> log, CancellationToken ct)
    {
        var cfg = await Config(config, log, ct);
        if (cfg is null || !cfg.Habilitado) return ErrorJson("temporarily_unavailable", "Servicio no disponible.", 503);

        JsonDocument doc;
        try { doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct); }
        catch (JsonException) { return ErrorJson("invalid_client_metadata", "El cuerpo no es JSON valido."); }
        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return ErrorJson("invalid_client_metadata", "Se esperaba un objeto JSON.");

            if (!raiz.TryGetProperty("redirect_uris", out var uris) || uris.ValueKind != JsonValueKind.Array || uris.GetArrayLength() is < 1 or > 5)
                return ErrorJson("invalid_redirect_uri", "redirect_uris debe tener entre 1 y 5 URIs.");
            var lista = new List<string>();
            foreach (var u in uris.EnumerateArray())
            {
                var s = u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                if (!RedirectPolicy.Permitida(s, cfg.RedirectsPermitidos ?? []))
                    return ErrorJson("invalid_redirect_uri", "Redirect URI no permitida para este servidor (IA_MCP_OAUTH_REDIRECTS).");
                lista.Add(s!);
            }
            if (raiz.TryGetProperty("token_endpoint_auth_method", out var m) && m.GetString() is not (null or "none"))
                return ErrorJson("invalid_client_metadata", "Solo se admiten clientes publicos (token_endpoint_auth_method=none).");

            if (await store.ContarClientesAsync(ct) >= cfg.MaxClientesOauth)
                return ErrorJson("temporarily_unavailable", "Se alcanzo el maximo de clientes registrados.", 503);

            var nombre = LimpiarNombre(raiz.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
            var cliente = new ClienteOAuth("mcp_" + Secretos.NuevoToken()[..22], nombre, lista, DateTime.UtcNow);
            await store.AddClienteAsync(cliente, ct);
            await store.EventoAsync("cliente_registrado", null, cliente.ClientId, Ip(ctx), nombre, ct);

            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Json(new Dictionary<string, object?>
            {
                ["client_id"] = cliente.ClientId,
                ["client_id_issued_at"] = new DateTimeOffset(cliente.AltaUtc).ToUnixTimeSeconds(),
                ["client_name"] = nombre,
                ["redirect_uris"] = lista,
                ["grant_types"] = new[] { "authorization_code", "refresh_token" },
                ["response_types"] = new[] { "code" },
                ["token_endpoint_auth_method"] = "none",
                ["scope"] = AuthOptions.Scope
            }, statusCode: 201);
        }
    }

    // ---- /oauth/authorize ----------------------------------------------------------------------------------------------
    private static async Task<(Solicitud? S, IResult? Error)> Validar(Func<string, string?> v, IAuthStore store, AuthOptions opt, CancellationToken ct)
    {
        // Hasta validar client_id y redirect_uri NUNCA se redirige: un error aca se muestra, no se envia a una URL no confiable.
        var cliente = v("client_id") is { Length: > 0 and <= 100 } id ? await store.GetClienteAsync(id, ct) : null;
        if (cliente is null) return (null, PaginaError("Cliente no registrado. Volve a iniciar la conexion desde la aplicacion."));
        var redirect = v("redirect_uri");
        if (!RedirectPolicy.CoincideConRegistrada(redirect, cliente.RedirectUris))
            return (null, PaginaError("La direccion de retorno no coincide con la registrada para este cliente."));

        var state = v("state") is { Length: <= 500 } st ? st : null;
        IResult Falla(string error, string desc) => Redirigir(redirect!, state, opt.Issuer, ("error", error), ("error_description", desc));

        if (v("response_type") != "code") return (null, Falla("unsupported_response_type", "Solo response_type=code."));
        var challenge = v("code_challenge");
        if (!Pkce.ChallengeValido(challenge) || v("code_challenge_method") != "S256")
            return (null, Falla("invalid_request", "Se requiere PKCE con code_challenge_method=S256."));
        if (v("resource") is { Length: > 0 } recurso && !string.Equals(recurso.TrimEnd('/'), opt.Resource, StringComparison.Ordinal))
            return (null, Falla("invalid_target", "El parametro resource no corresponde a este servidor."));
        var pedido = (v("scope") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pedido.Length > 0 && !pedido.Contains(AuthOptions.Scope)) return (null, Falla("invalid_scope", "Scope no soportado."));

        return (new Solicitud(cliente, redirect!, state, challenge!, AuthOptions.Scope), null);
    }

    /// <summary>El ingreso por mail requiere la clave IA_MCP_OAUTH_EMAIL_HABILITADO y el SMTP configurado; si no, solo queda usuario y clave.</summary>
    private static bool PorEmail(McpRuntimeSettings cfg, IEmailSender mail) => cfg.EmailHabilitado && mail.Disponible;

    private static async Task<IResult> AutorizarGet(HttpContext ctx, IAuthStore store, AuthOptions opt, IMcpConfigProvider config, IAntiforgery af,
        IEmailSender mail, ILogger<TokenService> log, CancellationToken ct)
    {
        if ((await Config(config, log, ct)) is not { Habilitado: true } cfg) return PaginaError("El servicio no esta disponible por el momento. Proba de nuevo en unos minutos.");
        var q = ctx.Request.Query;
        var (s, error) = await Validar(k => q[k].FirstOrDefault(), store, opt, ct);
        if (error is not null) return error;
        var campos = Reenviar(k => q[k].FirstOrDefault());
        var af1 = af.GetAndStoreTokens(ctx).RequestToken!;
        var conEmail = PorEmail(cfg, mail);
        return q["modo"].FirstOrDefault() == "clave" || !conEmail
            ? Pagina(LoginPage.Render(s!.Cliente.Nombre, Host(s.RedirectUri), campos, af1, null, cfg.RequiereTotp, conEmail ? LinkModo(campos, null) : null))
            : Pagina(LoginPage.RenderEmail(s!.Cliente.Nombre, Host(s.RedirectUri), campos, af1, null, LinkModo(campos, "clave")));
    }

    private static string LinkModo(Dictionary<string, string?> campos, string? modo)
    {
        var d = new Dictionary<string, string?>(campos);
        if (modo is not null) d["modo"] = modo;
        return QueryHelpers.AddQueryString("/oauth/authorize", d);
    }

    private static async Task<IResult> AutorizarPost(HttpContext ctx, IAuthStore store, IAccesoStore accesos, IEmailSender mail, AuthOptions opt,
        IMcpConfigProvider config, IAntiforgery af, SecretProtector protector, ILogger<TokenService> log, CancellationToken ct)
    {
        var cfg = await Config(config, log, ct);
        if (cfg is not { Habilitado: true }) return PaginaError("El servicio no esta disponible por el momento. Proba de nuevo en unos minutos.");
        try { await af.ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException) { return PaginaError("La pagina de login vencio. Volve a iniciar la conexion desde la aplicacion."); }

        var form = await ctx.Request.ReadFormAsync(ct);
        string? V(string k) => form[k].FirstOrDefault();
        var (s, error) = await Validar(V, store, opt, ct);
        if (error is not null) return error;

        var accion = V("accion");
        if (accion == "cancelar")
            return Redirigir(s!.RedirectUri, s.State, opt.Issuer, ("error", "access_denied"), ("error_description", "El usuario cancelo la autorizacion."));
        if (accion is "enviar-codigo" or "verificar-codigo" or "elegir-base")
        {
            if (!PorEmail(cfg, mail)) return PaginaError("El ingreso por mail no esta disponible. Ingresa con usuario y clave o consulta con ALFA NET.");
            return await IngresoPorEmailAsync(ctx, accion, V, s!, store, accesos, mail, opt, cfg, af, log, ct);
        }

        var usuario = (V("usuario") ?? "").Trim();
        var clave = V("clave") ?? "";
        var codigoTotp = (V("codigo") ?? "").Trim();
        const string generico = "Usuario, clave o codigo incorrectos, o cuenta bloqueada temporalmente. Si es tu primer ingreso, pedile al administrador que configure tu segundo factor.";
        IResult Reintentar(string mensaje) => Pagina(LoginPage.Render(s!.Cliente.Nombre, Host(s.RedirectUri), Reenviar(V), af.GetAndStoreTokens(ctx).RequestToken!, mensaje, cfg.RequiereTotp,
            PorEmail(cfg, mail) ? LinkModo(Reenviar(V), null) : null));

        if (usuario.Length is 0 or > 100 || clave.Length is 0 or > 200 || codigoTotp.Length > 20) { PasswordHasher.IgualarTiempo(clave); return Reintentar(generico); }

        var ahora = DateTime.UtcNow;
        var u = await store.GetUsuarioAsync(usuario, ct);
        if (u is null || !u.Activo || (u.BloqueadoHasta is { } b && b > ahora))
        {
            PasswordHasher.IgualarTiempo(clave);
            await store.EventoAsync(u is null ? "login_usuario_inexistente" : "login_rechazado", usuario, s!.Cliente.ClientId, Ip(ctx), u is null ? null : "inactivo o bloqueado", ct);
            return Reintentar(generico);
        }

        // Primer factor (clave) y, solo si la clave es correcta, segundo factor (TOTP de un solo uso).
        var claveOk = PasswordHasher.Verificar(clave, u.ClaveHash);
        var segundoOk = false;
        if (claveOk)
        {
            if (u.TotpSecreto is not null)
            {
                try
                {
                    var (ok, paso) = Totp.Verificar(protector.Revelar(u.TotpSecreto), codigoTotp, ahora);
                    segundoOk = ok && await store.UsarPasoTotpAsync(u.Id, paso, ct); // el mismo codigo no sirve dos veces
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    log.LogError(ex, "No se pudo descifrar el secreto TOTP de {Usuario} (clave de cifrado cambiada?).", u.Usuario);
                    await store.EventoAsync("totp_ilegible", u.Usuario, s!.Cliente.ClientId, Ip(ctx), "no se pudo descifrar el secreto", ct);
                }
            }
            else if (!cfg.RequiereTotp) segundoOk = true;
            else
            {
                await store.EventoAsync("login_sin_totp", u.Usuario, s!.Cliente.ClientId, Ip(ctx), "clave correcta pero el usuario no tiene segundo factor", ct);
                return Reintentar(generico); // mismo mensaje: no se revela que la clave era correcta
            }
        }
        if (!claveOk || !segundoOk)
        {
            var intentos = u.Intentos + 1;
            DateTime? bloqueo = intentos >= cfg.MaxIntentosLogin ? ahora.AddMinutes(cfg.BloqueoMin) : null;
            await store.RegistrarLoginAsync(u.Id, false, bloqueo is null ? intentos : 0, bloqueo, ct);
            await store.EventoAsync(bloqueo is not null ? "usuario_bloqueado" : claveOk ? "totp_fallido" : "login_fallido", u.Usuario, s!.Cliente.ClientId, Ip(ctx), $"intento {intentos}", ct);
            return Reintentar(generico);
        }

        await store.RegistrarLoginAsync(u.Id, true, 0, null, ct);
        var codigo = Secretos.NuevoToken();
        await store.AddCodigoAsync(new CodigoAuth(Secretos.Hash(codigo), s!.Cliente.ClientId, u.Id, s.RedirectUri, s.CodeChallenge, s.Scope, opt.Resource, ahora.AddSeconds(cfg.CodigoSeg),
            string.IsNullOrWhiteSpace(u.Base) ? null : u.Base.Trim(), "pwd"), ct);
        await store.EventoAsync("login_ok", u.Usuario, s.Cliente.ClientId, Ip(ctx), u.TotpSecreto is null ? "sin segundo factor" : "con segundo factor", ct);
        return Redirigir(s.RedirectUri, s.State, opt.Issuer, ("code", codigo));
    }

    // ---- ingreso por mail: codigo de un solo uso enviado al mail registrado en ALFA_CENTRAL ------------------------------
    private static async Task<IResult> IngresoPorEmailAsync(HttpContext ctx, string accion, Func<string, string?> V, Solicitud s, IAuthStore store,
        IAccesoStore accesos, IEmailSender mail, AuthOptions opt, McpRuntimeSettings cfg, IAntiforgery af, ILogger log, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var campos = Reenviar(V);
        string Af() => af.GetAndStoreTokens(ctx).RequestToken!;
        var linkClave = LinkModo(campos, "clave");
        IResult PaginaEmail(string? err) => Pagina(LoginPage.RenderEmail(s.Cliente.Nombre, Host(s.RedirectUri), campos, Af(), err, linkClave));
        IResult PaginaCodigo(string email, string? aviso, string? err) => Pagina(LoginPage.RenderCodigo(s.Cliente.Nombre, Host(s.RedirectUri), campos, Af(), email, aviso, err, linkClave));
        const string sinAcceso = "Tu mail no tiene acceso habilitado. Consulta con ALFA NET.";

        if (accion == "elegir-base")
        {
            var emailTicket = await accesos.ConsumirTicketAsync(Secretos.Hash(V("ticket") ?? ""), ahora, ct);
            if (emailTicket is null) return PaginaError("La seleccion vencio. Volve a iniciar la conexion desde la aplicacion.");
            var elegida = int.TryParse(V("base"), out var idb) ? (await accesos.AccesosAsync(emailTicket, ct)).FirstOrDefault(a => a.IdBase == idb) : null;
            if (elegida is null) return PaginaError(sinAcceso);
            return await FinalizarEmailAsync(ctx, emailTicket, elegida, s, store, accesos, mail, opt, cfg, log, ct);
        }

        var email = EmailNormalizado.Normalizar(V("email"));
        if (email is null) return PaginaEmail("Escribi un mail valido.");

        if (accion == "enviar-codigo")
        {
            var aviso = $"Si {email} tiene acceso habilitado, te enviamos un codigo de 6 digitos. Vence en {cfg.EmailCodigoMin} minutos. Revisa tambien el correo no deseado.";
            if (await accesos.ContarCodigosEmailAsync(email, ahora.AddMinutes(-15), ct) >= cfg.EmailMaxEnvios)
            {
                await store.EventoAsync("email_limite", email, s.Cliente.ClientId, Ip(ctx), null, ct);
                return PaginaCodigo(email, null, "Ya te enviamos varios codigos. Usa el ultimo que recibiste o espera 15 minutos para pedir otro.");
            }
            if ((await accesos.AccesosAsync(email, ct)).Count == 0)
            {
                // Misma respuesta que si existiera: la pantalla no revela que mails tienen acceso.
                await store.EventoAsync("email_sin_acceso", email, s.Cliente.ClientId, Ip(ctx), null, ct);
                return PaginaCodigo(email, aviso, null);
            }
            var codigo = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            await accesos.GuardarCodigoEmailAsync(email, HashCodigo(email, codigo), ahora.AddMinutes(cfg.EmailCodigoMin), Ip(ctx), ct);
            try { await mail.EnviarAsync(Mails.Codigo(email, codigo, cfg.EmailCodigoMin, s.Cliente.Nombre), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "No se pudo enviar el codigo de ingreso por mail.");
                await store.EventoAsync("email_error_envio", email, s.Cliente.ClientId, Ip(ctx), ex.GetType().Name, ct);
                return PaginaCodigo(email, null, "No pudimos enviar el mail en este momento. Proba de nuevo en unos minutos.");
            }
            await store.EventoAsync("email_codigo_enviado", email, s.Cliente.ClientId, Ip(ctx), null, ct);
            return PaginaCodigo(email, aviso, null);
        }

        // verificar-codigo
        var digitos = new string((V("codigo_email") ?? "").Where(char.IsDigit).ToArray());
        if (digitos.Length != 6 || !await accesos.ConsumirCodigoEmailAsync(email, HashCodigo(email, digitos), ahora, cfg.MaxIntentosLogin, ct))
        {
            await store.EventoAsync("email_codigo_fallido", email, s.Cliente.ClientId, Ip(ctx), null, ct);
            return PaginaCodigo(email, null, "Codigo incorrecto o vencido. Si pediste varios, vale solo el ultimo. Despues de varios errores hay que pedir uno nuevo.");
        }
        var bases = await accesos.AccesosAsync(email, ct);
        if (bases.Count == 0) return PaginaError(sinAcceso);
        if (bases.Count == 1) return await FinalizarEmailAsync(ctx, email, bases[0], s, store, accesos, mail, opt, cfg, log, ct);

        // Varias bases: se elige despues de validar el codigo (antes no se muestra ningun nombre de cliente).
        var ticket = Secretos.NuevoToken();
        await accesos.GuardarTicketAsync(email, Secretos.Hash(ticket), ahora.AddMinutes(10), ct);
        return Pagina(LoginPage.RenderBases(s.Cliente.Nombre, Host(s.RedirectUri), campos, Af(), ticket, bases));
    }

    /// <summary>El hash incluye el mail: el mismo codigo de 6 digitos de otro mail no sirve.</summary>
    private static string HashCodigo(string email, string codigo) => Secretos.Hash(email + "|" + codigo);

    private static async Task<IResult> FinalizarEmailAsync(HttpContext ctx, string email, AccesoBase acceso, Solicitud s, IAuthStore store,
        IAccesoStore accesos, IEmailSender mail, AuthOptions opt, McpRuntimeSettings cfg, ILogger log, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        // Registro tecnico del usuario (para los refresh tokens): sin clave utilizable, solo entra por mail.
        var u = await store.GetUsuarioAsync(email, ct);
        if (u is null)
        {
            try { await store.CrearUsuarioAsync(email, email, "email-otp", null, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Alta concurrente del usuario por mail; se reintenta la lectura."); }
            u = await store.GetUsuarioAsync(email, ct);
        }
        if (u is not { Activo: true })
        {
            await store.EventoAsync("login_rechazado", email, s.Cliente.ClientId, Ip(ctx), "usuario inactivo", ct);
            return PaginaError("Tu usuario esta dado de baja. Consulta con ALFA NET.");
        }
        await store.RegistrarLoginAsync(u.Id, true, 0, null, ct);
        await accesos.RegistrarIngresoAsync(email, acceso.IdBase, ct);
        var codigo = Secretos.NuevoToken();
        await store.AddCodigoAsync(new CodigoAuth(Secretos.Hash(codigo), s.Cliente.ClientId, u.Id, s.RedirectUri, s.CodeChallenge, s.Scope, opt.Resource,
            ahora.AddSeconds(cfg.CodigoSeg), acceso.IdBase.ToString(System.Globalization.CultureInfo.InvariantCulture), ConexionPorBaseResolver.MetodoEmail), ct);
        await store.EventoAsync("login_ok", email, s.Cliente.ClientId, Ip(ctx), $"por mail, base {acceso.IdBase}", ct);

        if (cfg.EmailAvisoConexion)
        {
            try { await mail.EnviarAsync(Mails.AvisoConexion(email, s.Cliente.Nombre, acceso.NombreBase, HoraArgentina(ahora)), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "No se pudo enviar el aviso de conexion."); }
        }
        return Redirigir(s.RedirectUri, s.State, opt.Issuer, ("code", codigo));
    }

    private static DateTime HoraArgentina(DateTime utc)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time")); }
        catch (Exception) { return utc.AddHours(-3); }
    }

    // ---- /oauth/token --------------------------------------------------------------------------------------------------
    private static async Task<IResult> Token(HttpContext ctx, IAuthStore store, IAccesoStore accesos, AuthOptions opt, TokenService tokens, IMcpConfigProvider config, ILogger<TokenService> log, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers.Pragma = "no-cache";
        try
        {
            var cfg = await Config(config, log, ct);
            if (cfg is null || !cfg.Habilitado) return ErrorJson("temporarily_unavailable", "Servicio no disponible.", 503);
            if (!ctx.Request.HasFormContentType) return ErrorJson("invalid_request", "Se espera application/x-www-form-urlencoded.");
            var f = await ctx.Request.ReadFormAsync(ct);
            string? V(string k) => f[k].FirstOrDefault();
            var ahora = DateTime.UtcNow;
            if (Random.Shared.Next(25) == 0) await store.PurgarVencidosAsync(ahora, ct);

            var clientId = V("client_id");
            if (string.IsNullOrEmpty(clientId) || await store.GetClienteAsync(clientId, ct) is null) return ErrorJson("invalid_client", "Cliente desconocido.", 401);
            if (V("resource") is { Length: > 0 } r && !string.Equals(r.TrimEnd('/'), opt.Resource, StringComparison.Ordinal))
                return ErrorJson("invalid_target", "El parametro resource no corresponde a este servidor.");

            switch (V("grant_type"))
            {
                case "authorization_code":
                {
                    var (cod, reuso) = await store.ConsumirCodigoAsync(Secretos.Hash(V("code") ?? ""), ahora, ct);
                    if (cod is null) return ErrorJson("invalid_grant", "Codigo invalido o vencido.");
                    if (reuso)
                    {
                        await store.RevocarUsuarioClienteAsync(cod.UsuarioId, cod.ClientId, ct);
                        await store.EventoAsync("codigo_reutilizado", null, cod.ClientId, Ip(ctx), "se revocaron los refresh tokens del usuario/cliente", ct);
                        return ErrorJson("invalid_grant", "Codigo ya utilizado.");
                    }
                    if (cod.ClientId != clientId || cod.RedirectUri != V("redirect_uri") || !Pkce.VerificarS256(V("code_verifier"), cod.CodeChallenge))
                    {
                        await store.EventoAsync("token_rechazado", null, clientId, Ip(ctx), "client/redirect/PKCE no coinciden", ct);
                        return ErrorJson("invalid_grant", "El codigo no coincide con la solicitud.");
                    }
                    var u = await store.GetUsuarioPorIdAsync(cod.UsuarioId, ct);
                    if (u is not { Activo: true }) return ErrorJson("invalid_grant", "Usuario inactivo.");
                    return await Emitir(store, tokens, cfg, u, clientId, cod.Scope, cod.Resource, Guid.NewGuid(), null, cod.Base, cod.Metodo, ahora, ctx, ct);
                }
                case "refresh_token":
                {
                    var hash = Secretos.Hash(V("refresh_token") ?? "");
                    var (rt, invalido) = await store.GetRefreshAsync(hash, ct);
                    if (rt is null) return ErrorJson("invalid_grant", "Refresh token invalido.");
                    if (invalido)
                    {
                        await store.RevocarFamiliaAsync(rt.Familia, ct); // reuso de un token ya rotado: se corta toda la cadena
                        await store.EventoAsync("refresh_reutilizado", null, rt.ClientId, Ip(ctx), "se revoco la familia", ct);
                        return ErrorJson("invalid_grant", "Refresh token ya utilizado.");
                    }
                    if (rt.ClientId != clientId || rt.ExpiraUtc <= ahora) return ErrorJson("invalid_grant", "Refresh token invalido o vencido.");
                    var u = await store.GetUsuarioPorIdAsync(rt.UsuarioId, ct);
                    if (u is not { Activo: true }) { await store.RevocarFamiliaAsync(rt.Familia, ct); return ErrorJson("invalid_grant", "Usuario inactivo."); }
                    // Ingreso por mail: el acceso a la base se revalida en cada renovacion (baja en ALFA_CENTRAL => no renueva mas).
                    if (rt.Metodo == ConexionPorBaseResolver.MetodoEmail
                        && !(await accesos.AccesosAsync(u.Usuario, ct)).Any(a => a.IdBase.ToString(System.Globalization.CultureInfo.InvariantCulture) == rt.Base))
                    {
                        await store.RevocarFamiliaAsync(rt.Familia, ct);
                        await store.EventoAsync("refresh_sin_acceso", u.Usuario, rt.ClientId, Ip(ctx), $"base {rt.Base}", ct);
                        return ErrorJson("invalid_grant", "El acceso a esta base fue dado de baja.");
                    }
                    return await Emitir(store, tokens, cfg, u, clientId, rt.Scope, rt.Resource, rt.Familia, hash, rt.Base, rt.Metodo, ahora, ctx, ct);
                }
                default:
                    return ErrorJson("unsupported_grant_type", "grant_type no soportado.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var incidente = Guid.NewGuid().ToString("N")[..8];
            log.LogError(ex, "Falla en /oauth/token (incidente {Incidente}).", incidente);
            return ErrorJson("server_error", $"Error interno (incidente {incidente}).", 500);
        }
    }

    private static async Task<IResult> Emitir(IAuthStore store, TokenService tokens, McpRuntimeSettings cfg, UsuarioAuth u, string clientId,
        string scope, string resource, Guid familia, string? hashAnterior, string? baseId, string metodo, DateTime ahora, HttpContext ctx, CancellationToken ct)
    {
        var refresh = Secretos.NuevoToken();
        var nuevo = new RefreshAuth(Secretos.Hash(refresh), clientId, u.Id, scope, resource, ahora.AddDays(cfg.RefreshDias), familia, baseId, metodo);
        if (hashAnterior is null) await store.AddRefreshAsync(nuevo, ct);
        else if (!await store.RotarRefreshAsync(hashAnterior, nuevo, ahora, ct))
        {
            await store.RevocarFamiliaAsync(familia, ct); // dos requests usaron el mismo refresh a la vez
            await store.EventoAsync("refresh_carrera", u.Usuario, clientId, Ip(ctx), "uso simultaneo, familia revocada", ct);
            return ErrorJson("invalid_grant", "Refresh token ya utilizado.");
        }
        var vida = TimeSpan.FromMinutes(cfg.AccessTokenMin);
        await store.EventoAsync(hashAnterior is null ? "token_emitido" : "token_renovado", u.Usuario, clientId, Ip(ctx), null, ct);
        return Json(new Dictionary<string, object?>
        {
            ["access_token"] = tokens.CrearAccessToken(u, clientId, scope, vida, ahora, baseId, metodo),
            ["token_type"] = "Bearer",
            ["expires_in"] = (int)vida.TotalSeconds,
            ["refresh_token"] = refresh,
            ["scope"] = scope
        });
    }

    // ---- utilidades ----------------------------------------------------------------------------------------------------
    /// <summary>Lee la configuracion; si la base no responde devuelve null (el llamador muestra "servicio no disponible" en vez de un 500 crudo).</summary>
    private static async Task<McpRuntimeSettings?> Config(IMcpConfigProvider config, ILogger log, CancellationToken ct)
    {
        try { return await config.GetAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "No se pudo leer la configuracion IA_MCP_* (OAuth).");
            return null;
        }
    }

    private static Dictionary<string, string?> Reenviar(Func<string, string?> v) =>
        new[] { "client_id", "redirect_uri", "response_type", "code_challenge", "code_challenge_method", "state", "scope", "resource" }
            .Where(k => v(k) is not null).ToDictionary(k => k, k => v(k));

    private static IResult Redirigir(string redirectUri, string? state, string issuer, params (string K, string V)[] pares)
    {
        var d = pares.ToDictionary(p => p.K, p => (string?)p.V);
        if (state is not null) d["state"] = state;
        d["iss"] = issuer; // RFC 9207: protege contra confusion de servidores de autorizacion
        return Results.Redirect(QueryHelpers.AddQueryString(redirectUri, d));
    }

    private static string LimpiarNombre(string? nombre)
    {
        var s = new string((nombre ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length == 0 ? "Cliente MCP" : s.Length > 100 ? s[..100] : s;
    }

    private static string Host(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var u) ? u.Host : "";
    private static string? Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();
    private static IResult Json(object o) => Results.Json(o);

    private static IResult ErrorJson(string error, string descripcion, int status = 400) =>
        Results.Json(new Dictionary<string, object?> { ["error"] = error, ["error_description"] = descripcion }, statusCode: status);

    private static IResult Pagina(string html) =>
        Results.Content(html, "text/html; charset=utf-8");

    private static IResult PaginaError(string mensaje) =>
        Results.Content(LoginPage.RenderError(mensaje), "text/html; charset=utf-8", statusCode: 400);
}

public static class LoginPage
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private const string Estilo = "body{font-family:system-ui,sans-serif;background:#f4f5f7;margin:0;display:flex;min-height:100vh;align-items:center;justify-content:center}" +
        ".c{background:#fff;padding:2rem;border-radius:12px;max-width:380px;width:100%;box-shadow:0 2px 12px #0002}h1{font-size:1.2rem;margin:0 0 .5rem}" +
        "p{color:#444;font-size:.92rem;line-height:1.4}label{display:block;margin:.8rem 0 .2rem;font-size:.85rem}input[type=text],input[type=password],input[type=email]{width:100%;box-sizing:border-box;padding:.6rem;border:1px solid #bbb;border-radius:6px}" +
        "button{padding:.6rem 1rem;border-radius:6px;border:0;font-size:.95rem;cursor:pointer}.ok{background:#1b5e20;color:#fff}.no{background:#e0e0e0;margin-left:.5rem}.err{color:#b00020;font-size:.88rem;margin-top:.8rem}" +
        ".aviso{color:#1b5e20;font-size:.88rem;margin-top:.8rem}.alt{font-size:.82rem;margin-top:1rem}.alt a{color:#555}.b{display:block;margin:.4rem 0;font-size:.92rem}";

    private static string Inicio(string cliente, string host, Dictionary<string, string?> campos, string antiforgery) => $"""
        <!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Autorizar acceso</title><style>{Estilo}</style></head><body><div class="c">
        <h1>Autorizar acceso a los datos</h1>
        <p><b>{E(cliente)}</b> (<span>{E(host)}</span>) pide permiso para <b>consultar en modo solo lectura</b> la informacion de ventas, compras, clientes, proveedores, cuenta corriente y stock.</p>
        <form method="post" autocomplete="off">{string.Concat(campos.Select(c => $"<input type=\"hidden\" name=\"{E(c.Key)}\" value=\"{E(c.Value)}\">"))}
        <input type="hidden" name="__RequestVerificationToken" value="{E(antiforgery)}">
        """;

    private static string Mensajes(string? aviso, string? error) =>
        (aviso is null ? "" : $"<div class=\"aviso\" role=\"status\">{E(aviso)}</div>") + (error is null ? "" : $"<div class=\"err\" role=\"alert\">{E(error)}</div>");

    private static string Fin(string boton, string valor, string? linkAlternativo, string textoAlternativo) => $"""
        <p style="margin-top:1.2rem"><button class="ok" type="submit" name="accion" value="{valor}">{E(boton)}</button>
        <button class="no" type="submit" name="accion" value="cancelar" formnovalidate>Cancelar</button></p>
        </form>{(linkAlternativo is null ? "" : $"<p class=\"alt\"><a href=\"{E(linkAlternativo)}\">{E(textoAlternativo)}</a></p>")}</div></body></html>
        """;

    /// <summary>Ingreso con usuario, clave y (si corresponde) codigo de la app autenticadora. Para el personal de ALFA NET.</summary>
    public static string Render(string cliente, string host, Dictionary<string, string?> campos, string antiforgery, string? error, bool codigoObligatorio, string? linkEmail = null) =>
        Inicio(cliente, host, campos, antiforgery) + $"""
        <label for="u">Usuario</label><input id="u" type="text" name="usuario" required maxlength="100" autofocus>
        <label for="p">Clave</label><input id="p" type="password" name="clave" required maxlength="200">
        <label for="c">Codigo de la app autenticadora (6 digitos)</label><input id="c" type="text" name="codigo" inputmode="numeric" autocomplete="one-time-code" maxlength="20" {(codigoObligatorio ? "required" : "")}>
        {Mensajes(null, error)}
        """ + Fin("Ingresar y autorizar", "autorizar", linkEmail, "Ingresar con mi mail");

    /// <summary>Paso 1 del ingreso por mail.</summary>
    public static string RenderEmail(string cliente, string host, Dictionary<string, string?> campos, string antiforgery, string? error, string? linkClave) =>
        Inicio(cliente, host, campos, antiforgery) + $"""
        <label for="e">Tu mail</label><input id="e" type="email" name="email" required maxlength="200" autocomplete="email" autofocus>
        <p style="font-size:.82rem;color:#666">Te enviamos un codigo de 6 digitos a ese mail. Tiene que ser el mail que tenes registrado en ALFA NET.</p>
        {Mensajes(null, error)}
        """ + Fin("Enviarme el codigo", "enviar-codigo", linkClave, "Ingresar con usuario y clave (personal de ALFA NET)");

    /// <summary>Paso 2 del ingreso por mail.</summary>
    public static string RenderCodigo(string cliente, string host, Dictionary<string, string?> campos, string antiforgery, string email, string? aviso, string? error, string? linkClave) =>
        Inicio(cliente, host, campos, antiforgery) + $"""
        <input type="hidden" name="email" value="{E(email)}">
        <p>Mail: <b>{E(email)}</b></p>
        <label for="k">Codigo recibido por mail (6 digitos)</label><input id="k" type="text" name="codigo_email" inputmode="numeric" autocomplete="one-time-code" maxlength="12" required autofocus>
        {Mensajes(aviso, error)}
        <p style="margin-top:1.2rem"><button class="ok" type="submit" name="accion" value="verificar-codigo">Ingresar y autorizar</button>
        <button class="no" type="submit" name="accion" value="enviar-codigo" formnovalidate>Enviarme otro codigo</button></p>
        </form><p class="alt"><a href="{E(linkClave)}">Ingresar con usuario y clave (personal de ALFA NET)</a></p></div></body></html>
        """;

    /// <summary>Paso 3 (solo si el mail tiene varias bases): se muestra despues de validar el codigo.</summary>
    public static string RenderBases(string cliente, string host, Dictionary<string, string?> campos, string antiforgery, string ticket, IReadOnlyList<AccesoBase> bases) =>
        Inicio(cliente, host, campos, antiforgery) + $"""
        <input type="hidden" name="ticket" value="{E(ticket)}">
        <p>Tu mail tiene acceso a varias bases. Elegi cual queres consultar con esta conexion (para cambiar, desconecta y volve a conectar):</p>
        {string.Concat(bases.Select((b, i) => $"<label class=\"b\"><input type=\"radio\" name=\"base\" value=\"{b.IdBase}\" {(i == 0 ? "checked" : "")}> {E(b.NombreBase)}{(string.IsNullOrWhiteSpace(b.Cliente) ? "" : " - " + E(b.Cliente))}</label>"))}
        """ + Fin("Conectar", "elegir-base", null, "");

    public static string RenderError(string mensaje) =>
        $"<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Error</title><style>{Estilo}</style></head><body><div class=\"c\"><h1>No se pudo continuar</h1><p>{E(mensaje)}</p></div></body></html>";
}
