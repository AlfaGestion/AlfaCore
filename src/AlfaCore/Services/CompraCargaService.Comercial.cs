using System.Text.Json;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Orden de compra y remito: se graban como una factura de artículos, pero sin MV_ASIENTOS, libro de IVA ni centros de costos.
public sealed partial class CompraCargaService
{
    internal static void ValidarRegistroComercial(CompraCargaBorrador d,CompraCargaProveedor p,CompraCargaContexto c,CompraCargaVista v,string huella)
    {
        if(v.Errores.Count>0)throw new InvalidOperationException(string.Join(" ",v.Errores));
        if(p.Moneda is not ("" or "1"))throw new InvalidOperationException("Esta etapa registra proveedores en pesos.");
        if(d.Lineas.Any(l=>l.Neto!=CompraCargaPlanificador.Moneda(l.Neto)||l.IvaInformado is {} iva&&iva!=CompraCargaPlanificador.Moneda(iva))||d.TotalDocumento is {} t&&t!=CompraCargaPlanificador.Moneda(t))
            throw new InvalidOperationException("Los importes del documento deben tener como máximo dos decimales.");
        if(v.DiferenciaDocumento is not null and not 0)throw new InvalidOperationException("El total calculado no coincide con el documento.");
        if(HuellaRevision(d,p,c,v)!=huella)throw new InvalidOperationException("Los datos o la configuración cambiaron. Volvé a revisar el comprobante antes de confirmar.");
    }

