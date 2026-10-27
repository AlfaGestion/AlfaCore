using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Motor compartido de recibos: la UI y la importación no escriben contabilidad.
public sealed class CobranzasService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events)
{
    private sealed class Usuario
    {
        public string Nombre { get; set; } = "";
        public string Unidad { get; set; } = "";
        public bool Admin { get; set; }
        public string Caja { get; set; } = "";
    }
    private sealed class Movimiento
    {
        public long Id { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Importe { get; set; }
        public string Cuenta { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Moneda { get; set; } = "";
        public string Estado { get; set; } = "";
        public string Categoria { get; set; } = "";
        public string TipoMatch { get; set; } = "";
    }

    private async Task<T> Ejecutar<T>(string accion, Func<SqlConnection,Usuario,Guid,CancellationToken,Task<T>> work, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var session))
            throw new InvalidOperationException("Iniciá sesión en la base donde querés registrar la cobranza.");
        var connection=sessions.GetConnectionString();
        try
        {
            if(!await permissions.HasAccessAsync(ConciliacionBancariaService.MenuKey,ct))
                throw new InvalidOperationException("No tenés permiso para registrar cobranzas desde Conciliación.");
            VerificarSesion(session);
            await using var cn=new SqlConnection(connection);
            await cn.OpenAsync(ct);
            var usuario=await cn.QuerySingleOrDefaultAsync<Usuario>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(NOMBRE)) Nombre,LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,CONVERT(bit,ISNULL(AdminCentral,0)) Admin,
                    LTRIM(RTRIM(ISNULL(IdCaja,''))) Caja FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@usuario);
                """,new { usuario=users.GetCurrentUserName() },cancellationToken:ct))
                ?? throw new InvalidOperationException("El usuario actual no está definido en la base activa.");
            return await work(cn,usuario,session,ct);
        }
        catch(OperationCanceledException) { throw; }
        catch(AppUserFacingException) { throw; }
        catch(Exception ex)
        {
            var mensaje=ex is InvalidOperationException ? ex.Message : "No se pudo registrar la cobranza. Conservamos los datos para reintentar.";
            var code=await events.LogErrorAsync("Cobranzas",accion,ex,mensaje,ct:ct);
            throw new AppUserFacingException(mensaje,code,ex);
        }
    }
    private void VerificarSesion(Guid session)
    {
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var actual)||actual!=session)
            throw new OperationCanceledException("La sesión cambió. Abrí nuevamente la cobranza en la base activa.");
    }
    private static void ValidarUnidad(Usuario user,string unidad)
    {
        if(string.IsNullOrWhiteSpace(unidad)||(!user.Admin&&user.Unidad!=unidad))
            throw new InvalidOperationException("La factura no tiene una unidad válida o no está autorizada para el usuario.");
    }
    private static CommandDefinition Cmd(string sql,object? args,SqlTransaction? tx,CancellationToken ct)
        => new(sql,args,tx,commandTimeout:120,cancellationToken:ct);

    private static async Task<Movimiento> LeerMovimiento(SqlConnection cn,long id,SqlTransaction? tx,CancellationToken ct)
    {
        if(await cn.ExecuteScalarAsync<bool>(Cmd("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_ConciliacionBanco WHERE MovimientoId=@id AND Activa=1) THEN 1 ELSE 0 END);",new{id},tx,ct)))
            throw new InvalidOperationException("El movimiento ya está conciliado con una registración de Alfa. Revisá ese vínculo antes de generar una cobranza.");
        var row=await cn.QuerySingleOrDefaultAsync<Movimiento>(Cmd("""
            SELECT m.MovimientoId Id,m.FechaMovimiento Fecha,m.Importe,ISNULL(i.CuentaBanco,'') Cuenta,
                ISNULL(p.ClienteCodigo,'') Cliente,m.MonedaCodigo Moneda,m.EstadoRevision Estado,ISNULL(c.TipoMovimiento,'') Categoria,
                ISNULL(p.TipoMatch,'') TipoMatch
            FROM dbo.BK_ExtractoMovimiento m JOIN dbo.BK_ImportacionExtracto i ON i.ImportacionId=m.ImportacionId
            OUTER APPLY(SELECT TOP 1 TipoMovimiento FROM dbo.BK_MovimientoClasificado WHERE MovimientoId=m.MovimientoId ORDER BY ClasificacionId DESC) c
            OUTER APPLY(SELECT TOP 1 ClienteCodigo,TipoMatch FROM dbo.BK_MovimientoMatch WHERE MovimientoId=m.MovimientoId ORDER BY MatchId DESC) p
            WHERE m.MovimientoId=@id;
            """,new { id },tx,ct)) ?? throw new InvalidOperationException("El movimiento bancario ya no está disponible.");
        if(row.Importe<=0||row.Categoria!="Cobranza"||row.Cliente.Trim().Length==0)
            throw new InvalidOperationException("Para cobrar, seleccioná un ingreso clasificado como Cobranza con un cliente identificado.");
        if(row.Estado=="Cobrado") throw new InvalidOperationException("Este movimiento ya tiene una cobranza registrada.");
        if(row.TipoMatch=="CobranzaYaRegistrada") throw new InvalidOperationException("La propuesta indica una cobranza ya registrada. Revisá el recibo existente antes de generar otro.");
        if(row.Moneda!="ARS") throw new InvalidOperationException("La cobranza rápida admite movimientos en pesos.");
        row.Cuenta=row.Cuenta.Trim(); row.Cliente=row.Cliente.Trim();
        return row;
    }

    private static async Task<IReadOnlyList<CobranzaDocumento>> Documentos(SqlConnection cn,string cliente,SqlTransaction? tx,CancellationToken ct)
        => (await cn.QueryAsync<CobranzaDocumento>(Cmd("""
            SELECT f.ID Id,LTRIM(RTRIM(f.CUENTA)) Cliente,LTRIM(RTRIM(f.NOMBRE)) Nombre,
                LTRIM(RTRIM(f.UNEGOCIO)) Unidad,LTRIM(RTRIM(f.TC)) Tc,f.IDCOMPROBANTE Comprobante,
                COALESCE(NULLIF(LTRIM(RTRIM(f.MONEDA)),''),asiento.Moneda) Moneda,CONVERT(decimal(18,2),s.SALDO) Saldo
            FROM dbo.VE_CPTES_SALDOS_VENTAS s JOIN dbo.V_MV_CPTE f
                ON LTRIM(RTRIM(f.CUENTA))=LTRIM(RTRIM(s.CUENTA)) AND LTRIM(RTRIM(f.TC))=LTRIM(RTRIM(s.TC))
                AND f.SUCURSAL=s.SUCURSAL AND f.NUMERO=s.NUMERO AND f.LETRA=s.LETRA AND f.IDCOMPLEMENTO=0
            OUTER APPLY (
                SELECT CASE WHEN COUNT(DISTINCT NULLIF(LTRIM(RTRIM(a.MONEDA)),''))=1
                    THEN MIN(NULLIF(LTRIM(RTRIM(a.MONEDA)),'')) END Moneda
                FROM dbo.MV_ASIENTOS a WHERE LTRIM(RTRIM(a.CUENTA))=LTRIM(RTRIM(f.CUENTA))
                    AND a.TC=f.TC AND a.SUCURSAL=f.SUCURSAL AND a.NUMERO=f.NUMERO AND a.LETRA=f.LETRA
            ) asiento
            WHERE LTRIM(RTRIM(s.CUENTA))=@cliente AND s.SALDO>0 AND ISNULL(f.ANULADA,0)=0
                AND LTRIM(RTRIM(f.TC)) IN ('FC','FP','ND')
            ORDER BY f.FECHA,f.ID;
            """,new { cliente },tx,ct))).AsList();

    private static async Task<IReadOnlyList<CobranzaMedio>> Medios(SqlConnection cn,SqlTransaction? tx,CancellationToken ct)
        => (await cn.QueryAsync<CobranzaMedio>(Cmd("""
            SELECT LTRIM(RTRIM(CODIGO)) Cuenta,LTRIM(RTRIM(DESCRIPCION)) Descripcion,
                LTRIM(RTRIM(MEDIODEPAGO)) Tipo,LTRIM(RTRIM(MONEDA)) Moneda
            FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO)) IN ('DB','RI','RB','RG','RO')
                AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 ORDER BY DESCRIPCION,CODIGO;
            """,null,tx,ct))).AsList();

    private static async Task<IReadOnlyList<CobranzaTalonario>> Talonarios(SqlConnection cn,string unidad,SqlTransaction? tx,CancellationToken ct)
    {
        var all=(await cn.QueryAsync<CobranzaTalonario>(Cmd("""
            SELECT Talonario,LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,X_SUC_DEFAULT Sucursal,
                X_ULTIMO_NRO Proximo,X_NRO_DESDE Desde,X_NRO_HASTA Hasta,VenceX Vence
            FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CB' AND LTRIM(RTRIM(SISTEMA))='Ventas'
                AND LTRIM(RTRIM(DEBEHABER))='H' AND CHARINDEX('X',LETRAS)>0 AND ISNULL(Externo,0)=0
                AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('','0',@unidad) ORDER BY Talonario;
            """,new { unidad },tx,ct))).AsList();
        return all.Any(x=>x.Unidad==unidad) ? all.Where(x=>x.Unidad==unidad).ToList() : all;
    }

    // Se invoca solo para clientes/unidades autorizados de Documentos. El asiento conserva
    // la última elección confirmada, también para recibos ingresados desde Alfa Gestión.
    internal static Task<CobranzaRetencionSugerida?> UltimaRetencionAsync(SqlConnection cn,string cliente,string unidad,
        SqlTransaction? tx,CancellationToken ct)
        => cn.QueryFirstOrDefaultAsync<CobranzaRetencionSugerida>(Cmd("""
            SELECT TOP 1 @cliente Cliente,@unidad Unidad,LTRIM(RTRIM(r.CUENTA)) Cuenta,
                r.SUCURSAL+r.NUMERO+r.LETRA Comprobante,r.FECHA Fecha
            FROM dbo.MV_ASIENTOS r
            JOIN dbo.MA_CUENTAS c ON c.CODIGO=r.CUENTA
            WHERE LTRIM(RTRIM(r.TC))='CB' AND LTRIM(RTRIM(r.UNEGOCIO))=@unidad
                AND r.[DEBE-HABER]='D' AND r.IMPORTE>0 AND LTRIM(RTRIM(r.MONEDA))='1'
                AND ISNULL(r.EsResumen,0)=0 AND ISNULL(r.ES_SALDO_APERTURA,0)=0 AND ISNULL(r.EsCierre,0)=0
                AND LTRIM(RTRIM(c.MEDIODEPAGO)) IN ('RI','RB','RG','RO')
                AND LTRIM(RTRIM(c.MONEDA))='1' AND ISNULL(c.Dada_De_Baja,0)=0 AND ISNULL(c.TITULO,0)=0
                AND EXISTS (
                    SELECT 1 FROM dbo.MV_ASIENTOS a
                    WHERE a.PERIODO=r.PERIODO AND a.MES_OPERATIVO=r.MES_OPERATIVO
                        AND a.[NUMERO ASIENTO]=r.[NUMERO ASIENTO] AND a.TIPO_REG=r.TIPO_REG
                        AND a.TC=r.TC AND a.SUCURSAL=r.SUCURSAL AND a.NUMERO=r.NUMERO AND a.LETRA=r.LETRA
                        AND LTRIM(RTRIM(a.UNEGOCIO))=@unidad AND LTRIM(RTRIM(a.CUENTA))=@cliente
                        AND a.[DEBE-HABER]='H' AND a.IMPORTE>0 AND ISNULL(a.EsResumen,0)=0
                        AND ISNULL(a.ES_SALDO_APERTURA,0)=0 AND ISNULL(a.EsCierre,0)=0
                )
                AND NOT EXISTS (
                    SELECT 1 FROM dbo.V_MV_CPTE f WHERE f.TC=r.TC AND f.SUCURSAL=r.SUCURSAL
                        AND f.NUMERO=r.NUMERO AND f.LETRA=r.LETRA AND LTRIM(RTRIM(f.CUENTA))=@cliente
                        AND LTRIM(RTRIM(f.UNEGOCIO))=@unidad AND ISNULL(f.ANULADA,0)=1
                )
            ORDER BY COALESCE(r.FechaHora_Grabacion,r.FECHA) DESC,r.FECHA DESC,r.PERIODO DESC,
                r.MES_OPERATIVO DESC,r.[NUMERO ASIENTO] DESC,r.TIPO_REG DESC,r.SECUENCIA DESC,r.CUENTA;
            """,new { cliente,unidad },tx,ct));

    public Task<CobranzaPreparacion> PrepararConciliacionAsync(long movimientoId,CancellationToken ct=default)
        => Ejecutar("Preparar",async(cn,user,session,token)=>
        {
            var mov=await LeerMovimiento(cn,movimientoId,null,token);
            var docs=(await Documentos(cn,mov.Cliente,null,token)).Where(d=>d.Moneda=="1"&&(user.Admin||d.Unidad==user.Unidad)).ToList();
            if(docs.Count==0) throw new InvalidOperationException("El cliente no tiene comprobantes pendientes autorizados en pesos para cobrar.");
            var medios=await Medios(cn,null,token);
            if(!medios.Any(m=>m.Cuenta==mov.Cuenta&&m.Tipo=="DB"))
                throw new InvalidOperationException("La importación no tiene una cuenta bancaria activa. Seleccioná la cuenta al importar.");
            var talonarios=new List<CobranzaTalonario>();
            var retenciones=new List<CobranzaRetencionSugerida>();
            foreach(var unidad in docs.Select(d=>d.Unidad).Distinct())
            {
                talonarios.AddRange(await Talonarios(cn,unidad,null,token));
                if(await UltimaRetencionAsync(cn,mov.Cliente,unidad,null,token) is { } retencion) retenciones.Add(retencion);
            }
            var propuestas=(await cn.QueryAsync<CobranzaAplicacion>(Cmd("""
                SELECT f.ID FacturaId,CONVERT(decimal(18,2),a.ImporteAplicado) Importe
                FROM dbo.BK_MovimientoAplicacion a
                JOIN dbo.V_MV_CPTE f ON LTRIM(RTRIM(f.TC))=a.TipoComprobante AND f.LETRA=a.Letra
                    AND f.SUCURSAL=RIGHT('0000'+CONVERT(varchar(12),a.CentroEmisor),4)
                    AND f.NUMERO=RIGHT('00000000'+CONVERT(varchar(20),a.NumeroComprobante),8)
                    AND LTRIM(RTRIM(f.CUENTA))=@cliente AND f.IDCOMPLEMENTO=0
                WHERE a.MatchId=(SELECT TOP(1) MatchId FROM dbo.BK_MovimientoMatch
                    WHERE MovimientoId=@id AND EstadoPropuesta<>'RECLASIFICADO' ORDER BY MatchId DESC)
                ORDER BY f.ID;
                """,new { id=mov.Id,cliente=mov.Cliente },null,token))).AsList();
            var propuestaVigente=propuestas.Count is >0 and <=50 && propuestas.Select(a=>a.FacturaId).Distinct().Count()==propuestas.Count
                && propuestas.All(a=>a.Importe>0 && docs.Any(d=>d.Id==a.FacturaId && d.Saldo>=a.Importe))
                && docs.Where(d=>propuestas.Any(a=>a.FacturaId==d.Id)).Select(d=>d.Unidad).Distinct().Count()==1;
            var saldoAl=DateTime.Today;
            var saldos=(await cn.QueryAsync<CobranzaSaldoCliente>(Cmd("""
                SELECT LTRIM(RTRIM(a.CUENTA)) Cliente,
                    COALESCE(NULLIF(MAX(LTRIM(RTRIM(mon.DESCRIPCION))),''),NULLIF(LTRIM(RTRIM(a.MONEDA)),''),'Sin informar') Moneda,
                    CONVERT(decimal(19,4),SUM(CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE
                        WHEN a.[DEBE-HABER]='H' THEN -a.IMPORTE ELSE 0 END)) Saldo
                FROM dbo.MV_ASIENTOS a
                LEFT JOIN dbo.TA_MONEDAS mon ON LTRIM(RTRIM(mon.CODIGO))=LTRIM(RTRIM(a.MONEDA))
                WHERE LTRIM(RTRIM(a.CUENTA))=@cliente AND a.EsResumen=0 AND a.FECHA<@hasta
                    AND (@admin=1 OR LTRIM(RTRIM(a.UNEGOCIO))=@unidad)
                GROUP BY LTRIM(RTRIM(a.CUENTA)),LTRIM(RTRIM(a.MONEDA))
                ORDER BY LTRIM(RTRIM(a.MONEDA));
                """,new { cliente=mov.Cliente,hasta=saldoAl.AddDays(1),admin=user.Admin,unidad=user.Unidad },null,token))).AsList();
            VerificarSesion(session);
            return new CobranzaPreparacion { MovimientoId=mov.Id,Fecha=mov.Fecha,CuentaBanco=mov.Cuenta,ImporteBanco=mov.Importe,
                SaldoAl=saldoAl,SaldosCliente=saldos,
                Documentos=docs,Medios=medios,Talonarios=talonarios.DistinctBy(t=>(t.Talonario,t.Unidad)).ToList(),
                RetencionesSugeridas=retenciones,AplicacionesSugeridas=propuestaVigente ? propuestas : [] };
        },ct);

    public static decimal ValidarImportes(CobranzaSolicitud solicitud)
    {
        if(solicitud.Detalle?.Trim().Length>100)
            throw new InvalidOperationException("El detalle de la cobranza admite hasta 100 caracteres.");
        if(solicitud.Operacion==Guid.Empty||solicitud.FacturaId<=0||solicitud.Fecha.TimeOfDay!=TimeSpan.Zero)
            throw new InvalidOperationException("Revisá el comprobante y la fecha de la cobranza.");
        if(solicitud.Pagos.Count is <1 or >20||solicitud.Pagos.Any(p=>string.IsNullOrWhiteSpace(p.Cuenta)||p.Cuenta.Length>15||
            p.Importe<=0||p.Importe!=decimal.Round(p.Importe,2)||p.Certificado.Length>50))
            throw new InvalidOperationException("Informá medios de pago con importes positivos de hasta dos decimales y referencias de hasta 50 caracteres.");
        var total=solicitud.Pagos.Sum(p=>p.Importe);
        if(total>=10000000000000000m) throw new InvalidOperationException("El importe de la cobranza excede el máximo permitido.");
        ResolverAplicaciones(solicitud,total);
        return total;
    }

    internal static IReadOnlyList<CobranzaAplicacion> ResolverAplicaciones(CobranzaSolicitud solicitud,decimal total)
    {
        var aplicaciones=solicitud.Aplicaciones ?? [new(solicitud.FacturaId,total)];
        if(aplicaciones.Count is <1 or >50 || aplicaciones.Any(a=>a.FacturaId<=0 || a.Importe<=0 || a.Importe!=decimal.Round(a.Importe,2))
            || aplicaciones.Select(a=>a.FacturaId).Distinct().Count()!=aplicaciones.Count
            || !aplicaciones.Any(a=>a.FacturaId==solicitud.FacturaId) || aplicaciones.Sum(a=>a.Importe)!=total)
            throw new InvalidOperationException("Seleccioná comprobantes distintos con importes positivos de dos decimales. El total aplicado debe coincidir con banco más retenciones.");
        return aplicaciones;
    }

    public Task<CobranzaResultado> CobrarAsync(CobranzaSolicitud solicitud,CancellationToken ct=default)
        => Ejecutar("Cobrar",async(cn,user,session,token)=>
        {
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var result=await GrabarAsync(cn,tx,solicitud,user,token);
            VerificarSesion(session); token.ThrowIfCancellationRequested();
            if(!string.Equals(user.Nombre,users.GetCurrentUserName(),StringComparison.OrdinalIgnoreCase))
                throw new OperationCanceledException("El usuario cambió durante la cobranza.");
            tx.Commit();
            return result;
        },ct);

    // Se usa también en ensayos con transacción externa y rollback, sin modificar producción.
    internal async Task<CobranzaResultado> GrabarEnTransaccionAsync(SqlConnection cn,SqlTransaction tx,CobranzaSolicitud solicitud,CancellationToken ct)
    {
        var user=await cn.QuerySingleAsync<Usuario>(Cmd("""
            SELECT LTRIM(RTRIM(NOMBRE)) Nombre,LTRIM(RTRIM(UNEGOCIO)) Unidad,CONVERT(bit,ISNULL(AdminCentral,0)) Admin,
                LTRIM(RTRIM(ISNULL(IdCaja,''))) Caja FROM dbo.TA_USUARIOS WHERE LTRIM(RTRIM(NOMBRE))=@usuario;
            """,new { usuario=users.GetCurrentUserName() },tx,ct));
        return await GrabarAsync(cn,tx,solicitud,user,ct);
    }

    private async Task<CobranzaResultado> GrabarAsync(SqlConnection cn,SqlTransaction tx,CobranzaSolicitud solicitud,Usuario user,CancellationToken ct)
    {
        CommandDefinition Q(string sql,object? args=null)=>Cmd(sql,args,tx,ct);
        var total=ValidarImportes(solicitud);
        var aplicaciones=ResolverAplicaciones(solicitud,total);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(solicitud))));
        await cn.ExecuteAsync(Q("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='ALFACORE_COBRANZAS',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
            IF @r<0 THROW 50001,'Hay otra cobranza en proceso. Volvé a intentar.',1;
            """));
        var previo=await cn.QuerySingleOrDefaultAsync<OperacionGuardada>(Q("""
            SELECT Operacion,CobranzaId,Comprobante,NumeroAsiento,Importe,SolicitudHash,Unidad
            FROM dbo.ALFACORE_COBRANZA_OPERACION WITH(UPDLOCK,HOLDLOCK) WHERE Operacion=@Operacion;
            """,new { solicitud.Operacion }));
        if(previo is not null)
        {
            ValidarUnidad(user,previo.Unidad);
            if(previo.SolicitudHash!=hash) throw new InvalidOperationException("Esta operación ya fue confirmada con otros datos. Volvé a abrir el movimiento.");
            return new(previo.Operacion,previo.CobranzaId,previo.Comprobante,previo.NumeroAsiento,previo.Importe);
        }
        if(solicitud.MovimientoId is long movimiento)
        {
            // Bloquear también el extracto: eliminar importación y cobrar deben serializarse.
            await cn.ExecuteScalarAsync<long>(Q("SELECT MovimientoId FROM dbo.BK_ExtractoMovimiento WITH(UPDLOCK,HOLDLOCK) WHERE MovimientoId=@movimiento;",new{movimiento}));
            if(await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.ALFACORE_COBRANZA_OPERACION WHERE MovimientoId=@movimiento;",new{movimiento}))>0)
                throw new InvalidOperationException("El movimiento ya fue cobrado. Actualizá la revisión.");
        }
        var facturas=(await cn.QueryAsync<Factura>(Q("""
            SELECT f.ID,f.TC,f.IDCOMPROBANTE Comprobante,LTRIM(RTRIM(f.CUENTA)) Cliente,LTRIM(RTRIM(f.UNEGOCIO)) Unidad,
                LTRIM(RTRIM(f.MONEDA)) Moneda,CONVERT(bit,ISNULL(f.ANULADA,0)) Anulada,f.NOMBRE Nombre,
                COALESCE(NULLIF(LTRIM(RTRIM(f.IdVendedor)),''),NULLIF(LTRIM(RTRIM(c.IdVendedor)),'')) Vendedor
            FROM dbo.V_MV_CPTE f WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.VT_CLIENTES c ON LTRIM(RTRIM(c.CODIGO))=LTRIM(RTRIM(f.CUENTA))
            WHERE f.ID IN @ids AND f.IDCOMPLEMENTO=0;
            """,new { ids=aplicaciones.Select(a=>a.FacturaId).ToArray() }))).AsList();
        if(facturas.Count!=aplicaciones.Count) throw new InvalidOperationException("Uno de los comprobantes ya no está disponible.");
        var factura=facturas.Single(f=>f.Id==solicitud.FacturaId);
        foreach(var elegida in facturas)
        {
        ValidarUnidad(user,elegida.Unidad);
        if(elegida.Cliente!=factura.Cliente || elegida.Unidad!=factura.Unidad)
            throw new InvalidOperationException("Un recibo debe aplicar a comprobantes del mismo cliente y unidad de negocio.");
        // Cabeceras legacy pueden omitir MONEDA: resolverla del asiento una vez bloqueado.
        if(elegida.Anulada||elegida.Tc.Trim() is not ("FC" or "FP" or "ND")||(!string.IsNullOrWhiteSpace(elegida.Moneda)&&elegida.Moneda!="1"))
            throw new InvalidOperationException("Seleccioná una factura, proforma o nota de débito vigente en pesos.");
        // La vista oficial de saldos incluye aplicaciones previas y movimientos contables.
        // La vista oficial contiene NOLOCK: proteger las filas y rangos antes de leerla.
        await cn.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(CUENTA))=@cliente AND TC=@tc AND SUCURSAL=SUBSTRING(@comprobante,1,4)
                AND NUMERO=SUBSTRING(@comprobante,5,8) AND LETRA=RIGHT(@comprobante,1);
            SELECT COUNT(*) FROM dbo.MV_APLICACION WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(CUENTA))=@cliente AND TCO_ORIGEN=@tc AND IDCOMPROBANTE_ORIGEN=@comprobante;
            """,new { cliente=elegida.Cliente,tc=elegida.Tc,comprobante=elegida.Comprobante }));
        }
        var docs=await Documentos(cn,factura.Cliente,tx,ct);
        foreach(var aplicada in aplicaciones)
        {
        var doc=docs.SingleOrDefault(d=>d.Id==aplicada.FacturaId);
        if(doc is not null&&doc.Moneda!="1")
            throw new InvalidOperationException("La moneda del comprobante no es pesos o no se puede determinar desde su asiento.");
        if(doc is null||aplicada.Importe>doc.Saldo) throw new InvalidOperationException("El importe aplicado supera el saldo pendiente actual de uno de los comprobantes. Revisá los importes o actualizá la selección.");
        }
        var medios=await Medios(cn,tx,ct);
        foreach(var pago in solicitud.Pagos)
        {
            var medio=medios.SingleOrDefault(m=>m.Cuenta==pago.Cuenta);
            if(medio is null||medio.Moneda!="1") throw new InvalidOperationException("Uno de los medios de pago no está activo o no corresponde a pesos.");
        }
        if(solicitud.MovimientoId is long mid)
        {
            var mov=await LeerMovimiento(cn,mid,tx,ct);
            if(mov.Cliente!=factura.Cliente||solicitud.Pagos.Where(p=>p.Cuenta==mov.Cuenta).Sum(p=>p.Importe)!=mov.Importe||
                solicitud.Pagos.Any(p=>p.Cuenta!=mov.Cuenta&&medios.Single(m=>m.Cuenta==p.Cuenta).Tipo=="DB"))
                throw new InvalidOperationException("La cobranza debe corresponder al cliente y al importe bancario completo del movimiento. Agregá solo retenciones como medios adicionales.");
        }
        // Serializa contador también con otros grabadores que no utilizan nuestro applock.
        await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.V_TA_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(CODIGO))='CB';"));
        var tal=(await Talonarios(cn,factura.Unidad,tx,ct)).SingleOrDefault(t=>t.Talonario==solicitud.Talonario)
            ?? throw new InvalidOperationException("El talonario CB no corresponde a la unidad de la factura.");
        if(tal.Sucursal is <1 or >9999||tal.Vence?.Date<solicitud.Fecha)
            throw new InvalidOperationException("El talonario CB tiene una sucursal inválida o está vencido.");
        var suc=tal.Sucursal.ToString("D4");
        var max=await cn.ExecuteScalarAsync<int>(Q("""
            SELECT ISNULL(MAX(n),0) FROM (
                SELECT CONVERT(int,CASE WHEN LTRIM(RTRIM(NUMERO)) NOT LIKE '%[^0-9]%' THEN NULLIF(LTRIM(RTRIM(NUMERO)),'') ELSE '0' END) n
                    FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC))='CB' AND SUCURSAL=@suc AND LETRA='X'
                UNION ALL SELECT CONVERT(int,CASE WHEN LTRIM(RTRIM(NUMERO)) NOT LIKE '%[^0-9]%' THEN NULLIF(LTRIM(RTRIM(NUMERO)),'') ELSE '0' END)
                    FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC))='CB' AND SUCURSAL=@suc AND LETRA='X'
            ) x;
            """,new{suc}));
        var numero=Math.Max(Math.Max(tal.Proximo,Math.Max(tal.Desde,1)),checked(max+1));
        if(numero>tal.Hasta||numero>99999999) throw new InvalidOperationException("El talonario CB agotó su rango de numeración.");
        var comprobante=suc+numero.ToString("D8")+"X";
        var periodo=await cn.QuerySingleOrDefaultAsync<int?>(Q("""
            SELECT PERIODO FROM dbo.MV_EJERCICIOS WHERE @Fecha BETWEEN [FECHA DESDE] AND [FECHA HASTA] AND CERRADO=0;
            """,new{solicitud.Fecha})) ?? throw new InvalidOperationException("La fecha no pertenece a un ejercicio abierto.");
        var eq=await cn.QuerySingleOrDefaultAsync<string>(Q("""
            SELECT COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux)) FROM dbo.TA_CONFIGURACION WHERE CLAVE='EQU_CB';
            """));
        if(eq?.Trim()!="CB") throw new InvalidOperationException("La cobranza rápida requiere la equivalencia contable CB. Revisá EQU_CB antes de continuar.");
        var sub=await cn.QuerySingleOrDefaultAsync<string>(Q("""
            SELECT LTRIM(RTRIM(s.CODIGO)) FROM dbo.MA_SUBDIARIOSCOMPROBANTES c JOIN dbo.MA_SUBDIARIOS s ON s.CODIGO=c.Subdiario WHERE LTRIM(RTRIM(c.TC))=@eq;
            """,new{eq}));
        if(string.IsNullOrWhiteSpace(sub)) throw new InvalidOperationException("Falta configurar la equivalencia contable o el subdiario de CB.");
        var asiento=await NumeracionAsiento.SiguienteAsync(cn,tx,periodo.ToString(),solicitud.Fecha.Month,sub,ct);
        var p=new { Fecha=solicitud.Fecha, Tc="CB", Comprobante=comprobante, Total=total,
            Cliente=factura.Cliente.Trim(),Unidad=CodigoPk.Format(factura.Unidad,4),Moneda=CodigoPk.Format("1",4),
            Usuario=user.Nombre,solicitud.FacturaId,Periodo=periodo.ToString(),Mes=solicitud.Fecha.Month,
            Asiento=asiento,Sub=CodigoPk.Format(sub,4),Caja=CodigoPk.Format(string.IsNullOrWhiteSpace(user.Caja)?"1":user.Caja,4),
            Vendedor=string.IsNullOrWhiteSpace(factura.Vendedor)?null:CodigoPk.Format(factura.Vendedor,4),
            Suc=suc,Numero=numero.ToString("D8"),Detalle=string.IsNullOrWhiteSpace(solicitud.Detalle)?$"Cob. {factura.Nombre.Trim()}":solicitud.Detalle.Trim() };
        var id=await cn.ExecuteScalarAsync<int>(Q("""
            INSERT INTO dbo.V_MV_CPTE(TC,IDCOMPROBANTE,IDCOMPLEMENTO,FECHA,FECHAESTFIN,FECHAESTINICIO,CUENTA,NOMBRE,
                DOMICILIO,TELEFONO,LOCALIDAD,IDPROVINCIA,CODIGOPOSTAL,DOCUMENTOTIPO,DOCUMENTONUMERO,CONDICIONIVA,
                IDCOND_CPRA_VTA,IMPORTE,IMPORTE_S_IVA,MONEDA,IDVENDEDOR,CLASEPRECIO,UNEGOCIO,UNEGOCIO_DESTINO,
                IdLista,IDMOTIVOCPRAVTA,IDDEPOSITO,USUARIO,FechaHora_Grabacion,COMENTARIOS)
            SELECT @Tc,@Comprobante,0,@Fecha,@Fecha,@Fecha,@Cliente,NOMBRE,DOMICILIO,TELEFONO,LOCALIDAD,IDPROVINCIA,
                CODIGOPOSTAL,DOCUMENTOTIPO,DOCUMENTONUMERO,CONDICIONIVA,IDCOND_CPRA_VTA,@Total,0,@Moneda,@Vendedor,
                CLASEPRECIO,@Unidad,@Unidad,IdLista,IDMOTIVOCPRAVTA,IDDEPOSITO,@Usuario,GETDATE(),@Detalle
            FROM dbo.V_MV_CPTE WHERE ID=@FacturaId;
            SELECT CONVERT(int,SCOPE_IDENTITY());
            """,p));
        var sec=1;
        async Task Linea(string cuenta,decimal importe,string sentido,string certificado,string referencia)
        {
            var args=new DynamicParameters(p); args.Add("Cuenta",cuenta.Trim());args.Add("Importe",importe);
            args.Add("Sentido",sentido);args.Add("Sec",sec++);args.Add("Certificado",certificado);args.Add("Referencia",referencia);
            await cn.ExecuteAsync(Q("""
                INSERT INTO dbo.MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,
                    [DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,TIPO_REG,CONTABILIZADO,FechaHora_Grabacion,
                    FechaSubdiario,USUARIO_LOGEADO,IDCAJAS,UNEGOCIO,CABCUENTA,CABNOMBRE,VENCIMIENTO,TJ_AUTORIZO,NroComprobanteBancario,IdVendedor)
                SELECT @Cuenta,@Sec,@Mes,@Asiento,@Fecha,@Detalle,@Tc,@Suc,@Numero,'X',@Sentido,@Importe,@Moneda,1,@Periodo,
                    @Total,@Sub,0,GETDATE(),@Fecha,@Usuario,@Caja,@Unidad,NULL,NULL,@Fecha,@Certificado,@Referencia,@Vendedor
                FROM dbo.V_MV_CPTE WHERE ID=@FacturaId;
                """,args));
        }
        await Linea(factura.Cliente,total,"H","","");
        foreach(var pago in solicitud.Pagos) await Linea(pago.Cuenta,pago.Importe,"D",pago.Certificado.Trim(),"");
        foreach(var aplicada in aplicaciones)
        {
        var origen=facturas.Single(f=>f.Id==aplicada.FacturaId);
        var aplicacion=new DynamicParameters(p);aplicacion.Add("OrigenTc",origen.Tc.Trim());aplicacion.Add("Origen",origen.Comprobante);
        aplicacion.Add("ImporteAplicado",aplicada.Importe);
        await cn.ExecuteAsync(Q("""
            INSERT INTO dbo.MV_APLICACION(CUENTA,TC,SUCURSAL,NUMERO,LETRA,IDCOMPROBANTE,TCO_ORIGEN,SUCURSAL_ORIGEN,NUMERO_ORIGEN,
                LETRA_ORIGEN,IDComprobante_Origen,IDCOMPLEMENTO_ORIGEN,IMPORTE,Transmision,Recepcion,UNegocio,Secuencia,TC_PRINT,IDCOMPROBANTE_PRINT)
            VALUES(@Cliente,@Tc,@Suc,@Numero,'X',@Comprobante,@OrigenTc,SUBSTRING(@Origen,1,4),SUBSTRING(@Origen,5,8),
                RIGHT(@Origen,1),@Origen,0,@ImporteAplicado,0,0,@Unidad,0,@Tc,@Comprobante);
            """,aplicacion));
        }
        if(await cn.ExecuteScalarAsync<decimal>(Q("""
            SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS
            WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND TIPO_REG=@Sub AND [NUMERO ASIENTO]=@Asiento;
            """,p))!=0) throw new InvalidOperationException("El asiento de cobranza no balancea. No se grabaron cambios.");
        await cn.ExecuteAsync(Q("""
            UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO=CASE WHEN X_ULTIMO_NRO<@proximo THEN @proximo ELSE X_ULTIMO_NRO END
                WHERE LTRIM(RTRIM(CODIGO))='CB' AND Talonario=@Talonario;
            INSERT INTO dbo.ALFACORE_COBRANZA_OPERACION(Operacion,MovimientoId,FacturaId,CobranzaId,Comprobante,NumeroAsiento,Importe,SolicitudHash,Unidad,Usuario)
            VALUES(@Operacion,@MovimientoId,@FacturaId,@id,@comprobante,@asiento,@total,@hash,@unidad,@usuario);
            """,new {proximo=numero+1,solicitud.Talonario,solicitud.Operacion,solicitud.MovimientoId,solicitud.FacturaId,id,comprobante,asiento,total,hash,
                unidad=CodigoPk.Format(factura.Unidad,4),usuario=user.Nombre}));
        if(solicitud.MovimientoId is long movId)
            await cn.ExecuteAsync(Q("""
                UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Cobrado' WHERE MovimientoId=@movId;
                UPDATE dbo.BK_MovimientoMatch SET EstadoPropuesta='GENERATED' WHERE MovimientoId=@movId;
                INSERT INTO dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
                VALUES(@movId,'COBRANZA','OK',@mensaje,@usuario);
                """,new{movId,mensaje=$"CB {comprobante}; ID {id}; operación {solicitud.Operacion}",usuario=user.Nombre}));
        return new(solicitud.Operacion,id,comprobante,asiento,total);
    }
    private sealed class Factura
    {
        public int Id { get; set; }
        public string Nombre { get; set; }="";
        public string? Vendedor { get; set; }
        public string Tc { get; set; }="";
        public string Comprobante { get; set; }="";
        public string Cliente { get; set; }="";
        public string Unidad { get; set; }="";
        public string Moneda { get; set; }="";
        public bool Anulada { get; set; }
    }
    private sealed class OperacionGuardada
    {
        public Guid Operacion { get; set; }
        public int CobranzaId { get; set; }
        public string Comprobante { get; set; }="";
        public int NumeroAsiento { get; set; }
        public decimal Importe { get; set; }
        public string SolicitudHash { get; set; }="";
        public string Unidad { get; set; }="";
    }
}
