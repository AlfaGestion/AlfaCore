using System.Data;
using System.Text.Json;
using System.Xml.Linq;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    // El código de moneda 1 es pesos en el circuito contable existente. El importador
    // actual identifica los extractos como ARS: no convertir ni mezclar otras monedas.
    private const string CompararBaseSql = """
        IF NOT EXISTS(SELECT 1 FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@usuario))
            THROW 50001,'El usuario no está definido en la base activa.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@cuenta
            AND LTRIM(RTRIM(MEDIODEPAGO))='DB' AND LTRIM(RTRIM(MONEDA))='1'
            AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0)
            THROW 50001,'Seleccioná una cuenta bancaria activa en pesos.',1;
        SELECT m.MovimientoId Id,m.FechaMovimiento Fecha,m.DescripcionOriginal Detalle,
            ISNULL(m.ReferenciaExterna,'') Referencia,m.Importe,m.EstadoRevision Registracion,
            CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(CONVERT(char(10),m.FechaMovimiento,126),'|',
                m.Importe,'|',m.MonedaCodigo,'|',i.CuentaBanco,'|',m.DescripcionOriginal,'|',m.ReferenciaExterna)),2) Huella,
            r.ConciliacionId,r.Huella HuellaAnterior
        INTO #BancoBase FROM dbo.BK_ExtractoMovimiento m JOIN dbo.BK_ImportacionExtracto i ON i.ImportacionId=m.ImportacionId
        LEFT JOIN dbo.BK_ConciliacionBanco r ON r.MovimientoId=m.MovimientoId AND r.Activa=1
        WHERE LTRIM(RTRIM(i.CuentaBanco))=@cuenta AND m.MonedaCodigo='ARS'
            AND m.FechaMovimiento>=@desde AND m.FechaMovimiento<DATEADD(day,1,@hasta);
        SELECT *,CONVERT(bit,CASE WHEN HuellaAnterior<>Huella THEN 1 ELSE 0 END) Alterado INTO #Banco FROM #BancoBase;
        SELECT a.PERIODO Periodo,a.MES_OPERATIVO Mes,a.[NUMERO ASIENTO] Asiento,a.TIPO_REG TipoRegistro,a.SECUENCIA Secuencia,
            a.FECHA Fecha,ISNULL(a.DETALLE,'') Detalle,
            CONCAT(LTRIM(RTRIM(a.TC)),' ',a.SUCURSAL,'-',a.NUMERO,' ',a.LETRA) Comprobante,
            ISNULL(a.NroComprobanteBancario,'') Referencia,ISNULL(LTRIM(RTRIM(a.UNEGOCIO)),'') Unidad,
            LTRIM(RTRIM(a.MONEDA)) Moneda,
            CONVERT(decimal(19,4),CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE ELSE -a.IMPORTE END) Importe,
            CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(CONVERT(varchar(23),a.FECHA,126),'|',a.[DEBE-HABER],'|',
                a.IMPORTE,'|',a.MONEDA,'|',a.UNEGOCIO,'|',a.TC,'|',a.SUCURSAL,'|',a.NUMERO,'|',a.LETRA,'|',a.DETALLE,'|',a.NroComprobanteBancario)),2) Huella,
            r.ConciliacionId,r.Huella HuellaAnterior,CONVERT(bit,ISNULL(a.Conciliado,0)) Marca,
            CONVERT(bit,CASE WHEN ISNULL(a.Conciliado,0)=1 OR EXISTS(SELECT 1 FROM dbo.MV_CONCILIACION l
                WHERE LTRIM(RTRIM(l.Cuenta))=@cuenta AND l.Periodo=a.PERIODO AND l.Mes_Operativo=a.MES_OPERATIVO
                    AND l.[Numero Asiento]=a.[NUMERO ASIENTO] AND l.Tipo_Reg=a.TIPO_REG AND l.Secuencia=a.SECUENCIA
                    AND l.Conciliado=1) THEN 1 ELSE 0 END) Legacy
        INTO #AlfaBase FROM dbo.MV_ASIENTOS a
        LEFT JOIN dbo.BK_ConciliacionAlfa r ON r.Cuenta=@cuenta AND r.Periodo=a.PERIODO AND r.Mes=a.MES_OPERATIVO
            AND r.Asiento=a.[NUMERO ASIENTO] AND r.TipoRegistro=a.TIPO_REG AND r.Secuencia=a.SECUENCIA AND r.Activa=1
        WHERE LTRIM(RTRIM(a.CUENTA))=@cuenta AND a.EsResumen=0 AND a.ES_SALDO_APERTURA=0 AND ISNULL(a.EsCierre,0)=0
            AND a.FECHA>=@desde AND a.FECHA<DATEADD(day,1,@hasta) AND a.[DEBE-HABER] IN ('D','H')
            AND EXISTS(SELECT 1 FROM dbo.TA_USUARIOS u WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario)
                AND (ISNULL(u.AdminCentral,0)=1 OR (NULLIF(LTRIM(RTRIM(u.UNEGOCIO)),'') IS NOT NULL
                    AND LTRIM(RTRIM(u.UNEGOCIO))=LTRIM(RTRIM(a.UNEGOCIO)))));
        SELECT *,CONVERT(bit,CASE WHEN ConciliacionId IS NOT NULL AND (HuellaAnterior<>Huella OR Marca=0) THEN 1 ELSE 0 END) Alterado
        INTO #Alfa FROM #AlfaBase;
        """;

    private static void ValidarComparacion(ConciliacionCompararFiltro f)
    {
        if(string.IsNullOrWhiteSpace(f.Cuenta)||f.Cuenta.Trim().Length>15 || f.Desde.Date>f.Hasta.Date
            || (f.Hasta.Date-f.Desde.Date).TotalDays>366 || f.Hasta.Date>=DateTime.MaxValue.Date)
            throw new InvalidOperationException("Elegí una cuenta y un período de hasta 367 días.");
        if(f.Estado is not ("Pendientes" or "Conciliados" or "Todos") || f.Buscar.Length>150 || f.PaginaAlfa<1 || f.PaginaBanco<1)
            throw new InvalidOperationException("Revisá los filtros y la página de comparación.");
    }
    private object ArgumentosComparacion(ConciliacionCompararFiltro f) => new {
        cuenta=f.Cuenta.Trim(),desde=f.Desde.Date,hasta=f.Hasta.Date,usuario=users.GetCurrentUserName(),
        estado=f.Estado,texto=f.Buscar.Trim(),patron="%"+f.Buscar.Trim().Replace("[","[[]").Replace("%","[%]").Replace("_","[_]")+"%",
        skipBanco=checked((f.PaginaBanco-1)*50),skipAlfa=checked((f.PaginaAlfa-1)*50) };

    public Task<ConciliacionComparacion> CompararAsync(ConciliacionCompararFiltro filtro,CancellationToken ct=default)
        => Ejecutar("CompararBanco",(cn,token)=>CompararEnConexionAsync(cn,null,filtro,token),ct);

    internal async Task<ConciliacionComparacion> CompararEnConexionAsync(SqlConnection cn,SqlTransaction? tx,
        ConciliacionCompararFiltro filtro,CancellationToken token=default)
        {
            ValidarComparacion(filtro);
            var valor=await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
                SELECT COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux))
                FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-DIAS-BANCO';
                """,transaction:tx,cancellationToken:token));
            if(!int.TryParse(valor,out var dias)||dias is <0 or >31)
                throw new InvalidOperationException("Revisá CONCILIACION-DIAS-BANCO (0 a 31).");
            var parametros=new DynamicParameters(ArgumentosComparacion(filtro));parametros.Add("dias",dias);
            using var r=await cn.QueryMultipleAsync(new CommandDefinition(CompararBaseSql+"""
                SELECT * INTO #FB FROM #Banco WHERE (@estado='Todos' OR (@estado='Pendientes' AND ConciliacionId IS NULL)
                    OR (@estado='Conciliados' AND ConciliacionId IS NOT NULL))
                    AND (@texto='' OR CONCAT(Detalle,' ',Referencia,' ',Importe,' ',CONVERT(char(10),Fecha,103)) LIKE @patron);
                SELECT * INTO #FA FROM #Alfa WHERE (@estado='Todos' OR (@estado='Pendientes' AND ConciliacionId IS NULL AND Legacy=0)
                    OR (@estado='Conciliados' AND (ConciliacionId IS NOT NULL OR Legacy=1)))
                    AND (@texto='' OR CONCAT(Detalle,' ',Referencia,' ',Comprobante,' ',Importe,' ',CONVERT(char(10),Fecha,103)) LIKE @patron);
                SELECT (SELECT COUNT(*) FROM #FB) Banco,(SELECT COUNT(*) FROM #FA) Alfa,
                    ISNULL((SELECT SUM(Importe) FROM #Banco),0) TotalBanco,ISNULL((SELECT SUM(Importe) FROM #Alfa WHERE Moneda='1'),0) TotalAlfa,@dias Dias;
                SELECT * INTO #PaginaBanco FROM #FB ORDER BY Fecha DESC,Id DESC OFFSET @skipBanco ROWS FETCH NEXT 50 ROWS ONLY;
                SELECT * FROM #PaginaBanco ORDER BY Fecha DESC,Id DESC;
                SELECT * FROM #FA ORDER BY Fecha DESC,Periodo DESC,Mes DESC,Asiento DESC,TipoRegistro,Secuencia
                    OFFSET @skipAlfa ROWS FETCH NEXT 50 ROWS ONLY;
                SELECT b.Id MovimientoId,s.* FROM #PaginaBanco b CROSS APPLY(SELECT TOP(1) a.*,COUNT(*) OVER() Candidatos
                    FROM #Alfa a WHERE a.ConciliacionId IS NULL AND a.Legacy=0 AND a.Moneda='1'
                        AND a.Importe=b.Importe AND b.Importe<>0 AND ABS(DATEDIFF(day,a.Fecha,b.Fecha))<=@dias
                    ORDER BY CASE WHEN NULLIF(b.Referencia,'') IS NOT NULL AND a.Referencia=b.Referencia THEN 0 ELSE 1 END,
                        ABS(DATEDIFF(day,a.Fecha,b.Fecha)),a.Fecha,a.Periodo,a.Mes,a.Asiento,a.TipoRegistro,a.Secuencia)s
                    WHERE b.ConciliacionId IS NULL;
                """,parametros,tx,commandTimeout:120,cancellationToken:token));
            var resumen=await r.ReadSingleAsync<ConciliacionCompararResumen>();
            var banco=(await r.ReadAsync<ConciliacionFilaBanco>()).AsList();var alfa=(await r.ReadAsync<ConciliacionFilaAlfa>()).AsList();
            var candidatos=(await r.ReadAsync<ConciliacionFilaAlfa>()).Select(a=>new ConciliacionSugerencia(a.MovimientoId,a,a.Candidatos)).ToList();
            return new ConciliacionComparacion(resumen,banco,alfa,candidatos);
        }

    public Task<long> ConciliarAsync(ConciliacionVincularSolicitud solicitud,CancellationToken ct=default)
        => Ejecutar("Conciliar",async(cn,token)=>
        {
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var id=await ConciliarEnTransaccionAsync(cn,tx,solicitud,token);
            tx.Commit();return id;
        },ct);

    internal async Task<long> ConciliarEnTransaccionAsync(SqlConnection cn,SqlTransaction tx,
        ConciliacionVincularSolicitud solicitud,CancellationToken token=default)
        {
            ValidarComparacion(solicitud.Filtro);
            if(solicitud.Operacion==Guid.Empty || solicitud.Banco.Count is <1 or >100 || solicitud.Alfa.Count is <1 or >100
                || solicitud.Banco.Select(b=>b.Id).Distinct().Count()!=solicitud.Banco.Count
                || solicitud.Alfa.Select(a=>a.Clave).Distinct().Count()!=solicitud.Alfa.Count)
                throw new InvalidOperationException("Seleccioná de 1 a 100 movimientos distintos de cada lado.");
            var huella=Hash(JsonSerializer.Serialize(new { Cuenta=solicitud.Filtro.Cuenta.Trim(),Desde=solicitud.Filtro.Desde.Date,Hasta=solicitud.Filtro.Hasta.Date,
                Banco=solicitud.Banco.OrderBy(b=>b.Id),Alfa=solicitud.Alfa.OrderBy(a=>JsonSerializer.Serialize(a.Clave),StringComparer.Ordinal) }));
            var session=sessions.GetActiveSession()!.Id;
            CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:token);
            await BloquearCuenta(cn,tx,solicitud.Filtro.Cuenta.Trim(),token);
            var anterior=await cn.QuerySingleOrDefaultAsync<dynamic>(Q("SELECT Id,Huella,Activa FROM dbo.BK_Conciliacion WITH(UPDLOCK,HOLDLOCK) WHERE Operacion=@Operacion;",new{solicitud.Operacion}));
            if(anterior is not null)
            {
                if((string)anterior.Huella!=huella || !(bool)anterior.Activa)
                    throw new InvalidOperationException("La operación ya fue utilizada. Actualizá la comparación.");
                await ValidarVinculoUsuario(cn,tx,(long)anterior.Id,token);
                VerificarSesion(session);token.ThrowIfCancellationRequested();return (long)anterior.Id;
            }
            var args=new DynamicParameters(ArgumentosComparacion(solicitud.Filtro));args.Add("ids",solicitud.Banco.Select(b=>b.Id).ToArray());
            args.Add("seleccion",new XElement("Seleccion",solicitud.Alfa.Select(a=>new XElement("Asiento",
                new XAttribute("Periodo",a.Clave.Periodo),new XAttribute("Mes",a.Clave.Mes),new XAttribute("Asiento",a.Clave.Asiento),
                new XAttribute("TipoRegistro",a.Clave.TipoRegistro),new XAttribute("Secuencia",a.Clave.Secuencia)))).ToString(SaveOptions.DisableFormatting));
            using var r=await cn.QueryMultipleAsync(Q(CompararBaseSql+"""
                SELECT * FROM #Banco WHERE Id IN @ids;
                DECLARE @xml xml=CONVERT(xml,@seleccion);
                SELECT a.* FROM #Alfa a JOIN @xml.nodes('/Seleccion/Asiento') s(n)
                    ON a.Periodo=s.n.value('@Periodo','nvarchar(6)') AND a.Mes=s.n.value('@Mes','tinyint')
                    AND a.Asiento=s.n.value('@Asiento','int') AND a.TipoRegistro=s.n.value('@TipoRegistro','nvarchar(4)')
                    AND a.Secuencia=s.n.value('@Secuencia','int');
                """,args));
            var banco=(await r.ReadAsync<ConciliacionFilaBanco>()).AsList();var alfa=(await r.ReadAsync<ConciliacionFilaAlfa>()).AsList();
            if(banco.Count!=solicitud.Banco.Count||alfa.Count!=solicitud.Alfa.Count
                || banco.Any(b=>b.ConciliacionId.HasValue||b.Huella!=solicitud.Banco.Single(s=>s.Id==b.Id).Huella)
                || alfa.Any(a=>a.ConciliacionId.HasValue||a.Legacy||a.Moneda!="1"||a.Huella!=solicitud.Alfa.Single(s=>s.Clave==a.Clave).Huella))
                throw new InvalidOperationException("Los movimientos cambiaron, están conciliados o no están autorizados. Actualizá antes de conciliar.");
            var total=banco.Sum(b=>b.Importe);
            if(total==0||alfa.Sum(a=>a.Importe)!=total||banco.Any(b=>Math.Sign(b.Importe)!=Math.Sign(total))||alfa.Any(a=>Math.Sign(a.Importe)!=Math.Sign(total)))
                throw new InvalidOperationException("Los totales deben coincidir exactamente y todos los movimientos deben tener el mismo sentido. No se generan ajustes automáticos.");
            var cuenta=solicitud.Filtro.Cuenta.Trim();var usuario=users.GetCurrentUserName();
            var id=await cn.ExecuteScalarAsync<long>(Q("""
                INSERT dbo.BK_Conciliacion(Operacion,Huella,Cuenta,Moneda,Importe,Usuario)
                OUTPUT INSERTED.Id VALUES(@Operacion,@huella,@cuenta,'1',@total,@usuario);
                """,new{solicitud.Operacion,huella,cuenta,total,usuario}));
            foreach(var b in banco) await cn.ExecuteAsync(Q("""
                INSERT dbo.BK_ConciliacionBanco(ConciliacionId,MovimientoId,Fecha,Importe,Detalle,Huella)
                VALUES(@vinculo,@movimiento,@Fecha,@Importe,@Detalle,@Huella);
                """,new{vinculo=id,movimiento=b.Id,b.Fecha,b.Importe,b.Detalle,b.Huella}));
            foreach(var a in alfa)
            {
                var p=new DynamicParameters(a);p.Add("id",id);p.Add("cuenta",cuenta);
                await cn.ExecuteAsync(Q("""
                    INSERT dbo.BK_ConciliacionAlfa(ConciliacionId,Cuenta,Periodo,Mes,Asiento,TipoRegistro,Secuencia,Fecha,Importe,Detalle,Comprobante,Unidad,Huella)
                    VALUES(@id,@cuenta,@Periodo,@Mes,@Asiento,@TipoRegistro,@Secuencia,@Fecha,@Importe,@Detalle,@Comprobante,@Unidad,@Huella);
                    UPDATE dbo.MV_ASIENTOS SET Conciliado=1 WHERE LTRIM(RTRIM(CUENTA))=@cuenta AND PERIODO=@Periodo
                        AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND TIPO_REG=@TipoRegistro AND SECUENCIA=@Secuencia;
                    """,p));
            }
            VerificarSesion(session);token.ThrowIfCancellationRequested();return id;
        }

    private static async Task BloquearCuenta(SqlConnection cn,SqlTransaction tx,string cuenta,CancellationToken ct)
    {
        var result=await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @r;
            """,new{recurso="ALFACORE_CONCILIACION:"+cuenta.ToUpperInvariant()},tx,cancellationToken:ct));
        if(result<0) throw new InvalidOperationException("Otra conciliación está utilizando esta cuenta. Reintentá en unos segundos.");
    }
    private async Task ValidarVinculoUsuario(SqlConnection cn,SqlTransaction? tx,long id,CancellationToken ct)
    {
        var permitido=await cn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_Conciliacion WHERE Id=@id)
                AND EXISTS(SELECT 1 FROM dbo.TA_USUARIOS u WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario)
                    AND (ISNULL(u.AdminCentral,0)=1 OR (NULLIF(LTRIM(RTRIM(u.UNEGOCIO)),'') IS NOT NULL
                        AND NOT EXISTS(SELECT 1 FROM dbo.BK_ConciliacionAlfa a
                        WHERE a.ConciliacionId=@id AND a.Unidad<>LTRIM(RTRIM(u.UNEGOCIO)))))) THEN 1 ELSE 0 END);
            """,new{id,usuario=users.GetCurrentUserName()},tx,cancellationToken:ct));
        if(!permitido) throw new InvalidOperationException("El vínculo no está disponible para el usuario actual.");
    }

    public Task<ConciliacionVinculoDetalle> VinculoAsync(long id,CancellationToken ct=default)
        => Ejecutar("ConsultarVinculo",async(cn,token)=>
        {
            await ValidarVinculoUsuario(cn,null,id,token);
            using var r=await cn.QueryMultipleAsync(new CommandDefinition("""
                SELECT * FROM dbo.BK_Conciliacion WHERE Id=@id;
                SELECT MovimientoId Id,Fecha,Importe,Detalle,Huella,ConciliacionId FROM dbo.BK_ConciliacionBanco WHERE ConciliacionId=@id ORDER BY Fecha,Id;
                SELECT * FROM dbo.BK_ConciliacionAlfa WHERE ConciliacionId=@id ORDER BY Fecha,Id;
                """,new{id},cancellationToken:token));
            return new ConciliacionVinculoDetalle(await r.ReadSingleAsync<ConciliacionVinculoCabecera>(),
                (await r.ReadAsync<ConciliacionFilaBanco>()).AsList(),(await r.ReadAsync<ConciliacionFilaAlfa>()).AsList());
        },ct);

    public Task<IReadOnlyList<ConciliacionVinculoCabecera>> HistorialBancoAsync(long movimientoId,CancellationToken ct=default)
        => Ejecutar<IReadOnlyList<ConciliacionVinculoCabecera>>("HistorialConciliacion",async(cn,token)=>
            (await cn.QueryAsync<ConciliacionVinculoCabecera>(new CommandDefinition("""
                SELECT c.* FROM dbo.BK_Conciliacion c JOIN dbo.BK_ConciliacionBanco b ON b.ConciliacionId=c.Id
                WHERE b.MovimientoId=@movimientoId AND EXISTS(SELECT 1 FROM dbo.TA_USUARIOS u
                    WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario) AND (ISNULL(u.AdminCentral,0)=1 OR
                        (NULLIF(LTRIM(RTRIM(u.UNEGOCIO)),'') IS NOT NULL AND
                        NOT EXISTS(SELECT 1 FROM dbo.BK_ConciliacionAlfa a WHERE a.ConciliacionId=c.Id
                            AND a.Unidad<>LTRIM(RTRIM(u.UNEGOCIO)))))) ORDER BY c.Id DESC;
                """,new{movimientoId,usuario=users.GetCurrentUserName()},cancellationToken:token))).AsList(),ct);

    public Task<bool> DesconciliarAsync(long id,string motivo,CancellationToken ct=default)
        => Ejecutar("Desconciliar",async(cn,token)=>
        {
            if(string.IsNullOrWhiteSpace(motivo)||motivo.Trim().Length is <3 or >300)
                throw new InvalidOperationException("Indicá el motivo de la desconciliación (3 a 300 caracteres).");
            var session=sessions.GetActiveSession()!.Id;
            var cab=await cn.QuerySingleOrDefaultAsync<ConciliacionVinculoCabecera>(new CommandDefinition("SELECT * FROM dbo.BK_Conciliacion WHERE Id=@id;",new{id},cancellationToken:token))
                ?? throw new InvalidOperationException("El vínculo ya no está disponible.");
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var result=await DesconciliarEnTransaccionAsync(cn,tx,id,cab.Cuenta,motivo,token);
            tx.Commit();return result;
        },ct);

    internal async Task<bool> DesconciliarEnTransaccionAsync(SqlConnection cn,SqlTransaction tx,long id,
        string cuenta,string motivo,CancellationToken token=default)
        {
            if(string.IsNullOrWhiteSpace(motivo)||motivo.Trim().Length is <3 or >300)
                throw new InvalidOperationException("Indicá el motivo de la desconciliación (3 a 300 caracteres).");
            var session=sessions.GetActiveSession()!.Id;
            await BloquearCuenta(cn,tx,cuenta,token);
            var cab=await cn.QuerySingleOrDefaultAsync<ConciliacionVinculoCabecera>(new CommandDefinition(
                "SELECT * FROM dbo.BK_Conciliacion WHERE Id=@id AND Cuenta=@cuenta;",new{id,cuenta},tx,cancellationToken:token))
                ?? throw new InvalidOperationException("El vínculo ya no está disponible.");
            await ValidarVinculoUsuario(cn,tx,id,token);
            // Releer después del bloqueo, incluyendo anulaciones concurrentes.
            if(!await cn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT Activa FROM dbo.BK_Conciliacion WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id;",new{id},tx,cancellationToken:token))) return false;
            var fechas=await cn.QuerySingleAsync<(DateTime Desde,DateTime Hasta)>(new CommandDefinition("""
                SELECT CONVERT(date,MIN(Fecha)) Desde,CONVERT(date,MAX(Fecha)) Hasta FROM dbo.BK_ConciliacionAlfa WHERE ConciliacionId=@id;
                """,new{id},tx,cancellationToken:token));
            var f=new ConciliacionCompararFiltro(cab.Cuenta,fechas.Desde,fechas.Hasta);
            var args=new DynamicParameters(ArgumentosComparacion(f));args.Add("id",id);
            var conflicto=await cn.ExecuteScalarAsync<bool>(new CommandDefinition(CompararBaseSql+"""
                SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_ConciliacionAlfa r LEFT JOIN #Alfa a
                    ON a.Periodo=r.Periodo AND a.Mes=r.Mes AND a.Asiento=r.Asiento AND a.TipoRegistro=r.TipoRegistro AND a.Secuencia=r.Secuencia
                    WHERE r.ConciliacionId=@id AND (a.Asiento IS NULL OR a.Alterado=1))
                    OR EXISTS(SELECT 1 FROM dbo.BK_ConciliacionAlfa r JOIN dbo.MV_CONCILIACION l ON LTRIM(RTRIM(l.Cuenta))=r.Cuenta
                        AND l.Periodo=r.Periodo AND l.Mes_Operativo=r.Mes AND l.[Numero Asiento]=r.Asiento AND l.Tipo_Reg=r.TipoRegistro AND l.Secuencia=r.Secuencia
                        WHERE r.ConciliacionId=@id AND l.Conciliado=1)
                    THEN 1 ELSE 0 END);
                """,args,tx,commandTimeout:120,cancellationToken:token));
            if(conflicto) throw new InvalidOperationException("El asiento cambió o tiene una conciliación en Alfa Gestión. Revisalo antes de quitar la marca.");
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE a SET Conciliado=0 FROM dbo.MV_ASIENTOS a JOIN dbo.BK_ConciliacionAlfa r ON LTRIM(RTRIM(a.CUENTA))=r.Cuenta
                    AND a.PERIODO=r.Periodo AND a.MES_OPERATIVO=r.Mes AND a.[NUMERO ASIENTO]=r.Asiento AND a.TIPO_REG=r.TipoRegistro AND a.SECUENCIA=r.Secuencia
                    WHERE r.ConciliacionId=@id;
                UPDATE dbo.BK_ConciliacionBanco SET Activa=0 WHERE ConciliacionId=@id;
                UPDATE dbo.BK_ConciliacionAlfa SET Activa=0 WHERE ConciliacionId=@id;
                UPDATE dbo.BK_Conciliacion SET Activa=0,Motivo=@motivo,UsuarioAnulacion=@usuario,FechaAnulacionUtc=SYSUTCDATETIME() WHERE Id=@id;
                """,new{id,motivo=motivo.Trim(),usuario=users.GetCurrentUserName()},tx,cancellationToken:token));
            VerificarSesion(session);token.ThrowIfCancellationRequested();return true;
        }
}
