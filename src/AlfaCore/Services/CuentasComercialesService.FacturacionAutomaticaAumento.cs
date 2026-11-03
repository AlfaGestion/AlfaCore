using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// «Aumentar Valores de Facturación automática» del VB6 (FrmPrincipalCliNew, FrmAumentofa):
/// porcentaje sobre cada renglón con importe, o importe fijo, guardando el anterior en ImporteOld.
/// Se agregan redondeo a pasos (0,25 · 0,50 · 1 · 10 · 100 · 1000) y reducciones con porcentaje negativo.
/// </summary>
public sealed partial class CuentasComercialesService
{
    public static readonly IReadOnlyList<decimal> PasosRedondeo = [0.25m, 0.5m, 1m, 10m, 100m, 1000m];

    /// <summary>Nuevo importe sin IVA de un renglón. Con redondeo «con IVA» (como el VB6) se redondea el
    /// total con IVA y el neto se recalcula desde ese total (4 decimales, como la columna money).</summary>
    public static decimal CalcularImporteAumento(decimal importe, bool porcentaje, decimal valor, decimal redondeo, bool conIva, decimal alicuota)
    {
        var neto = porcentaje ? importe * (1 + valor / 100m) : valor;
        if (redondeo > 0)
        {
            if (conIva && alicuota > 0)
            {
                var factor = 1 + alicuota / 100m;
                neto = RedondearAPaso(neto * factor, redondeo) / factor;
            }
            else neto = RedondearAPaso(neto, redondeo);
        }
        return Math.Round(neto, 4, MidpointRounding.AwayFromZero);
    }

    private static decimal RedondearAPaso(decimal valor, decimal paso)
        => Math.Round(valor / paso, 0, MidpointRounding.AwayFromZero) * paso;

    private static void ValidarAumento(FacturacionAutomaticaAumentoPedido p)
    {
        if (p.Redondeo != 0 && !PasosRedondeo.Contains(p.Redondeo))
            throw new InvalidOperationException("Elegí un redondeo de la lista.");
        if (p.Porcentaje)
        {
            if (p.Valor <= -100 || p.Valor > 1000) throw new InvalidOperationException("El porcentaje tiene que estar entre -99,99 y 1000.");
            if (p.Valor == 0 && p.Redondeo == 0) throw new InvalidOperationException("Ingresá un porcentaje o elegí un redondeo.");
        }
        else if (p.Valor <= 0) throw new InvalidOperationException("Ingresá el importe sin IVA a asignar.");
    }

