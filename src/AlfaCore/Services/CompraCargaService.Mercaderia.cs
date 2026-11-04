using System.Text.Json;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    public Task<CompraCargaBorrador> PrepararDesdeOrigenAsync(int origen,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"PrepararDesdeOrigen",(c,t)=>PrepararDesdeOrigenEnConexionAsync(c,origen,t),ct);
    internal async Task<CompraCargaBorrador> PrepararDesdeOrigenEnConexionAsync(SqlConnection c,int origen,CancellationToken t,SqlTransaction? tx=null)
    {
        var (cab,filas,huella)=await LeerOrigenMercaderiaAsync(c,origen,t,tx);
        var proveedor=await Proveedor(c,cab.Cuenta,t,tx);
        var d=new CompraCargaBorrador{CompraOrigenId=origen,CompraOrigenHuella=huella,CompraOrigenTipo=cab.Tipo,Deposito=cab.Deposito,Proveedor=cab.Cuenta,Unidad=cab.Unidad,
            Letra=proveedor.CondicionIva=="5"?"C":"A",Motivo=cab.Motivo,ServiciosGastosConfirmados=false,Fecha=DateTime.Today,FechaContable=DateTime.Today,Vencimiento=DateTime.Today};
        d.Lineas=filas.Where(f=>f.Pendiente>0).Select(f=>new CompraCargaLinea{Articulo=f.Articulo,UnidadArticulo=f.Unidad,Cantidad=f.Pendiente,CostoUnitario=f.Precio,
            Descripcion=f.Descripcion,Neto=CompraCargaPlanificador.Moneda(f.Precio*f.Pendiente),Tasa=proveedor.CondicionIva=="5"||f.Exento?0:f.Tasa,Tratamiento=f.Exento?"Exento":"Gravado"}).ToList();
        d.TotalDocumento=d.Lineas.Sum(f=>f.Neto+CompraCargaPlanificador.Moneda(f.Neto*f.Tasa/100));return d;
    }

    private async Task<(OrigenMercaderia Cab,List<LineaOrigenMercaderia> Filas,string Huella)> LeerOrigenMercaderiaAsync(SqlConnection c,int id,CancellationToken ct,SqlTransaction? tx=null)
    {
        var contexto=await Contexto(c,ct,tx);
        var cab=await c.QuerySingleOrDefaultAsync<OrigenMercaderia>(Cmd("""
            SELECT ID Id,LTRIM(RTRIM(TC)) Tipo,IDCOMPROBANTE Comprobante,LTRIM(RTRIM(CUENTA)) Cuenta,LTRIM(RTRIM(UNEGOCIO)) Unidad,
              LTRIM(RTRIM(IDDEPOSITO)) Deposito,LTRIM(RTRIM(ISNULL(MONEDA,''))) Moneda,ISNULL(COTIZACION,1) Cotizacion,LTRIM(RTRIM(IDMOTIVOCPRAVTA)) Motivo,
              CONVERT(bit,CASE WHEN ISNULL(ANULADA,0)=1 OR ISNULL(Finalizado,0)=1 OR ISNULL(Bloqueado,0)=1 OR ISNULL(Cerrada,0)=1 THEN 1 ELSE 0 END) NoDisponible
            FROM dbo.C_MV_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE ID=@id;
            """,new{id},ct,tx))??throw new InvalidOperationException("El comprobante de origen no está disponible.");
        if(cab.Moneda is not("" or "1")||cab.Cotizacion!=1)throw new InvalidOperationException("El origen debe estar en pesos con cotización 1. No se convirtió su moneda.");
        if(cab.Tipo is not("OC" or "RMC")||cab.NoDisponible||!contexto.Unidades.Any(u=>u.Codigo==cab.Unidad))throw new InvalidOperationException("Elegí una OC o un remito activo, pendiente y de una unidad autorizada.");
        var requiereIr=await c.ExecuteScalarAsync<string?>(Cmd("SELECT LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE='IR_Oblig';",null,ct,tx));
        if(cab.Tipo=="OC"&&string.Equals(requiereIr,"SI",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La configuración exige ingreso de recepción previo (IR_Oblig). Facturá desde el circuito de recepción del legacy; no se ingresó stock directamente desde la OC.");
        // Las vistas legacy suman por artículo. Una factura con varios orígenes vuelve ambiguo ese saldo.
        if(await c.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM dbo.MV_APLICACION a WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(a.CUENTA))=@Cuenta AND LTRIM(RTRIM(a.TCO_ORIGEN))=@Tipo AND LTRIM(RTRIM(a.IDComprobante_Origen))=@Comprobante
              AND (LTRIM(RTRIM(a.TC))<>'FCC' OR EXISTS(
                SELECT 1 FROM dbo.MV_APLICACION b WITH(UPDLOCK,HOLDLOCK)
                WHERE b.CUENTA=a.CUENTA AND b.TC=a.TC AND b.IDComprobante=a.IDComprobante
                  AND (LTRIM(RTRIM(b.TCO_ORIGEN))<>@Tipo OR LTRIM(RTRIM(b.IDComprobante_Origen))<>@Comprobante)));
            """,cab,ct,tx))>0)throw new InvalidOperationException("El origen tiene recepciones o aplicaciones de varios orígenes. Revisá sus cantidades en el circuito legacy antes de facturarlo aquí.");
        if(cab.Tipo=="OC"&&await c.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM dbo.V_MV_STOCK s WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(s.TC_Origen))='OC' AND s.IdComprobante_Origen=@Comprobante
                AND LTRIM(RTRIM(s.CuentaProveedor))=@Cuenta AND ISNULL(s.Anulado,0)=0
                AND (LTRIM(RTRIM(s.TC))<>'FCC' OR NOT EXISTS(SELECT 1 FROM dbo.MV_APLICACION a WITH(UPDLOCK,HOLDLOCK)
                    WHERE a.CUENTA=s.CuentaProveedor AND a.TC=s.TC AND a.IDComprobante=s.IDCOMPROBANTE
                      AND LTRIM(RTRIM(a.TCO_ORIGEN))='OC' AND a.IDComprobante_Origen=@Comprobante));
            """,cab,ct,tx))>0)
            throw new InvalidOperationException("La OC ya tiene recepción de stock. Facturala desde su remito para evitar duplicar el ingreso.");
        if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.V_TA_DEPOSITO WHERE LTRIM(RTRIM(IdDeposito))=@Deposito;",cab,ct,tx))!=1)throw new InvalidOperationException("El depósito del origen no está disponible.");
        var filas=(await c.QueryAsync<LineaOrigenMercaderia>(Cmd("""
            SELECT i.Secuencia,LTRIM(RTRIM(i.IDARTICULO)) Articulo,i.DESCRIPCION Descripcion,LTRIM(RTRIM(i.IDUNIDAD)) Unidad,
              CONVERT(decimal(18,4),i.CANTIDAD) Cantidad,CONVERT(decimal(18,4),i.IMPORTE_S_IVA) Precio,ISNULL(i.AlicIva,0) Tasa,
              CONVERT(bit,ISNULL(i.EXENTO,0)) Exento,
              CONVERT(bit,CASE WHEN a.IDARTICULO IS NULL OR ISNULL(a.SUSPENDIDO,0)=1 OR ISNULL(a.USASERIE,0)=1 OR ISNULL(a.USALOTE,0)=1
                OR ISNULL(a.UsaTalle,0)=1 OR ISNULL(a.PideEquivalencia,0)=1 OR ISNULL(a.NO_CONTROLA_STOCK,0)=1 OR LTRIM(RTRIM(ISNULL(a.CuentaContable,'')))<>''
                OR ISNULL(i.IMPUESTOS,0)<>0 OR ISNULL(i.ITC,0)<>0 OR ISNULL(i.ImporteDto,0)<>0 OR ISNULL(i.PorcDto,0)<>0 OR ISNULL(i.ImpFlete,0)<>0 OR ISNULL(i.ImpSeguro,0)<>0
                OR ISNULL(i.EQUIV_UDBASE,1)<>1 OR ISNULL(i.CANTIDADUD,1)<>i.CANTIDAD
                OR LTRIM(RTRIM(ISNULL(i.IDUNIDADBASE,'')))<>LTRIM(RTRIM(i.IDUNIDAD))
                OR LTRIM(RTRIM(ISNULL(i.NRO_SERIE,'')))<>'' OR LTRIM(RTRIM(ISNULL(i.NRO_LOTE,'')))<>'' THEN 1 ELSE 0 END) Especial
            FROM dbo.C_MV_CPTEINSUMOS i WITH(UPDLOCK,HOLDLOCK) LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO=i.IDARTICULO
            WHERE i.TC=@Tipo AND i.IDCOMPROBANTE=@Comprobante AND LTRIM(RTRIM(i.CUENTA))=@Cuenta ORDER BY i.Secuencia,i.ID;
            """,cab,ct,tx))).ToList();
        if(filas.Count is 0 or >100||filas.Any(f=>f.Especial||f.Cantidad<=0||f.Precio<=0||f.Unidad.Length==0)||filas.Select(f=>f.Articulo).Distinct().Count()!=filas.Count)
            throw new InvalidOperationException("El origen requiere series, lotes, equivalencias, artículos repetidos o un tratamiento especial. Usá el circuito legacy para este caso.");
        if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.C_MV_CPTE_OBSERV WHERE TC=@Tipo AND IDCOMPROBANTE=@Comprobante AND LTRIM(RTRIM(CUENTA))=@Cuenta AND TIPO_OBS='OC';",cab,ct,tx))>0)
            throw new InvalidOperationException("El origen combina mercadería con otros conceptos. Revisalo en el legacy.");
        var aplicaciones=(await c.QueryAsync<CantidadAplicadaOrigen>(Cmd(cab.Tipo=="RMC"?"""
            SELECT LTRIM(RTRIM(IDARTICULO)) Articulo,CONVERT(decimal(18,4),CantidadAplicada) Cantidad
            FROM dbo.VT_C_MV_INSUMOS_APLICACION WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(CUENTA))=@Cuenta AND LTRIM(RTRIM(TCO_ORIGEN))=@Tipo AND LTRIM(RTRIM(IdComprobanteOrigen))=@Comprobante;
            """:"""
            SELECT LTRIM(RTRIM(IDARTICULO)) Articulo,CONVERT(decimal(18,4),Aplicado) Cantidad
            FROM dbo.CO_AplicacionOC WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(CUENTA))=@Cuenta AND LTRIM(RTRIM(TCO_ORIGEN))=@Tipo AND LTRIM(RTRIM(IDComprobante_Origen))=@Comprobante;
            """,cab,ct,tx))).ToList();
        if(aplicaciones.Any(a=>a.Cantidad<0||!filas.Any(f=>f.Articulo==a.Articulo))||aplicaciones.GroupBy(a=>a.Articulo).Any(g=>g.Count()>1))
            throw new InvalidOperationException("Las aplicaciones del origen requieren revisión en el legacy; no se pudo determinar una cantidad pendiente única por artículo.");
        foreach(var f in filas)
        {
            f.Aplicada=aplicaciones.SingleOrDefault(a=>a.Articulo==f.Articulo)?.Cantidad??0;
            if(f.Pendiente<0)throw new InvalidOperationException("El origen tiene cantidades aplicadas superiores a las originales. Revisalo en el legacy.");
        }
        if(filas.All(f=>f.Pendiente==0))throw new InvalidOperationException("El origen ya está facturado íntegramente; no tiene cantidades pendientes.");
        var stock=(await c.QueryAsync<StockOrigenMercaderia>(Cmd("""
            SELECT ID Id,LTRIM(RTRIM(IDArticulo)) Articulo,CONVERT(decimal(18,4),Cantidad) Cantidad,LTRIM(RTRIM(IdDeposito)) Deposito,
              LTRIM(RTRIM(UNEGOCIO)) Unidad FROM dbo.V_MV_STOCK WITH(UPDLOCK,HOLDLOCK)
            WHERE TC=@Tipo AND IDCOMPROBANTE=@Comprobante AND LTRIM(RTRIM(CuentaProveedor))=@Cuenta AND ISNULL(Anulado,0)=0 ORDER BY ID;
            """,cab,ct,tx))).ToList();
        if(cab.Tipo=="RMC"&&(stock.Any(s=>s.Deposito!=cab.Deposito||s.Unidad!=cab.Unidad)||stock.Count!=filas.Count||filas.Any(f=>stock.Where(s=>s.Articulo==f.Articulo).Sum(s=>s.Cantidad)!=f.Cantidad)))
            throw new InvalidOperationException("El stock vigente del remito no coincide con sus cantidades, depósito o unidad. Revisalo antes de facturarlo.");
        return(cab,filas,HashCompra(JsonSerializer.Serialize(new{cab,filas,stock,requiereIr})));
    }
    private async Task RegistrarMercaderiaAsync(SqlConnection c,SqlTransaction tx,CompraCargaBorrador d,CompraCargaVista vista,CancellationToken ct)
    {
        if(d.CompraOrigenId is not {} origen)
        {
            var contexto=await Contexto(c,ct,tx);var articulos=await ValidarArticulosDirectosAsync(c,d,contexto,ct,tx);
            foreach(var (l,i) in d.Lineas.Select((l,i)=>(l,i)).Where(x=>x.l.Articulo.Length>0))
            {
                var a=articulos.Single(a=>a.Codigo==l.Articulo);
                await GrabarArticuloAsync(c,tx,d,vista,l,i,!a.NoStock,"","",ct);
            }
            return;
        }
        var (cab,filas,huella)=await LeerOrigenMercaderiaAsync(c,origen,ct,tx);
        if(d.Tipo!="FCC"||huella!=d.CompraOrigenHuella||d.Proveedor!=cab.Cuenta||d.Unidad!=cab.Unidad||d.Deposito!=cab.Deposito)
            throw new InvalidOperationException("El origen cambió. Volvé a preparar la compra desde su remito u OC.");
        ValidarCantidadesOrigen(d,filas);
        foreach(var (l,i) in d.Lineas.Select((l,i)=>(l,i)))
        {
            await GrabarArticuloAsync(c,tx,d,vista,l,i,cab.Tipo=="OC",cab.Tipo,cab.Comprobante,ct);
        }
        // El legacy ofrece cerrar el saldo solo para OC; en un remito no hay nada que dar por finalizado voluntariamente.
        if(d.CerrarSaldoOrigen&&cab.Tipo!="OC")throw new InvalidOperationException("Solo se puede cerrar el saldo pendiente de una orden de compra.");
        var completo=d.CerrarSaldoOrigen||filas.All(f=>f.Pendiente==d.Lineas.Where(l=>l.Articulo==f.Articulo).Sum(l=>l.Cantidad));
        await c.ExecuteAsync(Cmd("""
            INSERT dbo.MV_APLICACION(CUENTA,TC,SUCURSAL,NUMERO,LETRA,TCO_ORIGEN,SUCURSAL_ORIGEN,NUMERO_ORIGEN,LETRA_ORIGEN,IDCOMPLEMENTO_ORIGEN,
              IMPORTE,IDComprobante,IDComprobante_Origen,UNEGOCIO,Secuencia)
            VALUES(@cuenta,@tc,@suc,@nro,@letra,@origenTc,@origenSuc,@origenNro,@origenLetra,0,0,@id,@origenId,@unidad,1);
            UPDATE dbo.C_MV_CPTE SET Finalizado=@completo WHERE ID=@origen;
            UPDATE dbo.C_MV_CPTE SET TcOrigen=@origenTc,ComprobanteOrigen=@origenId,IDDEPOSITO=@deposito WHERE TC=@tc AND IDCOMPROBANTE=@id AND LTRIM(RTRIM(CUENTA))=@cuenta;
            """,new{cuenta=d.Proveedor.Trim(),tc=d.Tipo.Trim(),suc=vista.IdComprobante[..4],nro=vista.IdComprobante.Substring(4,8),letra=d.Letra,origenTc=cab.Tipo.Trim(),
                origenSuc=cab.Comprobante[..4],origenNro=cab.Comprobante.Substring(4,8),origenLetra=cab.Comprobante[12..],id=vista.IdComprobante,origenId=cab.Comprobante,unidad=CodigoPk.Format(d.Unidad,4),origen,completo,deposito=CodigoPk.Format(d.Deposito,4)},ct,tx));
    }
    private static void ValidarCantidadesOrigen(CompraCargaBorrador d,List<LineaOrigenMercaderia> filas)
    {
        if(d.Lineas.Count==0||d.Lineas.Select(l=>l.Articulo).Distinct().Count()!=d.Lineas.Count)
            throw new InvalidOperationException("Elegí artículos pendientes del origen, sin repetirlos.");
        foreach(var l in d.Lineas)
        {
            var f=filas.SingleOrDefault(f=>f.Articulo==l.Articulo);
            if(f is null||l.Cantidad<=0||decimal.Round(l.Cantidad,4)!=l.Cantidad||l.Cantidad>f.Pendiente||f.Unidad!=l.UnidadArticulo||f.Exento!=(l.Tratamiento=="Exento"))
                throw new InvalidOperationException("La cantidad a facturar debe ser positiva y no superar el saldo pendiente del artículo en el origen. Volvé a consultar el remito u OC.");
        }
    }
    private static async Task GrabarArticuloAsync(SqlConnection c,SqlTransaction tx,CompraCargaBorrador d,CompraCargaVista vista,CompraCargaLinea l,int i,bool mueveStock,string origenTipo,string origenId,CancellationToken ct)
    {
        var precio=l.CostoUnitario??decimal.Round(l.Neto/l.Cantidad,4,MidpointRounding.AwayFromZero);
            if(CompraCargaPlanificador.Moneda(precio*l.Cantidad)!=l.Neto)throw new InvalidOperationException("El costo unitario requiere más precisión que la admitida. Revisá el importe de la mercadería.");
            var p=new{tc=d.Tipo.Trim(),id=vista.IdComprobante,cuenta=d.Proveedor.Trim(),articulo=CodigoPk.Format(l.Articulo,25),descripcion=l.Descripcion,
                unidad=CodigoPk.Format(l.UnidadArticulo,4),cantidad=l.Cantidad,cantidadStock=l.Cantidad*(d.Tipo=="NCC"?-1:1),costo=precio,neto=l.Neto,iva=vista.Bases[i].Iva,tasa=l.Tasa,exento=l.Tratamiento=="Exento",secuencia=i+1,
                deposito=CodigoPk.Format(d.Deposito,4),fecha=d.Fecha.Date,unegocio=CodigoPk.Format(d.Unidad,4),origenTipo=origenTipo.Trim(),origenId};
            await c.ExecuteAsync(Cmd("""
                INSERT dbo.C_MV_CPTEINSUMOS(TC,IDCOMPROBANTE,CUENTA,IDARTICULO,DESCRIPCION,IDUNIDAD,IDUNIDADBASE,CANTIDADUD,CANTIDAD,EQUIV_UDBASE,
                  COSTO,IMPORTE,IMPORTE_S_IVA,TOTAL,EXENTO,AlicIva,Secuencia)
                VALUES(@tc,@id,@cuenta,@articulo,@descripcion,@unidad,@unidad,@cantidad,@cantidad,1,@costo,@costo,@costo,@neto,@exento,@tasa,@secuencia);
                """,p,ct,tx));
            if(mueveStock)await c.ExecuteAsync(Cmd("""
                INSERT dbo.V_MV_STOCK(TC,IDCOMPROBANTE,IDCOMPLEMENTO,SECUENCIA,FECHA,IDArticulo,Descripcion,IDUnidad,IdUnidadBase,CantidadUD,Cantidad,EQUIV_UDBASE,
                  Costo,IMPORTE_S_IVA,IVARI,CuentaProveedor,IdDeposito,TC_Origen,IdComprobante_Origen,UNEGOCIO)
                VALUES(@tc,@id,0,@secuencia,@fecha,@articulo,@descripcion,@unidad,@unidad,@cantidadStock,@cantidadStock,1,@costo,@neto,@tasa,@cuenta,@deposito,NULLIF(@origenTipo,''),NULLIF(@origenId,''),@unegocio);
                """,p,ct,tx));
    }
    private sealed class OrigenMercaderia
    {
        public int Id{get;set;}public string Tipo{get;set;}="";public string Comprobante{get;set;}="";public string Cuenta{get;set;}="";public string Unidad{get;set;}="";
        public string Moneda{get;set;}="";public decimal Cotizacion{get;set;}public string Deposito{get;set;}="";public string Motivo{get;set;}="";public bool NoDisponible{get;set;}
    }
    private sealed class LineaOrigenMercaderia
    {
        public int Secuencia{get;set;}public string Articulo{get;set;}="";public string Descripcion{get;set;}="";public string Unidad{get;set;}="";
        public decimal Cantidad{get;set;}public decimal Precio{get;set;}public decimal Aplicada{get;set;}public decimal Pendiente=>Cantidad-Aplicada;public decimal Tasa{get;set;}public bool Exento{get;set;}public bool Especial{get;set;}
    }
    private sealed class CantidadAplicadaOrigen
    {
        public string Articulo{get;set;}="";public decimal Cantidad{get;set;}
    }
    private sealed class StockOrigenMercaderia
    {
        public int Id{get;set;}public string Articulo{get;set;}="";public decimal Cantidad{get;set;}public string Deposito{get;set;}="";public string Unidad{get;set;}="";
    }
}