    private async Task<CompraCargaRegistrada> RegistrarComercialAsync(SqlConnection cn,SqlTransaction tx,CompraCargaRegistroSolicitud s,string hash,
        CompraCargaProveedor proveedor,CompraCargaContexto contexto,CompraCargaVista vista,CancellationToken ct)
    {
        var d=s.Borrador;CommandDefinition Q(string sql,object? args=null)=>Cmd(sql,args,ct,tx);
        var articulos=await ValidarArticulosDirectosAsync(cn,d,contexto,ct,tx);
        var id=vista.IdComprobante;var suc=id[..4];var numero=id.Substring(4,8);var esOc=d.Tipo=="OC";
        // La OC se numera por tipo; el remito lleva el número del proveedor y es único por proveedor.
        if(await cn.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.C_MV_CPTE WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(TC))=@tc AND IDCOMPROBANTE=@id AND (@esOc=1 OR LTRIM(RTRIM(CUENTA))=@cuenta);
            """,new{tc=d.Tipo.Trim(),id,esOc,cuenta=proveedor.Codigo}))>0)
            throw new InvalidOperationException(esOc?"Ya existe una orden de compra con ese número. Revisá nuevamente antes de registrar.":"El remito ya existe para este proveedor, incluso anulado. Revisalo antes de cargar otro.");
        var talonario=contexto.Tipos.FirstOrDefault(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad==d.Unidad)
            ??contexto.Tipos.Single(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad.Length==0);
        if(!talonario.Externo)
        {
            var siguiente=await NumeroInternoAsync(cn,d,talonario,ct,tx);
            if(siguiente.Sucursal!=suc||siguiente.Numero!=numero)throw new InvalidOperationException("Otra terminal consumió la numeración interna. Revisá nuevamente antes de registrar.");
            var columna=d.Letra=="C"?"C_ULTIMO_NRO":"A_ULTIMO_NRO";
            await cn.ExecuteAsync(Q($"UPDATE dbo.V_TA_CPTE SET {columna}=@siguiente WHERE LTRIM(RTRIM(CODIGO))=@Codigo AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@Unidad;",
                new{siguiente=long.Parse(numero)+1,talonario.Codigo,talonario.Talonario,talonario.Unidad}));
        }
        var ficha=await LeerFichaProveedorAsync(cn,tx,proveedor.Codigo,ct);
        var fiscal=CompraCargaFiscal.Crear(d,vista,contexto.TasasFiscales);
        var usuario=usuarios.GetCurrentUserName();
        var p=new{tc=d.Tipo.Trim(),cuenta=proveedor.Codigo.Trim(),nombre=proveedor.Nombre,documento=proveedor.Documento.Trim(),fecha=d.Fecha.Date,vencimiento=d.Vencimiento.Date,id,suc,numero,letra=d.Letra,
            unidad=CodigoPk.Format(d.Unidad,4),motivo=CodigoPk.Format(d.Motivo,4),ivaCond=CodigoPk.Format(proveedor.CondicionIva,4),moneda=CodigoPk.Format("1",4),usuario,
            domicilio=(ficha.Calle+" "+ficha.Numero).Trim(),ficha.Localidad,ficha.Postal,provincia=CodigoPk.Format(ficha.Provincia,4),ficha.Telefono,docTipo=CodigoPk.Format(ficha.DocumentoTipo,4),
            condicion=CodigoPk.Format(ficha.Condicion,4),total=vista.Total,neto=vista.Neto,gravado=fiscal.Gravado,noGravado=fiscal.NoGravado,iva1=fiscal.Importes[0],iva2=fiscal.Importes[1],
            tasa1=fiscal.Tasas[0],tasa2=fiscal.Tasas[1],deposito=CodigoPk.Format(d.Deposito,4),aprobado=esOc,destino=esOc?CodigoPk.Format(d.Deposito,4):null,vigencia=esOc?d.Vencimiento.Date:(DateTime?)null};
        var compra=await cn.ExecuteScalarAsync<int>(Q("""
            INSERT dbo.C_MV_CPTE(TC,IDCOMPROBANTE,CUENTA,FECHA,FechaSubdiario,VENCIMIENTO,NOMBRE,DOMICILIO,LOCALIDAD,CODIGOPOSTAL,IDPROVINCIA,TELEFONO,DOCUMENTOTIPO,DOCUMENTONUMERO,
              CONDICIONIVA,IDCOND_CPRA_VTA,IDMOTIVOCPRAVTA,IMPORTE,IMPORTE_S_IVA,ImporteInsumos,ImporteIva,ImporteIva2,NetoGravado,NetoNoGravado,AlicIva,AlicIva2,
              SUCURSAL,NUMERO,LETRA,UNEGOCIO,MONEDA,COTIZACION,IDDEPOSITO,IdDepositoDestino,Aprobado,Finalizado,FECHA_A_VENCER,USUARIO,FechaHora_Grabacion,COMENTARIOS)
            VALUES(@tc,@id,@cuenta,@fecha,@fecha,@vencimiento,@nombre,@domicilio,@Localidad,@Postal,@provincia,@Telefono,@docTipo,@documento,
              @ivaCond,@condicion,@motivo,@total,@neto,@neto,@iva1,@iva2,@gravado,@noGravado,@tasa1,@tasa2,
              @suc,@numero,@letra,@unidad,@moneda,1,@deposito,@destino,@aprobado,0,@vigencia,@usuario,GETDATE(),N'Compra desde AlfaCore');
            SELECT CONVERT(int,SCOPE_IDENTITY());
            """,p));
        foreach(var (l,i) in d.Lineas.Select((l,i)=>(l,i)))
        {
            var a=articulos.Single(a=>a.Codigo==l.Articulo.Trim());
            // La OC no mueve stock (el legacy solo lo hace en garantías); el remito ingresa lo que controla existencias.
            await GrabarArticuloAsync(cn,tx,d,vista,l,i,!esOc&&!a.NoStock,"","",ct);
        }
        await cn.ExecuteAsync(Q("""
            INSERT dbo.V_MV_CPTEACCIONES(TC,IDCOMPROBANTE,IDCOMPLEMENTO,TIPO_ACCION,FECHAHORA,USUARIO,PC,SYSTEMUSER,Fecha,Cuenta,Proceso)
            VALUES(@tc,@id,0,'A',GETDATE(),@usuario,HOST_NAME(),SYSTEM_USER,@fecha,@cuenta,N'AlfaCore: carga de OC / remito de compra');
            INSERT dbo.ALFACORE_COMPRA_OPERACION(Operacion,SolicitudHash,Usuario,DocumentoId,CompraId,Comprobante,Periodo,Mes,Asiento,TipoRegistro,Total,SolicitudJson)
            VALUES(@Operacion,@hash,@usuario,NULL,@compra,@id,'',0,0,'',@total,@json);
            """,new{p.tc,id,p.usuario,p.fecha,p.cuenta,s.Operacion,hash,compra,total=vista.Total,json=JsonSerializer.Serialize(d)}));
        ct.ThrowIfCancellationRequested();
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out var sesion)||sesion!=s.Sesion)throw new OperationCanceledException("La sesión cambió.");
        return new(compra,id,"",0,0,"",vista.Total);
    }

    private static Task<FichaProveedorCompra> LeerFichaProveedorAsync(SqlConnection cn,SqlTransaction tx,string cuenta,CancellationToken ct)
        =>cn.QuerySingleAsync<FichaProveedorCompra>(Cmd("""
            SELECT ISNULL(CALLE,'') Calle,ISNULL(NUMERO,'') Numero,ISNULL(LOCALIDAD,'') Localidad,ISNULL(CPOSTAL,'') Postal,
              LTRIM(RTRIM(ISNULL(PROVINCIA,''))) Provincia,ISNULL(TELEFONO,'') Telefono,LTRIM(RTRIM(ISNULL(DOCUMENTO_TIPO,''))) DocumentoTipo,
              LTRIM(RTRIM(ISNULL(idCond_Cpra_Vta,''))) Condicion FROM dbo.VT_PROVEEDORES WHERE LTRIM(RTRIM(CODIGO))=@cuenta;
            """,new{cuenta},ct,tx));
}
