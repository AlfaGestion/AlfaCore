using System.Security.Cryptography;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Valida la factura aplicada y conserva sus precios para devoluciones de artículos.</summary>
public sealed class CargaComprobantesAsociadoPreparador
{
    public async Task<(ArcaComprobanteAsociadoDto Fiscal, string Comprobante, string Huella)> PrepararAsync(
        SqlConnection cn, CargaComprobantesDirecta draft, CargaComprobantesOrigen origen, DateTime fecha, CancellationToken ct, SqlTransaction? tx)
    {
        var factura = await cn.QuerySingleOrDefaultAsync<Factura>(new CommandDefinition("""
            SELECT c.IDCOMPROBANTE Comprobante,c.FECHA Fecha,p.Cuit,p.TipoComprobante Tipo,p.PuntoVenta,p.Numero,c.IMPORTE Total
            FROM dbo.V_MV_CPTE c JOIN dbo.ALFACORE_VENTAS_CARGA_OPERACION op ON op.FacturaId=c.ID AND op.Estado='GRABADA'
            JOIN dbo.ALFACORE_VENTAS_HOMO_PRUEBA p ON p.Operacion=op.Operacion AND p.Estado='APROBADA' AND p.Ambiente='HOMOLOGACION'
            WHERE c.ID=@Id AND LTRIM(RTRIM(c.TC))='FC' AND c.IDCOMPLEMENTO=0 AND ISNULL(c.ANULADA,0)=0
                AND LTRIM(RTRIM(c.CUENTA))=@Cliente AND LTRIM(RTRIM(c.UNEGOCIO))=@Unidad;
            """, new { Id = draft.FacturaAsociadaId, Cliente = draft.Cliente.Trim(), Unidad = draft.UnidadNegocio.Trim() }, tx, cancellationToken: ct))
            ?? throw new CargaComprobantesReglaException("Elegí una factura grabada y aprobada en homologación, del mismo cliente y unidad de negocio.");
        if (factura.Fecha.Date > fecha.Date || factura.Tipo is not (1 or 6) || factura.Numero <= 0)
            throw new CargaComprobantesReglaException("La fecha o el tipo de factura asociada no son compatibles con esta nota.");
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION
            WHERE FacturaAsociadaId=@Id AND Borrador<>@Borrador AND Estado NOT IN ('GRABADA','RECHAZADA');
            """, new { Id = draft.FacturaAsociadaId, draft.Borrador }, tx, cancellationToken: ct)) != 0)
            throw new CargaComprobantesReglaException("La factura tiene otra nota pendiente. Recuperá esa operación antes de generar una nueva.");
        if (draft.Tc == "NC")
        {
            var lineas = (await cn.QueryAsync<Linea>(new CommandDefinition("""
                SELECT i.ID,LTRIM(RTRIM(i.IDARTICULO)) Articulo,LTRIM(RTRIM(i.IDUNIDAD)) Unidad,i.CANTIDAD Cantidad,
                    i.IMPORTE Precio,i.IMPORTE_S_IVA Neto,ISNULL(i.PorcDto,0) Descuento,ISNULL(i.AlicIVA,0) Iva,
                    ISNULL(i.CANTIDADUD,1) Coeficiente,ISNULL(i.EXENTO,0) Exento,LTRIM(RTRIM(i.IDUNIDADBASE)) UnidadBase,
                    ISNULL(i.EQUIV_UDBASE,1) Equivalencia,ISNULL(i.COSTO,0) Costo,
                    ISNULL(i.CANT_BL,0) Bultos,ISNULL(i.CANT_KG,0) Kilos,ISNULL(i.CANT_M3,0) MetrosCubicos,
                    ISNULL((SELECT SUM(n.CANTIDAD) FROM dbo.MV_APLICACION a
                        JOIN dbo.V_MV_CPTE c ON c.TC=a.TC AND c.IDCOMPROBANTE=a.IDCOMPROBANTE AND c.IDCOMPLEMENTO=0 AND c.ANULADA=0
                        JOIN dbo.V_MV_CPTEINSUMOS n ON n.TC=c.TC AND n.IDCOMPROBANTE=c.IDCOMPROBANTE AND n.IDCOMPLEMENTO=0
                        WHERE LTRIM(RTRIM(a.TC))='NC' AND LTRIM(RTRIM(a.TCO_ORIGEN))='FC'
                            AND a.IDCOMPROBANTE_ORIGEN=@Comprobante AND n.IDARTICULO=i.IDARTICULO),0) Acreditada
                FROM dbo.V_MV_CPTEINSUMOS i WHERE LTRIM(RTRIM(i.TC))='FC' AND i.IDCOMPROBANTE=@Comprobante AND i.IDCOMPLEMENTO=0;
                """, new { factura.Comprobante }, tx, cancellationToken: ct))).ToList();
            foreach (var l in origen.Lineas)
            {
                var matching = lineas.Where(x => x.Articulo == l.Articulo).ToList();
                if (matching.Count != 1) throw new CargaComprobantesReglaException($"El artículo {l.Articulo} no pertenece a la factura o aparece en varios renglones.");
                var original = matching[0];
                if (l.CantidadOriginal > original.Cantidad - original.Acreditada || original.Coeficiente != 1 || original.Unidad != l.Unidad ||
                    original.UnidadBase != original.Unidad || original.Equivalencia <= 0 || original.Exento)
                    throw new CargaComprobantesReglaException($"Revisá {l.Articulo}: la cantidad supera el saldo acreditable o requiere otra unidad/exención.");
                l.Precio = original.Precio; l.PrecioSinIva = original.Neto; l.Descuento = original.Descuento; l.AlicuotaIva = original.Iva;
                l.ReferenciaOrigenId = original.Id; l.CantidadReferencia = original.Cantidad;
                l.EquivalenciaReferencia = original.Equivalencia; l.CostoReferencia = original.Costo;
                l.BultosReferencia = original.Bultos; l.KilosReferencia = original.Kilos; l.MetrosCubicosReferencia = original.MetrosCubicos;
            }
        }
        var material = await cn.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT (SELECT c.*,
                JSON_QUERY((SELECT i.* FROM dbo.V_MV_CPTEINSUMOS i WHERE i.TC=c.TC AND i.IDCOMPROBANTE=c.IDCOMPROBANTE AND i.IDCOMPLEMENTO=0
                    ORDER BY i.SECUENCIA,i.ID FOR JSON PATH,INCLUDE_NULL_VALUES)) Detalle,
                JSON_QUERY((SELECT a.* FROM dbo.MV_APLICACION a WHERE a.TCO_ORIGEN=c.TC AND a.IDCOMPROBANTE_ORIGEN=c.IDCOMPROBANTE
                    ORDER BY a.TC,a.IDCOMPROBANTE FOR JSON PATH,INCLUDE_NULL_VALUES)) Aplicaciones
                FROM dbo.V_MV_CPTE c WHERE c.ID=@Id FOR JSON PATH,INCLUDE_NULL_VALUES);
            """, new { Id = draft.FacturaAsociadaId }, tx, cancellationToken: ct));
        return (new(factura.Tipo, factura.PuntoVenta, factura.Numero, factura.Cuit, factura.Fecha.Date), factura.Comprobante.Trim(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))));
    }
    private sealed class Factura
    {
        public string Comprobante { get; set; } = "";
        public string Cuit { get; set; } = "";
        public DateTime Fecha { get; set; }
        public int Tipo { get; set; }
        public int PuntoVenta { get; set; }
        public long Numero { get; set; }
        public decimal Total { get; set; }
    }
    private sealed class Linea
    {
        public int Id { get; set; }
        public string Articulo { get; set; } = "";
        public string Unidad { get; set; } = "";
        public string UnidadBase { get; set; } = "";
        public decimal Cantidad { get; set; }
        public decimal Acreditada { get; set; }
        public decimal Precio { get; set; }
        public decimal Neto { get; set; }
        public decimal Descuento { get; set; }
        public decimal Iva { get; set; }
        public decimal Coeficiente { get; set; }
        public bool Exento { get; set; }
        public decimal Equivalencia { get; set; }
        public decimal Costo { get; set; }
        public decimal Bultos { get; set; }
        public decimal Kilos { get; set; }
        public decimal MetrosCubicos { get; set; }
    }
}
