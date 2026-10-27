namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    // Algunas bases legacy no numeran TJ_Presentacion en las cobranzas de Ventas.
    // Una línea interna conserva la exigencia de presentación y cupón; no se incluyen ajustes CC sin esos datos.
    internal const string MovimientosAlfaSql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@usuario)) THROW 50001,'El usuario no existe en la base activa.',1;
            SELECT TOP(10001) LTRIM(RTRIM(a.CUENTA)) Cuenta,CONCAT(RTRIM(a.PERIODO),'|',a.MES_OPERATIVO,'|',a.[NUMERO ASIENTO],'|',RTRIM(a.TIPO_REG),'|',a.SECUENCIA,
                '|',RTRIM(a.TC),'|',RTRIM(a.SUCURSAL),'|',RTRIM(a.NUMERO),'|',RTRIM(a.LETRA)) Clave,
                CONCAT(RTRIM(a.PERIODO),'|',a.MES_OPERATIVO,'|',a.[NUMERO ASIENTO],'|',RTRIM(a.TIPO_REG),'|',a.SECUENCIA) ClaveAnterior,
                CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(CONVERT(varchar(23),a.FECHA,126),'|',a.IMPORTE,'|',a.CUENTA,'|',a.TC,'|',a.SUCURSAL,'|',a.NUMERO,'|',a.LETRA,'|',a.UNEGOCIO,'|',a.TJ_NroCupon,'|',a.TJ_Presentacion,'|',a.TJ_Liquidacion,'|',a.DETALLE)),2) Huella,
                a.FECHA Fecha,CONCAT(RTRIM(a.TC),' ',a.SUCURSAL,'-',a.NUMERO,' ',a.LETRA) Comprobante,RTRIM(a.SUCURSAL) Sucursal,
                ISNULL(a.DETALLE,'') Detalle,ISNULL(a.TJ_NroCupon,'') Cupon,ISNULL(a.TJ_Presentacion,0) Presentacion,a.IMPORTE Importe,
                CONVERT(bit,CASE WHEN ISNULL(a.TJ_Liquidacion,0)>0 THEN 1 ELSE 0 END) LegacyLiquidado
            FROM dbo.MV_ASIENTOS a WHERE LTRIM(RTRIM(a.CUENTA))=@cuenta AND LTRIM(RTRIM(a.MONEDA))='1'
                AND ISNULL(a.EsResumen,0)=0 AND ISNULL(a.EsCierre,0)=0 AND ISNULL(a.ES_SALDO_APERTURA,0)=0
                AND a.[DEBE-HABER]='D' AND a.IMPORTE>0
                AND ((ISNULL(a.TJ_Presentacion,0)>0 AND NULLIF(LTRIM(RTRIM(a.TJ_NroCupon)),'') IS NOT NULL) OR EXISTS(SELECT 1 FROM dbo.V_TA_Cpte tipo
                    WHERE LTRIM(RTRIM(tipo.Codigo))=LTRIM(RTRIM(a.TC)) AND LTRIM(RTRIM(tipo.SISTEMA))='Ventas'))
                AND a.FECHA>=@desde AND a.FECHA<DATEADD(day,1,@hasta)
                AND (@sucursal='' OR LTRIM(RTRIM(a.SUCURSAL))=@sucursal)
                AND EXISTS(SELECT 1 FROM dbo.TA_USUARIOS u WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario)
                    AND (ISNULL(u.AdminCentral,0)=1 OR (NULLIF(LTRIM(RTRIM(u.UNEGOCIO)),'') IS NOT NULL AND LTRIM(RTRIM(u.UNEGOCIO))=LTRIM(RTRIM(a.UNEGOCIO)))))
            ORDER BY a.FECHA,a.[NUMERO ASIENTO],a.SECUENCIA;
    """;
    // Misma fuente, exclusiones y permisos que Comparar; se amplía solo a las cuentas TJ elegibles.
    internal static readonly string MovimientosDeteccionSql=MovimientosAlfaSql.Replace(
        "LTRIM(RTRIM(a.CUENTA))=@cuenta",
        "EXISTS(SELECT 1 FROM dbo.MA_CUENTAS tc WHERE LTRIM(RTRIM(tc.CODIGO))=LTRIM(RTRIM(a.CUENTA)) AND LTRIM(RTRIM(tc.MEDIODEPAGO))='TJ' AND LTRIM(RTRIM(tc.MONEDA))='1' AND ISNULL(tc.TITULO,0)=0 AND ISNULL(tc.Dada_De_Baja,0)=0)",StringComparison.Ordinal);
}
