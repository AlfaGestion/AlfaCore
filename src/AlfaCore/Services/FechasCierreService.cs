using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Models
{
    /// <summary>Fechas de cierre y fecha operativa (FechasDeCierre.vbp del legacy, claves de TA_CONFIGURACION).</summary>
    public sealed class FechasCierreDto
    {
        public DateTime? CierreVentas { get; set; }
        public DateTime? CierreCompras { get; set; }
        public DateTime? CierreCaja { get; set; }
        public DateTime? CierreStock { get; set; }
        public DateTime? OperativaDesde { get; set; }
        public DateTime? OperativaHasta { get; set; }
        /// <summary>MANUAL, DIA (hasta = hoy), MENOS30 o MENOS60 (desde = hoy − 30/60 días, hasta = hoy).</summary>
        public string ModoOperativa { get; set; } = "MANUAL";
        public bool PuedeEditar { get; set; }
    }
}

namespace AlfaCore.Services
{
    public interface IFechasCierreService
    {
        Task<FechasCierreDto> GetAsync(CancellationToken ct = default);
        /// <summary>Graba las fechas. Devuelve la lista de cierres que retrocedieron (períodos reabiertos).</summary>
        Task<IReadOnlyList<string>> GuardarAsync(FechasCierreDto dto, bool confirmaReapertura, CancellationToken ct = default);
    }

