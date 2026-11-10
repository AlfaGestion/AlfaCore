using AlfaCore.ConciliacionBancaria.Application.Services;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Pago a proveedores desde un egreso del extracto bancario (Conciliación bancaria). Es la misma orden de pago (OPG/PGFP):
// el movimiento solo aporta el medio (la cuenta bancaria de la importación por el importe del egreso) y queda marcado «Pagado».
public sealed partial class OrdenPagoService
{
    /// <summary>Clasificaciones desde las que se puede pagar a un proveedor; al grabar el movimiento queda como PagoProveedor.</summary>
    private static readonly string[] CategoriasPagables = ["PagoProveedor", "Gasto", "NoIdentificado"];

    private sealed class MovimientoPago
    {
        public long Id { get; set; } public DateTime Fecha { get; set; } public decimal Importe { get; set; } public string Cuenta { get; set; } = "";
        public string Moneda { get; set; } = ""; public string Estado { get; set; } = ""; public string Descripcion { get; set; } = ""; public string Categoria { get; set; } = "";
    }

    private async Task RequerirConciliacionAsync(CancellationToken ct)
    {
        if (!await permissions.HasAccessAsync(ConciliacionBancariaService.MenuKey, ct))
            throw new InvalidOperationException("No tenés permiso para registrar pagos desde Conciliación.");
    }

    private static async Task<bool> JournalTieneMovimientoAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN COL_LENGTH('dbo.ALFACORE_PAGO_OPERACION','MovimientoId') IS NULL THEN 0 ELSE 1 END;", null, tx, ct)) == 1;

