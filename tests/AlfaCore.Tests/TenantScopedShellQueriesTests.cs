using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Consultas tenant-sensitive que MainLayout ejecuta FUERA del @Body (polling global de
/// notificaciones de Conversaciones y panel rápido de tareas). Deben seguir la misma regla que el
/// @Body: sólo con sesión activa autorizada para el usuario (TenantDataAccessGuard). Antes
/// alcanzaba IsAuthenticated y un usuario de A en la ruta de B veía contacto + último mensaje de B.
/// </summary>
public sealed class TenantScopedShellQueriesTests
{
    private static readonly SessionDto BaseA = Session(100, "BASE_A");
    private static readonly SessionDto BaseB = Session(5000, "BASE_B");

    [Fact]
    public async Task AuthenticatedUser_OnUnauthorizedBaseB_ZeroInboxCalls()
    {
        var sessions = new MutableSessionService { Active = BaseB };
        var user = new MutableAppUserSession { User = User(), AuthorizedSessionId = BaseA.Id };
        var conversaciones = CountingConversaciones(out var spy);

        var result = await new GlobalConversationNotificationsSource(sessions, user, conversaciones).FetchPendingAsync();

        Assert.False(result.Executed);
        Assert.Equal(0, spy.InboxCalls);
        Assert.Equal(0, spy.SchemaCalls);
    }

    [Fact]
    public async Task NullSession_ZeroInboxCalls()
    {
        var sessions = new MutableSessionService { Active = null };
        var user = new MutableAppUserSession { User = User(), AuthorizedSessionId = BaseA.Id };
        var conversaciones = CountingConversaciones(out var spy);

        var result = await new GlobalConversationNotificationsSource(sessions, user, conversaciones).FetchPendingAsync();

        Assert.False(result.Executed);
        Assert.Equal(0, spy.InboxCalls);
    }

    [Fact]
    public async Task Anonymous_ZeroInboxCalls()
    {
        var sessions = new MutableSessionService { Active = BaseA };
        var user = new MutableAppUserSession { User = null };
        var conversaciones = CountingConversaciones(out var spy);

        var result = await new GlobalConversationNotificationsSource(sessions, user, conversaciones).FetchPendingAsync();

        Assert.False(result.Executed);
        Assert.Equal(0, spy.InboxCalls);
    }

    [Fact]
    public async Task SwitchAtoB_BeforeBIsAuthorized_ZeroInboxCalls_ThenResumesWhenAuthorized()
    {
        var sessions = new MutableSessionService { Active = BaseA };
        var user = new MutableAppUserSession { User = User(), AuthorizedSessionId = BaseA.Id };
        var conversaciones = CountingConversaciones(out var spy);
        var source = new GlobalConversationNotificationsSource(sessions, user, conversaciones);

        var onA = await source.FetchPendingAsync();
        Assert.True(onA.Executed);
        Assert.Equal(BaseA.Id, onA.SessionId);
        Assert.Equal(1, spy.InboxCalls);

        // Cambio de base: la activa pasa a B, la autorización sigue siendo la de A (SwitchSession
        // invalida el login interno anterior; el de B todavía no ocurrió).
        sessions.Active = BaseB;
        var duringTransition = await source.FetchPendingAsync();
        Assert.False(duringTransition.Executed);
        Assert.Equal(1, spy.InboxCalls);

        // Login interno de B completado: el polling se reanuda contra B.
        user.AuthorizedSessionId = BaseB.Id;
        var onB = await source.FetchPendingAsync();
        Assert.True(onB.Executed);
        Assert.Equal(BaseB.Id, onB.SessionId);
        Assert.Equal(2, spy.InboxCalls);
    }

    [Fact]
    public async Task AuthorizedBase_PollsNormally()
    {
        var sessions = new MutableSessionService { Active = BaseB };
        var user = new MutableAppUserSession { User = User(), AuthorizedSessionId = BaseB.Id };
        var conversaciones = CountingConversaciones(out var spy);

        var result = await new GlobalConversationNotificationsSource(sessions, user, conversaciones).FetchPendingAsync();

        Assert.True(result.Executed);
        Assert.Equal(BaseB.Id, result.SessionId);
        Assert.Equal(1, spy.SchemaCalls);
        Assert.Equal(1, spy.InboxCalls);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task BaseChangesWhileQueryIsRunning_ResultIsDiscarded()
    {
        var sessions = new MutableSessionService { Active = BaseA };
        var user = new MutableAppUserSession { User = User(), AuthorizedSessionId = BaseA.Id };
        var conversaciones = CountingConversaciones(out var spy);
        spy.OnInbox = () => sessions.Active = BaseB;

        var result = await new GlobalConversationNotificationsSource(sessions, user, conversaciones).FetchPendingAsync();

        Assert.Equal(1, spy.InboxCalls);
        Assert.False(result.Executed);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(false, 0)] // usuario de A en la base B sin login interno de B
    [InlineData(true, 1)]  // B autorizada
    public async Task TasksPanel_FollowsSameRule(bool authorizedForB, int expectedCalls)
    {
        var sessions = new MutableSessionService { Active = BaseB };
        var user = new MutableAppUserSession { User = User(), AuthorizedSessionId = authorizedForB ? BaseB.Id : BaseA.Id };
        var tareas = DispatchProxy.Create<ITareasService, CountingTareasProxy>();
        var spy = (CountingTareasProxy)(object)tareas;

        var loaded = await TenantDataAccessGuard.RunForAuthorizedSessionAsync(
            sessions, user, token => tareas.GetPageAsync("ANA", token));

        Assert.Equal(expectedCalls, spy.PageCalls);
        Assert.Equal(authorizedForB, loaded.Executed);
    }

    [Fact]
    public void MainLayout_ShellQueriesGoThroughTenantDataAccessGuard()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AlfaCore", "Components", "Layout", "MainLayout.razor"));

        // Una sola fuente de verdad de autorización para @Body, panel y polling.
        Assert.Contains("TenantDataAccessGuard.IsActiveSessionAuthorized(SessionSvc, AppUserSession)", source, StringComparison.Ordinal);
        Assert.Contains("CanShowTasksPanel => !IsPosFullscreenRoute && !IsDirectMode && IsActiveSessionAuthorized", source, StringComparison.Ordinal);
        Assert.Contains("TenantDataAccessGuard.RunForAuthorizedSessionAsync(", source, StringComparison.Ordinal);
        Assert.Contains("GlobalConversationNotifications.FetchPendingAsync(", source, StringComparison.Ordinal);

        // El layout ya no consulta el inbox ni las tareas por fuera del guard.
        Assert.DoesNotContain("ConversacionesSvc.GetInboxAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConversacionesSvc.HasConversationSchemaAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await TareasSvc.GetPageAsync(", source, StringComparison.Ordinal);
    }

    internal static IConversacionesService CountingConversaciones(out CountingConversacionesProxy spy)
    {
        var proxy = DispatchProxy.Create<IConversacionesService, CountingConversacionesProxy>();
        spy = (CountingConversacionesProxy)(object)proxy;
        return proxy;
    }

    private static AppUserSessionInfo User() => new() { UserName = "ANA", IdCliente = "C_A", IdWeb = "EMPRESAA" };

    private static SessionDto Session(int baseId, string nombre) => new()
    {
        Id = SessionDto.BuildGuidFromBaseId(baseId),
        BaseId = baseId,
        Nombre = nombre,
        Servidor = "invalid-server",
        BaseDatos = $"AW_{baseId}",
        Activa = true
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("No se pudo ubicar la raíz del repositorio.");
    }

    public class CountingConversacionesProxy : DispatchProxy
    {
        public int SchemaCalls { get; private set; }
        public int InboxCalls { get; private set; }
        public Action? OnInbox { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IConversacionesService.HasConversationSchemaAsync):
                    SchemaCalls++;
                    return Task.FromResult(true);
                case nameof(IConversacionesService.GetInboxAsync):
                    InboxCalls++;
                    OnInbox?.Invoke();
                    return Task.FromResult<IReadOnlyList<ConversacionInboxItemDto>>(
                        [new ConversacionInboxItemDto { IdConversacion = 1, IdUltimoMensajeCliente = 10, ContactoNombre = "Contacto" }]);
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    public class CountingTareasProxy : DispatchProxy
    {
        public int PageCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ITareasService.GetPageAsync))
            {
                PageCalls++;
                return Task.FromResult(new TareasPageDto());
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class MutableSessionService : ISessionService
    {
        public SessionDto? Active { get; set; }
        public event Action? SessionChanged { add { } remove { } }
        public string GetConnectionString() => Active is null ? string.Empty : $"Server={Active.Servidor};Database={Active.BaseDatos}";
        public SessionDto? GetActiveSession() => Active;
        public void SetWebhookOverride(SessionDto session) => throw new NotSupportedException();
        public SessionDto? GetWebhookOverride(int expectedBaseId) => null;
        public void ClearWebhookOverride() { }
        public IReadOnlyList<SessionDto> GetAllSessions() => Active is null ? [] : [Active];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() => Active = null;
    }

    private sealed class MutableAppUserSession : IAppUserSessionService
    {
        public AppUserSessionInfo? User { get; set; }
        public Guid? AuthorizedSessionId { get; set; }
        public event Action? StateChanged { add { } remove { } }
        public bool IsAuthenticated => User is not null;
        public AppUserSessionInfo? CurrentUser => User;
        public bool RequiresInternalLogin => false;
        public string? CurrentToken => null;
        public Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public void AdoptInternalUser(AppUserSessionInfo internalUser) => throw new NotSupportedException();
        public bool TryRestoreFromToken(string token) => false;
        public void Logout() => User = null;
        public void HandleSqlSessionChanged() { }
        public string GetCurrentUserName(string fallback = "") => User?.UserName ?? fallback;
        // Misma semántica que AppUserSessionService.IsAuthorizedForSession.
        public bool IsAuthorizedForSession(Guid? activeSessionId)
            => User is not null && activeSessionId is not null && AuthorizedSessionId == activeSessionId;
        public void EnsureAuthorizedForSession(Guid? activeSessionId) { }
    }
}