    public Task<FacturacionAutomaticaAumentoResultadoDto> AumentarFacturacionAutomaticaAsync(FacturacionAutomaticaAumentoPedido pedido, bool aplicar,
        CancellationToken ct = default)
        => ExecuteLoggedAsync<FacturacionAutomaticaAumentoResultadoDto>("Clientes", "FacturacionAutomaticaAumento", async token =>
        {
            ArgumentNullException.ThrowIfNull(pedido);
            ValidarAumento(pedido);
            var todos = pedido.Codigos is not { Count: > 0 };
            var codigos = todos ? [] : NormalizarCodigos(pedido.Codigos!);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            var sqlTx = (SqlTransaction)tx;

            // Alícuota por unidad de negocio: monotributo/exento (5, 7) no discrimina IVA, como el VB6.
            var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
                SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'PIVA', N'CONDIVA', N'CONDIVAEMPRESA') OR UPPER(CLAVE) LIKE N'CONDICIONIVA[_]UN[_]%';
                """, transaction: sqlTx, cancellationToken: token)))
                .GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.OrdinalIgnoreCase);
            var piva = decimal.TryParse(cfg.GetValueOrDefault("PIVA", "21").Replace(',', '.'), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var pv) ? pv : 21m;
            var condGeneral = cfg.GetValueOrDefault("CONDIVAEMPRESA") is { Length: > 0 } ce && ce != "0" ? ce : cfg.GetValueOrDefault("CONDIVA", "1");
            decimal Alicuota(string unidad)
            {
                var cond = cfg.GetValueOrDefault("CONDICIONIVA_UN_" + unidad.Trim()) is { Length: > 0 } cu ? cu : condGeneral;
                return cond.Trim() is "5" or "7" ? 0 : piva;
            }

            // Renglones vigentes con importe (sin cuotas cumplidas). Todos = clientes no dados de baja con la facturación activa.
            var renglones = (await cn.QueryAsync<Renglon>(new CommandDefinition($"""
                SELECT D.ID AS Id, LTRIM(RTRIM(D.Cuenta)) AS Cuenta, ISNULL(A.RAZON_SOCIAL,'') AS RazonSocial,
                       LTRIM(RTRIM(ISNULL(D.Detalle,''))) AS Detalle, D.Importe AS Importe, LTRIM(RTRIM(ISNULL(U.UNEGOCIO,''))) AS Unidad
                FROM dbo.MA_CUENTAS_AUTOCPTESDET D WITH (UPDLOCK)
                LEFT JOIN dbo.VT_CLIENTES A ON A.CODIGO = D.Cuenta
                OUTER APPLY (SELECT TOP (1) C.UNEGOCIO FROM dbo.MA_CUENTAS_AUTOCPTES C WHERE C.Cuenta = D.Cuenta
                             ORDER BY CASE WHEN ISNULL(C.SucursalCuenta,0) = ISNULL(D.SucursalCuenta,0) THEN 0 ELSE 1 END) U
                WHERE ISNULL(D.Importe,0) <> 0 AND NOT ({FiltroCumplido})
                  AND {(todos
                    ? "ISNULL(A.Dada_De_Baja,0) = 0 AND EXISTS (SELECT 1 FROM dbo.MA_CUENTAS_AUTOCPTES C WHERE C.Cuenta = D.Cuenta AND ISNULL(C.Activo,0) = 1)"
                    : "D.Cuenta IN @Codigos")}
                ORDER BY A.CTACODIGO, D.ID;
                """, new { Codigos = codigos }, sqlTx, 120, cancellationToken: token))).ToList();

            var filas = new List<(Renglon R, FacturacionAutomaticaAumentoFilaDto F)>();
            var omitidos = 0;
            foreach (var r in renglones)
            {
                var alicuota = Alicuota(r.Unidad);
                var nuevo = CalcularImporteAumento(r.Importe, pedido.Porcentaje, pedido.Valor, pedido.Redondeo, pedido.RedondearConIva, alicuota);
                if (nuevo <= 0) { omitidos++; continue; }
                filas.Add((r, new FacturacionAutomaticaAumentoFilaDto
                {
                    Cuenta = r.Cuenta, RazonSocial = r.RazonSocial, Detalle = r.Detalle, Alicuota = alicuota, Antes = r.Importe, Despues = nuevo
                }));
            }
            var cambian = filas.Where(x => x.F.Despues != x.F.Antes).ToList();

            if (aplicar && cambian.Count > 0)
            {
                // Igual que el VB6: guarda el importe anterior y la fecha del cambio.
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.MA_CUENTAS_AUTOCPTESDET SET ImporteOld = Importe, FechaCambio = GETDATE(), Importe = @Nuevo
                    WHERE ID = @Id AND Importe = @Antes;
                    """, cambian.Select(x => new { x.R.Id, Nuevo = x.F.Despues, Antes = x.R.Importe }), sqlTx, 120, cancellationToken: token));
                await sqlTx.CommitAsync(token);
                await appEvents.LogAuditAsync("Clientes", "FacturacionAutomaticaAumento", "MA_CUENTAS_AUTOCPTESDET",
                    todos ? "TODOS" : string.Join(",", codigos.Take(50)),
                    $"{(pedido.Porcentaje ? $"Aplicó {pedido.Valor:0.##}%" : $"Asignó {pedido.Valor:N2}")}{(pedido.Redondeo > 0 ? $" redondeando a {pedido.Redondeo:0.##} {(pedido.RedondearConIva ? "con IVA" : "sin IVA")}" : "")} en {cambian.Count} renglón(es) de facturación automática.",
                    new { pedido.Porcentaje, pedido.Valor, pedido.Redondeo, pedido.RedondearConIva, Todos = todos, Renglones = cambian.Count, Omitidos = omitidos,
                        Antes = cambian.Sum(x => x.F.Antes), Despues = cambian.Sum(x => x.F.Despues) },
                    CancellationToken.None);
            }
            else await sqlTx.RollbackAsync(token);

            return new FacturacionAutomaticaAumentoResultadoDto
            {
                Aplicado = aplicar && cambian.Count > 0,
                Clientes = cambian.Select(x => x.F.Cuenta).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Renglones = cambian.Count,
                Omitidos = omitidos,
                TotalAntes = cambian.Sum(x => x.F.Antes),
                TotalDespues = cambian.Sum(x => x.F.Despues),
                Muestra = cambian.Take(30).Select(x => x.F).ToList()
            };
        }, "No se pudieron actualizar los importes de facturación automática.", ct);

    private sealed class Renglon
    {
        public int Id { get; set; }
        public string Cuenta { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public decimal Importe { get; set; }
        public string Unidad { get; set; } = string.Empty;
    }
}
