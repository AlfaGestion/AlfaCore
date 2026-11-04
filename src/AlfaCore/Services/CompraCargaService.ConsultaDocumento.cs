using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    public Task<CompraDocumentoConsulta> ConsultarDocumentoAsync(long documento,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"ConsultarCompraDocumento",(c,t)=>ConsultarDocumentoEnConexionAsync(c,documento,t),ct);

    internal async Task<CompraDocumentoConsulta> ConsultarDocumentoEnConexionAsync(SqlConnection cn,long documento,CancellationToken ct,SqlTransaction? tx=null)
    {
        if(!await permisos.HasAccessAsync("D016004",ct))throw new InvalidOperationException("No tenés permiso para consultar Recepción documental.");
        var compraId=await cn.QuerySingleOrDefaultAsync<int?>(Cmd("SELECT o.CompraId FROM dbo.ALFACORE_COMPRA_OPERACION o JOIN dbo.INT_COMPROBANTE_RECIBIDO d ON d.IdComprobanteRecibido=o.DocumentoId WHERE o.DocumentoId=@documento AND d.Eliminado=0;",new{documento},ct,tx))
            ??throw new InvalidOperationException("El documento no tiene una compra web disponible para consultar.");
        return await ConsultarCompraEnConexionAsync(cn,compraId,ct,tx);
    }
    public Task<CompraDocumentoConsulta> ConsultarCompraAsync(int compraId,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"ConsultarCompra",(c,t)=>ConsultarCompraEnConexionAsync(c,compraId,t),ct);
    internal async Task<CompraDocumentoConsulta> ConsultarCompraEnConexionAsync(SqlConnection cn,int compraId,CancellationToken ct,SqlTransaction? tx=null)
    {
        var contexto=await Contexto(cn,ct,tx);
        CommandDefinition Q(string sql,object? args=null)=>Cmd(sql,args,ct,tx);
        var compra=await cn.QuerySingleOrDefaultAsync<CompraReferencia>(Q("""
            SELECT f.ID CompraId,LTRIM(RTRIM(f.CUENTA)) Cuenta,LTRIM(RTRIM(f.TC)) TC,f.IDCOMPROBANTE Comprobante,LTRIM(RTRIM(f.UNEGOCIO)) Unidad,
              CONVERT(bit,ISNULL(f.ANULADA,0)) Anulada,ISNULL(o.Periodo,'') Periodo,ISNULL(o.Mes,0) Mes,ISNULL(o.Asiento,0) Asiento,ISNULL(LTRIM(RTRIM(o.TipoRegistro)),'') Tipo
            FROM dbo.C_MV_CPTE f LEFT JOIN dbo.ALFACORE_COMPRA_OPERACION o ON o.CompraId=f.ID WHERE f.ID=@compraId;
            """,new{compraId}))??throw new InvalidOperationException("No se encontró la compra.");
        if(!contexto.Unidades.Any(u=>u.Codigo==compra.Unidad))throw new InvalidOperationException("No tenés permisos sobre la unidad de esta compra.");
        if(compra.Asiento==0&&!compra.Anulada)
        {
            var candidatos=(await cn.QueryAsync<CompraReferencia>(Q("""
                SELECT DISTINCT LTRIM(RTRIM(a.PERIODO)) Periodo,a.MES_OPERATIVO Mes,a.[NUMERO ASIENTO] Asiento,LTRIM(RTRIM(a.TIPO_REG)) Tipo
                FROM dbo.MV_ASIENTOS a WHERE a.SECUENCIA=1 AND a.SUCURSAL+a.NUMERO+a.LETRA=@Comprobante
                  AND LTRIM(RTRIM(a.CUENTA))=@Cuenta AND LTRIM(RTRIM(a.TC)) IN (@Tc,ISNULL((SELECT LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE='EQU_'+@Tc),@Tc));
                """,compra))).ToList();
            if(candidatos.Count>1)throw new InvalidOperationException("Hay varios asientos para esta compra. Revisá la identificación en contabilidad.");
            if(candidatos.SingleOrDefault() is {} candidato){compra.Periodo=candidato.Periodo;compra.Mes=candidato.Mes;compra.Asiento=candidato.Asiento;compra.Tipo=candidato.Tipo;}
        }
        var corresponde=await cn.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.MV_ASIENTOS a WHERE LTRIM(RTRIM(a.PERIODO))=@Periodo AND a.MES_OPERATIVO=@Mes
              AND a.[NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(a.TIPO_REG))=@Tipo AND a.SECUENCIA=1
              AND LTRIM(RTRIM(a.CUENTA))=@Cuenta AND a.SUCURSAL+a.NUMERO+a.LETRA=@Comprobante;
            """,compra));
        if(corresponde!=1&&compra.Asiento!=0&&!compra.Anulada)throw new InvalidOperationException("El asiento original no está disponible o cambió fuera de este circuito. Revisá la compra en contabilidad.");
        if(compra.Anulada){compra.Periodo="";compra.Asiento=0;}
        var cabecera=await cn.QuerySingleAsync<ComprobanteCabeceraDto>(Q("""
            SELECT c.TC Tc,c.IDCOMPROBANTE IdComprobante,'COMPRAS' SistemaOrigen,c.FECHA Fecha,c.CUENTA Cuenta,c.NOMBRE Nombre,
              ISNULL(c.DOMICILIO,'') Domicilio,ISNULL(c.LOCALIDAD,'') Localidad,ISNULL(c.TELEFONO,'') Telefono,ISNULL(c.CODIGOPOSTAL,'') CodigoPostal,
              ISNULL(c.IDCOND_CPRA_VTA,'') CondicionComercialCodigo,ISNULL(cv.Descripcion,'') CondicionComercialDescripcion,
              c.USUARIO Usuario,LTRIM(RTRIM(c.UNEGOCIO)) UnidadNegocio,ISNULL(un.Descripcion,'') UnidadNegocioDescripcion,
              c.IMPORTE ImporteTotal,c.IMPORTE_S_IVA ImporteSinIva,c.ImporteOtrosConceptos,c.NetoGravado,c.NetoNoGravado,
              c.ANULADA Anulada,c.Bloqueado Bloqueada,c.Cerrada Cerrada,c.COMENTARIOS Comentarios,
              ISNULL((SELECT SUM(ISNULL(a.LIVA_ImpIVA,0)+ISNULL(a.LIVA_ImpIVA2,0)+ISNULL(a.LIVA_ImpIVA3,0)+ISNULL(a.LIVA_ImpIVA4,0))
                FROM dbo.MV_ASIENTOS a WHERE LTRIM(RTRIM(a.PERIODO))=@Periodo AND a.MES_OPERATIVO=@Mes AND a.[NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(a.TIPO_REG))=@Tipo),0) Iva
            FROM dbo.C_MV_CPTE c LEFT JOIN dbo.V_TA_Cpra_Vta cv ON cv.IDCond_Cpra_Vta=c.IDCOND_CPRA_VTA
            LEFT JOIN dbo.V_TA_UNIDADNEGOCIO un ON un.CODIGO=c.UNEGOCIO WHERE c.ID=@CompraId;
            """,compra));
        if(compra.Anulada){compra.Periodo="";compra.Asiento=0;}
        var filas=(await cn.QueryAsync<ComprobanteAsientoDto>(Q("""
            SELECT a.[NUMERO ASIENTO] NumeroAsiento,a.FECHA Fecha,a.FechaHora_Grabacion FechaHoraGrabacion,a.CUENTA Cuenta,
              ISNULL(c.DESCRIPCION,'') DescripcionCuenta,a.DETALLE Detalle,a.[DEBE-HABER] DebeHaber,a.IMPORTE Importe,
              CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE ELSE 0 END Debe,CASE WHEN a.[DEBE-HABER]='H' THEN a.IMPORTE ELSE 0 END Haber,
              ISNULL(a.NroComprobanteBancario,'') Cheque,a.VENCIMIENTO Vencimiento,ISNULL(a.TJ_Cuotas,0) Cuotas,
              LTRIM(RTRIM(a.UNEGOCIO)) UnidadNegocio,ISNULL(a.USUARIO_LOGEADO,'') Usuario
            FROM dbo.MV_ASIENTOS a LEFT JOIN dbo.MA_CUENTAS c ON c.CODIGO=a.CUENTA
            WHERE LTRIM(RTRIM(a.PERIODO))=@Periodo AND a.MES_OPERATIVO=@Mes AND a.[NUMERO ASIENTO]=@Asiento
              AND LTRIM(RTRIM(a.TIPO_REG))=@Tipo ORDER BY a.SECUENCIA,a.CUENTA;
            """,compra))).AsList();
        if(filas.Any(f=>f.UnidadNegocio!=compra.Unidad))throw new InvalidOperationException("El asiento incluye otras unidades. Revisá la registración original antes de consultarlo aquí.");
        var conceptos=(await cn.QueryAsync<ComprobanteObservacionDto>(Q("""
            SELECT TIPO_OBS TipoObservacion,OBSERVACION Observacion,IMPORTE Importe,IMPORTE_S_IVA ImporteSinIva
            FROM dbo.C_MV_CPTE_OBSERV WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante AND LTRIM(RTRIM(CUENTA))=@Cuenta ORDER BY Secuencia,ID;
            """,compra))).AsList();
        var historial=(await cn.QueryAsync<ComprobanteAccionDto>(Q("""
            SELECT TIPO_ACCION TipoAccion,CAST(SYSTEMUSER AS nvarchar(max)) Comentario,FECHAHORA FechaHora,USUARIO Usuario,PC Pc,Proceso
            FROM dbo.V_MV_CPTEACCIONES WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante AND LTRIM(RTRIM(Cuenta))=@Cuenta ORDER BY FECHAHORA DESC,ID DESC;
            """,compra))).AsList();
        var insumos=(await cn.QueryAsync<ComprobanteInsumoDto>(Q("""
            SELECT LTRIM(RTRIM(IDARTICULO)) CodigoArticulo,DESCRIPCION Descripcion,IDUNIDAD Unidad,CANTIDADUD Cantidad,
              COSTO PrecioImporte,TOTAL Total,NRO_SERIE NumeroSerie,NRO_LOTE NumeroLote,Secuencia
            FROM dbo.C_MV_CPTEINSUMOS WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante AND LTRIM(RTRIM(CUENTA))=@Cuenta ORDER BY Secuencia,ID;
            """,compra))).AsList();
        return new(new(){Cabecera=cabecera,Insumos=insumos,Observaciones=conceptos,Acciones=historial,Asientos=filas,TieneAsiento=filas.Count>0},filas);
    }
    private sealed class CompraReferencia
    {
        public bool Anulada{get;set;}public int CompraId{get;set;}public string Cuenta{get;set;}="";public string Tc{get;set;}="";public string Comprobante{get;set;}="";
        public string Unidad{get;set;}="";public string Periodo{get;set;}="";public int Mes{get;set;}public int Asiento{get;set;}public string Tipo{get;set;}="";
    }
}
