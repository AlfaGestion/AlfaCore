using System.Data;
using System.Globalization;
using AlfaCore.ConciliacionBancaria.Application.Services;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.ConciliacionBancaria.Infrastructure;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    public Task<bool> ReclasificarAsync(long movimientoId,string categoria,string? motivo,CancellationToken ct=default)
        => Ejecutar("Reclasificar",async(cn,token)=>
        {
            if(!Enum.TryParse<MovementCategory>(categoria,out var parsed)||!Enum.IsDefined(parsed)||parsed.ToString()!=categoria)
                throw new InvalidOperationException("Seleccioná una clasificación de la lista.");
            if(motivo?.Length>300) throw new InvalidOperationException("El motivo admite hasta 300 caracteres.");
            var session=sessions.GetActiveSession()!.Id;
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:token);
            var actual=await cn.QuerySingleOrDefaultAsync<RevisionActual>(Q("""
                SELECT m.ImportacionId,m.EstadoRevision Estado,ISNULL(c.TipoMovimiento,'NoIdentificado') Categoria
                FROM dbo.BK_ExtractoMovimiento m WITH(UPDLOCK,HOLDLOCK)
                OUTER APPLY(SELECT TOP 1 TipoMovimiento FROM dbo.BK_MovimientoClasificado WHERE MovimientoId=m.MovimientoId ORDER BY ClasificacionId DESC)c
                WHERE m.MovimientoId=@movimientoId;
                """,new{movimientoId})) ?? throw new InvalidOperationException("El movimiento ya no está disponible.");
            if(actual.Estado=="Cobrado"||await cn.ExecuteScalarAsync<bool>(Q("""
                SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_GeneracionAlfaLog WHERE MovimientoId=@movimientoId AND Accion='COBRANZA' AND Resultado='OK') THEN 1 ELSE 0 END);
                """,new{movimientoId})))
                throw new InvalidOperationException("El movimiento ya tiene una cobranza registrada. Su clasificación no se puede cambiar desde la revisión.");
            if(actual.Categoria==categoria) return false;
            var usuario=users.GetCurrentUserName();
            var mensaje=$"Clasificación manual: {actual.Categoria} → {categoria}. {motivo?.Trim()}";
            await cn.ExecuteAsync(Q("""
                INSERT INTO dbo.BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza,ReglaCodigo,Motivo)
                VALUES(@movimientoId,@categoria,100,'MANUAL',@mensaje);
                UPDATE dbo.BK_MovimientoMatch SET EstadoPropuesta='RECLASIFICADO' WHERE MovimientoId=@movimientoId;
                UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision=CASE WHEN @categoria='Cobranza' THEN 'Pendiente' ELSE 'Revisado' END WHERE MovimientoId=@movimientoId;
                INSERT INTO dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
                VALUES(@movimientoId,'RECLASIFICAR','OK',@mensaje,@usuario);
                """,new{movimientoId,categoria,mensaje,usuario}));
            if(parsed==MovementCategory.Cobranza)
            {
                var valor=await cn.ExecuteScalarAsync<string?>(Q("""
                    SELECT COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux)) FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-TOLERANCIA';
                    """));
                if(!decimal.TryParse(valor,NumberStyles.Number,CultureInfo.InvariantCulture,out var tolerancia)||tolerancia<0||tolerancia>100)
                    throw new InvalidOperationException("Revisá CONCILIACION-TOLERANCIA en la configuración.");
                await new DeterministicMatchingService(new ConciliacionRepository(cn,tx,movimientoId),tolerancia).GenerateMatchesAsync(actual.ImportacionId,token);
            }
            VerificarSesion(session);token.ThrowIfCancellationRequested();tx.Commit();return true;
        },ct);

    public Task<ConciliacionContextoComprobantes> ContextoComprobantesAsync(long movimientoId,CancellationToken ct=default)
        => Ejecutar("ContextoComprobantes",async(cn,token)=>
        {
            using var r=await cn.QueryMultipleAsync(new CommandDefinition("""
                DECLARE @cliente nvarchar(30),@fecha date,@match bigint;
                SELECT TOP 1 @cliente=p.ClienteCodigo,@match=p.MatchId,@fecha=m.FechaMovimiento
                FROM dbo.BK_ExtractoMovimiento m JOIN dbo.BK_MovimientoMatch p ON p.MovimientoId=m.MovimientoId
                WHERE m.MovimientoId=@movimientoId AND p.EstadoPropuesta<>'RECLASIFICADO' ORDER BY p.MatchId DESC;
                SELECT TOP (40) f.ID Id,LTRIM(RTRIM(f.TC)) Tc,f.IDCOMPROBANTE Comprobante,f.NOMBRE Nombre,
                    ISNULL(f.DOCUMENTONUMERO,'') Cuit,LTRIM(RTRIM(f.UNEGOCIO)) Unidad,f.FECHA Fecha,
                    COALESCE(NULLIF(LTRIM(RTRIM(mon.DESCRIPCION)),''),moneda.Codigo,'Sin informar') Moneda,
                    saldo.Vencimiento,CONVERT(decimal(18,2),ISNULL(f.IMPORTE,0)) Importe,saldo.SaldoActual,
                    ISNULL(f.COMENTARIOS,'') Comentarios,ISNULL(CONVERT(nvarchar(max),f.OBSERVACIONES),'') Observaciones,
                    CONVERT(bit,ISNULL(f.ANULADA,0)) Anulada,
                    CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_MovimientoAplicacion a WHERE a.MatchId=@match
                        AND a.TipoComprobante=LTRIM(RTRIM(f.TC)) AND RIGHT('0000'+CONVERT(varchar(12),a.CentroEmisor),4)=f.SUCURSAL
                        AND RIGHT('00000000'+CONVERT(varchar(20),a.NumeroComprobante),8)=f.NUMERO AND a.Letra=f.LETRA) THEN 1 ELSE 0 END) Propuesto
                INTO #Comparar FROM dbo.V_MV_CPTE f
                OUTER APPLY(SELECT COALESCE(NULLIF(LTRIM(RTRIM(f.MONEDA)),''),
                    CASE WHEN COUNT(DISTINCT NULLIF(LTRIM(RTRIM(a.MONEDA)),''))=1 THEN MIN(NULLIF(LTRIM(RTRIM(a.MONEDA)),'')) END) Codigo
                    FROM dbo.MV_ASIENTOS a WHERE LTRIM(RTRIM(a.CUENTA))=LTRIM(RTRIM(f.CUENTA)) AND a.TC=f.TC
                        AND a.SUCURSAL=f.SUCURSAL AND a.NUMERO=f.NUMERO AND a.LETRA=f.LETRA) moneda
                LEFT JOIN dbo.TA_MONEDAS mon ON LTRIM(RTRIM(mon.CODIGO))=moneda.Codigo
                OUTER APPLY(SELECT SUM(CONVERT(decimal(18,2),s.SALDO)) SaldoActual,MIN(s.VENCIMIENTO) Vencimiento FROM dbo.VE_CPTES_SALDOS_VENTAS s
                    WHERE LTRIM(RTRIM(s.CUENTA))=LTRIM(RTRIM(f.CUENTA)) AND s.TC=f.TC AND s.SUCURSAL=f.SUCURSAL AND s.NUMERO=f.NUMERO AND s.LETRA=f.LETRA) saldo
                WHERE LTRIM(RTRIM(f.CUENTA))=LTRIM(RTRIM(@cliente)) AND LTRIM(RTRIM(f.TC)) IN ('FC','FP','ND') AND f.IDCOMPLEMENTO=0
                    AND EXISTS(SELECT 1 FROM dbo.TA_USUARIOS u WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario)
                        AND (ISNULL(u.AdminCentral,0)=1 OR LTRIM(RTRIM(u.UNEGOCIO))=LTRIM(RTRIM(f.UNEGOCIO))))
                ORDER BY Propuesto DESC,ABS(DATEDIFF(day,f.FECHA,@fecha)),f.ID DESC;
                SELECT d.*,ISNULL(LEFT(STUFF((SELECT TOP(5) N' · '+LTRIM(RTRIM(i.DESCRIPCION)) FROM dbo.V_MV_CPTEINSUMOS i
                    WHERE i.TC=d.Tc AND i.IDCOMPROBANTE=d.Comprobante AND i.IDCOMPLEMENTO=0 ORDER BY i.SECUENCIA,i.ID
                    FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,3,''),500),'') Conceptos
                FROM #Comparar d ORDER BY d.Fecha DESC,d.Id DESC;
                SELECT DISTINCT d.Id FacturaId,LTRIM(RTRIM(a.TCO_ORIGEN)) Tc,a.IDCOMPROBANTE_ORIGEN Comprobante,
                    o.FECHA Fecha,ISNULL(o.COMENTARIOS,'') Comentarios
                FROM #Comparar d JOIN dbo.MV_APLICACION a ON a.TC=d.Tc AND a.IDCOMPROBANTE=d.Comprobante
                LEFT JOIN dbo.V_MV_CPTE o ON o.TC=a.TCO_ORIGEN AND o.IDCOMPROBANTE=a.IDCOMPROBANTE_ORIGEN AND o.IDCOMPLEMENTO=a.IDCOMPLEMENTO_ORIGEN
                WHERE LTRIM(RTRIM(a.TCO_ORIGEN)) IN ('NP','RM') ORDER BY d.Id,Tc,Comprobante;
                """,new{movimientoId,usuario=users.GetCurrentUserName()},commandTimeout:120,cancellationToken:token));
            var docs=(await r.ReadAsync<ConciliacionComprobanteInfo>()).AsList();
            var origenes=(await r.ReadAsync<ConciliacionOrigenInfo>()).AsList();
            return new ConciliacionContextoComprobantes(docs,origenes);
        },ct);

    private sealed class RevisionActual
    {
        public long ImportacionId { get; set; }
        public string Estado { get; set; }="";
        public string Categoria { get; set; }="";
    }
}
