using System.Data;
using System.Text.Json;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    private sealed class AsientoUsuario
    {
        public string Unidad { get; set; }="";
        public string Caja { get; set; }="";
        public bool Admin { get; set; }
    }
    private sealed class AsientoOrigen
    {
        public string Cuenta { get; set; }="";
        public DateTime Fecha { get; set; }
        public string Categoria { get; set; }="";
    }
    private async Task<AsientoUsuario> AsientoUsuarioAsync(SqlConnection cn,SqlTransaction? tx,CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<AsientoUsuario>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,LTRIM(RTRIM(ISNULL(IdCaja,''))) Caja,
                CONVERT(bit,ISNULL(AdminCentral,0)) Admin FROM dbo.TA_USUARIOS
            WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@usuario);
            """,new{usuario=users.GetCurrentUserName()},tx,cancellationToken:ct))
            ?? throw new InvalidOperationException("El usuario no está definido en la base activa.");

    public Task<ConciliacionAsientoCatalogo> AsientoCatalogoAsync(CancellationToken ct=default)
        => Ejecutar("CatalogoAsientos",(cn,token)=>AsientoCatalogoEnConexionAsync(cn,null,token),ct);

    internal async Task<ConciliacionAsientoCatalogo> AsientoCatalogoEnConexionAsync(SqlConnection cn,SqlTransaction? tx,CancellationToken ct)
    {
        var user=await AsientoUsuarioAsync(cn,tx,ct);
        using var r=await cn.QueryMultipleAsync(new CommandDefinition("""
            SELECT LTRIM(RTRIM(c.CODIGO)) Codigo,c.DESCRIPCION Descripcion FROM dbo.MA_CUENTAS c
            WHERE ISNULL(c.TITULO,0)=0 AND ISNULL(c.Dada_De_Baja,0)=0 AND LTRIM(RTRIM(c.Moneda))='1'
                AND ISNULL(LTRIM(RTRIM(c.MedioDePago)),'') IN ('','RI','RB','RG','RO')
                AND NOT EXISTS(SELECT 1 FROM dbo.VT_CLIENTES x WHERE x.CODIGO=c.CODIGO)
                AND NOT EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES x WHERE x.CODIGO=c.CODIGO)
            ORDER BY c.DESCRIPCION,c.CODIGO;
            SELECT LTRIM(RTRIM(CODIGO)) Codigo,DESCRIPCION Descripcion,'1' Moneda FROM dbo.MA_CUENTAS
                WHERE ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 AND LTRIM(RTRIM(Moneda))='1'
                    AND LTRIM(RTRIM(MedioDePago))='DB' ORDER BY DESCRIPCION,CODIGO;
            SELECT LTRIM(RTRIM(Codigo)) Codigo,Descripcion FROM dbo.V_TA_UnidadNegocio
                WHERE @admin=1 OR LTRIM(RTRIM(Codigo))=@unidad ORDER BY Codigo;
            SELECT LTRIM(RTRIM(CODIGO)) Codigo,DESCRIPCION Descripcion FROM dbo.MA_SUBDIARIOS ORDER BY CODIGO;
            SELECT Talonario,LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,X_SUC_DEFAULT Sucursal,
                X_ULTIMO_NRO Proximo,X_NRO_DESDE Desde,X_NRO_HASTA Hasta,VenceX Vence
            FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CC' AND UPPER(LTRIM(RTRIM(SISTEMA)))='CONTABLE'
                AND CHARINDEX('X',LETRAS)>0 AND ISNULL(Externo,0)=0;
            SELECT CONVERT(nvarchar(max),ValorAux) FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-ASIENTOS';
            """,new{admin=user.Admin,unidad=user.Unidad},tx,cancellationToken:ct));
        var cuentas=(await r.ReadAsync<ConciliacionAsientoCuenta>()).AsList();
        var bancos=(await r.ReadAsync<ConciliacionCuenta>()).AsList();
        var unidades=(await r.ReadAsync<ConciliacionAsientoCuenta>()).AsList();
        var subdiarios=(await r.ReadAsync<ConciliacionAsientoCuenta>()).AsList();
        var talonarios=(await r.ReadAsync<CobranzaTalonario>()).AsList();
        var json=await r.ReadSingleOrDefaultAsync<string>();
        var config=string.IsNullOrWhiteSpace(json) ? new ConciliacionAsientoConfig() :
            JsonSerializer.Deserialize<ConciliacionAsientoConfig>(json) ?? throw new InvalidOperationException("La configuración de asientos no es válida.");
        return new(config,cuentas,bancos,unidades,subdiarios,talonarios,user.Unidad)
        {
            SugerenciasIniciales=config.Reglas.Count==0 ? ConciliacionAsientoDefaults.Proponer(cuentas) : []
        };
    }

    public Task<bool> GuardarAsientoConfigAsync(ConciliacionAsientoConfig config,CancellationToken ct=default)
        => Ejecutar("GuardarCuentasAsientos",async(cn,token)=>
        {
            var session=sessions.GetActiveSession()!.Id;
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var catalogo=await AsientoCatalogoEnConexionAsync(cn,tx,token);
            ValidarAsientoConfig(config,catalogo);
            await cn.ExecuteAsync(new CommandDefinition("""
                IF EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WITH(UPDLOCK,HOLDLOCK) WHERE CLAVE='CONCILIACION-ASIENTOS')
                    UPDATE dbo.TA_CONFIGURACION SET VALOR='JSON',ValorAux=@json,FechaHora_Modificacion=GETDATE() WHERE CLAVE='CONCILIACION-ASIENTOS';
                ELSE INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION)
                    VALUES('CAJA','CONCILIACION-ASIENTOS','JSON',@json,N'Cuentas y conceptos para asientos bancarios');
                """,new{json=JsonSerializer.Serialize(config)},tx,cancellationToken:token));
            VerificarSesion(session);token.ThrowIfCancellationRequested();tx.Commit();return true;
        },ct);

    internal static void ValidarAsientoConfig(ConciliacionAsientoConfig c,ConciliacionAsientoCatalogo catalogo)
    {
        if(c.TipoRegistro.Length>4 || (c.TipoRegistro.Trim().Length>0&&!catalogo.Subdiarios.Any(s=>s.Codigo==c.TipoRegistro.Trim()))
            || c.Reglas.Count>100 || c.Reglas.Select(r=>r.Id).Distinct().Count()!=c.Reglas.Count)
            throw new InvalidOperationException("Revisá el subdiario y los conceptos (hasta 100, con identificadores distintos).");
        foreach(var r in c.Reglas)
            if(r.Id==Guid.Empty || r.Nombre.Trim().Length is <1 or >60 || r.Texto.Length>150 || r.Detalle.Length>200
                || !Enum.TryParse<ConciliacionBancaria.Domain.Enums.MovementCategory>(r.Categoria,out var categoria)
                || !Enum.IsDefined(categoria) || categoria is ConciliacionBancaria.Domain.Enums.MovementCategory.Cobranza or ConciliacionBancaria.Domain.Enums.MovementCategory.TransferenciaInterna
                || !catalogo.Cuentas.Any(x=>x.Codigo==r.Contrapartida.Trim())
                || (r.CuentaBanco.Trim().Length>0&&!catalogo.Bancos.Any(b=>b.Codigo==r.CuentaBanco.Trim())))
                throw new InvalidOperationException("Cada concepto requiere nombre, clasificación válida y contrapartida contable activa en pesos.");
    }

    internal static Guid? SugerirReglaAsiento(ConciliacionAsientoConfig config,string categoria,string banco,string descripcion)
    {
        var reglas=config.Reglas.Where(r=>r.Categoria==categoria&&(r.CuentaBanco.Trim().Length==0||r.CuentaBanco.Trim()==banco)
            &&(r.Texto.Trim().Length==0||descripcion.Contains(r.Texto.Trim(),StringComparison.OrdinalIgnoreCase)))
            .Select(r=>new{r.Id,Puntos=(r.CuentaBanco.Trim().Length>0 ? 2 : 0)+(r.Texto.Trim().Length>0 ? 1 : 0)}).ToList();
        if(reglas.Count==0)return null;
        var mejores=reglas.Where(r=>r.Puntos==reglas.Max(x=>x.Puntos)).ToList();
        return mejores.Count==1 ? mejores[0].Id : null;
    }

    public Task<ConciliacionAsientoPreparacion> PrepararAsientoAsync(long id,CancellationToken ct=default)
        => Ejecutar("PrepararAsiento",(cn,token)=>PrepararAsientoEnConexionAsync(cn,null,id,token),ct);

    internal async Task<ConciliacionAsientoPreparacion> PrepararAsientoEnConexionAsync(SqlConnection cn,SqlTransaction? tx,long id,CancellationToken ct)
    {
        if(!await cn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT CONVERT(bit,CASE WHEN OBJECT_ID('dbo.BK_AsientoOperacion','U') IS NULL THEN 0 ELSE 1 END);",transaction:tx,cancellationToken:ct)))
            throw new InvalidOperationException("Aplicá la actualización de asientos de conciliación antes de generar registraciones.");
        var origen=await cn.QuerySingleOrDefaultAsync<AsientoOrigen>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(i.CuentaBanco)) Cuenta,m.FechaMovimiento Fecha,ISNULL(c.TipoMovimiento,'NoIdentificado') Categoria
            FROM dbo.BK_ExtractoMovimiento m JOIN dbo.BK_ImportacionExtracto i ON i.ImportacionId=m.ImportacionId
            OUTER APPLY(SELECT TOP(1) TipoMovimiento FROM dbo.BK_MovimientoClasificado WHERE MovimientoId=m.MovimientoId ORDER BY ClasificacionId DESC)c
            WHERE m.MovimientoId=@id;
            """,new{id},tx,cancellationToken:ct)) ?? throw new InvalidOperationException("El movimiento no está disponible.");
        if(origen.Categoria is "Cobranza" or "TransferenciaInterna")
            throw new InvalidOperationException("Las cobranzas y transferencias entre cuentas propias requieren su circuito específico. Revisá la clasificación del movimiento.");
        var criterios=await LeerCriterios(cn,tx,ct);
        var f=new ConciliacionCompararFiltro(origen.Cuenta,origen.Fecha.AddDays(-criterios.Dias),origen.Fecha.AddDays(criterios.Dias));
        var args=new DynamicParameters(ArgumentosComparacion(f));args.Add("id",id);args.Add("tolerancia",criterios.Tolerancia);
        using var r=await cn.QueryMultipleAsync(new CommandDefinition(CompararBaseSql+"""
            SELECT * FROM #Banco WHERE Id=@id;
            SELECT TOP(5) *,COUNT(*) OVER() Candidatos FROM #Alfa WHERE Moneda='1'
                AND EXISTS(SELECT 1 FROM #Banco b WHERE b.Id=@id AND SIGN(b.Importe)=SIGN(#Alfa.Importe) AND ABS(b.Importe-#Alfa.Importe)<=@tolerancia)
                ORDER BY Fecha,Asiento,Secuencia;
            """,args,tx,cancellationToken:ct));
        var banco=await r.ReadSingleOrDefaultAsync<ConciliacionFilaBanco>();
        var posibles=(await r.ReadAsync<ConciliacionFilaAlfa>()).AsList();
        if(banco is null||banco.Importe==0||banco.ConciliacionId.HasValue||banco.Registracion is "Cobrado" or "Registrado")
            throw new InvalidOperationException("Seleccioná un movimiento en pesos, pendiente de registración y conciliación.");
        if(await cn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_AsientoOperacion WHERE MovimientoId=@id)
                OR EXISTS(SELECT 1 FROM dbo.ALFACORE_COBRANZA_OPERACION WHERE MovimientoId=@id) THEN 1 ELSE 0 END);
            """,new{id},tx,cancellationToken:ct)))
            throw new InvalidOperationException("Este movimiento ya generó una registración. Compará el asiento existente; no se genera otro al desconciliar.");
        var catalogo=await AsientoCatalogoEnConexionAsync(cn,tx,ct);
        return new(banco,origen.Cuenta,origen.Categoria,catalogo,SugerirReglaAsiento(catalogo.Config,origen.Categoria,origen.Cuenta,banco.Detalle),posibles);
    }

    internal static decimal ValidarAsientoImportes(ConciliacionAsientoSolicitud s,decimal importe)
    {
        if(s.Operacion==Guid.Empty||s.Detalle.Trim().Length is <3 or >200||s.Contrapartidas.Count is <1 or >20
            || s.Contrapartidas.Any(l=>l.Cuenta.Trim().Length is <1 or >15||l.Importe<=0||decimal.Round(l.Importe,2)!=l.Importe)
            || s.Contrapartidas.Sum(l=>l.Importe)!=Math.Abs(importe))
            throw new InvalidOperationException("Revisá el detalle y las contrapartidas: deben sumar exactamente el importe bancario (hasta 20 renglones).");
        return Math.Abs(importe);
    }

    public Task<ConciliacionAsientoResultado> GenerarAsientoAsync(ConciliacionAsientoSolicitud solicitud,CancellationToken ct=default)
        => Ejecutar("GenerarAsiento",async(cn,token)=>
        {
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var result=await GenerarAsientoEnTransaccionAsync(cn,tx,solicitud,token);
            tx.Commit();return result;
        },ct);

    internal async Task<ConciliacionAsientoResultado> GenerarAsientoEnTransaccionAsync(SqlConnection cn,SqlTransaction tx,ConciliacionAsientoSolicitud s,CancellationToken ct=default)
    {
        var session=sessions.GetActiveSession()!.Id;
        var json=JsonSerializer.Serialize(s);var hash=Hash(json);
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:ct);
        // Misma jerarquía de bloqueo que la conciliación: cuenta, operación y movimiento.
        var cuenta=await cn.ExecuteScalarAsync<string>(Q("SELECT LTRIM(RTRIM(i.CuentaBanco)) FROM dbo.BK_ExtractoMovimiento m JOIN dbo.BK_ImportacionExtracto i ON i.ImportacionId=m.ImportacionId WHERE m.MovimientoId=@id;",new{id=s.MovimientoId}));
        if(string.IsNullOrWhiteSpace(cuenta))throw new InvalidOperationException("El extracto no tiene cuenta bancaria.");
        await BloquearCuenta(cn,tx,cuenta,ct);
        var user=await AsientoUsuarioAsync(cn,tx,ct);
        if(!user.Admin&&user.Unidad!=s.Unidad.Trim())throw new InvalidOperationException("La unidad de negocio no está autorizada.");
        var previo=await cn.QuerySingleOrDefaultAsync<(string Hash,string Unidad) ?>(Q("SELECT SolicitudHash Hash,Unidad FROM dbo.BK_AsientoOperacion WITH(UPDLOCK,HOLDLOCK) WHERE Operacion=@Operacion;",new{s.Operacion}));
        if(previo.HasValue)
        {
            if(previo.Value.Hash!=hash||previo.Value.Unidad!=s.Unidad.Trim())throw new InvalidOperationException("La operación ya fue confirmada con otros datos.");
            var resultado=await cn.QuerySingleAsync<ConciliacionAsientoResultado>(Q("SELECT * FROM dbo.BK_AsientoOperacion WHERE Operacion=@Operacion;",new{s.Operacion}));
            VerificarSesion(session);return resultado;
        }
        await cn.ExecuteScalarAsync<long>(Q("SELECT MovimientoId FROM dbo.BK_ExtractoMovimiento WITH(UPDLOCK,HOLDLOCK) WHERE MovimientoId=@id;",new{id=s.MovimientoId}));
        var prep=await PrepararAsientoEnConexionAsync(cn,tx,s.MovimientoId,ct);
        if(prep.Movimiento.Huella!=s.Huella)throw new InvalidOperationException("El movimiento cambió. Volvé a abrir el asiento.");
        var total=ValidarAsientoImportes(s,prep.Movimiento.Importe);
        if(prep.PosiblesDuplicados.Count>0&&!s.ConfirmarNuevaRegistracion)throw new InvalidOperationException("Hay registraciones posibles. Revisalas y confirmá expresamente que corresponde generar un asiento nuevo.");
        var catalogo=prep.Catalogo;
        if(!catalogo.Unidades.Any(u=>u.Codigo==s.Unidad.Trim()))throw new InvalidOperationException("La unidad no está disponible para este usuario.");
        if(s.Contrapartidas.Any(l=>!catalogo.Cuentas.Any(c=>c.Codigo==l.Cuenta.Trim())))throw new InvalidOperationException("Una contrapartida no está activa, no es contable o no corresponde a pesos.");
        var cfg=catalogo.Config;
        if(cfg.TipoRegistro.Trim().Length>0&&!catalogo.Subdiarios.Any(t=>t.Codigo==cfg.TipoRegistro.Trim()))throw new InvalidOperationException("El subdiario configurado ya no existe.");
        var periodo=await cn.QuerySingleOrDefaultAsync<int?>(Q("SELECT PERIODO FROM dbo.MV_EJERCICIOS WITH(UPDLOCK,HOLDLOCK) WHERE @fecha BETWEEN [FECHA DESDE] AND [FECHA HASTA] AND ISNULL(CERRADO,0)=0;",new{fecha=s.Fecha.Date}))
            ?? throw new InvalidOperationException("La fecha no pertenece a un ejercicio abierto.");
        if(Math.Abs((s.Fecha.Date-prep.Movimiento.Fecha.Date).TotalDays)>366)
            throw new InvalidOperationException("La fecha del asiento debe estar dentro de los 366 días del movimiento bancario.");
        var disponibles=catalogo.Talonarios.Where(t=>t.Unidad==s.Unidad.Trim()).ToList();
        if(disponibles.Count==0)disponibles=catalogo.Talonarios.Where(t=>t.Unidad is "" or "0").ToList();
        // Bloqueo del contador y relectura: comparte el contador CC con Alfa Gestión.
        await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.V_TA_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(CODIGO))='CC';"));
        var tal=disponibles.SingleOrDefault(t=>t.Talonario==s.Talonario) ?? throw new InvalidOperationException("Elegí un talonario CC válido para la unidad.");
        var talUnidad=tal.Unidad;
        tal=await cn.QuerySingleAsync<CobranzaTalonario>(Q("""
            SELECT Talonario,X_SUC_DEFAULT Sucursal,X_ULTIMO_NRO Proximo,X_NRO_DESDE Desde,X_NRO_HASTA Hasta,VenceX Vence
            FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CC' AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@talUnidad;
            """,new{s.Talonario,talUnidad}));
        if(tal.Sucursal is <1 or >9999||tal.Vence?.Date<s.Fecha.Date)throw new InvalidOperationException("El talonario CC está vencido o tiene una sucursal inválida.");
        var suc=tal.Sucursal.ToString("D4");
        var max=await cn.ExecuteScalarAsync<int>(Q("SELECT ISNULL(MAX(CONVERT(int,CASE WHEN LTRIM(RTRIM(NUMERO)) NOT LIKE '%[^0-9]%' THEN NULLIF(LTRIM(RTRIM(NUMERO)),'') ELSE '0' END)),0) FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE TC='CC' AND SUCURSAL=@suc AND LETRA='X';",new{suc}));
        var numero=Math.Max(Math.Max(tal.Proximo,Math.Max(tal.Desde,1)),checked(max+1));
        if(numero>tal.Hasta||numero>99999999)throw new InvalidOperationException("El talonario CC agotó su numeración.");
        var asiento=await NumeracionAsiento.SiguienteAsync(cn,tx,periodo.ToString()!,s.Fecha.Month,cfg.TipoRegistro,ct);
        var args=new DynamicParameters(new{periodo=periodo.ToString(),mes=s.Fecha.Month,asiento,tipo=CodigoPk.Format(cfg.TipoRegistro,4),
            fecha=s.Fecha.Date,detalle=s.Detalle.Trim(),suc,numero=numero.ToString("D8"),total,moneda=CodigoPk.Format("1",4),
            unidad=CodigoPk.Format(s.Unidad,4),caja=CodigoPk.Format(string.IsNullOrWhiteSpace(user.Caja)?"1":user.Caja,4),usuario=users.GetCurrentUserName(),
            referencia=prep.Movimiento.Referencia.Length>50 ? prep.Movimiento.Referencia[..50] : prep.Movimiento.Referencia});
        var lineas=new[]{new ConciliacionAsientoLinea(cuenta,total)}.Concat(s.Contrapartidas).ToList();
        for(var i=0;i<lineas.Count;i++)
        {
            args.Add("cuenta",lineas[i].Cuenta.Trim());args.Add("importe",lineas[i].Importe);args.Add("secuencia",i+1);
            args.Add("sentido",(i==0)==(prep.Movimiento.Importe>0) ? "D" : "H");
            await cn.ExecuteAsync(Q("""
                INSERT dbo.MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,
                    [DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,TIPO_REG,CONTABILIZADO,FechaHora_Grabacion,
                    FechaSubdiario,USUARIO_LOGEADO,IDCAJAS,UNEGOCIO,CABCUENTA,CABNOMBRE,VENCIMIENTO,NroComprobanteBancario)
                VALUES(@cuenta,@secuencia,@mes,@asiento,@fecha,@detalle,'CC',@suc,@numero,'X',@sentido,@importe,@moneda,1,
                    @periodo,@total,@tipo,0,GETDATE(),@fecha,@usuario,@caja,@unidad,NULL,NULL,@fecha,@referencia);
                """,args));
        }
        if(await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",args))!=lineas.Count
            || await cn.ExecuteScalarAsync<decimal>(Q("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",args))!=0)
            throw new InvalidOperationException("El asiento no quedó balanceado. No se confirmó la operación.");
        await cn.ExecuteAsync(Q("UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO=@proximo WHERE LTRIM(RTRIM(CODIGO))='CC' AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@talUnidad;",new{proximo=checked(numero+1),s.Talonario,talUnidad}));
        var desde=s.Fecha.Date<prep.Movimiento.Fecha.Date ? s.Fecha.Date : prep.Movimiento.Fecha.Date;
        var hasta=s.Fecha.Date>prep.Movimiento.Fecha.Date ? s.Fecha.Date : prep.Movimiento.Fecha.Date;
        var filtro=new ConciliacionCompararFiltro(cuenta,desde,hasta);
        // La clave recién creada no depende de la página visible.
        var banco=prep.Movimiento;
        var claves=new ConciliacionAsientoClave(periodo.ToString()!,checked((byte)s.Fecha.Month),asiento,CodigoPk.Format(cfg.TipoRegistro,4),1);
        var p=new DynamicParameters(ArgumentosComparacion(filtro));p.Add("asiento",asiento);p.Add("periodo",periodo.ToString());p.Add("tipo",CodigoPk.Format(cfg.TipoRegistro,4));p.Add("mes",s.Fecha.Month);
        var lineaBanco=await cn.QuerySingleAsync<ConciliacionFilaAlfa>(Q(CompararBaseSql+"SELECT * FROM #Alfa WHERE Periodo=@periodo AND Mes=@mes AND Asiento=@asiento AND TipoRegistro=@tipo AND Secuencia=1;",p));
        var vinculo=await ConciliarEnTransaccionAsync(cn,tx,new(s.Operacion,filtro,[new(banco.Id,banco.Huella)],[new(claves,lineaBanco.Huella)]),ct);
        var comprobante=suc+numero.ToString("D8")+"X";
        await cn.ExecuteAsync(Q("""
            INSERT dbo.BK_AsientoOperacion(Operacion,MovimientoId,SolicitudHash,SolicitudJson,CuentaBanco,Periodo,Mes,NumeroAsiento,TipoRegistro,Comprobante,Unidad,ConciliacionId,Usuario)
            VALUES(@Operacion,@MovimientoId,@hash,@json,@cuenta,@periodo,@mes,@asiento,@tipo,@comprobante,@unidad,@vinculo,@usuario);
            UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Registrado' WHERE MovimientoId=@MovimientoId;
            INSERT dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
                VALUES(@MovimientoId,'GENERAR_ASIENTO','OK',@mensaje,@usuario);
            """,new{s.Operacion,s.MovimientoId,hash,json,cuenta,periodo=periodo.ToString(),mes=s.Fecha.Month,asiento,
                tipo=CodigoPk.Format(cfg.TipoRegistro,4),comprobante,unidad=s.Unidad.Trim(),vinculo,usuario=users.GetCurrentUserName(),mensaje=$"CC {comprobante}; asiento {asiento}; conciliación {vinculo}"}));
        VerificarSesion(session);ct.ThrowIfCancellationRequested();
        return new(){Operacion=s.Operacion,Comprobante=comprobante,Periodo=periodo.ToString()!,Mes=checked((byte)s.Fecha.Month),NumeroAsiento=asiento,TipoRegistro=CodigoPk.Format(cfg.TipoRegistro,4),ConciliacionId=vinculo};
    }
}
