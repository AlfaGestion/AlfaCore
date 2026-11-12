using System.Data;
using System.Text.Json;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Asiento mensual de gastos e impuestos bancarios (equivale a "Procesar" de la liquidación de Alfa Gestión, sin IA).
public sealed partial class ConciliacionBancariaService
{
    private const string ClaveGastos="CONCILIACION-GASTOS-BANCARIOS",ClaveGastosModelo="CONCILIACION-GASTOS-MODELO",ClaveGastosComprobante="CONCILIACION-GASTOS-COMPROBANTE",ModeloGastosInicial="GASTOS BANCARIOS";
    private const int MaxMovimientosGastos=5000;

    private sealed class GastosImportacion
    {
        public string Cuenta { get; set; }="";
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
    }
    private sealed class GastosBanco
    {
        public string Nombre { get; set; }="";
        public string Cuit { get; set; }="";
        public string CondIva { get; set; }="";
    }

    public Task<ConciliacionGastosPreparacion> PrepararGastosAsync(long importacionId,CancellationToken ct=default)
        => Ejecutar("PrepararGastos",(cn,token)=>PrepararGastosEnConexionAsync(cn,null,importacionId,token),ct);

    internal async Task<ConciliacionGastosPreparacion> PrepararGastosEnConexionAsync(SqlConnection cn,SqlTransaction? tx,long importacionId,CancellationToken ct)
    {
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:ct);
        if(!await cn.ExecuteScalarAsync<bool>(Q("SELECT CONVERT(bit,CASE WHEN OBJECT_ID('dbo.BK_AsientoGastos','U') IS NULL OR COL_LENGTH('dbo.BK_AsientoGastos','Tc') IS NULL THEN 0 ELSE 1 END);")))
            throw new InvalidOperationException("Aplicá la actualización de asientos de gastos bancarios antes de generar el asiento.");
        var imp=await cn.QuerySingleOrDefaultAsync<GastosImportacion>(Q("""
            SELECT LTRIM(RTRIM(i.CuentaBanco)) Cuenta,MIN(m.FechaMovimiento) Desde,MAX(m.FechaMovimiento) Hasta
            FROM dbo.BK_ImportacionExtracto i JOIN dbo.BK_ExtractoMovimiento m ON m.ImportacionId=i.ImportacionId
            WHERE i.ImportacionId=@importacionId GROUP BY LTRIM(RTRIM(i.CuentaBanco));
            """,new{importacionId})) ?? throw new InvalidOperationException("El extracto no existe o no tiene movimientos.");
        if((imp.Hasta.Date-imp.Desde.Date).TotalDays>366)
            throw new InvalidOperationException("El extracto abarca más de un año. Importá el resumen de un mes para generar el asiento.");
        var tc=await LeerComprobanteGastosAsync(cn,tx,ct);
        var catalogo=await AsientoCatalogoEnConexionAsync(cn,tx,ct,tc);
        var (config,desdeModelo)=await LeerConfigGastosAsync(cn,tx,catalogo,ct);
        var banco=await cn.QuerySingleOrDefaultAsync<GastosBanco>(Q("""
            SELECT LTRIM(RTRIM(ISNULL(c.DESCRIPCION,''))) Nombre,LTRIM(RTRIM(ISNULL(a.NUMERO_DOCUMENTO,''))) Cuit,ISNULL(a.IVA,'') CondIva
            FROM dbo.MA_CUENTAS c LEFT JOIN dbo.MA_CUENTASADIC a ON LTRIM(RTRIM(a.CODIGO))=LTRIM(RTRIM(c.CODIGO))
            WHERE LTRIM(RTRIM(c.CODIGO))=@cuenta;
            """,new{cuenta=imp.Cuenta})) ?? new GastosBanco();
        var filtro=new ConciliacionCompararFiltro(imp.Cuenta,imp.Desde.Date,imp.Hasta.Date);
        var args=new DynamicParameters(ArgumentosComparacion(filtro));args.Add("imp",importacionId);
        var filas=(await cn.QueryAsync<ConciliacionFilaBanco>(Q(CompararBaseSql+"""
            SELECT b.* FROM #Banco b WHERE b.Id IN (SELECT MovimientoId FROM dbo.BK_ExtractoMovimiento WHERE ImportacionId=@imp);
            """,args))).AsList();
        var criterios=await LeerCriterios(cn,tx,ct);
        var calculo=ConciliacionGastosBancarios.Calcular(filas,config,criterios.Tolerancia);
        if(calculo.Movimientos.Count>MaxMovimientosGastos)
            calculo=calculo with{Errores=[..calculo.Errores,$"El extracto tiene más de {MaxMovimientosGastos} movimientos de gastos. Importá un período más corto."]};
        var fecha=new DateTime(imp.Hasta.Year,imp.Hasta.Month,DateTime.DaysInMonth(imp.Hasta.Year,imp.Hasta.Month));
        return new(importacionId,imp.Cuenta,banco.Nombre,banco.Cuit,banco.CondIva,imp.Desde.Date,imp.Hasta.Date,fecha,calculo,config,desdeModelo,catalogo,tc);
    }

    /// <summary>Comprobante contable del asiento de gastos bancarios (GB por defecto, clave CONCILIACION-GASTOS-COMPROBANTE). Si no existe en la tabla de comprobantes con letra X, se usa CC.</summary>
    private static async Task<string> LeerComprobanteGastosAsync(SqlConnection cn,SqlTransaction? tx,CancellationToken ct)
    {
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:ct);
        var valor=(await cn.ExecuteScalarAsync<string?>(Q("SELECT TOP(1) LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@clave AND NULLIF(LTRIM(RTRIM(VALOR)),'') IS NOT NULL;",new{clave=ClaveGastosComprobante})))?.ToUpperInvariant();
        var tc=valor is { Length: >=1 and <=8 } v&&v.All(char.IsAsciiLetterOrDigit) ? v : "GB";
        var existe=await cn.ExecuteScalarAsync<bool>(Q("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.V_TA_CPTE WHERE UPPER(LTRIM(RTRIM(CODIGO)))=@tc AND UPPER(LTRIM(RTRIM(SISTEMA)))='CONTABLE'
                AND CHARINDEX('X',LETRAS)>0 AND ISNULL(Externo,0)=0) THEN 1 ELSE 0 END);
            """,new{tc}));
        return existe ? tc : "CC";
    }

    /// <summary>Configuración propia; la primera vez toma las cuentas del asiento modelo de Alfa Gestión, sin escribirlo ni modificarlo.</summary>
    private static async Task<(ConciliacionGastosConfig Config,bool DesdeModelo)> LeerConfigGastosAsync(SqlConnection cn,SqlTransaction? tx,
        ConciliacionAsientoCatalogo catalogo,CancellationToken ct)
    {
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:ct);
        var json=await cn.ExecuteScalarAsync<string?>(Q("SELECT CONVERT(nvarchar(max),ValorAux) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@clave;",new{clave=ClaveGastos}));
        if(!string.IsNullOrWhiteSpace(json))
            return (JsonSerializer.Deserialize<ConciliacionGastosConfig>(json) ?? throw new InvalidOperationException("La configuración de gastos bancarios no es válida."),false);
        var nombre=await cn.ExecuteScalarAsync<string?>(Q("SELECT TOP(1) LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@clave AND NULLIF(LTRIM(RTRIM(VALOR)),'') IS NOT NULL;",new{clave=ClaveGastosModelo}));
        var filas=(await cn.QueryAsync<ConciliacionGastosBancarios.ModeloFila>(Q("""
            IF OBJECT_ID('dbo.MA_MODELOS','U') IS NULL SELECT CONVERT(nvarchar(15),'') Cuenta,CONVERT(nvarchar(100),'') Descripcion,CONVERT(nvarchar(1),'') DebeHaber,CONVERT(decimal(9,4),0) Alicuota WHERE 1=0
            ELSE SELECT LTRIM(RTRIM(m.CUENTA)) Cuenta,LTRIM(RTRIM(ISNULL(c.DESCRIPCION,''))) Descripcion,LTRIM(RTRIM(m.[DEBE-HABER])) DebeHaber,CONVERT(decimal(9,4),ISNULL(m.AlicIVA,0)) Alicuota
                FROM dbo.MA_MODELOS m LEFT JOIN dbo.MA_CUENTAS c ON LTRIM(RTRIM(c.CODIGO))=LTRIM(RTRIM(m.CUENTA))
                WHERE UPPER(LTRIM(RTRIM(m.NOMBRE)))=UPPER(@nombre) ORDER BY m.SECUENCIA;
            """,new{nombre=string.IsNullOrWhiteSpace(nombre) ? ModeloGastosInicial : nombre}))).AsList();
        var sugeridas=ConciliacionGastosBancarios.SugerirCuentas(filas).Where(x=>catalogo.Cuentas.Any(c=>c.Codigo==x.Value)).ToDictionary(x=>x.Key,x=>x.Value);
        return (new ConciliacionGastosConfig{Cuentas=sugeridas},true);
    }

    public Task<ConciliacionAsientoResultado> GenerarGastosAsync(ConciliacionGastosSolicitud solicitud,CancellationToken ct=default)
        => Ejecutar("GenerarGastosBancarios",async(cn,token)=>
        {
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var result=await GenerarGastosEnTransaccionAsync(cn,tx,solicitud,token);
            tx.Commit();return result;
        },ct);

    internal async Task<ConciliacionAsientoResultado> GenerarGastosEnTransaccionAsync(SqlConnection cn,SqlTransaction tx,ConciliacionGastosSolicitud s,CancellationToken ct=default)
    {
        var session=sessions.GetActiveSession()!.Id;
        var json=JsonSerializer.Serialize(s);var hash=Hash(json);
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:ct);
        if(s.Operacion==Guid.Empty||s.Detalle.Trim().Length is <3 or >200)throw new InvalidOperationException("Revisá el detalle del asiento (entre 3 y 200 caracteres).");
        var cuenta=await cn.ExecuteScalarAsync<string?>(Q("SELECT LTRIM(RTRIM(CuentaBanco)) FROM dbo.BK_ImportacionExtracto WHERE ImportacionId=@id;",new{id=s.ImportacionId}));
        if(string.IsNullOrWhiteSpace(cuenta))throw new InvalidOperationException("El extracto no tiene cuenta bancaria.");
        await BloquearCuenta(cn,tx,cuenta,ct);
        var user=await AsientoUsuarioAsync(cn,tx,ct);
        if(!user.Admin&&user.Unidad!=s.Unidad.Trim())throw new InvalidOperationException("La unidad de negocio no está autorizada.");
        var previo=await cn.QuerySingleOrDefaultAsync<(string Hash,string Unidad)?>(Q("SELECT SolicitudHash Hash,Unidad FROM dbo.BK_AsientoGastos WITH(UPDLOCK,HOLDLOCK) WHERE Operacion=@Operacion;",new{s.Operacion}));
        if(previo.HasValue)
        {
            if(previo.Value.Hash!=hash||previo.Value.Unidad!=s.Unidad.Trim())throw new InvalidOperationException("La operación ya fue confirmada con otros datos.");
            var resultado=await cn.QuerySingleAsync<ConciliacionAsientoResultado>(Q("SELECT Operacion,Comprobante,Periodo,Mes,NumeroAsiento,TipoRegistro,ConciliacionId,ISNULL(Tc,'CC') Tc FROM dbo.BK_AsientoGastos WHERE Operacion=@Operacion;",new{s.Operacion}));
            VerificarSesion(session);return resultado;
        }
        await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.BK_ExtractoMovimiento WITH(UPDLOCK,HOLDLOCK) WHERE ImportacionId=@id;",new{id=s.ImportacionId}));
        var prep=await PrepararGastosEnConexionAsync(cn,tx,s.ImportacionId,ct);
        var calculo=prep.Calculo;
        if(calculo.Huella!=s.Huella)throw new InvalidOperationException("Los movimientos del extracto cambiaron. Volvé a abrir el asiento.");
        if(calculo.Errores.Count>0)throw new InvalidOperationException(calculo.Errores[0]);
        var catalogo=prep.Catalogo;
        if(!catalogo.Unidades.Any(u=>u.Codigo==s.Unidad.Trim()))throw new InvalidOperationException("La unidad no está disponible para este usuario.");
        var cuentas=ConciliacionGastosBancarios.Claves.Where(c=>s.Cuentas.TryGetValue(c,out var v)&&!string.IsNullOrWhiteSpace(v)).ToDictionary(c=>c,c=>s.Cuentas[c].Trim());
        if(cuentas.Values.Any(c=>!catalogo.Cuentas.Any(x=>x.Codigo==c)))throw new InvalidOperationException("Una cuenta no está activa, no es contable o no corresponde a pesos.");
        var debe=ConciliacionGastosBancarios.LineasDebe(calculo,cuentas);
        var total=calculo.Total;
        if(debe.Sum(l=>l.Importe)!=total)throw new InvalidOperationException("Los renglones no suman el total de gastos e impuestos.");
        var cfg=catalogo.Config;
        if(cfg.TipoRegistro.Trim().Length>0&&!catalogo.Subdiarios.Any(t=>t.Codigo==cfg.TipoRegistro.Trim()))throw new InvalidOperationException("El subdiario configurado ya no existe.");
        var fecha=s.Fecha.Date;
        var periodo=await PeriodoAbiertoAsync(cn,tx,fecha,ct);
        var desde=fecha<prep.Desde ? fecha : prep.Desde;var hasta=fecha>prep.Hasta ? fecha : prep.Hasta;
        if((hasta-desde).TotalDays>366)throw new InvalidOperationException("La fecha del asiento debe estar dentro de los 366 días del extracto.");
        var (suc,numero,asiento,talUnidad)=await ReservarNumeracionCcAsync(cn,tx,fecha,s.Unidad,s.Talonario,catalogo,periodo,cfg.TipoRegistro,ct,prep.Tc);
        var tipo=CodigoPk.Format(cfg.TipoRegistro,4);
        var libro=ConciliacionGastosBancarios.ArmarLibroIva(calculo,prep.Config);
        var args=new DynamicParameters(new{tc=prep.Tc,periodo=periodo.ToString(),mes=fecha.Month,asiento,tipo,fecha,detalle=s.Detalle.Trim(),suc,numero=numero.ToString("D8"),total,
            moneda=CodigoPk.Format("1",4),unidad=CodigoPk.Format(s.Unidad,4),caja=CodigoPk.Format(string.IsNullOrWhiteSpace(user.Caja)?"1":user.Caja,4),usuario=users.GetCurrentUserName(),
            referencia=$"GASTOS {fecha:MM/yyyy}"});
        const string ColumnasComunes="""
            CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,
            TIPO_REG,CONTABILIZADO,FechaHora_Grabacion,FechaSubdiario,USUARIO_LOGEADO,IDCAJAS,UNEGOCIO,VENCIMIENTO,NroComprobanteBancario
            """;
        const string ValoresComunes="""
            @cuenta,@secuencia,@mes,@asiento,@fecha,@detalle,@tc,@suc,@numero,'X',@sentido,@importe,@moneda,1,@periodo,@total,
            @tipo,0,GETDATE(),@fecha,@usuario,@caja,@unidad,@fecha,@referencia
            """;
        // El Libro de IVA Compras sale del renglón del banco (secuencia 1), como en la liquidación de Alfa Gestión.
        args.Add("cuenta",cuenta);args.Add("importe",total);args.Add("secuencia",1);args.Add("sentido","H");
        args.Add("cabNombre",string.IsNullOrWhiteSpace(prep.CuentaBancoNombre) ? null : prep.CuentaBancoNombre);
        args.Add("cabCuit",string.IsNullOrWhiteSpace(prep.CuitBanco) ? null : prep.CuitBanco);
        args.Add("cabCondIva",string.IsNullOrWhiteSpace(prep.CondIvaBanco) ? null : prep.CondIvaBanco);
        args.Add("neto",libro.NetoGravado);args.Add("noGravado",libro.NetoNoGravado);args.Add("alicuota",libro.Alicuota);args.Add("iva",libro.Iva);
        // Código de comprobante de ARCA del CC X según la tabla de comprobantes (Alfa Gestión); sin él, el libro avisa que no va al TXT.
        var codigoArca=await LibroIvaService.CodigoArcaDeTablaAsync(cn,tx,prep.Tc,"X",ct);
        args.Add("codDgi",codigoArca is { } ca ? ca.ToString().PadLeft(4) : null);
        args.Add("percIva",libro.PercepcionIva);args.Add("percIibb",libro.PercepcionIibb);args.Add("percGan",libro.PercepcionGanancias);args.Add("totalLibro",libro.Total);
        await cn.ExecuteAsync(Q($$"""
            INSERT dbo.MV_ASIENTOS({{ColumnasComunes}},CABCUENTA,CABNOMBRE,CABCUIT,CABCONDIVA,LIVA_TIPO,LIVA_ImpNetoGrav,LIVA_ImpNetoNGrav,LIVA_AlicIVA,LIVA_ImpIVA,
                LIVA_Ret_Perc,LIVA_Ret_IBtos,LIVA_Ret_Ganancias,LIVA_TOTAL,CodCpteDGI)
            VALUES({{ValoresComunes}},@cuenta,@cabNombre,@cabCuit,@cabCondIva,'COMPRAS',@neto,@noGravado,@alicuota,@iva,@percIva,@percIibb,@percGan,@totalLibro,@codDgi);
            """,args));
        for(var i=0;i<debe.Count;i++)
        {
            args.Add("cuenta",debe[i].Cuenta);args.Add("importe",debe[i].Importe);args.Add("secuencia",i+2);args.Add("sentido","D");
            await cn.ExecuteAsync(Q($"INSERT dbo.MV_ASIENTOS({ColumnasComunes},CABCUENTA,CABNOMBRE) VALUES({ValoresComunes},NULL,NULL);",args));
        }
        var lineas=debe.Count+1;
        if(await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",args))!=lineas
            || await cn.ExecuteScalarAsync<decimal>(Q("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",args))!=0)
            throw new InvalidOperationException("El asiento no quedó balanceado. No se confirmó la operación.");
        await cn.ExecuteAsync(Q("UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO=@proximo WHERE LTRIM(RTRIM(CODIGO))=@tc AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@talUnidad;",
            new{proximo=checked(numero+1),s.Talonario,talUnidad,tc=prep.Tc}));
        // Un solo renglón del banco contra todos los movimientos de gastos del extracto.
        var filtro=new ConciliacionCompararFiltro(cuenta,desde,hasta);
        var claves=new ConciliacionAsientoClave(periodo.ToString(),checked((byte)fecha.Month),asiento,tipo,1);
        var p=new DynamicParameters(ArgumentosComparacion(filtro));p.Add("asiento",asiento);p.Add("periodo",periodo.ToString());p.Add("tipo",tipo);p.Add("mes",fecha.Month);
        var lineaBanco=await cn.QuerySingleAsync<ConciliacionFilaAlfa>(Q(CompararBaseSql+"SELECT * FROM #Alfa WHERE Periodo=@periodo AND Mes=@mes AND Asiento=@asiento AND TipoRegistro=@tipo AND Secuencia=1;",p));
        var vinculo=await ConciliarEnTransaccionAsync(cn,tx,new(s.Operacion,filtro,calculo.Movimientos.Select(m=>new ConciliacionBancoSeleccion(m.Id,m.Huella)).ToList(),
            [new(claves,lineaBanco.Huella)]),ct,MaxMovimientosGastos,permitirContrarios:true);
        var comprobante=suc+numero.ToString("D8")+"X";
        await cn.ExecuteAsync(Q("""
            INSERT dbo.BK_AsientoGastos(Operacion,ImportacionId,SolicitudHash,SolicitudJson,CuentaBanco,Periodo,Mes,NumeroAsiento,TipoRegistro,Comprobante,Unidad,ConciliacionId,Total,Usuario,Tc)
            VALUES(@Operacion,@ImportacionId,@hash,@json,@cuenta,@periodo,@mes,@asiento,@tipo,@comprobante,@unidad,@vinculo,@total,@usuario,@tc);
            """,new{tc=prep.Tc,s.Operacion,s.ImportacionId,hash,json,cuenta,periodo=periodo.ToString(),mes=fecha.Month,asiento,tipo,comprobante,unidad=s.Unidad.Trim(),vinculo,total,usuario=users.GetCurrentUserName()}));
        await cn.ExecuteAsync(Q("INSERT dbo.BK_AsientoGastosMovimiento(Operacion,MovimientoId,Concepto,Importe) VALUES(@Operacion,@Id,@Clave,@Importe);",
            calculo.Movimientos.Select(m=>new{s.Operacion,m.Id,m.Clave,m.Importe}).ToList()));
        await cn.ExecuteAsync(Q("""
            UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Registrado' WHERE MovimientoId IN(SELECT MovimientoId FROM dbo.BK_AsientoGastosMovimiento WHERE Operacion=@Operacion);
            INSERT dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
                SELECT MovimientoId,'GENERAR_ASIENTO_GASTOS','OK',@mensaje,@usuario FROM dbo.BK_AsientoGastosMovimiento WHERE Operacion=@Operacion;
            """,new{s.Operacion,usuario=users.GetCurrentUserName(),mensaje=$"{prep.Tc} {comprobante}; asiento {asiento}; conciliación {vinculo}"}));
        // Historial de acciones del comprobante (el visor de asientos lo lee de V_MV_CpteAcciones).
        await ComprobanteAcciones.RegistrarAsync(cn,tx,prep.Tc,comprobante,ComprobanteAcciones.Alta,users.GetCurrentUserName(),"AlfaCore · Gastos e impuestos bancarios (conciliación)",ct,
            comentario:"Alta desde AlfaCore",asiento:asiento,periodo:periodo.ToString(),tipoReg:tipo,fecha:fecha,cuenta:cuenta);
        await GuardarConfigGastosAsync(cn,tx,new ConciliacionGastosConfig{Cuentas=cuentas,AlicuotaIva=prep.Config.AlicuotaIva,Detalle=prep.Config.Detalle,Patrones=prep.Config.Patrones},ct);
        VerificarSesion(session);ct.ThrowIfCancellationRequested();
        return new(){Operacion=s.Operacion,Comprobante=comprobante,Tc=prep.Tc,Periodo=periodo.ToString(),Mes=checked((byte)fecha.Month),NumeroAsiento=asiento,TipoRegistro=tipo,ConciliacionId=vinculo};
    }

    private static Task<int> GuardarConfigGastosAsync(SqlConnection cn,SqlTransaction tx,ConciliacionGastosConfig config,CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            IF EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WITH(UPDLOCK,HOLDLOCK) WHERE CLAVE=@clave)
                UPDATE dbo.TA_CONFIGURACION SET VALOR='JSON',ValorAux=@json,FechaHora_Modificacion=GETDATE() WHERE CLAVE=@clave;
            ELSE INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION)
                VALUES('CAJA',@clave,'JSON',@json,N'Cuentas del asiento mensual de gastos e impuestos bancarios');
            """,new{clave=ClaveGastos,json=JsonSerializer.Serialize(config)},tx,cancellationToken:ct));
}