    /// <summary>
    /// «Fechas de Cierre» (D9882): CIERREVENTAS, CIERRECOMPRAS, CIERRECAJA, CIERRESTOCK y la fecha operativa
    /// posible (FECHAOPERDESDE/HASTA con UTILIZARFECHADIA y UTILIZARFECHADESDEMENOS30/60). El trigger
    /// MV_Asientos_ValidaFechas de la base las hace cumplir para Alfa Gestión y AlfaCore.
    /// </summary>
    public sealed class FechasCierreService(ISessionService sessions, IAppUserSessionService users,
        IPermissionService permissions, IAppEventService events) : IFechasCierreService
    {
        public const string Menu = "D9882";
        private static readonly string[] Claves = ["CIERREVENTAS", "CIERRECOMPRAS", "CIERRECAJA", "CIERRESTOCK", "FECHAOPERDESDE", "FECHAOPERHASTA",
            "UTILIZARFECHADIA", "UTILIZARFECHADESDEMENOS30", "UTILIZARFECHADESDEMENOS60"];
        private sealed class ReglaException(string message) : InvalidOperationException(message);

        public Task<FechasCierreDto> GetAsync(CancellationToken ct = default) => EjecutarAsync("Consultar", async cn =>
        {
            var cfg = await LeerAsync(cn, ct);
            return new FechasCierreDto
            {
                CierreVentas = Fecha(cfg, "CIERREVENTAS"), CierreCompras = Fecha(cfg, "CIERRECOMPRAS"), CierreCaja = Fecha(cfg, "CIERRECAJA"),
                CierreStock = Fecha(cfg, "CIERRESTOCK"), OperativaDesde = Fecha(cfg, "FECHAOPERDESDE"), OperativaHasta = Fecha(cfg, "FECHAOPERHASTA"),
                ModoOperativa = Si(cfg, "UTILIZARFECHADESDEMENOS30") ? "MENOS30" : Si(cfg, "UTILIZARFECHADESDEMENOS60") ? "MENOS60" : Si(cfg, "UTILIZARFECHADIA") ? "DIA" : "MANUAL",
                PuedeEditar = true
            };
        }, ct);

        public Task<IReadOnlyList<string>> GuardarAsync(FechasCierreDto dto, bool confirmaReapertura, CancellationToken ct = default)
            => EjecutarAsync<IReadOnlyList<string>>("Guardar", async cn =>
            {
                var hoy = await cn.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT CONVERT(date,GETDATE());", cancellationToken: ct));
                AplicarModo(dto, hoy.Date);
                if (dto.CierreVentas is null || dto.CierreCompras is null || dto.CierreCaja is null || dto.CierreStock is null)
                    throw new ReglaException("Completá las cuatro fechas de cierre.");
                if (dto.OperativaDesde is null || dto.OperativaHasta is null)
                    throw new ReglaException("Completá la fecha operativa desde y hasta.");
                if (dto.OperativaDesde > dto.OperativaHasta)
                    throw new ReglaException("La fecha operativa desde es posterior a la fecha hasta.");
                foreach (var (nombre, fecha) in new[] { ("ventas", dto.CierreVentas), ("compras", dto.CierreCompras), ("caja", dto.CierreCaja), ("stock", dto.CierreStock) })
                    if (fecha >= hoy.Date.AddDays(1))
                        throw new ReglaException($"El cierre de {nombre} no puede ser posterior a hoy.");

                var cfg = await LeerAsync(cn, ct);
                var reabiertos = new List<string>();
                foreach (var (clave, nombre, nueva) in new[] { ("CIERREVENTAS", "Ventas", dto.CierreVentas), ("CIERRECOMPRAS", "Compras", dto.CierreCompras),
                             ("CIERRECAJA", "Caja y bancos", dto.CierreCaja), ("CIERRESTOCK", "Stock", dto.CierreStock) })
                    if (Fecha(cfg, clave) is { } anterior && nueva < anterior)
                        reabiertos.Add($"{nombre}: del {anterior:dd/MM/yyyy} al {nueva:dd/MM/yyyy}");
                if (reabiertos.Count > 0 && !confirmaReapertura) return reabiertos;

                var valores = new Dictionary<string, string>
                {
                    ["CIERREVENTAS"] = F(dto.CierreVentas), ["CIERRECOMPRAS"] = F(dto.CierreCompras), ["CIERRECAJA"] = F(dto.CierreCaja), ["CIERRESTOCK"] = F(dto.CierreStock),
                    ["FECHAOPERDESDE"] = F(dto.OperativaDesde), ["FECHAOPERHASTA"] = F(dto.OperativaHasta),
                    ["UTILIZARFECHADIA"] = dto.ModoOperativa == "DIA" ? "SI" : "NO",
                    ["UTILIZARFECHADESDEMENOS30"] = dto.ModoOperativa == "MENOS30" ? "SI" : "NO",
                    ["UTILIZARFECHADESDEMENOS60"] = dto.ModoOperativa == "MENOS60" ? "SI" : "NO"
                };
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
                foreach (var (clave, valor) in valores)
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                        IF @@ROWCOUNT = 0
                            INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion, FechaHora_Modificacion)
                            VALUES (N'CONTABILIDAD', @Clave, @Valor, GETDATE(), GETDATE());
                        """, new { Clave = clave, Valor = valor }, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
                await events.LogAuditAsync("FechasCierre", "Guardar", "TA_CONFIGURACION", "FECHAS DE CIERRE",
                    reabiertos.Count > 0 ? $"Grabó las fechas de cierre y REABRIÓ: {string.Join("; ", reabiertos)}." : "Grabó las fechas de cierre y la fecha operativa.",
                    new { Antes = Claves.ToDictionary(c => c, c => cfg.GetValueOrDefault(c, "")), Despues = valores }, CancellationToken.None);
                return [];
            }, ct);

        /// <summary>Las opciones automáticas del legacy: hasta = hoy; con 30/60, desde = hoy − 30/60 días.</summary>
        internal static void AplicarModo(FechasCierreDto dto, DateTime hoy)
        {
            if (dto.ModoOperativa is "DIA" or "MENOS30" or "MENOS60") dto.OperativaHasta = hoy;
            if (dto.ModoOperativa == "MENOS30") dto.OperativaDesde = hoy.AddDays(-30);
            if (dto.ModoOperativa == "MENOS60") dto.OperativaDesde = hoy.AddDays(-60);
        }

        private static async Task<Dictionary<string, string>> LeerAsync(SqlConnection cn, CancellationToken ct)
            => (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition(
                "SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @Claves;",
                new { Claves }, cancellationToken: ct))).GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.OrdinalIgnoreCase);

        private static DateTime? Fecha(Dictionary<string, string> cfg, string clave)
            => DateTime.TryParseExact(cfg.GetValueOrDefault(clave, ""), ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f.Date : null;
        private static bool Si(Dictionary<string, string> cfg, string clave) => string.Equals(cfg.GetValueOrDefault(clave, ""), "SI", StringComparison.OrdinalIgnoreCase);
        private static string F(DateTime? d) => d!.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
        {
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _))
                throw new ReglaException("Ingresá en la base activa.");
            try
            {
                await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
                if (!await permissions.HasAccessAsync(Menu, ct))
                    throw new ReglaException("Tu usuario no tiene permiso para las fechas de cierre.");
                return await operacion(cn);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (ReglaException) { throw; }
            catch (AppUserFacingException) { throw; }
            catch (Exception ex)
            {
                var codigo = await events.LogErrorAsync("FechasCierre", accion, ex, "No se pudieron leer o grabar las fechas de cierre.", ct: ct);
                throw new AppUserFacingException("No se pudieron leer o grabar las fechas de cierre.", codigo, ex);
            }
        }
    }
}
