using System.Text.Json;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ConexionClienteService : IConexionClienteService, IDisposable
{
    private readonly IAppModeService _appMode;
    private readonly IAppUserSessionService _appUserSession;
    private readonly ICentralBasesService _basesService;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private IReadOnlyList<SessionDto> _cachedSessions = [];
    private Guid? _activeSessionId;
    private string? _cacheKey;
    private SessionDto? _webhookOverride;

    public ConexionClienteService(
        IAppModeService appMode,
        IAppUserSessionService appUserSession,
        ICentralBasesService basesService,
        IHostEnvironment hostEnvironment)
    {
        _appMode = appMode;
        _appUserSession = appUserSession;
        _basesService = basesService;
        _hostEnvironment = hostEnvironment;
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

    public SessionDto? GetActiveSession()
    {
        lock (_lock)
        {
            if (_webhookOverride is not null)
                return Clone(_webhookOverride, true);
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

    private static Guid BuildGuidFromBaseId(int baseId)
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
