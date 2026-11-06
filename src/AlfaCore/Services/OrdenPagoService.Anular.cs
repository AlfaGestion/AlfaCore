using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Anulación de órdenes de pago (OPG) y pagos proforma (PGFP) cargados desde AlfaCore.
public sealed partial class OrdenPagoService
{
    /// <summary>
    /// Anula una orden creada por el circuito web: como el legacy, retira el asiento y las aplicaciones (los comprobantes y créditos
    /// vuelven a quedar con saldo) y deja la cabecera marcada ANULADA con una baja en el historial. Guarda una copia de lo retirado.
    /// </summary>
    public Task AnularAsync(string tc, string idComprobante, string motivo, CancellationToken ct = default)
        => Ejecutar("Anular", async (cn, user, session, token) =>
        {
            using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
            await AnularAsync(cn, tx, tc, idComprobante, motivo, user, token);
            VerificarSesion(session); token.ThrowIfCancellationRequested();
            if (!string.Equals(user.Nombre, users.GetCurrentUserName(), StringComparison.OrdinalIgnoreCase))
                throw new OperationCanceledException("El usuario cambió durante la anulación.");
            tx.Commit();
            return true;
        }, ct);

    // Para ensayos con transacción externa y rollback, sin modificar la base.
    internal async Task AnularEnTransaccionAsync(SqlConnection cn, SqlTransaction tx, string tc, string idComprobante, string motivo, CancellationToken ct)
    {
        var user = await LeerUsuarioAsync(cn, tx, users.GetCurrentUserName(), ct) ?? throw new InvalidOperationException("El usuario actual no está definido en la base activa.");
        await AnularAsync(cn, tx, tc, idComprobante, motivo, user, ct);
    }

    private sealed class OrdenAnulable
    {
        public int Id { get; set; } public string Tc { get; set; } = ""; public string Comprobante { get; set; } = ""; public string Sucursal { get; set; } = ""; public string Numero { get; set; } = "";
        public string Letra { get; set; } = ""; public DateTime Fecha { get; set; } public string Cuenta { get; set; } = ""; public string Unidad { get; set; } = ""; public bool Anulada { get; set; }
        public bool Web { get; set; }
    }

