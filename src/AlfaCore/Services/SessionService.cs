using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class SessionService : ISessionService
{
    private readonly string _filePath;
    private readonly List<SessionDto> _sessions = [];
    private readonly object _lock = new();
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
        Load(configuration);
        _activeSessionId = ReadActiveSessionId(httpContextAccessor);
    }

    public string GetConnectionString()
    {
        lock (_lock)
        {
            var active = ResolveActiveSessionUnsafe();
            return active is null ? string.Empty : Build(active);
        }
    }

    public SessionDto? GetActiveSession()
    {
        lock (_lock)
        {
            var active = ResolveActiveSessionUnsafe();
            return active is null ? null : Clone(active, true);
        }
    }

    public IReadOnlyList<SessionDto> GetAllSessions()
    {
        lock (_lock)
        {
            var active = ResolveActiveSessionUnsafe();
            return _sessions
                .Select(s => Clone(s, active is not null && SameConnection(s, active)))
                .ToArray();
        }
    }

    public void SwitchSession(Guid id)
    {
        lock (_lock)
        {
            _ = _sessions.FirstOrDefault(s => s.Id == id)
                ?? throw new InvalidOperationException("Sesion no encontrada.");
            _activeSessionId = id;
        }

        SessionChanged?.Invoke();
    }

    public void AddSession(string nombre, string servidor, string baseDatos, string usuario, string password)
    {
        lock (_lock)
        {
            _sessions.Add(new SessionDto
            {
                Nombre = nombre.Trim(),
                Servidor = servidor.Trim(),
                BaseDatos = baseDatos.Trim(),
                Usuario = usuario.Trim(),
                Password = password,
                TrustServerCertificate = true,
                Activa = false
            });
            Save();
        }
    }

    public void DeleteSession(Guid id)
    {
        lock (_lock)
        {
            var s = _sessions.FirstOrDefault(s => s.Id == id);
            if (s is null || _activeSessionId == id)
                return;

            _sessions.Remove(s);
            Save();
        }
    }

    private void Load(IConfiguration configuration)
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<SessionesData>(json, JsonOpts);
                if (data?.Sessions.Count > 0)
                {
                    var hadPersistedActiveFlag = data.Sessions.Any(s => s.Activa);
                    _sessions.AddRange(data.Sessions.Select(s =>
                    {
                        s.Activa = false;
                        return s;
                    }));

                    if (hadPersistedActiveFlag)
                        Save();

                    return;
                }
            }
        }
        catch
        {
            // Archivo corrupto: se regenera desde la configuracion default.
        }

        SeedFromConfig(configuration);
    }

    private void SeedFromConfig(IConfiguration configuration)
    {
        _sessions.Add(_defaultSession is null
            ? new SessionDto { Nombre = "Sesion inicial", Activa = false }
            : Clone(_defaultSession, false));
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(new SessionesData
            {
                Sessions = _sessions.Select(s => Clone(s, false)).ToList()
            }, JsonOpts);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // No bloquear la aplicacion si el catalogo no puede escribirse.
        }
    }

    private SessionDto? ResolveActiveSessionUnsafe()
    {
        if (_activeSessionId is Guid activeId)
        {
            var selected = _sessions.FirstOrDefault(s => s.Id == activeId);
            if (selected is not null)
                return selected;
        }

        if (_defaultSession is not null)
            return _defaultSession;

        return _sessions.Count > 0 ? _sessions[0] : null;
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
