using System.Data;
using System.Globalization;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IStockMovimientosService
{
    Task<StockMovimientosContexto> GetContextoAsync(CancellationToken ct = default);
    /// <summary>Código exacto (IDARTICULO o CODIGOBARRA..CODIGOBARRA4) con su stock en el depósito; null si no existe.</summary>
    Task<StockMovimientoArticulo?> GetArticuloPorCodigoAsync(string codigo, string tc, string deposito, CancellationToken ct = default);
    /// <summary>Búsqueda F5 por código, descripción o código de barras.</summary>
    Task<IReadOnlyList<StockMovimientoArticulo>> BuscarArticulosAsync(string texto, string tc, string deposito, CancellationToken ct = default);
    /// <summary>Los artículos ya cargados con el stock del depósito (al cambiar de depósito o tipo).</summary>
    Task<IReadOnlyList<StockMovimientoArticulo>> GetArticulosAsync(IReadOnlyList<string> codigos, string tc, string deposito, CancellationToken ct = default);
    /// <summary>Calcula lo que se va a grabar (en AJP/SI, el saldo a la fecha y la diferencia). No graba nada.</summary>
    Task<StockMovimientoPlan> CalcularAsync(StockMovimientoBorrador borrador, CancellationToken ct = default);
    Task<StockMovimientoResultado> GrabarAsync(StockMovimientoBorrador borrador, Guid operacion, CancellationToken ct = default);
    Task<IReadOnlyList<StockMovimientoResumen>> BuscarComprobantesAsync(string tc, string texto, CancellationToken ct = default);
    Task<StockMovimientoCargado> GetComprobanteAsync(string tc, string comprobante, CancellationToken ct = default);
    Task<StockMovimientoResultado> AnularAsync(string tc, string comprobante, Guid operacion, CancellationToken ct = default);
}

