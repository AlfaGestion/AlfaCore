using System.Data;
using System.Security.Cryptography;
using System.Text;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed record LiquidacionCatalogo(ConciliacionAsientoCatalogo Catalogo,string Tc);
public sealed record LiquidacionSolicitud(Guid Operacion,long ImportacionId,long DetalleId,string Unidad,int Talonario,string Detalle,bool IncluirLibroIva=true,bool Unificado=true,DateTime? Fecha=null);
public sealed record LiquidacionGenerado(DateTime Fecha,string Comprobante,int NumeroAsiento,decimal Bruto);
public sealed record LiquidacionResultado(IReadOnlyList<LiquidacionGenerado> Generados,int Omitidos,string? Error);

// Graba el asiento de liquidación (un asiento por día de acreditación) con el mismo circuito que el de gastos bancarios:
// numeración CC compartida con Alfa Gestión, período abierto, conciliación de los créditos contra el renglón del banco e historial.
public sealed partial class ConciliacionBancariaService
{
    private const string ClaveLiquidacionComprobante="CONCILIACION-NAVE-COMPROBANTE";

    public Task<LiquidacionCatalogo> LiquidacionCatalogoAsync(CancellationToken ct=default)
        => Ejecutar("LiquidacionCatalogo",async(cn,token)=>
        {
            var tc=await LeerComprobanteAsync(cn,null,ClaveLiquidacionComprobante,"CC",token);
            return new LiquidacionCatalogo(await AsientoCatalogoEnConexionAsync(cn,null,token,tc),tc);
        },ct);