    /// <summary>Lee y valida el egreso: vigente, sin conciliar, en pesos, con clasificación pagable y sin una orden de pago vigente.</summary>
    private static async Task<MovimientoPago> LeerMovimientoPagoAsync(SqlConnection cn, SqlTransaction? tx, long id, CancellationToken ct)
    {
        if (!await JournalTieneMovimientoAsync(cn, tx, ct))
            throw new InvalidOperationException("Aplicá las actualizaciones de la base antes de pagar a proveedores desde Conciliación.");
        if (await cn.ExecuteScalarAsync<bool>(Cmd("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_ConciliacionBanco WHERE MovimientoId=@id AND Activa=1) THEN 1 ELSE 0 END);", new { id }, tx, ct)))
            throw new InvalidOperationException("El movimiento ya está conciliado con una registración de Alfa. Revisá ese vínculo antes de generar un pago.");
        var m = await cn.QuerySingleOrDefaultAsync<MovimientoPago>(Cmd("""
            SELECT m.MovimientoId Id,m.FechaMovimiento Fecha,m.Importe,LTRIM(RTRIM(ISNULL(i.CuentaBanco,''))) Cuenta,LTRIM(RTRIM(m.MonedaCodigo)) Moneda,
                m.EstadoRevision Estado,ISNULL(m.DescripcionOriginal,'') Descripcion,ISNULL(c.TipoMovimiento,'NoIdentificado') Categoria
            FROM dbo.BK_ExtractoMovimiento m JOIN dbo.BK_ImportacionExtracto i ON i.ImportacionId=m.ImportacionId
            OUTER APPLY(SELECT TOP 1 TipoMovimiento FROM dbo.BK_MovimientoClasificado WHERE MovimientoId=m.MovimientoId ORDER BY ClasificacionId DESC) c
            WHERE m.MovimientoId=@id;
            """, new { id }, tx, ct)) ?? throw new InvalidOperationException("El movimiento bancario ya no está disponible.");
        if (m.Importe >= 0 || !CategoriasPagables.Contains(m.Categoria))
            throw new InvalidOperationException("Para pagar a un proveedor, elegí un egreso clasificado como Pago Proveedor (o Gasto / No Identificado, que pasa a Pago Proveedor al grabar).");
        if (m.Estado is "Pagado" or "Cobrado" or "Registrado")
            throw new InvalidOperationException(m.Estado == "Pagado" ? "Este movimiento ya tiene un pago a proveedor registrado." : "Este movimiento ya está registrado en Alfa.");
        if (await cn.ExecuteScalarAsync<bool>(Cmd("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.ALFACORE_PAGO_OPERACION p
                JOIN dbo.V_MV_CPTE c ON c.IDCOMPROBANTE=p.Comprobante AND LTRIM(RTRIM(c.TC))=LTRIM(RTRIM(p.Tc)) AND ISNULL(c.IDCOMPLEMENTO,0)=0
                WHERE p.MovimientoId=@id AND ISNULL(c.ANULADA,0)=0) THEN 1 ELSE 0 END);
            """, new { id }, tx, ct)))
            throw new InvalidOperationException("Este movimiento ya tiene una orden de pago vigente. Actualizá la revisión.");
        if (m.Moneda != "ARS") throw new InvalidOperationException("El pago desde Conciliación admite movimientos en pesos.");
        return m;
    }

    private static async Task<OrdenPagoMedio> MedioBancoAsync(SqlConnection cn, SqlTransaction? tx, string cuenta, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<OrdenPagoMedio>(Cmd("""
            SELECT LTRIM(RTRIM(CODIGO)) Cuenta,LTRIM(RTRIM(DESCRIPCION)) Descripcion,LTRIM(RTRIM(MedioDePago)) Tipo
            FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@cuenta AND LTRIM(RTRIM(MedioDePago))='DB' AND LTRIM(RTRIM(MONEDA))='1'
                AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0;
            """, new { cuenta }, tx, ct))
            ?? throw new InvalidOperationException("La importación no tiene una cuenta bancaria activa en pesos. Seleccioná la cuenta al importar.");

    /// <summary>Datos del egreso para el diálogo de pago, con el proveedor sugerido si el extracto trae un CUIT que coincide con uno solo.</summary>
    public Task<OrdenPagoMovimiento> PrepararConciliacionAsync(long movimientoId, CancellationToken ct = default)
        => Ejecutar("PrepararConciliacion", async (cn, user, session, token) =>
        {
            await RequerirConciliacionAsync(token);
            var m = await LeerMovimientoPagoAsync(cn, null, movimientoId, token);
            var banco = await MedioBancoAsync(cn, null, m.Cuenta, token);
            var (cuit, nombre) = ContraparteBancaria.Extraer(m.Descripcion);
            OrdenPagoProveedor? sugerido = null;
            if (cuit is not null)
            {
                var coincidencias = (await cn.QueryAsync<OrdenPagoProveedor>(Cmd("""
                    SELECT TOP(2) LTRIM(RTRIM(CODIGO)) Codigo,LTRIM(RTRIM(RAZON_SOCIAL)) Nombre,LTRIM(RTRIM(ISNULL(NUMERO_DOCUMENTO,''))) Documento
                    FROM dbo.Vt_Proveedores WHERE ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0
                        AND REPLACE(REPLACE(REPLACE(NUMERO_DOCUMENTO,'-',''),'.',''),' ','')=@cuit;
                    """, new { cuit }, null, token))).AsList();
                if (coincidencias.Count == 1) sugerido = coincidencias[0];
            }
            VerificarSesion(session);
            return new OrdenPagoMovimiento(m.Id, m.Fecha.Date, -m.Importe, m.Cuenta, banco.Descripcion, m.Descripcion, m.Categoria,
                cuit ?? nombre, sugerido);
        }, ct);

    /// <summary>
    /// Se llama dentro de la transacción de grabación cuando la orden nace de un egreso: bloquea el movimiento, lo revalida y exige que el único
    /// medio sea la cuenta bancaria de la importación por el importe completo del egreso. Devuelve su clasificación anterior.
    /// </summary>
    private async Task<string> ValidarMovimientoDeOrdenAsync(SqlConnection cn, SqlTransaction tx, OrdenPagoSolicitud s, long movimientoId, CancellationToken ct)
    {
        await RequerirConciliacionAsync(ct);
        // Bloquear también el extracto: eliminar la importación, reclasificar y pagar deben serializarse.
        await cn.ExecuteScalarAsync<long>(Cmd("SELECT MovimientoId FROM dbo.BK_ExtractoMovimiento WITH(UPDLOCK,HOLDLOCK) WHERE MovimientoId=@movimientoId;", new { movimientoId }, tx, ct));
        var m = await LeerMovimientoPagoAsync(cn, tx, movimientoId, ct);
        var banco = await MedioBancoAsync(cn, tx, m.Cuenta, ct);
        if (s.ChequesTerceros.Count > 0 || s.Medios.Count != 1 || s.Medios[0].Cuenta.Trim() != banco.Cuenta || s.Medios[0].Importe != -m.Importe)
            throw new InvalidOperationException("El pago debe salir de la cuenta bancaria de la importación por el importe completo del movimiento. Los comprobantes, créditos y la retención se ajustan en la orden.");
        return m.Categoria;
    }

    /// <summary>Marca el egreso como pagado, lo clasifica como PagoProveedor y deja el rastro (mismo patrón que la cobranza).</summary>
    private static async Task VincularMovimientoAsync(SqlConnection cn, SqlTransaction tx, long movimientoId, string categoriaAnterior, Guid operacion,
        string tc, string comprobante, string usuario, CancellationToken ct)
    {
        var mensaje = $"{tc} {comprobante}; operación {operacion}";
        await cn.ExecuteAsync(Cmd("""
            UPDATE dbo.ALFACORE_PAGO_OPERACION SET MovimientoId=@movimientoId WHERE Operacion=@operacion;
            UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Pagado' WHERE MovimientoId=@movimientoId;
            IF @categoriaAnterior<>'PagoProveedor'
                INSERT INTO dbo.BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza,ReglaCodigo,Motivo)
                VALUES(@movimientoId,'PagoProveedor',100,'MANUAL',@motivo);
            INSERT INTO dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario) VALUES(@movimientoId,'PAGO','OK',@mensaje,@usuario);
            """, new { movimientoId, operacion, categoriaAnterior, motivo = $"Pago a proveedor: {categoriaAnterior} → PagoProveedor. {mensaje}", mensaje, usuario }, tx, ct));
    }

    /// <summary>
    /// Sugiere qué comprobantes cancelar con un importe: primero busca una combinación exacta de hasta 4 (entre los 20 más antiguos, la de menos
    /// comprobantes y más antigua); si no hay, cancela los más antiguos hasta cubrirlo y el último queda parcial. Si no alcanzan, devuelve todos.
    /// </summary>
    public static IReadOnlyList<OrdenPagoImporte> SugerirAplicacion(IEnumerable<OrdenPagoDocumento> documentos, decimal objetivo)
    {
        var docs = documentos.Where(d => d.Seleccionable && d.Saldo > 0).OrderBy(d => d.Fecha).ThenBy(d => d.Numero).ToList();
        if (objetivo <= 0 || docs.Count == 0) return [];
        var candidatos = docs.Take(20).ToList();
        for (var tamano = 1; tamano <= Math.Min(4, candidatos.Count); tamano++)
        {
            var elegido = BuscarCombinacion(candidatos, tamano, 0, objetivo, []);
            if (elegido is not null) return [.. elegido.Select(d => new OrdenPagoImporte(d.Clave, d.Saldo))];
        }
        var resultado = new List<OrdenPagoImporte>(); var falta = objetivo;
        foreach (var d in docs)
        {
            if (falta <= 0) break;
            var aplica = Math.Min(d.Saldo, falta);
            resultado.Add(new OrdenPagoImporte(d.Clave, aplica)); falta -= aplica;
        }
        return resultado;
    }

    private static List<OrdenPagoDocumento>? BuscarCombinacion(List<OrdenPagoDocumento> docs, int tamano, int desde, decimal falta, List<OrdenPagoDocumento> actual)
    {
        if (tamano == 0) return falta == 0 ? [.. actual] : null;
        for (var i = desde; i <= docs.Count - tamano; i++)
        {
            if (docs[i].Saldo > falta) continue;
            actual.Add(docs[i]);
            var r = BuscarCombinacion(docs, tamano - 1, i + 1, falta - docs[i].Saldo, actual);
            actual.RemoveAt(actual.Count - 1);
            if (r is not null) return r;
        }
        return null;
    }

    /// <summary>Al anular una orden que nació de un egreso, el movimiento vuelve a poder pagarse.</summary>
    private static async Task LiberarMovimientoAsync(SqlConnection cn, SqlTransaction tx, string tc, string comprobante, string usuario, CancellationToken ct)
    {
        if (!await JournalTieneMovimientoAsync(cn, tx, ct)) return;
        var movimientoId = await cn.QueryFirstOrDefaultAsync<long?>(Cmd("""
            SELECT TOP(1) MovimientoId FROM dbo.ALFACORE_PAGO_OPERACION WHERE Comprobante=@comprobante AND LTRIM(RTRIM(Tc))=@tc AND MovimientoId IS NOT NULL;
            """, new { comprobante, tc }, tx, ct));
        if (movimientoId is null) return;
        await cn.ExecuteAsync(Cmd("""
            UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Revisado' WHERE MovimientoId=@movimientoId AND EstadoRevision='Pagado';
            INSERT INTO dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
            VALUES(@movimientoId,'PAGO_ANULADO','OK',@mensaje,@usuario);
            """, new { movimientoId, mensaje = $"Anulada {tc} {comprobante}; el movimiento vuelve a estar disponible para pagar.", usuario }, tx, ct));
    }
}
