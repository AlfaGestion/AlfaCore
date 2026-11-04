using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    internal static string HuellaRevision(CompraCargaBorrador d,CompraCargaProveedor p,CompraCargaContexto c,CompraCargaVista v)
        => HashCompra(JsonSerializer.Serialize(new{Borrador=d,Proveedor=p,Contexto=c,v.Bases,v.Asiento,v.Total}));
    private static string HashCompra(string json)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    public Task<CompraCargaRegistrada> RegistrarAsync(CompraCargaRegistroSolicitud solicitud,int? baseEsperada=null,CancellationToken ct=default)
    {
        // Copia estable antes del primer await: la UI no puede alterar lo que se está confirmando.
        var copia=JsonSerializer.Deserialize<CompraCargaRegistroSolicitud>(JsonSerializer.Serialize(solicitud))!;
        return Ejecutar(baseEsperada,"Registrar",async(c,t)=>
        {
            using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var actor=usuarios.GetCurrentUserName();var sistema=usuarios.CurrentUser?.SystemCode;
            var resultado=await RegistrarEnTransaccionAsync(c,tx,copia,t);
            if(!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out var actual)||actual!=copia.Sesion||
                usuarios.GetCurrentUserName()!=actor||usuarios.CurrentUser?.SystemCode!=sistema)
                throw new OperationCanceledException("La sesión cambió. No se confirmó la compra.");
            t.ThrowIfCancellationRequested();tx.Commit();return resultado;
        },ct);
    }

    // Permite verificar el circuito real con una transacción externa y ROLLBACK en la base de pruebas.
    internal async Task<CompraCargaRegistrada> RegistrarEnTransaccionAsync(SqlConnection cn,SqlTransaction tx,CompraCargaRegistroSolicitud s,CancellationToken ct=default)
    {
        if(s.Operacion==Guid.Empty||s.Huella.Length!=64||!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out var sesion)||sesion!=s.Sesion)
            throw new InvalidOperationException("Revisá el asiento en la sesión activa antes de confirmar.");
        if(!await permisos.HasAccessAsync(MenuKey,ct))throw new InvalidOperationException("No tenés permiso para registrar compras.");
        CommandDefinition Q(string sql,object? args=null)=>Cmd(sql,args,ct,tx);
        if(await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID('dbo.ALFACORE_COMPRA_OPERACION');"))==0)
            throw new InvalidOperationException("Aplicá las actualizaciones de Compras antes de registrar.");
        var bloqueo=await cn.ExecuteScalarAsync<int>(Q("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'ALFACORE_COMPRAS_REGISTRO',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @r;
            """));
        if(bloqueo<0)throw new InvalidOperationException("Hay otra compra guardándose. Reintentá en unos segundos.");
        var d=s.Borrador;var contexto=await Contexto(cn,ct,tx);
        if(!contexto.Unidades.Any(u=>u.Codigo==d.Unidad.Trim()))throw new InvalidOperationException("La unidad de negocio no está autorizada.");
        var hash=HashCompra(JsonSerializer.Serialize(new{s.Borrador,s.Huella}));
        var anterior=await cn.QuerySingleOrDefaultAsync<OperacionCompra>(Q("""
            SELECT SolicitudHash,Usuario,CompraId,Comprobante,Periodo,Mes,Asiento,TipoRegistro,Total
            FROM dbo.ALFACORE_COMPRA_OPERACION WITH(UPDLOCK,HOLDLOCK) WHERE Operacion=@Operacion;
            """,new{s.Operacion}));
        if(anterior is not null)
        {
            if(anterior.SolicitudHash!=hash||anterior.Usuario!=usuarios.GetCurrentUserName())throw new InvalidOperationException("La operación ya se utilizó con otros datos o usuario.");
            return new(anterior.CompraId,anterior.Comprobante,anterior.Periodo,anterior.Mes,anterior.Asiento,anterior.TipoRegistro.Trim(),anterior.Total);
        }
        var proveedor=await Proveedor(cn,d.Proveedor,ct,tx);
        await ValidarArticulosDirectosAsync(cn,d,contexto,ct,tx);
        var vista=CompraCargaPlanificador.Preparar(d,proveedor,contexto);
        ValidarRegistro(d,proveedor,contexto,vista,s.Huella);
        if(d.Tipo is "OC" or "RMC")return await RegistrarComercialAsync(cn,tx,s,hash,proveedor,contexto,vista,ct);
        CompraOrigenBloqueado? origenBloqueado=null;
        if(d.DocumentoRecibido is {} doc)
        {
            if(!await permisos.HasAccessAsync("D016004",ct))throw new InvalidOperationException("No tenés permiso para utilizar el documento recibido.");
            origenBloqueado=await BloquearOrigenAsync(cn,tx,d,ct);
            if(await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.ALFACORE_COMPRA_OPERACION WITH(UPDLOCK,HOLDLOCK) WHERE DocumentoId=@doc;",new{doc}))>0)
                throw new InvalidOperationException("El documento recibido ya generó una compra desde esta pantalla.");
        }
        var equivalencia=await cn.QuerySingleOrDefaultAsync<string>(Q("SELECT LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@clave;",new{clave="EQU_"+d.Tipo}));
        if(string.IsNullOrWhiteSpace(equivalencia)||equivalencia.Length>4)throw new InvalidOperationException("Configurá la equivalencia contable del tipo de comprobante.");
        var subdiarios=(await cn.QueryAsync<string>(Q("""
            SELECT DISTINCT LTRIM(RTRIM(s.Subdiario)) FROM dbo.MA_SUBDIARIOSCOMPROBANTES s
            INNER JOIN dbo.MA_SUBDIARIOS m ON LTRIM(RTRIM(m.CODIGO))=LTRIM(RTRIM(s.Subdiario)) WHERE LTRIM(RTRIM(s.TC))=@tc;
            """,new{tc=d.Tipo}))).ToList();
        if(subdiarios.Count>1)throw new InvalidOperationException("Hay varios subdiarios para el comprobante. Revisá la configuración.");
        var tipo=subdiarios.SingleOrDefault()??"";
        var ejercicios=(await cn.QueryAsync<EjercicioCompra>(Q("""
            SELECT PERIODO Periodo,CONVERT(bit,ISNULL(CERRADO,0)) Cerrado FROM dbo.MV_EJERCICIOS
            WHERE [FECHA DESDE]<=@fecha AND [FECHA HASTA]>=@fecha;
            """,new{fecha=d.Fecha.Date}))).ToList();
        if(ejercicios.Count!=1||ejercicios[0].Cerrado)throw new InvalidOperationException("La fecha debe pertenecer a un único ejercicio abierto.");
        var periodo=ejercicios[0].Periodo.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var id=vista.IdComprobante;var suc=id[..4];var numero=id.Substring(4,8);
        if(await cn.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.C_MV_CPTE WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(CUENTA))=@cuenta AND LTRIM(RTRIM(TC))=@tcOperativo AND IDCOMPROBANTE=@id;
            """,new{tcOperativo=d.Tipo,cuenta=proveedor.Codigo,id}))>0)
            throw new InvalidOperationException("La factura ya existe para este proveedor, incluso como importación o anulada. Revisala antes de cargar otra.");
        if(await cn.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC)) IN (@tcOperativo,@equivalencia)
              AND SUCURSAL=@suc AND NUMERO=@numero AND LETRA=@letra
              AND (LTRIM(RTRIM(CUENTA))=@cuenta OR LTRIM(RTRIM(CABCUENTA))=@cuenta);
            """,new{tcOperativo=d.Tipo,equivalencia,suc,numero,letra=d.Letra,cuenta=proveedor.Codigo}))>0)
            throw new InvalidOperationException("La factura ya tiene registración contable o fiscal para este proveedor.");
        var ficha=await cn.QuerySingleAsync<FichaProveedorCompra>(Q("""
            SELECT ISNULL(CALLE,'') Calle,ISNULL(NUMERO,'') Numero,ISNULL(LOCALIDAD,'') Localidad,ISNULL(CPOSTAL,'') Postal,
              LTRIM(RTRIM(ISNULL(PROVINCIA,''))) Provincia,ISNULL(TELEFONO,'') Telefono,LTRIM(RTRIM(ISNULL(DOCUMENTO_TIPO,''))) DocumentoTipo,
              LTRIM(RTRIM(ISNULL(idCond_Cpra_Vta,''))) Condicion FROM dbo.VT_PROVEEDORES WHERE LTRIM(RTRIM(CODIGO))=@cuenta;
            """,new{cuenta=proveedor.Codigo}));
        var fiscal=CompraCargaFiscal.Crear(d,vista,contexto.TasasFiscales);
        var monotributo=await cn.ExecuteScalarAsync<string>(Q("SELECT LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE='MonotributoAlIVA';"));
        var liva=proveedor.CondicionIva!="5"||string.Equals(monotributo,"SI",StringComparison.OrdinalIgnoreCase)?"COMPRAS":"";
        var talonario=contexto.Tipos.FirstOrDefault(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad==d.Unidad)
            ??contexto.Tipos.Single(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad.Length==0);
        var columnaDgi=d.Letra=="A"?"CpteDGIA":"CpteDGIC"; // Solo nombres constantes; nunca entrada de usuario.
        var codigoDgi=await cn.QuerySingleAsync<string?>(Q($"SELECT LTRIM(RTRIM({columnaDgi})) FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))=@tcOperativo AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@Unidad;",new{tcOperativo=d.Tipo,talonario.Talonario,talonario.Unidad}));
        if(!talonario.Externo)
        {
            var siguiente=await NumeroInternoAsync(cn,d,talonario,ct,tx);
            if(siguiente.Sucursal!=suc||siguiente.Numero!=numero)throw new InvalidOperationException("Otra terminal consumió la numeración interna. Revisá nuevamente antes de registrar.");
            var columna=d.Letra=="A"?"A_ULTIMO_NRO":"C_ULTIMO_NRO";
            await cn.ExecuteAsync(Q($"UPDATE dbo.V_TA_CPTE SET {columna}=@siguiente WHERE LTRIM(RTRIM(CODIGO))=@Codigo AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@Unidad;",new{siguiente=long.Parse(numero)+1,talonario.Codigo,talonario.Talonario,talonario.Unidad}));
        }
        var asiento=await NumeracionAsiento.SiguienteAsync(cn,tx,periodo,d.Fecha.Month,tipo,ct);
        var signoFiscal=d.Tipo=="NCC"?-1m:1m;
        var p=new DynamicParameters(new{tcOperativo=d.Tipo.Trim(),cuenta=proveedor.Codigo.Trim(),nombre=proveedor.Nombre,documento=proveedor.Documento.Trim(),
            fecha=d.Fecha.Date,fechaSub=d.FechaContable.Date,vencimiento=d.Vencimiento.Date,id,suc,numero,letra=d.Letra,
            unidad=CodigoPk.Format(d.Unidad,4),motivo=CodigoPk.Format(d.Motivo,4),ivaCond=CodigoPk.Format(proveedor.CondicionIva,4),
            moneda=CodigoPk.Format("1",4),usuario=usuarios.GetCurrentUserName(),domicilio=(ficha.Calle+" "+ficha.Numero).Trim(),ficha.Localidad,
            ficha.Postal,provincia=CodigoPk.Format(ficha.Provincia,4),ficha.Telefono,docTipo=CodigoPk.Format(ficha.DocumentoTipo,4),condicion=CodigoPk.Format(ficha.Condicion,4),
            total=vista.Total,neto=vista.Neto,netoOtros=d.Lineas.Where(l=>l.Articulo.Length==0).Sum(l=>l.Neto),netoInsumos=d.Lineas.Where(l=>l.Articulo.Length>0).Sum(l=>l.Neto),gravadoCab=fiscal.Gravado,noGravadoCab=fiscal.NoGravado,iva1Cab=fiscal.Importes[0],iva2Cab=fiscal.Importes[1],gravado=fiscal.Gravado*signoFiscal,noGravado=fiscal.NoGravado*signoFiscal,
            iva1=fiscal.Importes[0]*signoFiscal,iva2=fiscal.Importes[1]*signoFiscal,iva3=fiscal.Importes[2]*signoFiscal,iva4=fiscal.Importes[3]*signoFiscal,
            tasa1=fiscal.Tasas[0],tasa2=fiscal.Tasas[1],tasa3=fiscal.Tasas[2],tasa4=fiscal.Tasas[3],
            impInterno=d.ImpuestoInterno,impInternoFiscal=d.ImpuestoInterno*signoFiscal,percIva=d.PercepcionIva*signoFiscal,percIibb=d.PercepcionIibb*signoFiscal,percGan=d.PercepcionGanancias*signoFiscal,totalFiscal=vista.Total*signoFiscal,
            periodo,mes=d.Fecha.Month,asiento,tipo=CodigoPk.Format(tipo,4),tc=equivalencia.Trim(),liva,
            cai=d.AutorizacionFiscal.Trim(),venceCai=d.VencimientoAutorizacion?.Date,codigoDgi=string.IsNullOrWhiteSpace(codigoDgi)?null:CodigoPk.Format(codigoDgi,4)});
        var compra=await cn.ExecuteScalarAsync<int>(Q("""
            INSERT dbo.C_MV_CPTE(TC,IDCOMPROBANTE,CUENTA,FECHA,FechaSubdiario,VENCIMIENTO,NOMBRE,DOMICILIO,LOCALIDAD,CODIGOPOSTAL,
              IDPROVINCIA,TELEFONO,DOCUMENTOTIPO,DOCUMENTONUMERO,CONDICIONIVA,IDCOND_CPRA_VTA,IDMOTIVOCPRAVTA,
              IMPORTE,IMPORTE_S_IVA,ImporteOtrosConceptos,ImporteInsumos,ImporteImpuestosInternos,NetoGravado,NetoNoGravado,ImporteIva,ImporteIva2,AlicIva,AlicIva2,
              SUCURSAL,NUMERO,LETRA,UNEGOCIO,MONEDA,COTIZACION,USUARIO,FechaHora_Grabacion,COMENTARIOS)
            VALUES(@tcOperativo,@id,@cuenta,@fecha,@fechaSub,@vencimiento,@nombre,@domicilio,@Localidad,@Postal,@provincia,@Telefono,
              @docTipo,@documento,@ivaCond,@condicion,@motivo,@total,@neto,@netoOtros,@netoInsumos,@impInterno,@gravadoCab,@noGravadoCab,@iva1Cab,@iva2Cab,@tasa1,@tasa2,
              @suc,@numero,@letra,@unidad,@moneda,1,@usuario,GETDATE(),N'Compra desde AlfaCore');
            SELECT CONVERT(int,SCOPE_IDENTITY());
            """,p));
        foreach(var (linea,i) in vista.Asiento.Select((a,i)=>(a,i)))
        {
            p.Add("lineaCuenta",linea.Cuenta.Trim());p.Add("secuencia",i+1);p.Add("detalle",linea.Descripcion.Length>200?linea.Descripcion[..200]:linea.Descripcion);
            p.Add("sentido",linea.Debe>0?"D":"H");p.Add("importe",linea.Debe+linea.Haber);p.Add("cabecera",i==0);
            await cn.ExecuteAsync(Q(InsertarAsientoCompraSql,p));
            await RegistrarCentrosAsync(cn,tx,d,linea,periodo,tipo,asiento,ct);
        }
        for(var i=0;i<d.Lineas.Count&&d.CompraOrigenId is null;i++)
        {
            var linea=d.Lineas[i];if(linea.Articulo.Length>0)continue;
            await cn.ExecuteAsync(Q("""
                INSERT dbo.C_MV_CPTE_OBSERV(TC,IDCOMPROBANTE,CUENTA,TIPO_OBS,OBSERVACION,IMPORTE,IMPORTE_S_IVA,Secuencia)
                VALUES(@tcOperativo,@id,@cuenta,'OC',@descripcion,@importe,@neto,@secuencia);
                """,new{tcOperativo=d.Tipo,id,cuenta=proveedor.Codigo,descripcion=linea.Descripcion,importe=vista.Bases[i].Neto+vista.Bases[i].Iva,neto=vista.Bases[i].Neto,secuencia=i+1}));
        }
        await RegistrarMercaderiaAsync(cn,tx,d,vista,ct);
        await AplicarNotaAsync(cn,tx,d,vista,equivalencia,ct);
        var balance=await cn.ExecuteScalarAsync<decimal>(Q("""
            SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS
            WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;
            """,p));
        var cantidad=await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",p));
        if(balance!=0||cantidad!=vista.Asiento.Count)throw new InvalidOperationException("El asiento no quedó completo y balanceado. No se confirmó la compra.");
        await cn.ExecuteAsync(Q("""
            INSERT dbo.V_MV_CPTEACCIONES(TC,IDCOMPROBANTE,IDCOMPLEMENTO,TIPO_ACCION,FECHAHORA,USUARIO,PC,SYSTEMUSER,Periodo,Tipo_Reg,Fecha,Mes_Operativo,NUMEROASIENTO,Cuenta,Proceso)
            VALUES(@tcOperativo,@id,0,'A',GETDATE(),@usuario,HOST_NAME(),SYSTEM_USER,@periodo,@tipo,@fecha,@mes,@asiento,@cuenta,N'AlfaCore: carga de compra');
            """,p));
        await cn.ExecuteAsync(Q("""
            INSERT dbo.ALFACORE_COMPRA_OPERACION(Operacion,SolicitudHash,Usuario,DocumentoId,CompraId,Comprobante,Periodo,Mes,Asiento,TipoRegistro,Total,SolicitudJson)
            VALUES(@Operacion,@hash,@usuario,@DocumentoId,@compra,@id,@periodo,@mes,@asiento,@tipo,@total,@json);
            """,new{s.Operacion,hash,usuario=usuarios.GetCurrentUserName(),DocumentoId=d.DocumentoRecibido,compra,id,periodo,mes=d.Fecha.Month,asiento,tipo=CodigoPk.Format(tipo,4),total=vista.Total,json=JsonSerializer.Serialize(d)}));
        if(origenBloqueado is not null)await MarcarOrigenRegistradoAsync(cn,tx,origenBloqueado,compra,id,d.Tipo,ct);
        if(await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.ALFACORE_COMPRA_OPERACION') AND name='AsientoHuella';"))>0)
        {
            var huellaAsiento=await HuellaAsientoGuardadoAsync(cn,tx,new(compra,id,periodo,d.Fecha.Month,asiento,tipo,vista.Total),ct);
            await cn.ExecuteAsync(Q("UPDATE dbo.ALFACORE_COMPRA_OPERACION SET AsientoHuella=@huellaAsiento WHERE CompraId=@compra;",new{huellaAsiento,compra}));
        }
        ct.ThrowIfCancellationRequested();
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out sesion)||sesion!=s.Sesion)throw new OperationCanceledException("La sesión cambió.");
        return new(compra,id,periodo,d.Fecha.Month,asiento,tipo,vista.Total);
    }

    internal static void ValidarRegistro(CompraCargaBorrador d,CompraCargaProveedor p,CompraCargaContexto c,CompraCargaVista v,string huella)
    {
        if(d.Tipo is "OC" or "RMC"){ValidarRegistroComercial(d,p,c,v,huella);return;}
        if(v.Errores.Count>0)throw new InvalidOperationException(string.Join(" ",v.Errores));
        if(d.Tipo=="FCC"&&!p.SinOc&&d.CompraOrigenId is null)throw new InvalidOperationException("El proveedor requiere control de orden de compra. Esta etapa registra únicamente servicios y gastos habilitados sin OC.");
        if(p.Moneda is not ("" or "1"))throw new InvalidOperationException("Esta etapa registra proveedores y facturas en pesos.");
        if(d.Lineas.Any(l=>l.Neto!=CompraCargaPlanificador.Moneda(l.Neto)||l.IvaInformado is {} iva&&iva!=CompraCargaPlanificador.Moneda(iva))||
            new[]{d.PercepcionIva,d.PercepcionIibb,d.PercepcionGanancias,d.ImpuestoInterno,d.NotaAplicar,d.TotalDocumento??0}.Any(i=>i!=CompraCargaPlanificador.Moneda(i)))
            throw new InvalidOperationException("Los importes del documento deben tener como máximo dos decimales.");
        if(d.TotalDocumento is null||v.DiferenciaDocumento!=0||v.DiferenciaAsiento!=0)throw new InvalidOperationException("Informá el total del documento y corregí las diferencias antes de registrar.");
        if(HuellaRevision(d,p,c,v)!=huella)throw new InvalidOperationException("Los datos o la configuración cambiaron. Volvé a revisar el asiento antes de confirmar.");
        var tipo=c.Tipos.FirstOrDefault(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad==d.Unidad)
            ??c.Tipos.FirstOrDefault(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad.Length==0);
        if(tipo is null||tipo.DebeHaber!=(d.Tipo=="NCC"?"D":"H"))throw new InvalidOperationException("Revisá el sentido contable configurado para el comprobante.");
        if(d.Letra=="A"&&p.CondicionIva!="1"||d.Letra=="C"&&p.CondicionIva!="5")throw new InvalidOperationException("Esta etapa registra A de responsables inscriptos o C de monotributistas. Revisá la condición fiscal del proveedor.");
        var cai=d.AutorizacionFiscal.Trim();
        if(cai.Length>0&&(cai.Length!=14||!cai.All(char.IsAsciiDigit)||d.VencimientoAutorizacion is null))throw new InvalidOperationException("Informá los 14 dígitos y el vencimiento de la autorización fiscal, o dejá ambos vacíos.");
        if(cai.Length==0&&d.VencimientoAutorizacion is not null)throw new InvalidOperationException("Completá el número de autorización fiscal.");
        if(d.VencimientoAutorizacion?.Date<d.Fecha.Date)throw new InvalidOperationException("La autorización fiscal vence antes de la fecha del comprobante.");
    }
    private sealed class EjercicioCompra{public int Periodo{get;set;}public bool Cerrado{get;set;}}
    private sealed class OperacionCompra
    {
        public string SolicitudHash{get;set;}="";public string Usuario{get;set;}="";public int CompraId{get;set;}public string Comprobante{get;set;}="";
        public string Periodo{get;set;}="";public int Mes{get;set;}public int Asiento{get;set;}public string TipoRegistro{get;set;}="";public decimal Total{get;set;}
    }
    private sealed class FichaProveedorCompra
    {
        public string Calle{get;set;}="";public string Numero{get;set;}="";public string Localidad{get;set;}="";public string Postal{get;set;}="";
        public string Provincia{get;set;}="";public string Telefono{get;set;}="";public string DocumentoTipo{get;set;}="";public string Condicion{get;set;}="";
    }
    internal const string InsertarAsientoCompraSql="""
        INSERT dbo.MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,
          [DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,VENCIMIENTO,TIPO_REG,FechaHora_Grabacion,
          FechaSubdiario,USUARIO_LOGEADO,UNEGOCIO,IDMOTIVO,CABCUENTA,CABNOMBRE,CABCUIT,CABCONDIVA,LIVA_TIPO,
          LIVA_ImpNetoGrav,LIVA_ImpNetoNGrav,LIVA_AlicIVA,LIVA_ImpIVA,LIVA_AlicIVA2,LIVA_ImpIVA2,LIVA_AlicIVA3,LIVA_ImpIVA3,
          LIVA_AlicIVA4,LIVA_ImpIVA4,LIVA_Ret_Perc,LIVA_Ret_IBtos,LIVA_Ret_Ganancias,LIVA_TOTAL,LIVA_EXENTO,NroCAI,FhVtoCAI,CodCpteDGI)
        VALUES(@lineaCuenta,@secuencia,@mes,@asiento,@fecha,@detalle,@tc,@suc,@numero,@letra,@sentido,@importe,@moneda,1,
          @periodo,@total,@vencimiento,@tipo,GETDATE(),@fechaSub,@usuario,@unidad,@motivo,
          CASE WHEN @cabecera=1 THEN @cuenta END,CASE WHEN @cabecera=1 THEN @nombre END,CASE WHEN @cabecera=1 THEN @documento END,
          CASE WHEN @cabecera=1 THEN @ivaCond END,CASE WHEN @cabecera=1 THEN @liva END,
          CASE WHEN @cabecera=1 THEN @gravado ELSE 0 END,CASE WHEN @cabecera=1 THEN @noGravado ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @tasa1 ELSE 0 END,CASE WHEN @cabecera=1 THEN @iva1 ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @tasa2 ELSE 0 END,CASE WHEN @cabecera=1 THEN @iva2 ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @tasa3 ELSE 0 END,CASE WHEN @cabecera=1 THEN @iva3 ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @tasa4 ELSE 0 END,CASE WHEN @cabecera=1 THEN @iva4 ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @percIva ELSE 0 END,CASE WHEN @cabecera=1 THEN @percIibb ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @percGan ELSE 0 END,CASE WHEN @cabecera=1 THEN @totalFiscal ELSE 0 END,
          CASE WHEN @cabecera=1 THEN @impInternoFiscal ELSE 0 END,CASE WHEN @cabecera=1 THEN NULLIF(@cai,'') END,CASE WHEN @cabecera=1 THEN @venceCai END,CASE WHEN @cabecera=1 THEN @codigoDgi END);
        """;
}
