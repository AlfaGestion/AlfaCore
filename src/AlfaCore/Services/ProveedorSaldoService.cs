using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Saldo de cuenta corriente de Proveedores. Reutiliza dbo.CO_CPTES_SALDOS / dbo.CO_CPTES_IMPAGOS,
// el equivalente del lado compras de dbo.VE_CPTES_SALDOS_VENTAS que ya usa PortalClienteService
// para Clientes -- mismo shape de columnas (CUENTA, SALDO, VENCIMIENTO, TC/SUCURSAL/NUMERO/LETRA),
// verificado contra la base de prueba antes de escribir esto. No se arma un Portal Proveedor
// completo, solo lo que necesita el asistente de Conversaciones para responder saldo.
public sealed class ProveedorSaldoService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents) : IProveedorSaldoService
{
    private const string ModuleName = "ProveedorSaldo";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<ProveedorSaldoResumenDto> GetResumenSaldoAsync(string codigoProveedor, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetResumenSaldo", async token =>
        {
            var codigo = (codigoProveedor ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se pudo identificar al proveedor.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var fila = await cn.QuerySingleOrDefaultAsync<ResumenRow>(new CommandDefinition(
                """
                SELECT
                    ISNULL(SUM(SALDO), 0) AS SaldoTotal,
                    ISNULL(SUM(CASE WHEN CAST(VENCIMIENTO AS date) < CAST(@Hoy AS date) THEN SALDO ELSE 0 END), 0) AS Vencido,
                    ISNULL(SUM(CASE WHEN CAST(VENCIMIENTO AS date) >= CAST(@Hoy AS date) THEN SALDO ELSE 0 END), 0) AS AVencer,
                    COUNT(*) AS CantidadPendientes
                FROM dbo.CO_CPTES_SALDOS
                WHERE UPPER(LTRIM(RTRIM(CUENTA))) = UPPER(LTRIM(RTRIM(@CodigoProveedor)));
                """,
                new { CodigoProveedor = codigo, Hoy = DateTime.Today },
                cancellationToken: token));

            return new ProveedorSaldoResumenDto
            {
                SaldoTotal = fila?.SaldoTotal ?? 0,
                Vencido = fila?.Vencido ?? 0,
                AVencer = fila?.AVencer ?? 0,
                CantidadPendientes = fila?.CantidadPendientes ?? 0
            };
        }, "No pudimos consultar el saldo del proveedor en este momento.", ct);

    public Task<IReadOnlyList<ProveedorComprobantePendienteDto>> GetComprobantesPendientesAsync(string codigoProveedor, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetComprobantesPendientes", async token =>
        {
            var codigo = (codigoProveedor ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se pudo identificar al proveedor.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var pendientes = await cn.QueryAsync<PendienteRow>(new CommandDefinition(
                """
                SELECT TOP (50)
                    ISNULL(LTRIM(RTRIM(TC)), '') AS Tc,
                    ISNULL(LTRIM(RTRIM(SUCURSAL)), '') AS Sucursal,
                    ISNULL(LTRIM(RTRIM(NUMERO)), '') AS Numero,
                    ISNULL(LTRIM(RTRIM(LETRA)), '') AS Letra,
                    FECHA AS Fecha,
                    VENCIMIENTO AS Vencimiento,
                    CONVERT(decimal(15,2), SALDO) AS Saldo
                FROM dbo.CO_CPTES_SALDOS
                WHERE UPPER(LTRIM(RTRIM(CUENTA))) = UPPER(LTRIM(RTRIM(@CodigoProveedor)))
                ORDER BY
                    CASE WHEN CAST(VENCIMIENTO AS date) < CAST(@Hoy AS date) THEN 0 ELSE 1 END,
                    VENCIMIENTO ASC;
                """,
                new { CodigoProveedor = codigo, Hoy = DateTime.Today },
                cancellationToken: token));

            var hoy = DateTime.Today;
            return (IReadOnlyList<ProveedorComprobantePendienteDto>)pendientes.Select(p => new ProveedorComprobantePendienteDto
            {
                Tc = p.Tc,
                Sucursal = p.Sucursal,
                Numero = p.Numero,
                Letra = p.Letra,
                Fecha = p.Fecha,
                Vencimiento = p.Vencimiento,
                Saldo = p.Saldo,
                EstaVencido = p.Vencimiento.Date < hoy
            }).ToList();
        }, "No pudimos consultar los comprobantes pendientes del proveedor en este momento.", ct);

    private sealed class ResumenRow
    {
        public decimal SaldoTotal { get; set; }
        public decimal Vencido { get; set; }
        public decimal AVencer { get; set; }
        public int CantidadPendientes { get; set; }
    }

    private sealed class PendienteRow
    {
        public string Tc { get; set; } = string.Empty;
        public string Sucursal { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string Letra { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public DateTime Vencimiento { get; set; }
        public decimal Saldo { get; set; }
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string friendlyMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (InvalidOperationException validationEx)
        {
            await appEvents.LogErrorAsync(
                module, action, validationEx, validationEx.Message,
                new { Usuario = appUserSession.GetCurrentUserName(Environment.UserName), SesionSql = sessionService.GetActiveSession()?.Nombre },
                AppEventSeverity.Warning, ct);
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                module,
                action,
                ex,
                friendlyMessage,
                new
                {
                    Usuario = appUserSession.GetCurrentUserName(Environment.UserName),
                    SesionSql = sessionService.GetActiveSession()?.Nombre
                },
                AppEventSeverity.Warning,
                ct);

            throw new AppUserFacingException(friendlyMessage, incidentId, ex);
        }
    }
}