    private async Task AnularAsync(SqlConnection cn, SqlTransaction tx, string tc, string idComprobante, string motivo, Usuario user, CancellationToken ct)
    {
        CommandDefinition Q(string sql, object? args = null) => Cmd(sql, args, tx, ct);
        tc = (tc ?? string.Empty).Trim().ToUpperInvariant(); idComprobante = (idComprobante ?? string.Empty).Trim(); motivo = (motivo ?? string.Empty).Trim();
        if (tc is not (TcOrdenPago or TcProforma) || idComprobante.Length != 13) throw new InvalidOperationException("Solo se anulan órdenes de pago y pagos proforma.");
        if (motivo.Length is < 5 or > 500) throw new InvalidOperationException("Escribí un motivo de entre 5 y 500 caracteres.");

        await cn.ExecuteAsync(Q("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='ALFACORE_ORDEN_PAGO',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
            IF @r<0 THROW 50001,'Hay otra orden de pago en proceso. Volvé a intentar.',1;
            """));
        if (await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.ALFACORE_PAGO_ANULACION') AND name='Motivo';")) == 0)
            throw new InvalidOperationException("Aplicá las actualizaciones de la base antes de anular órdenes de pago desde la web.");

        var o = await cn.QuerySingleOrDefaultAsync<OrdenAnulable>(Q("""
            SELECT c.ID Id, LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante, c.SUCURSAL Sucursal, c.NUMERO Numero, c.LETRA Letra, c.FECHA Fecha,
                LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta, LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) Unidad, CONVERT(bit,ISNULL(c.ANULADA,0)) Anulada,
                CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.ALFACORE_PAGO_OPERACION p WHERE p.Comprobante=c.IDCOMPROBANTE AND LTRIM(RTRIM(p.Tc))=LTRIM(RTRIM(c.TC))) THEN 1 ELSE 0 END) Web
            FROM dbo.V_MV_CPTE c WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(c.TC))=@tc AND c.IDCOMPROBANTE=@id AND ISNULL(c.IDCOMPLEMENTO,0)=0;
            """, new { tc, id = idComprobante })) ?? throw new InvalidOperationException("La orden de pago no existe.");
        if (!user.Admin && o.Unidad != UnidadDe(user)) throw new InvalidOperationException("La orden de pago es de otra unidad de negocio.");
        if (o.Anulada) return;
        if (!o.Web) throw new InvalidOperationException("Esta orden no se creó desde AlfaCore. Para anularla usá el proceso del sistema legacy.");

        if (await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.MV_EJERCICIOS WHERE @Fecha BETWEEN [FECHA DESDE] AND [FECHA HASTA] AND CERRADO=0;", new { o.Fecha })) == 0)
            throw new InvalidOperationException("La orden pertenece a un ejercicio cerrado. No puede anularse.");

        var clave = new { tc, suc = o.Sucursal, num = o.Numero, letra = o.Letra, id = idComprobante };
        var asientos = (await cn.QueryAsync(Q("""
            SELECT * FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=@suc AND NUMERO=@num AND LETRA=@letra ORDER BY SECUENCIA;
            """, clave))).ToList();
        if (await cn.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=@suc AND NUMERO=@num AND LETRA=@letra
                AND (ISNULL(CONTABILIZADO,0)=1 OR ISNULL(Conciliado,0)=1);
            """, clave)) > 0)
            throw new InvalidOperationException("El asiento de la orden está contabilizado o conciliado (por ejemplo, un cheque ya conciliado). No puede anularse desde aquí.");

        // Si el anticipo que dejó esta orden ya se aplicó a otros comprobantes, primero hay que deshacer esa aplicación.
        if (await cn.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.MV_APLICACION WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(TC))=@tc AND IDCOMPROBANTE=@id AND NOT (LTRIM(RTRIM(ISNULL(TC_PRINT,'')))=@tc AND ISNULL(IDCOMPROBANTE_PRINT,'')=@id);
            """, clave)) > 0)
            throw new InvalidOperationException("El saldo a favor que dejó esta orden ya se aplicó a otros comprobantes. Deshacé esa aplicación antes de anularla.");

        var aplicaciones = (await cn.QueryAsync(Q("""
            SELECT * FROM dbo.MV_APLICACION WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(ISNULL(TC_PRINT,'')))=@tc AND IDCOMPROBANTE_PRINT=@id;
            """, clave))).ToList();
        var asiento = asientos.Select(a => (IDictionary<string, object>)a).FirstOrDefault();
        string? Campo(string nombre) => asiento is not null && asiento.TryGetValue(nombre, out var v) ? v?.ToString()?.Trim() : null;
        await cn.ExecuteAsync(Q("""
            INSERT INTO dbo.ALFACORE_PAGO_ANULACION(ComprobanteId,Tc,Comprobante,Usuario,Motivo,AsientoJson,AplicacionesJson)
            VALUES(@idCab,@tc,@id,@usuario,@motivo,@asientos,@aplicaciones);
            """, new { idCab = o.Id, tc, id = idComprobante, usuario = user.Nombre, motivo, asientos = JsonSerializer.Serialize(asientos), aplicaciones = JsonSerializer.Serialize(aplicaciones) }));
        await cn.ExecuteAsync(Q("""
            DELETE dbo.MV_APLICACION WHERE LTRIM(RTRIM(ISNULL(TC_PRINT,'')))=@tc AND IDCOMPROBANTE_PRINT=@id;
            DELETE dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=@suc AND NUMERO=@num AND LETRA=@letra;
            UPDATE dbo.V_MV_CPTE SET ANULADA=1 WHERE ID=@idCab;
            """, new { tc, suc = o.Sucursal, num = o.Numero, letra = o.Letra, id = idComprobante, idCab = o.Id }));
        await ComprobanteAcciones.RegistrarAsync(cn, tx, tc, idComprobante, ComprobanteAcciones.Baja, user.Nombre, "AlfaCore: anulación de orden de pago. " + motivo, ct,
            comentario: "Baja desde AlfaCore", asiento: int.TryParse(Campo("NUMERO ASIENTO"), out var numeroAsiento) ? numeroAsiento : null,
            periodo: Campo("PERIODO"), tipoReg: Campo("TIPO_REG"), fecha: o.Fecha, cuenta: o.Cuenta);
    }
}