/// <summary>
/// Movimientos de stock web (IR, VS, TR, AJP, SI), port de V_MV_STOCK.exe (Mvstock.frm / ClsCmnCptes.ActualizaStock).
/// Graba solo en V_MV_STOCK (el legacy no tiene cabecera), numera con V_TA_CPTE (SISTEMA='Stock') y deja
/// rastro en V_MV_CPTEACCIONES y V_MV_CTROL_CPTE como el legacy. Los triggers de V_MV_STOCK controlan
/// fecha operativa, cierre de stock, unidad de negocio del depósito y NoFacturaSinStock; acá se validan
/// antes para dar un mensaje claro. Fuera de alcance por ahora: lotes/series/talle, insumos (PE automático),
/// asientos (AsientosXStock) y transferencias a otra base.
/// </summary>
public sealed class StockMovimientosService(
    ISessionService sessionService, IAppUserSessionService appUserSession,
    IPermissionService permissions, IAppEventService events) : IStockMovimientosService
{
    public const string MenuKey = "D80-MOVSTOCK";
    private const string Modulo = "StockMovimientos";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------------------------------------------------------------- Contexto

    public Task<StockMovimientosContexto> GetContextoAsync(CancellationToken ct = default)
        => ExecuteAsync("Contexto", (cn, ctx, _) => Task.FromResult(ctx.Publico), ct);

    // ---------------------------------------------------------------- Artículos

    public Task<StockMovimientoArticulo?> GetArticuloPorCodigoAsync(string codigo, string tc, string deposito, CancellationToken ct = default)
        => ExecuteAsync("ArticuloPorCodigo", async (cn, ctx, token) =>
        {
            codigo = (codigo ?? "").Trim();
            if (codigo.Length is 0 or > 50) return null;
            ValidarDepositoLectura(ctx, deposito);
            var articulos = await LeerArticulosAsync(cn, ctx, tc, deposito, codigoExacto: codigo, texto: null, ids: null, null, token);
            return articulos.FirstOrDefault();
        }, ct);

    public Task<IReadOnlyList<StockMovimientoArticulo>> BuscarArticulosAsync(string texto, string tc, string deposito, CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<StockMovimientoArticulo>>("BuscarArticulos", async (cn, ctx, token) =>
        {
            texto = (texto ?? "").Trim();
            if (texto.Length > 100) throw new CargaComprobantesReglaException("El texto de búsqueda es demasiado largo.");
            ValidarDepositoLectura(ctx, deposito);
            return await LeerArticulosAsync(cn, ctx, tc, deposito, codigoExacto: null, texto: texto, ids: null, null, token);
        }, ct);

    public Task<IReadOnlyList<StockMovimientoArticulo>> GetArticulosAsync(IReadOnlyList<string> codigos, string tc, string deposito, CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<StockMovimientoArticulo>>("Articulos", async (cn, ctx, token) =>
        {
            var ids = (codigos ?? []).Select(x => (x ?? "").Trim()).Where(x => x.Length is > 0 and <= 25).Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
            if (ids.Length == 0) return [];
            ValidarDepositoLectura(ctx, deposito);
            return await LeerArticulosAsync(cn, ctx, tc, deposito, codigoExacto: null, texto: null, ids: ids, null, token);
        }, ct);

    /// <summary>
    /// Artículos de V_MA_ARTICULOS con unidad, costo, stock actual del depósito y la restricción que impide
    /// moverlos en esta pantalla. Exacto: IDARTICULO y después CODIGOBARRA..CODIGOBARRA4 (como el POS y ventas).
    /// </summary>
    private static async Task<List<StockMovimientoArticulo>> LeerArticulosAsync(SqlConnection cn, Contexto ctx, string tc, string deposito,
        string? codigoExacto, string? texto, string[]? ids, SqlTransaction? tx, CancellationToken ct)
    {
        const string columnas = """
            LTRIM(RTRIM(a.IDARTICULO)) Codigo, ISNULL(a.DESCRIPCION,'') Descripcion, LTRIM(RTRIM(ISNULL(a.IDUNIDAD,''))) Unidad,
            CONVERT(decimal(18,4),ISNULL(a.COSTO,0)) Costo,
            CASE WHEN LTRIM(RTRIM(ISNULL(a.IDUNIDAD,'')))='' THEN 'no tiene unidad de medida'
                 WHEN ISNULL(a.USASERIE,0)=1 THEN 'usa números de serie'
                 WHEN ISNULL(a.USALOTE,0)=1 THEN 'usa lotes'
                 WHEN ISNULL(a.UsaTalle,0)=1 THEN 'usa talle y color'
                 WHEN ISNULL(a.PideEquivalencia,0)=1 THEN 'pide equivalencia al cargar'
                 WHEN ISNULL(a.PideMedidas,0)=1 THEN 'pide medidas al cargar'
                 ELSE '' END Restriccion,
            a.IDARTICULO IdCrudo
            """;
        List<FilaArticulo> filas;
        if (codigoExacto is not null)
        {
            filas = [];
            string[] criterios = ["LTRIM(RTRIM(a.IDARTICULO))=@Codigo", "LTRIM(RTRIM(ISNULL(a.CODIGOBARRA,'')))=@Codigo",
                "LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1,'')))=@Codigo", "LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2,'')))=@Codigo",
                "LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3,'')))=@Codigo", "LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4,'')))=@Codigo"];
            foreach (var criterio in criterios)
            {
                filas = (await cn.QueryAsync<FilaArticulo>(Cmd($"SELECT TOP(1) {columnas} FROM dbo.V_MA_ARTICULOS a WHERE ISNULL(a.SUSPENDIDO,0)=0 AND {criterio} ORDER BY a.IDARTICULO;",
                    new { Codigo = codigoExacto }, tx, ct))).AsList();
                if (filas.Count > 0) break;
            }
        }
        else if (ids is not null)
        {
            filas = (await cn.QueryAsync<FilaArticulo>(Cmd($"SELECT {columnas} FROM dbo.V_MA_ARTICULOS a WHERE ISNULL(a.SUSPENDIDO,0)=0 AND LTRIM(RTRIM(a.IDARTICULO)) IN @Ids;",
                new { Ids = ids }, tx, ct))).AsList();
        }
        else
        {
            var literal = (texto ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
            filas = (await cn.QueryAsync<FilaArticulo>(Cmd($"""
                SELECT TOP(100) {columnas} FROM dbo.V_MA_ARTICULOS a
                WHERE ISNULL(a.SUSPENDIDO,0)=0 AND (@Texto='' OR LTRIM(RTRIM(a.IDARTICULO)) LIKE @Patron ESCAPE '\'
                    OR a.DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @Patron ESCAPE '\' OR a.CODIGOBARRA LIKE @Patron ESCAPE '\')
                ORDER BY CASE WHEN LTRIM(RTRIM(a.IDARTICULO))=@Texto OR LTRIM(RTRIM(ISNULL(a.CODIGOBARRA,'')))=@Texto THEN 0 ELSE 1 END,
                    a.DESCRIPCION, a.IDARTICULO;
                """, new { Texto = texto ?? "", Patron = "%" + literal + "%" }, tx, ct))).AsList();
        }
        if (filas.Count == 0) return [];

        // Como ClsCmnCptes.ProcesaInsumos: IR/VS/TR de un artículo con insumos generan un parte de consumo (PE). Todavía no.
        if (!StockMovimientosCalculo.EsAjuste(tc) && !ctx.DescargaInsumosProduccion && ctx.HayInsumos)
        {
            var conInsumos = (await cn.QueryAsync<string>(Cmd("""
                SELECT DISTINCT LTRIM(RTRIM(i.IdArticulo)) FROM dbo.V_MA_INSUMOS i
                JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO=i.IdArticulo
                WHERE LTRIM(RTRIM(i.IdArticulo)) IN @Ids AND LTRIM(RTRIM(ISNULL(a.PROCEDENCIA,'')))<>'PRODUCCION';
                """, new { Ids = filas.Select(f => f.Codigo).ToArray() }, tx, ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in filas.Where(f => f.Restriccion.Length == 0 && conInsumos.Contains(f.Codigo)))
                f.Restriccion = "tiene insumos (genera parte de consumo)";
        }

        var stock = deposito.Trim().Length == 0 ? new Dictionary<string, decimal>()
            : await SaldosAsync(cn, filas.Select(f => f.IdCrudo).ToArray(), deposito, null, null, null, tx, ct);
        return filas.Select(f => new StockMovimientoArticulo
        {
            Codigo = f.Codigo, Descripcion = f.Descripcion.Trim(), Unidad = f.Unidad, Costo = f.Costo, Restriccion = f.Restriccion,
            Stock = stock.GetValueOrDefault(f.Codigo.Trim())
        }).ToList();
    }

    /// <summary>
    /// Saldo por artículo en un depósito: SUM(CantidadUD) de V_MV_STOCK no anulados (como Stk_Stock_UDKG).
    /// Con fecha, solo movimientos hasta ese día inclusive. Sin contar el comprobante que se modifica.
    /// </summary>
    private static async Task<Dictionary<string, decimal>> SaldosAsync(SqlConnection cn, string[] idsCrudos, string deposito, DateTime? hasta,
        string? excluirTc, string? excluirComprobante, SqlTransaction? tx, CancellationToken ct)
    {
        if (idsCrudos.Length == 0) return new(StringComparer.OrdinalIgnoreCase);
        var filas = await cn.QueryAsync<(string Id, decimal Saldo)>(Cmd("""
            SELECT LTRIM(RTRIM(s.IDArticulo)), CONVERT(decimal(18,4),SUM(ISNULL(s.CantidadUD,0)))
            FROM dbo.V_MV_STOCK s
            WHERE s.IDArticulo IN @Ids AND LTRIM(RTRIM(s.IdDeposito))=@Deposito AND ISNULL(s.Anulado,0)=0
              AND (@Hasta IS NULL OR s.FECHA < DATEADD(day,1,@Hasta))
              AND NOT (@Tc IS NOT NULL AND s.TC=@Tc AND s.IDCOMPROBANTE=@Comprobante AND s.IDCOMPLEMENTO=0)
            GROUP BY LTRIM(RTRIM(s.IDArticulo));
            """, new { Ids = idsCrudos, Deposito = deposito.Trim(), Hasta = hasta?.Date, Tc = excluirTc, Comprobante = excluirComprobante }, tx, ct));
        return filas.ToDictionary(x => x.Id, x => x.Saldo, StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- Calcular / grabar

    public Task<StockMovimientoPlan> CalcularAsync(StockMovimientoBorrador borrador, CancellationToken ct = default)
        => ExecuteAsync("Calcular", async (cn, ctx, token) => (await PrepararAsync(cn, ctx, borrador,
            (borrador.Comprobante ?? "").Trim() is { Length: > 0 } c ? c : null, controlarSaldo: false, token)).Plan, ct);

    public Task<StockMovimientoResultado> GrabarAsync(StockMovimientoBorrador borrador, Guid operacion, CancellationToken ct = default)
        => ExecuteAsync("Grabar", async (cn, ctx, token) =>
        {
            if (operacion == Guid.Empty) throw new CargaComprobantesReglaException("Falta el identificador de la operación. Recargá la pantalla.");
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            if (await OperacionGrabadaAsync(cn, tx, operacion, token) is { } previa) { await tx.RollbackAsync(token); return previa; }

            var modificacion = borrador.Comprobante.Trim().Length > 0;
            string comprobante;
            if (modificacion)
            {
                comprobante = borrador.Comprobante.Trim();
                var existente = await LeerFilasAsync(cn, ctx, borrador.Tc.Trim(), comprobante, tx, bloquear: true, token);
                await ValidarEditableAsync(cn, ctx, borrador.Tc.Trim(), comprobante, existente, tx, token);
                if (!string.Equals(existente[0].UnidadNegocio, (borrador.UnidadNegocio ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new CargaComprobantesReglaException("Al modificar no se puede cambiar la unidad de negocio del comprobante.");
            }
            else comprobante = "";

            var preparado = await PrepararAsync(cn, ctx, borrador, modificacion ? comprobante : null, controlarSaldo: true, token, tx);
            if (!modificacion) comprobante = await NumerarAsync(cn, ctx, preparado.Tc, preparado.UnidadNegocio, tx, token);
            else await cn.ExecuteAsync(Cmd("DELETE FROM dbo.V_MV_STOCK WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante AND IDCOMPLEMENTO=0;",
                new { preparado.Tc, Comprobante = comprobante }, tx, token));

            var filas = StockMovimientosCalculo.Filas(preparado.Tc, preparado.Lineas, preparado.Deposito, preparado.Destino);
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO dbo.V_MV_STOCK
                    (TC,IDCOMPROBANTE,IDCOMPLEMENTO,SECUENCIA,FECHA,IDArticulo,Descripcion,IDUnidad,IdUnidadBase,Cantidad,CantidadUD,EQUIV_UDBASE,
                     Costo,PrecioVenta,IMPORTE_S_IVA,IVARI,IVARNI,IMPUESTOS,IdDeposito,IDDEPOSITO_DESTINO,IdMotivoStock,ControladoPor,UNEGOCIO,
                     CANT_BL,CANT_KG,CANT_M3)
                VALUES (@Tc,@Comprobante,0,@Secuencia,@Fecha,@Articulo,@Descripcion,@Unidad,@Unidad,@Cantidad,@Cantidad,1,
                     @Costo,0,0,0,0,0,@Deposito,@Destino,@Motivo,@Observaciones,@UnidadNegocio,@Bultos,@Kilos,@Metros);
                """, filas.Select(f => new
                {
                    preparado.Tc, Comprobante = comprobante, f.Secuencia, Fecha = preparado.Fecha,
                    Articulo = CodigoPk.Format(f.Articulo, 25), Descripcion = Recortar(f.Descripcion, 100), Unidad = CodigoPk.Format(f.Unidad, 4),
                    f.Cantidad, f.Costo, Deposito = CodigoPk.Format(f.Deposito, 4),
                    Destino = f.DepositoDestino is null ? null : CodigoPk.Format(f.DepositoDestino, 4),
                    Motivo = preparado.Motivo.Length == 0 ? null : CodigoPk.Format(preparado.Motivo, 4),
                    Observaciones = preparado.Observaciones.Length == 0 ? null : preparado.Observaciones,
                    UnidadNegocio = CodigoPk.Format(preparado.UnidadNegocio, 4),
                    Bultos = f.Bultos, Kilos = f.Kilos, Metros = f.MetrosCubicos
                }).ToList(), tx, token));

            var accion = modificacion ? "M" : "A";
            await RegistrarAccionAsync(cn, ctx, preparado.Tc, comprobante, accion, operacion, tx, token);
            await tx.CommitAsync(token);

            await events.LogAuditAsync(Modulo, modificacion ? "Modificar" : "Alta", "V_MV_STOCK", $"{preparado.Tc} {comprobante}",
                $"{(modificacion ? "Modificó" : "Grabó")} {preparado.Tc} {comprobante} con {preparado.Lineas.Count} artículos.",
                new { preparado.Tc, Comprobante = comprobante, preparado.Deposito, preparado.Destino, Fecha = preparado.Fecha.ToString("yyyy-MM-dd", Inv), Operacion = operacion }, token);
            return new StockMovimientoResultado { Tc = preparado.Tc, Comprobante = comprobante, Accion = accion, Lineas = preparado.Lineas.Count };
        }, ct);

    /// <summary>Valida la carga y resuelve artículos, unidades, bultos y saldos; con transacción, dentro de ella.</summary>
    private async Task<Preparado> PrepararAsync(SqlConnection cn, Contexto ctx, StockMovimientoBorrador b, string? comprobanteModificado,
        bool controlarSaldo, CancellationToken ct, SqlTransaction? tx = null)
    {
        var tc = (b.Tc ?? "").Trim().ToUpperInvariant();
        var unidad = (b.UnidadNegocio ?? "").Trim();
        if (!StockMovimientosCalculo.Tipos.Contains(tc)) throw new CargaComprobantesReglaException("Elegí el tipo de movimiento.");
        if (!ctx.AdminCentral && unidad != ctx.UnidadNegocio)
            throw new CargaComprobantesReglaException("La unidad de negocio no pertenece al usuario actual.");
        if (!ctx.Publico.UnidadesNegocio.Any(u => u.Codigo == unidad)) throw new CargaComprobantesReglaException("Elegí la unidad de negocio.");
        var tipo = TipoPara(ctx, tc, unidad) ?? throw new CargaComprobantesReglaException($"El comprobante {tc} no está configurado en Stock para la unidad {unidad}.");

        var fecha = b.Fecha.Date;
        ValidarFecha(ctx, fecha);
        var deposito = (b.Deposito ?? "").Trim();
        ValidarDeposito(ctx, deposito, unidad, StockMovimientosCalculo.EsTransferencia(tc) ? "de origen" : "");
        string? destino = null;
        if (StockMovimientosCalculo.EsTransferencia(tc))
        {
            destino = (b.DepositoDestino ?? "").Trim();
            ValidarDeposito(ctx, destino, unidad, "de destino");
            if (string.Equals(destino, deposito, StringComparison.OrdinalIgnoreCase))
                throw new CargaComprobantesReglaException("El depósito de destino no puede ser el mismo que el de origen.");
        }
        var motivo = (b.Motivo ?? "").Trim();
        if (motivo.Length == 0) motivo = ctx.Publico.MotivoDefault;
        if (motivo.Length > 0 && !ctx.Publico.Motivos.Any(m => m.Codigo == motivo))
            throw new CargaComprobantesReglaException("Elegí un motivo de stock válido.");
        if (ctx.AsientosXStock && motivo.Length > 0 && ctx.MotivosConCuenta.Contains(motivo))
            throw new CargaComprobantesReglaException("Este motivo genera asiento contable (AsientosXStock). Por ahora cargá este movimiento en Alfa Gestión o elegí un motivo sin cuenta contable.");
        var observaciones = Recortar((b.Observaciones ?? "").Trim(), 50);

        var lineas = (b.Lineas ?? []).Where(l => (l.Articulo ?? "").Trim().Length > 0).ToList();
        if (lineas.Count == 0) throw new CargaComprobantesReglaException("Cargá al menos un artículo.");
        if (lineas.Count > 500) throw new CargaComprobantesReglaException("Un movimiento admite hasta 500 artículos.");
        var repetido = lineas.GroupBy(l => l.Articulo.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (repetido is not null) throw new CargaComprobantesReglaException($"El artículo {repetido.Key} está cargado dos veces.");

        var ids = lineas.Select(l => l.Articulo.Trim()).ToArray();
        var articulos = (await LeerArticulosAsync(cn, ctx, tc, "", null, null, ids, tx, ct)).ToDictionary(a => a.Codigo, StringComparer.OrdinalIgnoreCase);
        var crudos = (await cn.QueryAsync<string>(Cmd("SELECT IDARTICULO FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) IN @Ids;", new { Ids = ids }, tx, ct))).ToArray();
        var unidades = await CargaComprobantesUnidades.LeerAsync(cn, ids, ct, tx);
        var ajuste = StockMovimientosCalculo.EsAjuste(tc);
        var saldos = await SaldosAsync(cn, crudos, deposito, ajuste ? fecha : null, comprobanteModificado is null ? null : tc, comprobanteModificado, tx, ct);

        var plan = new StockMovimientoPlan { Tc = tc, Fecha = fecha };
        var resueltas = new List<StockMovimientosCalculo.LineaResuelta>();
        foreach (var l in lineas)
        {
            var codigo = l.Articulo.Trim();
            if (!articulos.TryGetValue(codigo, out var a)) throw new CargaComprobantesReglaException($"El artículo {codigo} no existe o está suspendido.");
            if (a.Restriccion.Length > 0)
                throw new CargaComprobantesReglaException($"El artículo {codigo} {a.Restriccion}: por ahora movelo desde Alfa Gestión.");
            CargaComprobantesUnidades.Cantidades cantidades;
            if (l.Bultos is { } bultos)
            {
                if (ajuste && bultos == 0) cantidades = new(0, 0, 0, 0);
                else cantidades = unidades.Resolver(codigo, a.Unidad, 0, bultos);
            }
            else
            {
                if (StockMovimientosCalculo.ValidarCantidad(tc, l.Cantidad) is { } error) throw new CargaComprobantesReglaException($"{codigo}: {error}");
                cantidades = l.Cantidad == 0 ? new(0, 0, 0, 0) : unidades.Resolver(codigo, a.Unidad, l.Cantidad, null);
            }
            if (StockMovimientosCalculo.ValidarCantidad(tc, cantidades.Unidades) is { } errorConvertida)
                throw new CargaComprobantesReglaException($"{codigo}: {errorConvertida}");

            var saldo = saldos.GetValueOrDefault(codigo);
            if (controlarSaldo && ajuste && l.SaldoEsperado is { } esperado && esperado != saldo)
                throw new CargaComprobantesReglaException($"El saldo de {codigo} cambió (era {esperado.ToString("0.####", Inv)}, ahora {saldo.ToString("0.####", Inv)}). Volvé a calcular antes de grabar.");
            var movimiento = StockMovimientosCalculo.Movimiento(tc, tipo.Es, cantidades.Unidades, saldo);
            if (controlarSaldo && !ajuste && movimiento < 0 && ctx.Publico.ValidaStockSalidas && saldo + movimiento < 0)
                throw new CargaComprobantesReglaException($"Sin stock suficiente de {codigo} en el depósito {deposito} (hay {saldo.ToString("0.####", Inv)}).");

            // Bultos/kilos/m³ del movimiento: en ajustes, los de la diferencia.
            var equivalentes = movimiento == 0 ? new CargaComprobantesUnidades.Cantidades(0, 0, 0, 0)
                : Math.Abs(movimiento) == cantidades.Unidades ? cantidades
                : unidades.Resolver(codigo, a.Unidad, Math.Abs(movimiento), null);
            resueltas.Add(new(codigo, a.Descripcion, a.Unidad, a.Costo, movimiento, equivalentes.Bultos, equivalentes.Kilos, equivalentes.MetrosCubicos));
            plan.Lineas.Add(new()
            {
                Articulo = codigo, Descripcion = a.Descripcion, Unidad = a.Unidad, Cantidad = cantidades.Unidades, Bultos = cantidades.Bultos,
                Saldo = saldo, Movimiento = movimiento
            });
        }
        if (!ajuste && plan.Lineas.Any(x => x.Movimiento < 0 && x.Saldo + x.Movimiento < 0))
            plan.Avisos.Add("Hay artículos que quedan con stock negativo en el depósito.");
        if (ajuste && plan.Lineas.All(x => x.Movimiento == 0))
            plan.Avisos.Add("Lo contado coincide con el saldo: el comprobante no mueve stock.");
        if (ajuste && await HayMovimientosPosterioresAsync(cn, crudos, deposito, fecha, tc, comprobanteModificado, tx, ct))
            plan.Avisos.Add($"Hay movimientos posteriores al {fecha:dd/MM/yyyy}: el saldo de hoy va a ser lo contado más esos movimientos.");
        return new Preparado(tc, unidad, fecha, deposito, destino, motivo, observaciones, resueltas, plan);
    }

    private static Task<bool> HayMovimientosPosterioresAsync(SqlConnection cn, string[] crudos, string deposito, DateTime fecha,
        string tc, string? comprobante, SqlTransaction? tx, CancellationToken ct)
        => crudos.Length == 0 ? Task.FromResult(false) : cn.ExecuteScalarAsync<bool>(Cmd("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.V_MV_STOCK s WHERE s.IDArticulo IN @Ids AND LTRIM(RTRIM(s.IdDeposito))=@Deposito
                AND ISNULL(s.Anulado,0)=0 AND s.FECHA >= DATEADD(day,1,@Fecha)
                AND NOT (@Comprobante IS NOT NULL AND s.TC=@Tc AND s.IDCOMPROBANTE=@Comprobante)) THEN 1 ELSE 0 END);
            """, new { Ids = crudos, Deposito = deposito.Trim(), Fecha = fecha, Tc = tc, Comprobante = comprobante }, tx, ct));

    /// <summary>
    /// Próximo número del talonario de Stock (Obtener_Nro_Comprobante_Contable): primero el de la unidad, después el general.
    /// El contador <L>_ULTIMO_NRO guarda el próximo número; se toma el mayor entre él y el último usado + 1.
    /// </summary>
    private static async Task<string> NumerarAsync(SqlConnection cn, Contexto ctx, string tc, string unidad, SqlTransaction tx, CancellationToken ct)
    {
        var talonario = await cn.QueryFirstOrDefaultAsync<FilaTalonario>(Cmd("""
            SELECT TOP(1) ISNULL(Talonario,0) Talonario, ISNULL(LETRAS,'') Letras FROM dbo.V_TA_CPTE WITH (UPDLOCK, HOLDLOCK)
            WHERE LTRIM(RTRIM(CODIGO))=@Tc AND UPPER(LTRIM(RTRIM(SISTEMA)))='STOCK' AND ISNULL(LTRIM(RTRIM(UNEGOCIO)),'') IN ('',@Unidad)
            ORDER BY CASE WHEN ISNULL(LTRIM(RTRIM(UNEGOCIO)),'')=@Unidad THEN 0 ELSE 1 END, Talonario;
            """, new { Tc = tc, Unidad = unidad }, tx, ct));
        if (talonario is null) throw new CargaComprobantesReglaException($"El comprobante {tc} no tiene talonario en Stock.");
        var letra = StockMovimientosCalculo.Letra(talonario.Letras);
        // Letra validada contra una lista fija: se puede armar el nombre de columna.
        var datos = await cn.QueryFirstAsync<FilaNumeracion>(Cmd($"""
            SELECT TOP(1) {letra}_SUC_DEFAULT Sucursal, {letra}_ULTIMO_NRO Ultimo FROM dbo.V_TA_CPTE WITH (UPDLOCK, HOLDLOCK)
            WHERE LTRIM(RTRIM(CODIGO))=@Tc AND UPPER(LTRIM(RTRIM(SISTEMA)))='STOCK' AND ISNULL(Talonario,0)=@Talonario;
            """, new { Tc = tc, talonario.Talonario }, tx, ct));
        var sucursal = Math.Max(datos.Sucursal ?? 1, 0);
        var prefijo = sucursal.ToString("0000", Inv);
        var usado = await cn.ExecuteScalarAsync<int?>(Cmd($"""
            SELECT MAX(n) FROM (
                SELECT MAX(CASE WHEN SUBSTRING(IDCOMPROBANTE,5,8) NOT LIKE '%[^0-9]%' THEN CONVERT(int,SUBSTRING(IDCOMPROBANTE,5,8)) END) n
                FROM dbo.V_MV_STOCK WHERE TC=@Tc AND IDCOMPROBANTE LIKE @Patron
                {(ctx.HayControlComprobantes ? "UNION ALL SELECT MAX(CASE WHEN LTRIM(RTRIM(NUMERO)) NOT LIKE '%[^0-9]%' AND LTRIM(RTRIM(NUMERO))<>'' THEN CONVERT(int,NUMERO) END) FROM dbo.V_MV_CTROL_CPTE WHERE LTRIM(RTRIM(TC))=@Tc AND SUCURSAL=@Sucursal AND LETRA=@Letra" : "")}
            ) x;
            """, new { Tc = tc, Patron = prefijo + "________" + letra, Sucursal = prefijo, Letra = letra }, tx, ct));
        var numero = StockMovimientosCalculo.ProximoNumero(datos.Ultimo, usado);
        await cn.ExecuteAsync(Cmd($"""
            UPDATE dbo.V_TA_CPTE SET {letra}_ULTIMO_NRO=@Siguiente
            WHERE LTRIM(RTRIM(CODIGO))=@Tc AND UPPER(LTRIM(RTRIM(SISTEMA)))='STOCK' AND ISNULL(Talonario,0)=@Talonario AND ISNULL({letra}_ULTIMO_NRO,0)<=@Siguiente;
            """, new { Tc = tc, talonario.Talonario, Siguiente = numero + 1 }, tx, ct));
        if (ctx.HayControlComprobantes)
            await cn.ExecuteAsync(Cmd("INSERT INTO dbo.V_MV_CTROL_CPTE (TC,SUCURSAL,NUMERO,LETRA) VALUES (@Tc,@Sucursal,@Numero,@Letra);",
                new { Tc = tc, Sucursal = prefijo, Numero = numero.ToString("00000000", Inv), Letra = letra }, tx, ct));
        return StockMovimientosCalculo.Comprobante(sucursal, numero, letra);
    }

    /// <summary>V_MV_CPTEACCIONES (A alta, M modificación, B baja, como ClsCmnCptes.ActualizaBuchon) y el registro de la operación web.</summary>
    private static async Task RegistrarAccionAsync(SqlConnection cn, Contexto ctx, string tc, string comprobante, string accion, Guid operacion,
        SqlTransaction tx, CancellationToken ct)
    {
        if (ctx.HayAcciones)
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO dbo.V_MV_CPTEACCIONES (TC,IDCOMPROBANTE,IDCOMPLEMENTO,TIPO_ACCION,FECHAHORA,USUARIO,PC,SYSTEMUSER)
                VALUES (@Tc,@Comprobante,0,@Accion,GETDATE(),@Usuario,'AlfaCore','AlfaCore Stock');
                """, new { Tc = tc, Comprobante = comprobante, Accion = accion, Usuario = Recortar(ctx.Publico.Usuario, 50) }, tx, ct));
        await cn.ExecuteAsync(Cmd("""
            INSERT INTO dbo.ALFACORE_STOCK_MOV_OPERACION (Operacion,TC,IdComprobante,Accion,Usuario,FechaHora)
            VALUES (@Operacion,@Tc,@Comprobante,@Accion,@Usuario,GETDATE());
            """, new { Operacion = operacion, Tc = tc, Comprobante = comprobante, Accion = accion, Usuario = Recortar(ctx.Publico.Usuario, 50) }, tx, ct));
    }

    /// <summary>Si la operación ya se grabó (doble clic, reconexión), devuelve ese resultado en vez de grabar de nuevo.</summary>
    private static async Task<StockMovimientoResultado?> OperacionGrabadaAsync(SqlConnection cn, SqlTransaction tx, Guid operacion, CancellationToken ct)
    {
        var f = await cn.QueryFirstOrDefaultAsync<FilaOperacion>(Cmd(
            "SELECT LTRIM(RTRIM(TC)) Tc, IdComprobante, Accion FROM dbo.ALFACORE_STOCK_MOV_OPERACION WHERE Operacion=@Operacion;",
            new { Operacion = operacion }, tx, ct));
        if (f is null) return null;
        var lineas = await cn.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(DISTINCT IDArticulo) FROM dbo.V_MV_STOCK WHERE TC=@Tc AND IDCOMPROBANTE=@Id;",
            new { Tc = f.Tc, Id = f.IdComprobante }, tx, ct));
        return new StockMovimientoResultado { Tc = f.Tc, Comprobante = f.IdComprobante, Accion = f.Accion, Lineas = lineas };
    }

    // ---------------------------------------------------------------- Modificar / anular

    public Task<IReadOnlyList<StockMovimientoResumen>> BuscarComprobantesAsync(string tc, string texto, CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<StockMovimientoResumen>>("BuscarComprobantes", async (cn, ctx, token) =>
        {
            tc = (tc ?? "").Trim().ToUpperInvariant();
            if (!StockMovimientosCalculo.Tipos.Contains(tc)) return [];
            texto = (texto ?? "").Trim();
            var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
            return (await cn.QueryAsync<StockMovimientoResumen>(Cmd("""
                SELECT TOP(50) LTRIM(RTRIM(s.TC)) Tc, s.IDCOMPROBANTE Comprobante, MIN(s.FECHA) Fecha,
                    LTRIM(RTRIM(MIN(CASE WHEN s.TC<>'TR' OR s.Cantidad<0 THEN s.IdDeposito END))) Deposito,
                    ISNULL(LTRIM(RTRIM(MIN(CASE WHEN s.Cantidad<0 THEN s.IDDEPOSITO_DESTINO END))),'') DepositoDestino,
                    ISNULL(LTRIM(RTRIM(MIN(s.UNEGOCIO))),'') UnidadNegocio,
                    COUNT(DISTINCT s.IDArticulo) Items, ISNULL(LTRIM(RTRIM(MIN(s.ControladoPor))),'') Observaciones
                FROM dbo.V_MV_STOCK s
                WHERE s.TC=@Tc AND s.IDCOMPLEMENTO=0 AND ISNULL(s.Anulado,0)=0
                  AND (@AdminCentral=1 OR LTRIM(RTRIM(s.UNEGOCIO))=@Unidad)
                  AND (@Digitos='' OR s.IDCOMPROBANTE LIKE '%'+@Digitos+'%')
                GROUP BY s.TC, s.IDCOMPROBANTE
                ORDER BY MIN(s.FECHA) DESC, s.IDCOMPROBANTE DESC;
                """, new { Tc = tc, ctx.AdminCentral, Unidad = ctx.UnidadNegocio, Digitos = digitos }, null, token))).ToList();
        }, ct);

    public Task<StockMovimientoCargado> GetComprobanteAsync(string tc, string comprobante, CancellationToken ct = default)
        => ExecuteAsync("Comprobante", async (cn, ctx, token) =>
        {
            tc = (tc ?? "").Trim().ToUpperInvariant();
            comprobante = (comprobante ?? "").Trim();
            var filas = await LeerFilasAsync(cn, ctx, tc, comprobante, null, bloquear: false, token);
            await ValidarEditableAsync(cn, ctx, tc, comprobante, filas, null, token);
            var transferencia = StockMovimientosCalculo.EsTransferencia(tc);
            var origen = transferencia ? filas.Where(f => f.Cantidad < 0).ToList() : filas;
            var primera = origen[0];
            var borrador = new StockMovimientoBorrador
            {
                Tc = tc, Comprobante = comprobante, Fecha = primera.Fecha.Date, UnidadNegocio = primera.UnidadNegocio,
                Deposito = primera.Deposito, DepositoDestino = transferencia ? primera.DepositoDestino : "",
                Motivo = primera.Motivo, Observaciones = primera.Observaciones
            };
            var porArticulo = origen.GroupBy(f => f.Articulo, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Articulo: g.Key, Cantidad: g.Sum(x => x.CantidadUD), Crudo: g.First().ArticuloCrudo)).ToList();
            Dictionary<string, decimal> saldos = new(StringComparer.OrdinalIgnoreCase);
            if (StockMovimientosCalculo.EsAjuste(tc))
                saldos = await SaldosAsync(cn, porArticulo.Select(x => x.Crudo).ToArray(), borrador.Deposito, borrador.Fecha, tc, comprobante, null, token);
            foreach (var x in porArticulo)
            {
                // En AJP/SI se grabó la diferencia: lo contado es el saldo a esa fecha sin este comprobante más la diferencia.
                var cantidad = StockMovimientosCalculo.EsAjuste(tc) ? saldos.GetValueOrDefault(x.Articulo) + x.Cantidad : Math.Abs(x.Cantidad);
                borrador.Lineas.Add(new() { Articulo = x.Articulo, Cantidad = decimal.Round(cantidad, 4) });
            }
            var articulos = await LeerArticulosAsync(cn, ctx, tc, borrador.Deposito, null, null, porArticulo.Select(x => x.Articulo).ToArray(), null, token);
            var resultado = new StockMovimientoCargado { Borrador = borrador };
            foreach (var a in articulos) resultado.Articulos[a.Codigo] = a;
            return resultado;
        }, ct);

    public Task<StockMovimientoResultado> AnularAsync(string tc, string comprobante, Guid operacion, CancellationToken ct = default)
        => ExecuteAsync("Anular", async (cn, ctx, token) =>
        {
            if (operacion == Guid.Empty) throw new CargaComprobantesReglaException("Falta el identificador de la operación. Recargá la pantalla.");
            tc = (tc ?? "").Trim().ToUpperInvariant();
            comprobante = (comprobante ?? "").Trim();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            if (await OperacionGrabadaAsync(cn, tx, operacion, token) is { } previa) { await tx.RollbackAsync(token); return previa; }
            var filas = await LeerFilasAsync(cn, ctx, tc, comprobante, tx, bloquear: true, token);
            await ValidarEditableAsync(cn, ctx, tc, comprobante, filas, tx, token);
            // Como AnulaComprobante del legacy: no se borra, se marca anulado (los saldos excluyen Anulado=1).
            await cn.ExecuteAsync(Cmd("UPDATE dbo.V_MV_STOCK SET Anulado=1 WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante AND IDCOMPLEMENTO=0;",
                new { Tc = tc, Comprobante = comprobante }, tx, token));
            await RegistrarAccionAsync(cn, ctx, tc, comprobante, "B", operacion, tx, token);
            await tx.CommitAsync(token);
            await events.LogAuditAsync(Modulo, "Anular", "V_MV_STOCK", $"{tc} {comprobante}", $"Anuló {tc} {comprobante}.",
                new { Tc = tc, Comprobante = comprobante, Operacion = operacion }, token);
            return new StockMovimientoResultado { Tc = tc, Comprobante = comprobante, Accion = "B", Lineas = filas.Select(f => f.Articulo).Distinct().Count() };
        }, ct);

    private static async Task<List<FilaMovimiento>> LeerFilasAsync(SqlConnection cn, Contexto ctx, string tc, string comprobante, SqlTransaction? tx, bool bloquear, CancellationToken ct)
    {
        if (!StockMovimientosCalculo.Tipos.Contains(tc) || comprobante.Length is 0 or > 13)
            throw new CargaComprobantesReglaException("Elegí un comprobante de stock.");
        var filas = (await cn.QueryAsync<FilaMovimiento>(Cmd($"""
            SELECT LTRIM(RTRIM(IDArticulo)) Articulo, IDArticulo ArticuloCrudo, FECHA Fecha, CONVERT(decimal(18,4),ISNULL(Cantidad,0)) Cantidad,
                CONVERT(decimal(18,4),ISNULL(CantidadUD,0)) CantidadUD, LTRIM(RTRIM(ISNULL(IdDeposito,''))) Deposito,
                LTRIM(RTRIM(ISNULL(IDDEPOSITO_DESTINO,''))) DepositoDestino, LTRIM(RTRIM(ISNULL(IdMotivoStock,''))) Motivo,
                LTRIM(RTRIM(ISNULL(ControladoPor,''))) Observaciones, LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) UnidadNegocio,
                CONVERT(bit,ISNULL(Anulado,0)) Anulado, LTRIM(RTRIM(ISNULL(NroLote,''))) Lote, LTRIM(RTRIM(ISNULL(NroSerie,''))) Serie,
                LTRIM(RTRIM(ISNULL(TC_Origen,''))) TcOrigen, {(ctx.HayViaje ? "LTRIM(RTRIM(ISNULL(IdViaje,'')))" : "''")} Viaje
            FROM dbo.V_MV_STOCK {(bloquear ? "WITH (UPDLOCK, HOLDLOCK)" : "")}
            WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante AND IDCOMPLEMENTO=0
            ORDER BY SECUENCIA;
            """, new { Tc = tc, Comprobante = comprobante }, tx, ct))).AsList();
        if (filas.Count == 0) throw new CargaComprobantesReglaException($"No existe el comprobante {tc} {comprobante}.");
        return filas;
    }

    /// <summary>Lo que el legacy controla antes de modificar o anular, más lo que esta pantalla todavía no sabe rehacer.</summary>
    private static async Task ValidarEditableAsync(SqlConnection cn, Contexto ctx, string tc, string comprobante, List<FilaMovimiento> filas,
        SqlTransaction? tx, CancellationToken ct)
    {
        string Cpte() => $"{tc} {comprobante}";
        if (filas.Any(f => f.Anulado)) throw new CargaComprobantesReglaException($"El comprobante {Cpte()} ya está anulado.");
        var unidad = filas[0].UnidadNegocio;
        if (!ctx.AdminCentral && unidad != ctx.UnidadNegocio)
            throw new CargaComprobantesReglaException($"El comprobante {Cpte()} es de otra unidad de negocio.");
        ValidarFecha(ctx, filas[0].Fecha.Date, "El comprobante tiene fecha");
        if (filas.Any(f => f.Lote.Length > 0 || f.Serie.Length > 0))
            throw new CargaComprobantesReglaException($"El comprobante {Cpte()} tiene lotes o series: modificalo desde Alfa Gestión.");
        if (filas.Any(f => f.TcOrigen.Length > 0))
            throw new CargaComprobantesReglaException($"El comprobante {Cpte()} se generó desde otro comprobante ({filas.First(f => f.TcOrigen.Length > 0).TcOrigen}): modificalo desde Alfa Gestión.");
        if (StockMovimientosCalculo.EsTransferencia(tc))
        {
            if (filas.Any(f => f.Viaje.Length > 0) || ctx.HayAplicacion && await cn.ExecuteScalarAsync<int>(Cmd(
                    "SELECT COUNT(1) FROM dbo.MV_APLICACION WHERE TC='TR' AND IDCOMPROBANTE=@Comprobante AND TCO_ORIGEN='VJ';",
                    new { Comprobante = comprobante }, tx, ct)) > 0)
                throw new CargaComprobantesReglaException($"La transferencia {comprobante} tiene viajes asociados: se modifica desde Logística → Transferencias entre depósitos por OC.");
        }
        else if (filas.Select(f => f.Deposito).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            throw new CargaComprobantesReglaException($"El comprobante {Cpte()} tiene artículos en distintos depósitos: modificalo desde Alfa Gestión.");

        var otros = await cn.QueryFirstAsync<FilaVinculos>(Cmd($"""
            SELECT
              (SELECT COUNT(1) FROM dbo.V_MV_STOCK WHERE TC='PE' AND TC_Origen=@Tc AND IdComprobante_Origen=@Comprobante AND ISNULL(Anulado,0)=0) Partes,
              (SELECT COUNT(1) FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))=@Tc AND SUCURSAL=@Sucursal AND NUMERO=@Numero AND LETRA=@Letra) Asientos,
              {(ctx.HayInventarios ? "(SELECT COUNT(1) FROM dbo.MV_INVENTARIOSCAB WHERE LTRIM(RTRIM(ISNULL(AjusteStock,'')))=@Comprobante)" : "0")} Inventarios;
            """, new { Tc = tc, Comprobante = comprobante, Sucursal = comprobante[..4], Numero = comprobante.Length >= 12 ? comprobante.Substring(4, 8) : "",
                Letra = comprobante.Length >= 13 ? comprobante[12..] : "" }, tx, ct));
        if (otros.Partes > 0) throw new CargaComprobantesReglaException($"El comprobante {Cpte()} generó un parte de consumo (insumos): modificalo desde Alfa Gestión.");
        if (otros.Asientos > 0) throw new CargaComprobantesReglaException($"El comprobante {Cpte()} tiene asiento contable: modificalo desde Alfa Gestión.");
        if (tc == "AJP" && otros.Inventarios > 0) throw new CargaComprobantesReglaException($"El ajuste {comprobante} salió de una toma de inventario: anulalo desde Alfa Gestión para reabrir el inventario.");
    }

    // ---------------------------------------------------------------- Validaciones comunes

    private static StockMovimientoTipo? TipoPara(Contexto ctx, string tc, string unidad)
        => ctx.Publico.Tipos.Where(t => t.Tc == tc && (t.UnidadNegocio.Length == 0 || t.UnidadNegocio == unidad))
            .OrderBy(t => t.UnidadNegocio == unidad ? 0 : 1).FirstOrDefault();

    private static void ValidarFecha(Contexto ctx, DateTime fecha, string prefijo = "La fecha")
    {
        var c = ctx.Publico;
        if (fecha.Year < 1990 || fecha > DateTime.Today.AddYears(1)) throw new CargaComprobantesReglaException("Revisá la fecha del movimiento.");
        if (c.CierreStock is { } cierre && fecha <= cierre)
            throw new CargaComprobantesReglaException($"{prefijo} {fecha:dd/MM/yyyy}, dentro del período de stock cerrado (cierre {cierre:dd/MM/yyyy}).");
        if (c.OperativaDesde is { } desde && fecha < desde || c.OperativaHasta is { } hasta && fecha > hasta)
            throw new CargaComprobantesReglaException($"{prefijo} {fecha:dd/MM/yyyy}, fuera de la fecha operativa permitida.");
    }

    // FMT_VALIDA_DEPOSITO: el depósito tiene que ser de la unidad de negocio del movimiento o compartido.
    private static void ValidarDeposito(Contexto ctx, string deposito, string unidad, string cual)
    {
        var texto = cual.Length == 0 ? "el depósito" : $"el depósito {cual}";
        var d = ctx.Publico.Depositos.FirstOrDefault(x => string.Equals(x.Codigo, deposito, StringComparison.OrdinalIgnoreCase))
            ?? throw new CargaComprobantesReglaException($"Elegí {texto}.");
        if (!d.Compartido && d.UnidadNegocio != unidad)
            throw new CargaComprobantesReglaException($"El depósito {d.Codigo} no está asociado a la unidad de negocio {unidad}.");
        if (cual.Length > 0 && d.Empresa.Length > 0)
            throw new CargaComprobantesReglaException($"El depósito {d.Codigo} es de otra empresa ({d.Empresa}): esa transferencia se hace desde Alfa Gestión.");
    }

    private static void ValidarDepositoLectura(Contexto ctx, string deposito)
    {
        var codigo = (deposito ?? "").Trim();
        if (codigo.Length > 0 && !ctx.Publico.Depositos.Any(d => string.Equals(d.Codigo, codigo, StringComparison.OrdinalIgnoreCase)))
            throw new CargaComprobantesReglaException("Elegí un depósito autorizado.");
    }

    // ---------------------------------------------------------------- Infraestructura

    private async Task<Contexto> LeerContextoAsync(SqlConnection cn, string usuario, CancellationToken ct)
    {
        var u = await cn.QueryFirstOrDefaultAsync<FilaUsuario>(Cmd("""
            SELECT LTRIM(RTRIM(NOMBRE)) Usuario, LTRIM(RTRIM(ISNULL(UNegocio,''))) Unidad, CONVERT(bit,ISNULL(AdminCentral,0)) Admin
            FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@Usuario);
            """, new { Usuario = usuario }, null, ct))
            ?? throw new CargaComprobantesReglaException("El usuario actual no está definido en la base activa.");
        if (!u.Admin && u.Unidad.Length == 0) throw new CargaComprobantesReglaException("El usuario debe tener una unidad de negocio para mover stock.");

        var esquema = await cn.QueryFirstAsync<Esquema>(Cmd("""
            SELECT CONVERT(bit,CASE WHEN COL_LENGTH('dbo.V_TA_CPTE','IdDeposito') IS NULL THEN 0 ELSE 1 END) CpteDeposito,
                   CONVERT(bit,CASE WHEN COL_LENGTH('dbo.V_TA_DEPOSITO','COMPARTIDO') IS NULL THEN 0 ELSE 1 END) Compartido,
                   CONVERT(bit,CASE WHEN COL_LENGTH('dbo.V_TA_DEPOSITO','Empresa') IS NULL THEN 0 ELSE 1 END) Empresa,
                   CONVERT(bit,CASE WHEN COL_LENGTH('dbo.V_TA_DEPOSITO','UnidadNegocio') IS NULL THEN 0 ELSE 1 END) DepositoUnidad,
                   CONVERT(bit,CASE WHEN OBJECT_ID('dbo.V_MA_INSUMOS') IS NULL THEN 0 ELSE 1 END) Insumos,
                   CONVERT(bit,CASE WHEN OBJECT_ID('dbo.V_MV_CTROL_CPTE') IS NULL THEN 0 ELSE 1 END) Control,
                   CONVERT(bit,CASE WHEN OBJECT_ID('dbo.V_MV_CPTEACCIONES') IS NULL THEN 0 ELSE 1 END) Acciones,
                   CONVERT(bit,CASE WHEN OBJECT_ID('dbo.MV_APLICACION') IS NULL THEN 0 ELSE 1 END) Aplicacion,
                   CONVERT(bit,CASE WHEN COL_LENGTH('dbo.MV_INVENTARIOSCAB','AjusteStock') IS NULL THEN 0 ELSE 1 END) Inventarios,
                   CONVERT(bit,CASE WHEN COL_LENGTH('dbo.V_MV_STOCK','IdViaje') IS NULL THEN 0 ELSE 1 END) Viaje,
                   CONVERT(bit,CASE WHEN OBJECT_ID('dbo.ALFACORE_STOCK_MOV_OPERACION') IS NULL THEN 0 ELSE 1 END) Operaciones;
            """, null, null, ct));
        if (!esquema.Operaciones)
            throw new CargaComprobantesReglaException("La base todavía no tiene la actualización de Movimientos de stock. Reiniciá AlfaCore para aplicarla.");

        var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(Cmd("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux),'')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('MOTIVOSTOCKDEFAULT','NOFACTURASINSTOCK','CIERRESTOCK','FECHAOPERDESDE','FECHAOPERHASTA',
                'ASIENTOSXSTOCK','DESCARGAINSUMOSPRODUCCION');
            """, null, null, ct))).GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor.Trim(), StringComparer.OrdinalIgnoreCase);
        static DateTime? Fecha(Dictionary<string, string> c, string k)
            => DateTime.TryParseExact(c.GetValueOrDefault(k, ""), ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], Inv, DateTimeStyles.None, out var f) ? f.Date : null;
        static bool Si(Dictionary<string, string> c, string k) => string.Equals(c.GetValueOrDefault(k, ""), "SI", StringComparison.OrdinalIgnoreCase);

        var unidadFiltro = new { Admin = u.Admin, Unidad = u.Unidad };
        var publico = new StockMovimientosContexto
        {
            Usuario = u.Usuario, UnidadNegocio = u.Unidad, AdminCentral = u.Admin,
            MotivoDefault = cfg.GetValueOrDefault("MOTIVOSTOCKDEFAULT", ""), ValidaStockSalidas = Si(cfg, "NOFACTURASINSTOCK"),
            CierreStock = Fecha(cfg, "CIERRESTOCK"), OperativaDesde = Fecha(cfg, "FECHAOPERDESDE"), OperativaHasta = Fecha(cfg, "FECHAOPERHASTA")
        };
        publico.Tipos = (await cn.QueryAsync<StockMovimientoTipo>(Cmd($"""
            SELECT LTRIM(RTRIM(CODIGO)) Tc, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion, ISNULL(LTRIM(RTRIM(UNEGOCIO)),'') UnidadNegocio,
                UPPER(LTRIM(RTRIM(ISNULL(ES,'')))) Es, {(esquema.CpteDeposito ? "ISNULL(LTRIM(RTRIM(IdDeposito)),'')" : "''")} IdDeposito
            FROM dbo.V_TA_CPTE
            WHERE UPPER(LTRIM(RTRIM(SISTEMA)))='STOCK' AND LTRIM(RTRIM(CODIGO)) IN @Tipos
              AND (@Admin=1 OR ISNULL(LTRIM(RTRIM(UNEGOCIO)),'') IN ('',@Unidad))
            ORDER BY CODIGO, Talonario;
            """, new { Tipos = StockMovimientosCalculo.Tipos.ToArray(), unidadFiltro.Admin, unidadFiltro.Unidad }, null, ct))).ToList();
        publico.UnidadesNegocio = (await cn.QueryAsync<CargaComprobantesOpcionFiltro>(Cmd("""
            SELECT LTRIM(RTRIM(Codigo)) Codigo, LTRIM(RTRIM(Descripcion)) Descripcion FROM dbo.V_TA_UnidadNegocio
            WHERE @Admin=1 OR LTRIM(RTRIM(Codigo))=@Unidad ORDER BY Codigo;
            """, unidadFiltro, null, ct))).ToList();
        publico.Depositos = (await cn.QueryAsync<StockMovimientoDeposito>(Cmd($"""
            SELECT LTRIM(RTRIM(IdDeposito)) Codigo, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion,
                {(esquema.DepositoUnidad ? "ISNULL(LTRIM(RTRIM(UnidadNegocio)),'')" : "''")} UnidadNegocio,
                {(esquema.Compartido ? "CONVERT(bit,ISNULL(COMPARTIDO,0))" : esquema.DepositoUnidad ? "CONVERT(bit,0)" : "CONVERT(bit,1)")} Compartido,
                {(esquema.Empresa ? "ISNULL(LTRIM(RTRIM(Empresa)),'')" : "''")} Empresa
            FROM dbo.V_TA_DEPOSITO
            WHERE LTRIM(RTRIM(ISNULL(IdDeposito,'')))<>''
              {FiltroDepositos(u.Admin, esquema)}
            ORDER BY IdDeposito;
            """, unidadFiltro, null, ct))).ToList();
        var motivos = (await cn.QueryAsync<(string Codigo, string Descripcion, string Cuenta)>(Cmd("""
            SELECT LTRIM(RTRIM(IdMotivoStock)), LTRIM(RTRIM(ISNULL(Descripcion,''))), LTRIM(RTRIM(ISNULL(CuentaContable,'')))
            FROM dbo.V_TA_MotivoStock WHERE LTRIM(RTRIM(ISNULL(IdMotivoStock,'')))<>'' ORDER BY IdMotivoStock;
            """, null, null, ct))).ToList();
        publico.Motivos = motivos.Select(m => new CargaComprobantesOpcionFiltro { Codigo = m.Codigo, Descripcion = m.Descripcion }).ToList();
        if (!publico.Motivos.Any(m => m.Codigo == publico.MotivoDefault)) publico.MotivoDefault = "";
        return new Contexto(publico)
        {
            AsientosXStock = Si(cfg, "ASIENTOSXSTOCK"), DescargaInsumosProduccion = Si(cfg, "DESCARGAINSUMOSPRODUCCION"),
            MotivosConCuenta = motivos.Where(m => m.Cuenta.Length > 0).Select(m => m.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase),
            HayInsumos = esquema.Insumos, HayControlComprobantes = esquema.Control, HayAcciones = esquema.Acciones,
            HayAplicacion = esquema.Aplicacion, HayInventarios = esquema.Inventarios, HayViaje = esquema.Viaje
        };
    }

    // FMT_VALIDA_DEPOSITO acepta el depósito de la unidad del usuario o uno compartido.
    private static string FiltroDepositos(bool admin, Esquema esquema)
    {
        if (admin || !esquema.DepositoUnidad) return "";
        return esquema.Compartido ? "AND (ISNULL(LTRIM(RTRIM(UnidadNegocio)),'')=@Unidad OR ISNULL(COMPARTIDO,0)=1)"
            : "AND ISNULL(LTRIM(RTRIM(UnidadNegocio)),'')=@Unidad";
    }

    private async Task<T> ExecuteAsync<T>(string action, Func<SqlConnection, Contexto, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessionService, appUserSession, out var sessionId))
            throw new CargaComprobantesReglaException("Ingresá con un usuario autorizado en la base activa.");
        var user = appUserSession.GetCurrentUserName().Trim();
        var system = appUserSession.CurrentUser?.SystemCode ?? string.Empty;
        try
        {
            await using var cn = new SqlConnection(sessionService.GetConnectionString());
            await cn.OpenAsync(ct);
            if (!await permissions.HasAccessAsync(MenuKey, ct))
            {
                // Igual que Carga de Comprobantes: false también para un usuario sin restricciones; confirmar que las tenga.
                var restringido = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TA_TAREAS
                        WHERE UPPER(LTRIM(RTRIM(USUARIO)))=UPPER(@Usuario) AND UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema) AND ISNULL(TAREA,'')<>'')
                        OR EXISTS(SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB
                        WHERE UPPER(LTRIM(RTRIM(USUARIO)))=UPPER(@Usuario) AND UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema) AND ISNULL(Clave,'')<>'')
                        THEN 1 ELSE 0 END;
                    """, new { Usuario = user, Sistema = system }, commandTimeout: 5, cancellationToken: ct));
                if (restringido != 0) throw new CargaComprobantesReglaException("Tu usuario no tiene acceso a Movimientos de stock.");
            }
            EnsureContext(sessionId, user, system);
            var contexto = await LeerContextoAsync(cn, user, ct);
            var result = await operation(cn, contexto, ct);
            EnsureContext(sessionId, user, system);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (CargaComprobantesReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            const string generico = "No se pudo completar el movimiento de stock. Revisá si quedó grabado antes de reintentar.";
            var sql = ex as SqlException ?? ex.InnerException as SqlException;
            // Los triggers de V_MV_STOCK rechazan con RAISERROR 16 y un texto para el usuario (fecha operativa, cierre, depósito, stock).
            var trigger = sql?.Errors.Cast<SqlError>().FirstOrDefault(e => e.Number == 50000 && e.Class == 16);
            var message = trigger is not null ? $"La base rechazó el movimiento: {trigger.Message.Trim()}"
                : sql is null ? generico : $"{CargaComprobantesErroresSql.Explicar(sql)} Revisá si quedó grabado antes de reintentar.";
            var code = await events.LogErrorAsync(Modulo, action, ex, message, ct: ct);
            throw new AppUserFacingException(message, code, ex);
        }
    }

    private void EnsureContext(Guid sessionId, string user, string system)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessionService, appUserSession, out var current) || current != sessionId ||
            !string.Equals(user, appUserSession.GetCurrentUserName().Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(system, appUserSession.CurrentUser?.SystemCode ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            throw new CargaComprobantesReglaException("Cambió la base o el usuario. Volvé a cargar el módulo en la sesión actual.");
    }

    private static CommandDefinition Cmd(string sql, object? p, SqlTransaction? tx, CancellationToken ct)
        => new(sql, p, tx, commandTimeout: 60, cancellationToken: ct);
    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed class Contexto(StockMovimientosContexto publico)
    {
        public StockMovimientosContexto Publico { get; } = publico;
        public bool AdminCentral => Publico.AdminCentral;
        public string UnidadNegocio => Publico.UnidadNegocio;
        public bool AsientosXStock { get; init; }
        public bool DescargaInsumosProduccion { get; init; }
        public HashSet<string> MotivosConCuenta { get; init; } = [];
        public bool HayInsumos { get; init; }
        public bool HayControlComprobantes { get; init; }
        public bool HayAcciones { get; init; }
        public bool HayAplicacion { get; init; }
        public bool HayInventarios { get; init; }
        public bool HayViaje { get; init; }
    }
    private sealed record Preparado(string Tc, string UnidadNegocio, DateTime Fecha, string Deposito, string? Destino, string Motivo,
        string Observaciones, List<StockMovimientosCalculo.LineaResuelta> Lineas, StockMovimientoPlan Plan);
    private sealed class Esquema
    {
        public bool CpteDeposito { get; set; }
        public bool Compartido { get; set; }
        public bool Empresa { get; set; }
        public bool DepositoUnidad { get; set; }
        public bool Insumos { get; set; }
        public bool Control { get; set; }
        public bool Acciones { get; set; }
        public bool Aplicacion { get; set; }
        public bool Inventarios { get; set; }
        public bool Viaje { get; set; }
        public bool Operaciones { get; set; }
    }
    private sealed class FilaArticulo
    {
        public string Codigo { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Unidad { get; set; } = "";
        public decimal Costo { get; set; }
        public string Restriccion { get; set; } = "";
        public string IdCrudo { get; set; } = "";
    }
    private sealed class FilaOperacion { public string Tc { get; set; } = ""; public string IdComprobante { get; set; } = ""; public string Accion { get; set; } = ""; }
    private sealed class FilaUsuario { public string Usuario { get; set; } = ""; public string Unidad { get; set; } = ""; public bool Admin { get; set; } }
    private sealed class FilaTalonario { public int Talonario { get; set; } public string Letras { get; set; } = ""; }
    private sealed class FilaNumeracion { public int? Sucursal { get; set; } public int? Ultimo { get; set; } }
    private sealed class FilaVinculos { public int Partes { get; set; } public int Asientos { get; set; } public int Inventarios { get; set; } }
    private sealed class FilaMovimiento
    {
        public string Articulo { get; set; } = "";
        public string ArticuloCrudo { get; set; } = "";
        public DateTime Fecha { get; set; }
        public decimal Cantidad { get; set; }
        public decimal CantidadUD { get; set; }
        public string Deposito { get; set; } = "";
        public string DepositoDestino { get; set; } = "";
        public string Motivo { get; set; } = "";
        public string Observaciones { get; set; } = "";
        public string UnidadNegocio { get; set; } = "";
        public bool Anulado { get; set; }
        public string Lote { get; set; } = "";
        public string Serie { get; set; } = "";
        public string TcOrigen { get; set; } = "";
        public string Viaje { get; set; } = "";
    }
}
