using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesEscrituraTests
{
    [CargaComprobantesSqlFact]
    public async Task ActualizaOrigenConReglasLegacyYRollbackRestauraLosRenglones()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        var repository = new CargaComprobantesRepository();
        var contexto = await repository.GetContextoAsync(cn, Environment.GetEnvironmentVariable("ALFACORE_TEST_USUARIO") ?? "Administrador", default);
        var tested = 0;
        CargaComprobantesOrigen? pedido = null;
        foreach (var tc in new[] { "NP", "RM" })
        {
            var ids = await cn.QueryAsync<int>("""
                SELECT TOP(30) c.ID FROM dbo.V_MV_CPTE c
                WHERE LTRIM(RTRIM(c.TC))=@Tc AND c.IDCOMPLEMENTO=0 AND c.ANULADA=0 AND c.FINALIZADA=0
                    AND ISNULL(c.BLOQUEADA,0)=0 AND ISNULL(c.CERRADA,0)=0 AND ISNULL(c.EnComodato,0)=0
                    AND c.FechaPreparacion IS NULL
                    AND (@AdminCentral=1 OR LTRIM(RTRIM(c.UNEGOCIO))=@UnidadNegocio)
                    AND NOT EXISTS(SELECT 1 FROM dbo.V_MV_RESERVAS r WHERE r.TC=c.TC AND r.IDCOMPROBANTE=c.IDCOMPROBANTE)
                    AND EXISTS(SELECT 1 FROM dbo.VT_V_MV_INSUMOS_SALDOSRM v
                        WHERE v.TC=c.TC AND v.IDCOMPROBANTE=c.IDCOMPROBANTE AND v.SALDO>=1)
                ORDER BY c.FECHA DESC,c.ID DESC;
                """, new { Tc = tc, contexto.AdminCentral, contexto.UnidadNegocio });
            CargaComprobantesOrigen? origen = null;
            foreach (var id in ids)
            {
                var candidate = await repository.GetOrigenAsync(cn, contexto, id, default);
                if (!candidate.TieneReservas && !candidate.TienePreparacion && !candidate.TieneOtrosDetalles &&
                    candidate.Moneda is "" or "1" && candidate.Lineas.Any(x => x.Pendiente >= 1 && x.RenglonesArticulo == 1))
                { origen = candidate; break; }
            }
            if (tc == "NP") { Assert.NotNull(origen); pedido = origen; }
            // Esta base puede no tener RM pendientes: crear el caso de remito dentro
            // de la misma transacción de prueba, copiando el esquema real de un NP apto.
            var sintetico = origen is null && tc == "RM";
            using (var transaction = cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                Estado? original = null;
                CargaComprobantesLineaOrigen? linea = null;
                try
                {
                    if (sintetico) origen = await CrearRemitoAsync(cn, transaction, pedido!);
                    linea = origen!.Lineas.First(x => x.Pendiente >= 1 && x.RenglonesArticulo == 1);
                    original = await LeerAsync(cn, linea.Id, transaction);
                    var updater = new CargaComprobantesActualizadorOrigen();
                    await updater.ActualizarAsync(cn, transaction, origen, [new() { LineaId = linea.Id, Cantidad = 1 }]);
                    var modified = await LeerAsync(cn, linea.Id, transaction);
                    Assert.Equal(-999, modified.Recepcion);
                    if (tc == "NP")
                    {
                        await using var concurrente = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
                        await concurrente.OpenAsync();
                        var bloqueo = await Assert.ThrowsAsync<SqlException>(() => concurrente.ExecuteScalarAsync<int>("""
                            SET LOCK_TIMEOUT 250;
                            SELECT ID FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE ID=@Id;
                            """, new { origen.Id }));
                        Assert.Equal(1222, bloqueo.Number);
                    }
                    if (tc == "NP")
                    {
                        Assert.True(modified.Preparada == (original.Preparada ?? 0) + 1);
                        Assert.True(modified.PreparadaUd == (original.PreparadaUd ?? 0) + decimal.Round(linea.CoeficienteUnidad, 4));
                        Assert.True(modified.AFacturar == 0m); Assert.True(modified.AFacturarUd == 0m);
                    }
                    else
                    {
                        decimal? esperada = original.AFacturar is null ? null : Math.Max(0, original.AFacturar.Value - 1);
                        decimal? esperadaUd = original.AFacturarUd is null ? null : Math.Max(0, original.AFacturarUd.Value - decimal.Round(linea.CoeficienteUnidad, 4));
                        Assert.True(esperada == modified.AFacturar); Assert.True(esperadaUd == modified.AFacturarUd);
                    }
                    // Una cantidad alterada entre consulta/grabación debe rechazar y revertir todo.
                    linea.CantidadOriginal += 1;
                    await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => updater.ActualizarAsync(cn, transaction, origen,
                        [new() { LineaId = linea.Id, Cantidad = 1 }]));
                    linea.CantidadOriginal -= 1;
                }
                finally { transaction.Rollback(); }
                if (!sintetico) Assert.Equal(original, await LeerAsync(cn, linea!.Id));
                else Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_CPTE WHERE ID=@Id;", new { origen!.Id }));
            }
            tested++;
        }
        Assert.Equal(2, tested);
    }

    private static async Task<CargaComprobantesOrigen> CrearRemitoAsync(SqlConnection cn, SqlTransaction tx, CargaComprobantesOrigen pedido)
    {
        var linea = pedido.Lineas.First(x => x.Pendiente >= 1 && x.RenglonesArticulo == 1);
        // Consultar columnas para el fixture, excluyendo identity/computed; nunca para grabación productiva.
        async Task<int> Copiar(string tabla, int id, string comprobante)
        {
            var columnas = (await cn.QueryAsync<string>("""
                SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(@Tabla) AND is_identity=0 AND is_computed=0 ORDER BY column_id;
                """, new { Tabla = "dbo." + tabla }, tx)).ToList();
            string Col(string value) => "[" + value.Replace("]", "]]") + "]";
            var sql = $"DECLARE @Creado TABLE(Id int); INSERT INTO dbo.{Col(tabla)} ({string.Join(',', columnas.Select(Col))}) OUTPUT inserted.ID INTO @Creado SELECT " +
                string.Join(',', columnas.Select(x => x.ToUpperInvariant() switch
                { "TC" => "@Tc", "IDCOMPROBANTE" => "@Comprobante", "FECHA" => "GETDATE()", _ => Col(x) })) +
                $" FROM dbo.{Col(tabla)} WHERE ID=@Id; SELECT Id FROM @Creado;";
            return await cn.ExecuteScalarAsync<int>(sql, new { Id = id, Comprobante = comprobante, Tc = AlfaCore.Common.CodigoPk.Format("RM", 4) }, tx);
        }
        var cpte = "9999" + Random.Shared.Next(10000000, 99999999).ToString() + "X";
        var cabeceraId = await Copiar("V_MV_CPTE", pedido.Id, cpte);
        var lineaId = await Copiar("V_MV_CPTEINSUMOS", linea.Id, cpte);
        await cn.ExecuteAsync("""
            UPDATE dbo.V_MV_CPTEINSUMOS SET CANTIDADAFACTURAR=.5,CANTIDADAFACTURARUD=.5,RECEPCION=123 WHERE ID=@Id;
            """, new { Id = lineaId }, tx);
        return new CargaComprobantesOrigen { Id = cabeceraId, Tc = "RM", Comprobante = cpte, Moneda = pedido.Moneda,
            Lineas = [new() { Id = lineaId, Secuencia = linea.Secuencia, Articulo = linea.Articulo, Deposito = linea.Deposito,
                CantidadOriginal = linea.CantidadOriginal, Pendiente = linea.CantidadOriginal,
                CoeficienteUnidad = linea.CoeficienteUnidad, RenglonesArticulo = 1 }] };
    }

    private static Task<Estado> LeerAsync(SqlConnection cn, int id, SqlTransaction? transaction = null) => cn.QuerySingleAsync<Estado>("""
        SELECT CONVERT(decimal(28,8),CANTIDADPREPARADA) Preparada,CONVERT(decimal(28,8),CANTIDADPREPARADAUD) PreparadaUd,
            CONVERT(decimal(28,8),CANTIDADAFACTURAR) AFacturar,CONVERT(decimal(28,8),CANTIDADAFACTURARUD) AFacturarUd,
            Recepcion FROM dbo.V_MV_CPTEINSUMOS WHERE ID=@Id;
        """, new { Id = id }, transaction);
    private sealed record Estado(decimal? Preparada, decimal? PreparadaUd, decimal? AFacturar, decimal? AFacturarUd, int? Recepcion);
}
