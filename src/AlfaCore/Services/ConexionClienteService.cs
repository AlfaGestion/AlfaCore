using System.Text.Json;
using AlfaCore.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ConexionClienteService : IConexionClienteService, IDisposable
{
    private readonly IAppModeService _appMode;
    private readonly IAppUserSessionService _appUserSession;
    private readonly ICentralBasesService _basesService;
    private readonly ICentralClientesService _clientesService;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly NavigationManager _navigationManager;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private IReadOnlyList<SessionDto> _cachedSessions = [];
    private Guid? _activeSessionId;
    private string? _cacheKey;
    private SessionDto? _webhookOverride;
    private SessionDto? _routeSessionOverride;
    private string? _routeSessionKey;
    // La URL actual tiene forma tenant (/{idweb}/{idbase}) pero no corresponde a una base válida
    // (base inexistente o idweb de otra empresa). En ese caso GetActiveSession falla cerrado: no
    // cae a la sesión cacheada del usuario ni a ninguna otra base.
    private bool _routeSessionRejected;

    public ConexionClienteService(
        IAppModeService appMode,
        IAppUserSessionService appUserSession,
        ICentralBasesService basesService,
        ICentralClientesService clientesService,
        IHostEnvironment hostEnvironment,
        NavigationManager navigationManager)
    {
        _appMode = appMode;
        _appUserSession = appUserSession;
        _basesService = basesService;
        _clientesService = clientesService;
        _hostEnvironment = hostEnvironment;
        _navigationManager = navigationManager;
        _appUserSession.StateChanged += OnUserStateChanged;
    }

    public event Action? SessionChanged;

    public string GetConnectionString()
    {
        var active = GetActiveSession();
        return active is null ? string.Empty : BuildConnectionString(active);
    }

    /// <summary>
    /// Fuerza la base activa de este scope a la indicada, sin pasar por la lista de bases
    /// centrales (IdCliente) de <see cref="IAppUserSessionService.CurrentUser"/>. Pensado
    /// originalmente para requests sin sesión de usuario (ej. webhooks de
    /// WhatsApp/Instagram/Facebook/MercadoLibre), donde el tenant se resuelve por otro medio (un
    /// token en la URL) antes de tocar cualquier tabla. También lo usa el login directo por ruta
    /// (/{idweb}/{idbase} sin sesión central previa, ver MainLayout.LoginAsync) para activar la
    /// conexión de la base recién validada — en ese caso SÓLO se llama después de validar
    /// usuario/contraseña contra esa base (nunca como autorización en sí misma). Tiene prioridad
    /// sobre la resolución normal mientras dure este scope, hasta <see cref="ClearWebhookOverride"/>
    /// o un logout (ver <see cref="OnUserStateChanged"/>).
    /// </summary>
    public void SetWebhookOverride(SessionDto session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            _webhookOverride = session;
        }

        SessionChanged?.Invoke();
    }

    public void ClearWebhookOverride()
    {
        lock (_lock)
        {
            _webhookOverride = null;
        }

        SessionChanged?.Invoke();
    }

    /// <summary>
    /// Ver <see cref="IConexionClienteService.GetWebhookOverride"/>. A propósito NO llama a
    /// <see cref="GetActiveSession"/>: solo mira <c>_webhookOverride</c> bajo lock. Ni siquiera en
    /// el escenario en que <see cref="GetActiveSession"/> volviera a resolver ruta/NavigationManager
    /// antes que el override (regresión de la ordenación de arriba) este método se vería afectado,
    /// porque no pasa por esa ruta de código en ningún caso.
    /// </summary>
    public SessionDto? GetWebhookOverride(int expectedBaseId)
    {
        lock (_lock)
        {
            return _webhookOverride is { } webhookOverride && webhookOverride.BaseId == expectedBaseId
                ? Clone(webhookOverride, true)
                : null;
        }
    }

    public SessionDto? GetActiveSession()
    {
        // El webhook override se resuelve ANTES que cualquier otra cosa y sin excepción: es la
        // única forma en que un request sin circuito Blazor (webhooks de WhatsApp/Instagram/
        // Facebook/MercadoLibre, login directo por ruta) puede pasar por acá. Si se resolviera
        // ResolveRouteSessionOverride primero, esa llamada toca NavigationManager.Uri, que en un
        // request HTTP puro (sin circuito) todavía no fue inicializado y tira
        // InvalidOperationException: "'RemoteNavigationManager' has not been initialized." --
        // visto en producción tumbando el webhook de WhatsApp con 500 antes de llegar a
        // procesar nada. Ver SetWebhookOverride: la intención siempre fue que tuviera prioridad.
        lock (_lock)
        {
            if (_webhookOverride is not null)
                return Clone(_webhookOverride, true);
        }

        if (_appMode.IsSaaSMode)
        {
            var routeSession = ResolveRouteSessionOverride();
            if (routeSession is not null)
                return Clone(routeSession, true);

            lock (_lock)
            {
                if (_routeSessionRejected)
                    return null;
            }
        }

        if (!_appMode.IsSaaSMode)
            return GetLegacyActiveSession();

        lock (_lock)
        {
            RefreshSaaSCacheIfNeeded();

            if (_activeSessionId is Guid activeId)
            {
                var selected = _cachedSessions.FirstOrDefault(s => s.Id == activeId);
                if (selected is not null)
                    return Clone(selected, true);
            }

            if (_cachedSessions.Count == 1)
            {
                _activeSessionId = _cachedSessions[0].Id;
                return Clone(_cachedSessions[0], true);
            }

            return null;
        }
    }

    public IReadOnlyList<SessionDto> GetAllSessions()
    {
        if (!_appMode.IsSaaSMode)
            return GetLegacyAllSessions();

        lock (_lock)
        {
            RefreshSaaSCacheIfNeeded();
            var active = GetActiveSession();
            return _cachedSessions
                .Select(s => Clone(s, active is not null && s.Id == active.Id))
                .ToArray();
        }
    }

    public void SwitchSession(Guid id)
    {
        if (!_appMode.IsSaaSMode)
        {
            SwitchLegacySession(id);
            _appUserSession.EnsureAuthorizedForSession(id);
            return;
        }

        lock (_lock)
        {
            RefreshSaaSCacheIfNeeded();
            var selected = _cachedSessions.FirstOrDefault(s => s.Id == id)
                ?? throw new InvalidOperationException("Base no encontrada.");

            _activeSessionId = selected.Id;
        }

        SessionChanged?.Invoke();

        // Cambiar de base activa nunca debe heredar un login interno (TA_USUARIOS) validado
        // contra otra base: si el usuario ya estaba autorizado pero para un id distinto, esto
        // vuelve a exigir RequiresInternalLogin=true para la base nueva.
        _appUserSession.EnsureAuthorizedForSession(id);
    }

    public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password)
    {
        if (_appMode.IsSaaSMode)
            throw new NotSupportedException("La administracion manual de conexiones fue reemplazada por ALFA_CENTRAL.");

        var sessions = LoadLegacySessions().Sessions;
        var id = Guid.NewGuid();
        sessions.Add(new SessionDto
        {
            Id = id,
            Nombre = string.IsNullOrWhiteSpace(nombre) ? $"{servidor} - {baseDatos}" : nombre.Trim(),
            Servidor = servidor.Trim(),
            BaseDatos = baseDatos.Trim(),
            Usuario = usuario.Trim(),
            Password = password.Trim(),
            TrustServerCertificate = true,
            Activa = sessions.Count == 0
        });
        SaveLegacySessions(new SessionesData { Sessions = sessions });
        SessionChanged?.Invoke();
        return id;
    }

    public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password)
    {
        if (_appMode.IsSaaSMode)
            throw new NotSupportedException("La administracion manual de conexiones fue reemplazada por ALFA_CENTRAL.");

        var legacy = LoadLegacySessions();
        var session = legacy.Sessions.FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException("Base no encontrada.");

        session.Nombre = string.IsNullOrWhiteSpace(nombre) ? $"{servidor} - {baseDatos}" : nombre.Trim();
        session.Servidor = servidor.Trim();
        session.BaseDatos = baseDatos.Trim();
        session.Usuario = usuario.Trim();
        session.Password = password.Trim();

        SaveLegacySessions(legacy);
        SessionChanged?.Invoke();
    }

    public void DeleteSession(Guid id)
    {
        if (_appMode.IsSaaSMode)
            throw new NotSupportedException("La administracion manual de conexiones fue reemplazada por ALFA_CENTRAL.");

        var legacy = LoadLegacySessions();
        legacy.Sessions.RemoveAll(s => s.Id == id);
        SaveLegacySessions(legacy);
        SessionChanged?.Invoke();
    }

    public void ClearActiveSession()
    {
        if (!_appMode.IsSaaSMode)
        {
            var legacy = LoadLegacySessions();
            foreach (var session in legacy.Sessions)
                session.Activa = false;
            SaveLegacySessions(legacy);
            SessionChanged?.Invoke();
            return;
        }

        lock (_lock)
        {
            _activeSessionId = null;
        }

        SessionChanged?.Invoke();
    }

    public void Dispose()
    {
        _appUserSession.StateChanged -= OnUserStateChanged;
    }

    private void OnUserStateChanged()
    {
        if (!_appMode.IsSaaSMode)
            return;

        lock (_lock)
        {
            _cachedSessions = Array.Empty<SessionDto>();
            if (_appUserSession.CurrentUser is null)
            {
                _activeSessionId = null;

                // Logout (o cualquier otro camino que deje _currentUser en null) debe invalidar
                // también la conexión activada por SetWebhookOverride para un login directo por
                // ruta (ver MainLayout.LoginAsync): sin esto, la base activada quedaba pegada al
                // scope después de cerrar sesión.
                _webhookOverride = null;
            }
            _routeSessionOverride = null;
            _routeSessionKey = null;
            _routeSessionRejected = false;
            _cacheKey = null;
        }

        SessionChanged?.Invoke();
    }

    private void RefreshSaaSCacheIfNeeded()
    {
        var currentUser = _appUserSession.CurrentUser;
        if (currentUser is null)
        {
            _cachedSessions = Array.Empty<SessionDto>();
            _activeSessionId = null;
            return;
        }

        var clientKey = $"{currentUser.IdCliente}:{currentUser.SuperAdmin}:{currentUser.IdWeb}";
        if (string.Equals(clientKey, _cacheKey, StringComparison.Ordinal))
            return;

        var previousActiveId = _activeSessionId;
        _cacheKey = clientKey;
        _cachedSessions = Task.Run(() => LoadSaaSSessionsAsync(currentUser)).GetAwaiter().GetResult();
        _activeSessionId = ResolveActiveSessionId(_cachedSessions, previousActiveId);
    }

    /// <summary>
    /// En SaaS, una ruta /{idweb}/{idbase}/... identifica la base efectiva desde el primer
    /// acceso. Esto se resuelve de forma síncrona porque los servicios de dominio pueden pedir
    /// la conexión durante el primer ciclo de vida de una página, antes de que MainLayout haya
    /// podido restaurar el token del navegador y ejecutar su activación posterior a F5.
    /// El override queda en este servicio scoped: dos circuitos/pestañas no lo comparten.
    /// Sólo se acepta una ruta con forma tenant real (<see cref="TenantRouteParser"/>: rutas root
    /// como /consultas/12 NO son idweb=consultas/idbase=12) y cuyo idweb sea el del cliente dueño
    /// de la base en ALFA_CENTRAL. Si la forma es tenant pero la base no existe o el idweb no
    /// coincide, se marca <c>_routeSessionRejected</c> y GetActiveSession falla cerrado.
    /// </summary>
    private SessionDto? ResolveRouteSessionOverride()
    {
        var path = _navigationManager.ToBaseRelativePath(_navigationManager.Uri);
        if (!TenantRouteParser.TryParse(path, out var idWeb, out var baseId))
        {
            lock (_lock)
            {
                _routeSessionOverride = null;
                _routeSessionKey = null;
                _routeSessionRejected = false;
            }

            return null;
        }

        var routeKey = $"{idWeb}|{baseId}";
        lock (_lock)
        {
            if (string.Equals(_routeSessionKey, routeKey, StringComparison.OrdinalIgnoreCase))
                return _routeSessionOverride;
        }

        // GetActiveSession es una API síncrona porque la consumen servicios de dominio durante
        // sus constructores/ciclos iniciales. Ejecutamos la consulta central fuera del contexto
        // de Blazor para no bloquearlo si la resolución ocurre durante un render inicial.
        SessionDto? resolved;
        try
        {
            resolved = Task.Run(() => ResolveValidatedRouteSessionAsync(idWeb, baseId))
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Sin poder validar contra ALFA_CENTRAL no hay base confiable para esta URL: se falla
            // cerrado sin cachear la clave, para reintentar en el próximo acceso.
            lock (_lock)
            {
                _routeSessionOverride = null;
                _routeSessionKey = null;
                _routeSessionRejected = true;
            }

            return null;
        }

        lock (_lock)
        {
            _routeSessionKey = routeKey;
            _routeSessionOverride = resolved;
            _routeSessionRejected = resolved is null;
        }

        return resolved;
    }

    private async Task<SessionDto?> ResolveValidatedRouteSessionAsync(string idWeb, int baseId)
    {
        var routeBase = await _basesService.GetByIdAsync(baseId).ConfigureAwait(false);
        if (routeBase is null)
            return null;

        // Mismo criterio que la autenticación de PublicLinks (Program.TryActivateVb6InstallationAsync):
        // el idweb de la URL tiene que ser el del cliente dueño de la base.
        var cliente = await _clientesService.GetByIdClienteAsync(routeBase.IdCliente).ConfigureAwait(false);
        if (cliente is null
            || string.IsNullOrWhiteSpace(cliente.IdWeb)
            || !string.Equals(cliente.IdWeb.Trim(), idWeb.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;

        return new SessionDto
        {
            Id = SessionDto.BuildGuidFromBaseId(routeBase.IdBase),
            BaseId = routeBase.IdBase,
            Nombre = routeBase.Nombre,
            Servidor = routeBase.DbServer,
            BaseDatos = routeBase.DbName,
            Usuario = routeBase.DbUser,
            Password = routeBase.DbPassword,
            TrustServerCertificate = true,
            Activa = true
        };
    }

    private async Task<IReadOnlyList<SessionDto>> LoadSaaSSessionsAsync(AppUserSessionInfo user)
    {
        var bases = user.SuperAdmin
            ? await _basesService.GetAllAsync().ConfigureAwait(false)
            : await _basesService.GetByClienteAsync(user.IdCliente, false).ConfigureAwait(false);

        return bases
            .Select(Map)
            .OrderBy(s => s.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private SessionDto? GetLegacyActiveSession()
    {
        var legacy = LoadLegacySessions();
        var active = legacy.Sessions.FirstOrDefault(s => s.Activa)
            ?? legacy.Sessions.FirstOrDefault();

        if (active is null)
            return null;

        return Clone(active, true);
    }

    private IReadOnlyList<SessionDto> GetLegacyAllSessions()
    {
        var legacy = LoadLegacySessions();
        var active = legacy.Sessions.FirstOrDefault(s => s.Activa)
            ?? legacy.Sessions.FirstOrDefault();

        return legacy.Sessions
            .Select(s => Clone(s, active is not null && s.Id == active.Id))
            .ToArray();
    }

    private void SwitchLegacySession(Guid id)
    {
        var legacy = LoadLegacySessions();
        var selected = legacy.Sessions.FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException("Base no encontrada.");

        foreach (var session in legacy.Sessions)
            session.Activa = session.Id == selected.Id;

        SaveLegacySessions(legacy);
        SessionChanged?.Invoke();
    }

    private SessionesData LoadLegacySessions()
    {
        var filePath = GetLegacySessionsPath();
        if (!File.Exists(filePath))
            return new SessionesData();

        try
        {
            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<SessionesData>(json, _jsonOptions);
            return data ?? new SessionesData();
        }
        catch
        {
            return new SessionesData();
        }
    }

    private void SaveLegacySessions(SessionesData data)
    {
        var filePath = GetLegacySessionsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? _hostEnvironment.ContentRootPath);
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    private string GetLegacySessionsPath()
        => Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", "sessions.json");

    private Guid? ResolveActiveSessionId(IReadOnlyList<SessionDto> sessions, Guid? preferredActiveId)
    {
        if (preferredActiveId is Guid activeId && sessions.Any(s => s.Id == activeId))
            return activeId;

        if (sessions.Count == 1)
            return sessions[0].Id;

        return null;
    }

    private static SessionDto Map(BaseCentralDto baseDto)
    {
        var sessionId = BuildGuidFromBaseId(baseDto.IdBase);
        return new SessionDto
        {
            Id = sessionId,
            BaseId = baseDto.IdBase,
            Nombre = baseDto.Nombre,
            Servidor = baseDto.DbServer,
            BaseDatos = baseDto.DbName,
            Usuario = baseDto.DbUser,
            Password = baseDto.DbPassword,
            TrustServerCertificate = true,
            Activa = false
        };
    }

    internal static Guid BuildGuidFromBaseId(int baseId)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.GetBytes(baseId).CopyTo(bytes);
        return new Guid(bytes);
    }

    private static SessionDto Clone(SessionDto source, bool activa)
        => new()
        {
            Id = source.Id,
            BaseId = source.BaseId,
            Nombre = source.Nombre,
            Servidor = source.Servidor,
            BaseDatos = source.BaseDatos,
            Usuario = source.Usuario,
            Password = source.Password.Trim(),
            TrustServerCertificate = source.TrustServerCertificate,
            Activa = activa
        };

    private static string BuildConnectionString(SessionDto s)
        => new SqlConnectionStringBuilder
        {
            DataSource = s.Servidor,
            InitialCatalog = s.BaseDatos,
            UserID = s.Usuario,
            Password = s.Password,
            TrustServerCertificate = s.TrustServerCertificate,
            ApplicationName = "AlfaCore"
        }.ConnectionString;
}