    public Task<LiquidacionResultado> GenerarLiquidacionAsync(LiquidacionSolicitud s,CancellationToken ct=default)
        => Ejecutar("LiquidacionGenerar",async(cn,token)=>
        {
            if(s.Operacion==Guid.Empty||s.Detalle.Trim().Length is <3 or >200) throw new InvalidOperationException("Revisá el detalle del asiento (entre 3 y 200 caracteres).");
            if(!await cn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT CONVERT(bit,CASE WHEN OBJECT_ID('dbo.BK_AsientoLiquidacion','U') IS NULL THEN 0 ELSE 1 END);",cancellationToken:token)))
                throw new InvalidOperationException("Aplicá la actualización de asientos de liquidación antes de generar los asientos.");
            // La propuesta se recalcula en el servidor: no se confía en lo que muestra la pantalla.
            var prop=await PrepararLiquidacionEnConexionAsync(cn,s.DetalleId,s.ImportacionId,token);
            if(prop.Errores.Count>0) throw new InvalidOperationException(prop.Errores[0]);
            var generados=new List<LiquidacionGenerado>();var omitidos=0;string? error=null;
            // Un solo asiento por todo el período (por defecto) o uno por día de acreditación.
            var documentos=new List<(LiquidacionDia Dia,DateTime Desde,DateTime Hasta)>();
            if(s.Unificado)
            {
                var desde=prop.Dias.Min(d=>d.Fecha);var hasta=prop.Dias.Max(d=>d.Fecha);
                var lineas=prop.Dias.SelectMany(d=>d.Lineas).GroupBy(l=>(l.Medio,l.Cuenta))
                    .Select(g=>new LiquidacionLinea(g.Key.Medio,g.Key.Cuenta,g.Sum(x=>x.Operaciones),g.Sum(x=>x.Bruto),g.Sum(x=>x.Comision),g.Sum(x=>x.IvaComision),g.Sum(x=>x.Neto))).ToList();
                var unico=new LiquidacionDia((s.Fecha??hasta).Date,lineas){Movimientos=prop.Dias.SelectMany(d=>d.Movimientos).Distinct().OrderBy(x=>x).ToList()};
                documentos.Add((unico,desde,hasta));
            }
            else documentos.AddRange(prop.Dias.Select(d=>(d,d.Fecha,d.Fecha)));
            foreach(var (dia,desdeCreditos,hastaCreditos) in documentos)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
                    var g=await GenerarLiquidacionDiaAsync(cn,tx,s,prop,dia,desdeCreditos,hastaCreditos,token);
                    if(g is null){omitidos++;tx.Rollback();continue;}
                    tx.Commit();generados.Add(g);
                }
                catch(InvalidOperationException ex)
                {
                    // Cada día es una transacción: lo ya generado queda firme y se informa dónde se detuvo.
                    error=$"{dia.Fecha:dd/MM/yyyy}: {ex.Message}";break;
                }
            }
            return new LiquidacionResultado(generados,omitidos,error);
        },ct);

    private static Guid OperacionDia(Guid baseOp,DateTime fecha)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"{baseOp:N}|{fecha:yyyyMMdd}")));

    private async Task<LiquidacionGenerado?> GenerarLiquidacionDiaAsync(SqlConnection cn,SqlTransaction tx,LiquidacionSolicitud s,LiquidacionPropuesta prop,LiquidacionDia dia,DateTime desdeCreditos,DateTime hastaCreditos,CancellationToken ct)
    {
        var session=sessions.GetActiveSession()!.Id;
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:ct);
        var operacion=OperacionDia(s.Operacion,dia.Fecha);
        var cuentaBanco=prop.CuentaBanco;
        await BloquearCuenta(cn,tx,cuentaBanco,ct);
        // Un día de un extracto se liquida una sola vez.
        if(await cn.ExecuteScalarAsync<bool>(Q("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_AsientoLiquidacion WITH(UPDLOCK,HOLDLOCK) WHERE ImportacionId=@i AND Fecha=@f) THEN 1 ELSE 0 END);",
                new{i=s.ImportacionId,f=dia.Fecha}))) return null;
        var user=await AsientoUsuarioAsync(cn,tx,ct);
        if(!user.Admin&&user.Unidad!=s.Unidad.Trim()) throw new InvalidOperationException("La unidad de negocio no está autorizada.");
        var tc=await LeerComprobanteAsync(cn,tx,ClaveLiquidacionComprobante,"CC",ct);
        var catalogo=await AsientoCatalogoEnConexionAsync(cn,tx,ct,tc);
        if(!catalogo.Unidades.Any(u=>u.Codigo==s.Unidad.Trim())) throw new InvalidOperationException("La unidad no está disponible para este usuario.");
        var cfg=catalogo.Config;
        if(cfg.TipoRegistro.Trim().Length>0&&!catalogo.Subdiarios.Any(t=>t.Codigo==cfg.TipoRegistro.Trim())) throw new InvalidOperationException("El subdiario configurado ya no existe.");

        // Renglones: Debe banco (neto), Debe comisión + IVA, Haber cada medio de pago (bruto).
        var lineas=new List<(string Cuenta,decimal Importe,string Sentido)>{(cuentaBanco,dia.Neto,"D")};
        if(dia.Comision>0) lineas.Add((prop.CuentaComision,dia.Comision,"D"));
        if(dia.IvaComision>0) lineas.Add((prop.CuentaIva,dia.IvaComision,"D"));
        if(dia.Comision<0||dia.IvaComision<0) throw new InvalidOperationException("La comisión o su IVA del día son negativos (devoluciones). Revisá el detalle.");
        // Libro de IVA Compras: neto gravado = comisión, IVA = IVA de la comisión; el total del libro es la factura del procesador.
        var alicuota=AlicuotaLibro(dia.Comision,dia.IvaComision) ?? throw new InvalidOperationException("El IVA de la comisión no corresponde a una alícuota válida.");
        var totalLibro=dia.Comision+dia.IvaComision;
        // Un renglón de Haber por cuenta (varios medios pueden compartir cuenta).
        foreach(var g in dia.Lineas.Where(l=>l.Bruto!=0).GroupBy(l=>l.Cuenta)) lineas.Add((g.Key,g.Sum(l=>l.Bruto),"H"));
        if(lineas.Any(l=>l.Importe<=0)) throw new InvalidOperationException("El día tiene importes negativos o en cero que no se pueden asentar. Revisá el detalle.");
        if(lineas.Where(l=>l.Sentido=="D").Sum(l=>l.Importe)!=lineas.Where(l=>l.Sentido=="H").Sum(l=>l.Importe))
            throw new InvalidOperationException("El asiento no balancea: bruto distinto de neto más comisión e IVA.");
        // Banco y tarjetas no están en la lista de cuentas de gastos del catálogo: se validan contra sus propias listas.
        var validas=catalogo.Cuentas.Select(c=>c.Codigo).Concat(catalogo.Bancos.Select(b=>b.Codigo)).Concat(prop.CuentasTarjeta.Select(c=>c.Codigo)).ToHashSet();
        if(lineas.Any(l=>l.Cuenta.Length==0||!validas.Contains(l.Cuenta)))
            throw new InvalidOperationException("Una cuenta no está activa, no es contable o no corresponde a pesos.");
        var total=lineas.Where(l=>l.Sentido=="D").Sum(l=>l.Importe);

        // Los créditos del día deben seguir pendientes y sin cambios.
        var desdeF=desdeCreditos<dia.Fecha ? desdeCreditos : dia.Fecha;var hastaF=hastaCreditos>dia.Fecha ? hastaCreditos : dia.Fecha;
        if((hastaF-desdeF).TotalDays>366) throw new InvalidOperationException("La fecha del asiento debe estar dentro de los 366 días de los cobros liquidados.");
        var filtro=new ConciliacionCompararFiltro(cuentaBanco,desdeF,hastaF);
        var args0=new DynamicParameters(ArgumentosComparacion(filtro));
        var ids=dia.Movimientos.ToHashSet();
        // Se filtra en memoria: un asiento por todo el período puede tener miles de créditos (límite de parámetros de IN).
        var banco=(await cn.QueryAsync<ConciliacionFilaBanco>(Q(CompararBaseSql+"SELECT b.* FROM #Banco b;",args0))).Where(b=>ids.Contains(b.Id)).ToList();
        if(banco.Count!=dia.Movimientos.Count||banco.Any(b=>b.ConciliacionId.HasValue||b.Importe<=0))
            throw new InvalidOperationException("Hay créditos del día ya conciliados o modificados. Actualizá la conciliación.");
        if(banco.Sum(b=>b.Importe)!=dia.Neto) throw new InvalidOperationException("El neto del día no coincide con lo acreditado por el banco.");

        var fecha=dia.Fecha.Date;
        var periodo=await PeriodoAbiertoAsync(cn,tx,fecha,ct);
        var (suc,numero,asiento,talUnidad)=await ReservarNumeracionCcAsync(cn,tx,fecha,s.Unidad,s.Talonario,catalogo,periodo,cfg.TipoRegistro,ct,tc);
        var tipo=CodigoPk.Format(cfg.TipoRegistro,4);
        var detalle=$"{s.Detalle.Trim()} {fecha:dd-MM-yyyy}";
        var args=new DynamicParameters(new{tc,periodo=periodo.ToString(),mes=fecha.Month,asiento,tipo,fecha,detalle=detalle.Length>200?detalle[..200]:detalle,suc,numero=numero.ToString("D8"),total,
            moneda=CodigoPk.Format("1",4),unidad=CodigoPk.Format(s.Unidad,4),caja=CodigoPk.Format(string.IsNullOrWhiteSpace(user.Caja)?"1":user.Caja,4),usuario=users.GetCurrentUserName(),
            referencia=$"LIQUIDACION {fecha:dd/MM/yyyy}"});
        const string ColumnasComunes="""
            CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,
            TIPO_REG,CONTABILIZADO,FechaHora_Grabacion,FechaSubdiario,USUARIO_LOGEADO,IDCAJAS,UNEGOCIO,VENCIMIENTO,NroComprobanteBancario
            """;
        const string ValoresComunes="""
            @cuenta,@secuencia,@mes,@asiento,@fecha,@detalle,@tc,@suc,@numero,'X',@sentido,@importe,@moneda,1,@periodo,@total,
            @tipo,0,GETDATE(),@fecha,@usuario,@caja,@unidad,@fecha,@referencia
            """;
        // El proveedor del libro es el titular de la cuenta bancaria (quien factura la comisión), como en el asiento de gastos.
        var banco0=await cn.QuerySingleOrDefaultAsync<(string Nombre,string Cuit,string CondIva)?>(Q("""
            SELECT LTRIM(RTRIM(ISNULL(c.DESCRIPCION,''))) Nombre,LTRIM(RTRIM(ISNULL(a.NUMERO_DOCUMENTO,''))) Cuit,ISNULL(a.IVA,'') CondIva
            FROM dbo.MA_CUENTAS c LEFT JOIN dbo.MA_CUENTASADIC a ON LTRIM(RTRIM(a.CODIGO))=LTRIM(RTRIM(c.CODIGO))
            WHERE LTRIM(RTRIM(c.CODIGO))=@cuenta;
            """,new{cuenta=cuentaBanco}));
        string? N(string? v)=>string.IsNullOrWhiteSpace(v) ? null : v;
        var codigoArca=await LibroIvaService.CodigoArcaDeTablaAsync(cn,tx,tc,"X",ct);
        args.Add("cabNombre",N(banco0?.Nombre));args.Add("cabCuit",N(banco0?.Cuit));args.Add("cabCondIva",N(banco0?.CondIva));
        args.Add("codDgi",codigoArca is { } ca ? ca.ToString().PadLeft(4) : null);
        args.Add("neto",dia.Comision);args.Add("alicuota",alicuota);args.Add("iva",dia.IvaComision);args.Add("totalLibro",totalLibro);
        for(var i=0;i<lineas.Count;i++)
        {
            args.Add("cuenta",lineas[i].Cuenta);args.Add("importe",lineas[i].Importe);args.Add("secuencia",i+1);args.Add("sentido",lineas[i].Sentido);
            if(i==0&&totalLibro>0&&s.IncluirLibroIva)
                // Renglón del banco: lleva el registro del Libro de IVA Compras.
                await cn.ExecuteAsync(Q($"""
                    INSERT dbo.MV_ASIENTOS({ColumnasComunes},CABCUENTA,CABNOMBRE,CABCUIT,CABCONDIVA,LIVA_TIPO,LIVA_ImpNetoGrav,LIVA_ImpNetoNGrav,LIVA_AlicIVA,LIVA_ImpIVA,
                        LIVA_Ret_Perc,LIVA_Ret_IBtos,LIVA_Ret_Ganancias,LIVA_TOTAL,CodCpteDGI)
                    VALUES({ValoresComunes},@cuenta,@cabNombre,@cabCuit,@cabCondIva,'COMPRAS',@neto,0,@alicuota,@iva,0,0,0,@totalLibro,@codDgi);
                    """,args));
            else
                await cn.ExecuteAsync(Q($"INSERT dbo.MV_ASIENTOS({ColumnasComunes}) VALUES({ValoresComunes});",args));
        }
        if(await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",args))!=lineas.Count
            || await cn.ExecuteScalarAsync<decimal>(Q("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",args))!=0)
            throw new InvalidOperationException("El asiento no quedó balanceado. No se confirmó la operación.");
        await cn.ExecuteAsync(Q("UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO=@proximo WHERE LTRIM(RTRIM(CODIGO))=@tc AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@talUnidad;",
            new{proximo=checked(numero+1),s.Talonario,talUnidad,tc}));

        // Los créditos del día se concilian contra el renglón del banco (secuencia 1).
        var claves=new ConciliacionAsientoClave(periodo.ToString(),checked((byte)fecha.Month),asiento,tipo,1);
        var p=new DynamicParameters(ArgumentosComparacion(filtro));p.Add("asiento",asiento);p.Add("periodo",periodo.ToString());p.Add("tipo",tipo);p.Add("mes",fecha.Month);
        var lineaBanco=await cn.QuerySingleAsync<ConciliacionFilaAlfa>(Q(CompararBaseSql+"SELECT * FROM #Alfa WHERE Periodo=@periodo AND Mes=@mes AND Asiento=@asiento AND TipoRegistro=@tipo AND Secuencia=1;",p));
        var vinculo=await ConciliarEnTransaccionAsync(cn,tx,new(operacion,filtro,banco.Select(b=>new ConciliacionBancoSeleccion(b.Id,b.Huella)).ToList(),
            [new(claves,lineaBanco.Huella)]),ct,MaxMovimientosGastos);
        var comprobante=suc+numero.ToString("D8")+"X";
        var hash=Hash($"{operacion:N}|{s.DetalleId}|{fecha:yyyyMMdd}|{total}|{string.Join(",",dia.Movimientos)}");
        await cn.ExecuteAsync(Q("""
            INSERT dbo.BK_AsientoLiquidacion(Operacion,ImportacionId,DetalleId,Fecha,SolicitudHash,CuentaBanco,Periodo,Mes,NumeroAsiento,TipoRegistro,Comprobante,Tc,Unidad,ConciliacionId,Bruto,Comision,IvaComision,Neto,Usuario)
            VALUES(@operacion,@ImportacionId,@DetalleId,@fecha,@hash,@cuentaBanco,@periodo,@mes,@asiento,@tipo,@comprobante,@tc,@unidad,@vinculo,@bruto,@com,@iva,@neto,@usuario);
            """,new{operacion,s.ImportacionId,s.DetalleId,fecha,hash,cuentaBanco,periodo=periodo.ToString(),mes=fecha.Month,asiento,tipo,comprobante,tc,unidad=s.Unidad.Trim(),vinculo,
                bruto=dia.Bruto,com=dia.Comision,iva=dia.IvaComision,neto=dia.Neto,usuario=users.GetCurrentUserName()}));
        await cn.ExecuteAsync(Q("INSERT dbo.BK_AsientoLiquidacionMovimiento(Operacion,MovimientoId,Importe) VALUES(@operacion,@Id,@Importe);",
            banco.Select(b=>new{operacion,b.Id,b.Importe}).ToList()));
        await cn.ExecuteAsync(Q("""
            UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Registrado' WHERE MovimientoId IN(SELECT MovimientoId FROM dbo.BK_AsientoLiquidacionMovimiento WHERE Operacion=@operacion);
            INSERT dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
                SELECT MovimientoId,'GENERAR_ASIENTO_LIQUIDACION','OK',@mensaje,@usuario FROM dbo.BK_AsientoLiquidacionMovimiento WHERE Operacion=@operacion;
            """,new{operacion,usuario=users.GetCurrentUserName(),mensaje=$"{tc} {comprobante}; asiento {asiento}; conciliación {vinculo}"}));
        await ComprobanteAcciones.RegistrarAsync(cn,tx,tc,comprobante,ComprobanteAcciones.Alta,users.GetCurrentUserName(),"AlfaCore · Liquidación de procesador de cobro (conciliación)",ct,
            comentario:"Alta desde AlfaCore",asiento:asiento,periodo:periodo.ToString(),tipoReg:tipo,fecha:fecha,cuenta:cuentaBanco);
        VerificarSesion(session);ct.ThrowIfCancellationRequested();
        return new(fecha,comprobante,asiento,dia.Bruto);
    }
}
