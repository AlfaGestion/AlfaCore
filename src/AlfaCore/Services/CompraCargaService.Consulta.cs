using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    public Task<IReadOnlyList<CompraConsultaFila>> ConsultarComprasAsync(DateTime desde,DateTime hasta,string texto,string tipo,string estado,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<CompraConsultaFila>>(baseEsperada,"ConsultarCompras",async(c,t)=>
        {
            if(desde.Year<1900||hasta<desde||hasta.Year>9998||texto.Length>100||tipo.Length>4||estado is not("" or "ACTIVAS" or "ANULADAS"))throw new InvalidOperationException("Revisá las fechas y los filtros de compras.");
            var contexto=await Contexto(c,t);
            // La tabla auxiliar es opcional en instalaciones anteriores; no filtra compras legacy.
            await c.ExecuteAsync(Cmd("""
                CREATE TABLE #ComprasWeb(Id int NOT NULL PRIMARY KEY);
                IF OBJECT_ID(N'dbo.ALFACORE_COMPRA_OPERACION',N'U') IS NOT NULL
                    EXEC(N'INSERT #ComprasWeb SELECT CompraId FROM dbo.ALFACORE_COMPRA_OPERACION');
                """,null,t));
            return (await c.QueryAsync<CompraConsultaFila>(Cmd("""
                SELECT TOP(501) c.ID Id,LTRIM(RTRIM(c.TC)) Tipo,c.IDCOMPROBANTE Comprobante,LTRIM(RTRIM(c.CUENTA)) Cuenta,
                  c.NOMBRE Nombre,LTRIM(RTRIM(c.UNEGOCIO)) Unidad,c.FECHA Fecha,ISNULL(c.IMPORTE,0) Total,
                  CONVERT(bit,ISNULL(c.ANULADA,0)) Anulada,CONVERT(bit,CASE WHEN w.Id IS NOT NULL THEN 1 ELSE 0 END) Web
                FROM dbo.C_MV_CPTE c LEFT JOIN #ComprasWeb w ON w.Id=c.ID
                WHERE c.FECHA>=@desde AND c.FECHA<@fin AND LTRIM(RTRIM(c.UNEGOCIO)) IN @unidades
                  AND (@tipo='' OR LTRIM(RTRIM(c.TC))=@tipo)
                  AND (@estado='' OR @estado='ACTIVAS' AND ISNULL(c.ANULADA,0)=0 OR @estado='ANULADAS' AND c.ANULADA=1)
                  AND (@texto='' OR c.NOMBRE LIKE @patron OR c.CUENTA LIKE @patron OR c.IDCOMPROBANTE LIKE @patron)
                ORDER BY c.FECHA DESC,c.ID DESC;
                """,new{desde=desde.Date,fin=hasta.Date.AddDays(1),unidades=contexto.Unidades.Select(x=>x.Codigo).ToArray(),tipo=tipo.Trim(),estado,texto=texto.Trim(),patron="%"+texto.Trim()+"%"},t))).AsList();
        },ct);

    public Task<CompraCargaBorrador> CopiarCompraAsync(int compra,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"CopiarCompra",async(c,t)=>
        {
            var contexto=await Contexto(c,t);
            var json=await c.QuerySingleOrDefaultAsync<string>(Cmd("""
                SELECT o.SolicitudJson FROM dbo.ALFACORE_COMPRA_OPERACION o JOIN dbo.C_MV_CPTE f ON f.ID=o.CompraId
                WHERE f.ID=@compra AND LTRIM(RTRIM(f.UNEGOCIO)) IN @unidades;
                """,new{compra,unidades=contexto.Unidades.Select(x=>x.Codigo).ToArray()},t))??throw new InvalidOperationException("Solo se puede recuperar la carga de compras web de las unidades autorizadas.");
            var borrador=JsonSerializer.Deserialize<CompraCargaBorrador>(json)??throw new InvalidOperationException("El borrador original no está disponible.");
            if(borrador.CompraOrigenId is not null)throw new InvalidOperationException("Para otra factura de mercadería, prepará una OC o remito pendiente; no se reutiliza un origen ya facturado.");
            borrador.NotaFacturaId=null;borrador.NotaAplicar=0;
            borrador.DocumentoRecibido=null;borrador.LecturaCompraId=null;borrador.LecturaCompraHuella="";borrador.Numero="";
            return borrador;
        },ct);

    internal static async Task<string> HuellaAsientoGuardadoAsync(SqlConnection c,SqlTransaction tx,CompraCargaRegistrada registro,CancellationToken ct)
    {
        var filas=await c.QueryAsync(Cmd("""
            SELECT *
            FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes
              AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro
              AND SUCURSAL+NUMERO+LETRA=@Comprobante ORDER BY SECUENCIA,CUENTA;
            """,registro,ct,tx));
        var centros=await c.QueryAsync(Cmd("""
            SELECT * FROM dbo.MV_ASIENTOSCCOSTO c WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes
              AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(Tipo_Reg))=@TipoRegistro
              AND EXISTS(SELECT 1 FROM dbo.MV_ASIENTOS x WHERE LTRIM(RTRIM(x.PERIODO))=@Periodo AND x.MES_OPERATIVO=@Mes AND x.[NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(x.TIPO_REG))=@TipoRegistro AND x.CUENTA=c.CUENTA AND x.SECUENCIA=c.SECUENCIA AND x.SUCURSAL+x.NUMERO+x.LETRA=@Comprobante) ORDER BY SECUENCIA,CUENTA,CCOSTO;
            """,registro,ct,tx));
        return HashCompra(JsonSerializer.Serialize(new{Asientos=filas.Select(row=>(IDictionary<string,object>)row),Centros=centros.Select(row=>(IDictionary<string,object>)row)}));
    }
}
