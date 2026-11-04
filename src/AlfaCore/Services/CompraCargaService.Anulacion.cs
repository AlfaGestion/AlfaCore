using System.Data;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    public Task AnularCompraAsync(int compra,string motivo,Guid sesion,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"AnularCompra",async(c,t)=>
        {
            using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var actor=usuarios.GetCurrentUserName();var sistema=usuarios.CurrentUser?.SystemCode;
            await AnularEnTransaccionAsync(c,tx,compra,motivo,sesion,t);
            if(sesiones.GetActiveSession()?.Id!=sesion||usuarios.GetCurrentUserName()!=actor||usuarios.CurrentUser?.SystemCode!=sistema)throw new OperationCanceledException("La sesión cambió.");
            t.ThrowIfCancellationRequested();tx.Commit();return true;
        },ct);

    internal async Task AnularEnTransaccionAsync(SqlConnection c,SqlTransaction tx,int compra,string motivo,Guid sesion,CancellationToken ct)
    {
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out var actual)||actual!=sesion||!await permisos.HasAccessAsync(MenuKey,ct))throw new InvalidOperationException("La sesión no está autorizada para anular compras.");
        if(string.IsNullOrWhiteSpace(motivo)||motivo.Trim().Length<5||motivo.Length>500)throw new InvalidOperationException("Escribí un motivo de entre 5 y 500 caracteres.");
        CommandDefinition Q(string sql,object? args=null)=>Cmd(sql,args,ct,tx);
        if(await c.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.ALFACORE_COMPRA_OPERACION') AND name='AsientoHuella';"))==0)
            throw new InvalidOperationException("Aplicá las actualizaciones de Compras antes de anular desde la web.");
        var bloqueo=await c.ExecuteScalarAsync<int>(Q("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'ALFACORE_COMPRAS_REGISTRO',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @r;"));
        if(bloqueo<0)throw new InvalidOperationException("Hay otra compra guardándose. Reintentá en unos segundos.");
        var contexto=await Contexto(c,ct,tx);
        var f=await c.QuerySingleOrDefaultAsync<CompraAnulable>(Q("""
            SELECT f.ID Id,LTRIM(RTRIM(f.TC)) Tipo,f.IDCOMPROBANTE Comprobante,LTRIM(RTRIM(f.CUENTA)) Cuenta,
              f.FECHA Fecha,LTRIM(RTRIM(f.UNEGOCIO)) Unidad,CONVERT(bit,ISNULL(f.ANULADA,0)) Anulada,
              f.IMPORTE TotalCabecera,o.Periodo,o.Mes,o.Asiento,LTRIM(RTRIM(o.TipoRegistro)) TipoRegistro,o.Total,o.AsientoHuella,
              CONVERT(bit,CASE WHEN ISNULL(f.Bloqueado,0)=1 OR ISNULL(f.Cerrada,0)=1 THEN 1 ELSE 0 END) Protegida
            FROM dbo.C_MV_CPTE f WITH(UPDLOCK,HOLDLOCK) JOIN dbo.ALFACORE_COMPRA_OPERACION o WITH(UPDLOCK,HOLDLOCK) ON o.CompraId=f.ID
            WHERE f.ID=@compra;
            """,new{compra}))??throw new InvalidOperationException("Esta operación solo anula compras creadas por el circuito web. Para documentos legacy usá su proceso de anulación.");
        if(!contexto.Unidades.Any(u=>u.Codigo==f.Unidad))throw new InvalidOperationException("La unidad de la compra no está autorizada.");
        if(f.Anulada)return;
        var tipoContable=await c.QuerySingleOrDefaultAsync<string>(Q("""
            SELECT LTRIM(RTRIM(TC)) FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes
              AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro AND SECUENCIA=1 AND LTRIM(RTRIM(CUENTA))=@Cuenta
              AND SUCURSAL+NUMERO+LETRA=@Comprobante AND FECHA=@Fecha AND LTRIM(RTRIM(UNEGOCIO))=@Unidad;
            """,f));
        if(string.IsNullOrWhiteSpace(tipoContable)||f.TotalCabecera!=f.Total)throw new InvalidOperationException("La cabecera comercial cambió o no corresponde al asiento original. Revisá la compra en el legacy.");
        f.TipoContable=tipoContable;
        if(f.Protegida||string.IsNullOrWhiteSpace(f.AsientoHuella))throw new InvalidOperationException("La compra está bloqueada/cerrada o no tiene control del asiento original. Revisala en el legacy.");
        if(contexto.Cierre is {} cierre&&f.Fecha.Date<=cierre.Date||contexto.Desde is {} desde&&f.Fecha.Date<desde.Date||contexto.Hasta is {} hasta&&f.Fecha.Date>hasta.Date)
            throw new InvalidOperationException("La compra está dentro del cierre o fuera del rango operativo.");
        if(await c.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.MV_EJERCICIOS WHERE LTRIM(RTRIM(CONVERT(nvarchar(6),PERIODO)))=@Periodo AND ISNULL(CERRADO,0)=0;",f))!=1)throw new InvalidOperationException("El ejercicio está cerrado o no está disponible.");
        if(await c.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.MV_APLICACION WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(CUENTA))=@Cuenta AND
              ((LTRIM(RTRIM(TC)) IN (@Tipo,@TipoContable) AND (LTRIM(RTRIM(IDComprobante))=@Comprobante OR SUCURSAL+NUMERO+LETRA=@Comprobante)) OR
               (LTRIM(RTRIM(TCO_ORIGEN)) IN (@Tipo,@TipoContable) AND (LTRIM(RTRIM(IDComprobante_Origen))=@Comprobante OR SUCURSAL_ORIGEN+NUMERO_ORIGEN+LETRA_ORIGEN=@Comprobante)));
            """,f))>0)throw new InvalidOperationException("La compra tiene aplicaciones, pagos o documentos asociados. Primero deben resolverse en su circuito.");
        if(await c.ExecuteScalarAsync<int>(Q("""
            SELECT (SELECT COUNT(*) FROM dbo.C_MV_CPTEINSUMOS WHERE TC=@Tipo AND IDCOMPROBANTE=@Comprobante AND LTRIM(RTRIM(CUENTA))=@Cuenta)
              +(SELECT COUNT(*) FROM dbo.V_MV_STOCK WHERE TC=@Tipo AND IDCOMPROBANTE=@Comprobante AND LTRIM(RTRIM(CuentaProveedor))=@Cuenta AND ISNULL(Anulado,0)=0);
            """,f))>0)throw new InvalidOperationException("La compra incluye mercadería: requiere su anulación de stock en el circuito correspondiente.");
        var registro=new CompraCargaRegistrada(f.Id,f.Comprobante,f.Periodo,f.Mes,f.Asiento,f.TipoRegistro,f.Total);
        if(await HuellaAsientoGuardadoAsync(c,tx,registro,ct)!=f.AsientoHuella)throw new InvalidOperationException("El asiento cambió después de registrar la compra. No se modificó; revisalo en contabilidad.");
        if(await c.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes
              AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro
              AND (ISNULL(CONTABILIZADO,0)=1 OR ISNULL(Conciliado,0)=1 OR LTRIM(RTRIM(UNEGOCIO))<>@Unidad);
            """,f))>0)throw new InvalidOperationException("El asiento está contabilizado, conciliado o incluye otra unidad. No puede anularse aquí.");
        var asientos=(await c.QueryAsync(Q("SELECT * FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro ORDER BY SECUENCIA;",f))).ToList();
        var centros=(await c.QueryAsync(Q("SELECT * FROM dbo.MV_ASIENTOSCCOSTO WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(Tipo_Reg))=@TipoRegistro ORDER BY SECUENCIA;",f))).ToList();
        await c.ExecuteAsync(Q("""
            INSERT dbo.ALFACORE_COMPRA_ANULACION(CompraId,Usuario,Motivo,AsientoJson,CentrosJson) VALUES(@compra,@usuario,@motivo,@asientos,@centros);
            """,new{compra,usuario=usuarios.GetCurrentUserName(),motivo=motivo.Trim(),asientos=JsonSerializer.Serialize(asientos),centros=JsonSerializer.Serialize(centros)}));
        // Igual que el legacy de compras: retirar asiento/libro en una única transacción. La cabecera, sus conceptos,
        // el documento recibido y los snapshots de auditoría se conservan para consultar el original anulado.
        await c.ExecuteAsync(Q("""
            DELETE dbo.MV_ASIENTOSCCOSTO WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(Tipo_Reg))=@TipoRegistro;
            DELETE dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro;
            UPDATE dbo.C_MV_CPTE SET ANULADA=1 WHERE ID=@Id;
            """,f));
        await c.ExecuteAsync(Q("""
            INSERT dbo.V_MV_CPTEACCIONES(TC,IDCOMPROBANTE,IDCOMPLEMENTO,TIPO_ACCION,FECHAHORA,USUARIO,PC,SYSTEMUSER,Periodo,Tipo_Reg,Fecha,Mes_Operativo,NUMEROASIENTO,Cuenta,Proceso)
            VALUES(@Tipo,@Comprobante,0,'B',GETDATE(),@usuario,HOST_NAME(),SYSTEM_USER,@Periodo,@TipoRegistro,@Fecha,@Mes,@Asiento,@Cuenta,@proceso);
            """,new{f.Tipo,f.Comprobante,usuario=usuarios.GetCurrentUserName(),f.Periodo,f.TipoRegistro,f.Fecha,f.Mes,f.Asiento,f.Cuenta,proceso="AlfaCore: anulación de compra. "+motivo.Trim()}));
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out actual)||actual!=sesion)throw new OperationCanceledException("La sesión cambió.");
    }
    private sealed class CompraAnulable:CompraConsultaFila
    {
        public string Periodo{get;set;}="";public int Mes{get;set;}public int Asiento{get;set;}public string TipoRegistro{get;set;}="";
        public decimal TotalCabecera{get;set;}public string TipoContable{get;set;}="";public string? AsientoHuella{get;set;}public bool Protegida{get;set;}
    }
}
