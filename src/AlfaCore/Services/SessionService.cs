using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class SessionService : ISessionService
{
    private static readonly object FileLock = new();

    private readonly string _filePath;
    private readonly SessionDto? _defaultSession;
    private Guid? _activeSessionId;

    public event Action? SessionChanged;

    public SessionService(
        IConfiguration configuration,
        IWebHostEnvironment env,
        IHttpContextAccessor httpContextAccessor)
    {
        _filePath = Path.Combine(env.ContentRootPath, "App_Data", "sessions.json");
        _defaultSession = CreateSessionFromConfig(configuration);
        _activeSessionId = ReadActiveSessionId(httpContextAccessor);
    }

    public string GetConnectionString()
    {
        lock (FileLock)
        {
            var sessions = LoadSessionsUnsafe();
            var active = ResolveActiveSessionUnsafe(sessions);
            return active is null ? string.Empty : Build(active);
        }
    }

    public SessionDto? GetActiveSession()
    {
        lock (FileLock)
        {
            var sessions = LoadSessionsUnsafe();
            var active = ResolveActiveSessionUnsafe(sessions);
            return active is null ? null : Clone(active, true);
        }
    }

    public IReadOnlyList<SessionDto> GetAllSessions()
    {
        lock (FileLock)
        {
            var sessions = LoadSessionsUnsafe();
            var active = ResolveActiveSessionUnsafe(sessions);
            return sessions
                .Select(s => Clone(s, active is not null && SameConnection(s, active)))
                .ToArray();
        }
    }

    public void SwitchSession(Guid id)
    {
        lock (FileLock)
        {
            var sessions = LoadSessionsUnsafe();
            var selected = sessions.FirstOrDefault(s => s.Id == id)
                ?? throw new InvalidOperationException("Sesion no encontrada.");

            _activeSessionId = id;

            foreach (var session in sessions)
                session.Activa = session.Id == selected.Id;

            SaveUnsafe(sessions);
        }

        SessionChanged?.Invoke();
    }

    public void AddSession(string nombre, string servidor, string baseDatos, string usuario, string password)
    {
        lock (FileLock)
        {
            var sessions = LoadSessionsUnsafe();
            sessions.Add(new SessionDto
            {
                Nombre = nombre.Trim(),
                Servidor = servidor.Trim(),
                BaseDatos = baseDatos.Trim(),
                Usuario = usuario.Trim(),
                Password = password,
                TrustServerCertificate = true,
                Activa = false
            });
            SaveUnsafe(sessions);
        }
    }

    public void DeleteSession(Guid id)
    {
        lock (FileLock)
        {
            var sessions = LoadSessionsUnsafe();
            var sessionToDelete = sessions.FirstOrDefault(s => s.Id == id);
            if (sessionToDelete is null || _activeSessionId == id)
                return;

            sessions.Remove(sessionToDelete);
            SaveUnsafe(sessions);
        }
    }

    private List<SessionDto> LoadSessionsUnsafe()
    {
        var fileExists = File.Exists(_filePath);
        try
        {
            if (fileExists)
            {
                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<SessionesData>(json, JsonOpts);
                if (data?.Sessions.Count > 0)
                {
                    PersistCookieSelectedSessionIfNeeded(data.Sessions);
                    return data.Sessions;
                }
            }
        }
        catch
        {
            // Archivo existente con formato invalido: no sobreescribir automaticamente.
            // Se mantiene fallback en memoria para no perder sesiones persistidas.
            return BuildFallbackSessions();
        }

        if (fileExists)
            return BuildFallbackSessions();

        var seededSessions = BuildFallbackSessions();
        SaveUnsafe(seededSessions);
        return seededSessions;
    }

    private List<SessionDto> BuildFallbackSessions() =>
    [
        _defaultSession is null
            ? new SessionDto { Nombre = "Sesion inicial", Activa = true }
            : Clone(_defaultSession, true)
    ];

    private void SaveUnsafe(List<SessionDto> sessions)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(new SessionesData
            {
                Sessions = sessions.Select(s => Clone(s, false)).ToList()
            }, JsonOpts);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // No bloquear la aplicacion si el catalogo no puede escribirse.
        }
    }

    private SessionDto? ResolveActiveSessionUnsafe(List<SessionDto> sessions)
    {
        if (_activeSessionId is Guid activeId)
        {
            var selected = sessions.FirstOrDefault(s => s.Id == activeId);
            if (selected is not null)
                return selected;
        }

        var persistedActive = sessions.FirstOrDefault(s => s.Activa);
        if (persistedActive is not null)
            return persistedActive;

        if (sessions.Count > 0)
            return sessions[0];

        if (_defaultSession is not null)
            return _defaultSession;

        return null;
    }

    private void PersistCookieSelectedSessionIfNeeded(List<SessionDto> sessions)
    {
        if (_activeSessionId is not Guid activeId)
            return;

        var selected = sessions.FirstOrDefault(s => s.Id == activeId);
        if (selected is null)
            return;

        if (sessions.Any(s => s.Activa && s.Id == activeId))
            return;

        foreach (var session in sessions)
            session.Activa = session.Id == activeId;

        SaveUnsafe(sessions);
    }

    private static Guid? ReadActiveSessionId(IHttpContextAccessor httpContextAccessor)
    {
        var raw = httpContextAccessor.HttpContext?.Request.Cookies[ISessionService.ActiveSessionCookieName];
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static SessionDto? CreateSessionFromConfig(IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("AlfaGestion") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cs))
            return null;

        try
        {
            var b = new SqlConnectionStringBuilder(cs);
            return new SessionDto
            {
                Nombre = $"{b.DataSource} - {b.InitialCatalog}",
                Servidor = b.DataSource,
                BaseDatos = b.InitialCatalog,
                Usuario = b.UserID,
                Password = b.Password,
                TrustServerCertificate = true,
                Activa = false
            };
        }
        catch
        {
            return null;
        }
    }

    private static SessionDto Clone(SessionDto source, bool activa) =>
        new()
        {
            Id = source.Id,
            Nombre = source.Nombre,
            Servidor = source.Servidor,
            BaseDatos = source.BaseDatos,
            Usuario = source.Usuario,
            Password = source.Password,
            TrustServerCertificate = source.TrustServerCertificate,
            Activa = activa
        };

    private static bool SameConnection(SessionDto left, SessionDto right)
        => string.Equals(left.Servidor, right.Servidor, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.BaseDatos, right.BaseDatos, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Usuario, right.Usuario, StringComparison.OrdinalIgnoreCase);

    private static string Build(SessionDto s) =>
        new SqlConnectionStringBuilder
        {
            DataSource = s.Servidor,
            InitialCatalog = s.BaseDatos,
            UserID = s.Usuario,
            Password = s.Password,
            TrustServerCertificate = s.TrustServerCertificate,
            ApplicationName = "AlfaCore"
        }.ConnectionString;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
}
