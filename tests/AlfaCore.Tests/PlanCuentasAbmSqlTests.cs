using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prueba el ABM del plan de cuentas contra una base real (PLAN_CUENTAS_TEST_SQL): da de alta una cuenta debajo de 425,
/// la modifica, revisa validaciones y la borra. Deja la base como estaba (salvo el registro de acciones del legacy).
/// </summary>
public class PlanCuentasAbmSqlTests
{
    [PlanCuentasSqlFact]
    public async Task AltaModificacionYBajaComoElLegacy()
    {
        var connection = Environment.GetEnvironmentVariable("PLAN_CUENTAS_TEST_SQL")!;
        var service = new PlanCuentasAbmService(new Sesion(connection), new Usuario(), new Permisos(), new Eventos());
        var catalogo = await service.CatalogoAsync();
        Assert.True(catalogo.PuedeModificar);
        Assert.Contains(catalogo.Titulos, t => t.Codigo == "425");

        var codigo = await service.SiguienteCodigoAsync("425");
        try
        {
            var nueva = await service.PrepararAsync(codigo);
            Assert.False(nueva.Existe);
            Assert.Equal("425", nueva.CuentaMadre);

            nueva.Descripcion = "PRUEBA ABM WEB BORRAR";
            nueva.MedioPago = "";
            var alta = await service.GuardarAsync(nueva, alta: true);
            Assert.True(alta.Existe);
            Assert.False(alta.Titulo);
            Assert.Equal("PRUEBA ABM WEB BORRAR", alta.Descripcion);

            // Validaciones del legacy.
            var otra = await service.PrepararAsync(codigo);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GuardarAsync(otra, alta: true));            // ya existe
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.PrepararAsync("4259"));                    // dígitos
            var hija = await service.PrepararAsync(codigo + "0001");
            hija.Descripcion = "HIJA";
            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GuardarAsync(hija, alta: true));  // madre imputable
            Assert.Contains("título", ex.Message);
            var titulo = await service.PrepararAsync("425");
            titulo.Titulo = false;
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GuardarAsync(titulo, alta: false));       // tiene nivel inferior

            alta.Descripcion = "PRUEBA ABM WEB MODIFICADA";
            alta.PideVencimiento = true;
            var mod = await service.GuardarAsync(alta, alta: false);
            Assert.Equal("PRUEBA ABM WEB MODIFICADA", mod.Descripcion);
            Assert.True(mod.PideVencimiento);

            Assert.NotEmpty((await service.ConsultarBajaAsync("425")).Bloqueo);
            var baja = await service.ConsultarBajaAsync(codigo);
            Assert.Empty(baja.Bloqueo);
            Assert.False(baja.SeMarcaDeBaja);
            Assert.Equal(PlanCuentaBajaResultado.Borrada, await service.EliminarAsync(codigo, confirmaCashFlow: false));
            await using var cn = new SqlConnection(connection);
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MA_CUENTAS WHERE CODIGO = @c;", new { c = codigo }));
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MA_CUENTAS_USUARIOS WHERE Cuenta = @c;", new { c = codigo }));
        }
        finally
        {
            await using var cn = new SqlConnection(connection);
            await cn.ExecuteAsync("""
                DELETE FROM dbo.MA_CUENTAS_USUARIOS WHERE Cuenta = @c;
                DELETE FROM dbo.MA_CUENTASCCOSTO WHERE CODIGO = @c;
                DELETE FROM dbo.MA_CUENTAS WHERE CODIGO = @c AND DESCRIPCION LIKE N'PRUEBA ABM WEB%';
                """, new { c = codigo });
        }
    }

    private sealed class Sesion(string connection) : ISessionService
    {
        private readonly SessionDto _sesion = new() { Nombre = "Prueba" };
        public string GetConnectionString() => connection;
        public SessionDto? GetActiveSession() => _sesion;
        public void SetWebhookOverride(SessionDto session) { }
        public SessionDto? GetWebhookOverride(int expectedBaseId) => null;
        public void ClearWebhookOverride() { }
        public IReadOnlyList<SessionDto> GetAllSessions() => [_sesion];
        public void SwitchSession(Guid id) { }
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => Guid.Empty;
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) { }
        public void DeleteSession(Guid id) { }
        public void ClearActiveSession() { }
        public event Action? SessionChanged { add { } remove { } }
    }

    private sealed class Usuario : IAppUserSessionService
    {
        public event Action? StateChanged { add { } remove { } }
        public bool IsAuthenticated => true;
        public AppUserSessionInfo? CurrentUser { get; } = new() { UserName = "PRUEBA-ABM-WEB" };
        public bool RequiresInternalLogin => false;
        public string? CurrentToken => null;
        public Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public void AdoptInternalUser(AppUserSessionInfo internalUser) => throw new NotSupportedException();
        public bool TryRestoreFromToken(string token) => false;
        public void Logout() { }
        public void HandleSqlSessionChanged() { }
        public string GetCurrentUserName(string fallback = "") => "PRUEBA-ABM-WEB";
        public bool IsAuthorizedForSession(Guid? activeSessionId) => true;
        public void EnsureAuthorizedForSession(Guid? activeSessionId) { }
    }

    private sealed class Permisos : IPermissionService
    {
        public Task<IReadOnlySet<string>?> GetAllowedTaskKeysAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>?>(null);
        public Task<bool> HasAccessAsync(string clave, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Eventos : IAppEventService
    {
        public Task<string> LogErrorAsync(string module, string action, Exception exception, string userMessage, object? data = null,
            AppEventSeverity severity = AppEventSeverity.Error, CancellationToken ct = default) => throw exception;
        public Task<string> LogAuditAsync(string module, string action, string entityType, string entityId, string message, object? data = null,
            CancellationToken ct = default) => Task.FromResult("");
        public Task<Guid> WriteAuditAsync(AuditWriteRequest request, CancellationToken ct = default) => Task.FromResult(Guid.Empty);
        public Task<Guid> WriteAuditAsync(AuditWriteRequest request, SqlConnection connection, SqlTransaction transaction, CancellationToken ct = default) => Task.FromResult(Guid.Empty);
        public Task<AuditActivityPageDto> GetActivityAsync(string entityType, string recordId, int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(SqlConnection connection, SqlTransaction? transaction = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

public sealed class PlanCuentasSqlFactAttribute : FactAttribute
{
    public PlanCuentasSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PLAN_CUENTAS_TEST_SQL")))
            Skip = "Requiere una base con el plan de cuentas legacy (cuenta título 425) en PLAN_CUENTAS_TEST_SQL; da de alta y borra una cuenta de prueba.";
    }
}
