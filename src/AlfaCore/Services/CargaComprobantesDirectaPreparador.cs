using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Resuelve una carga nueva desde los maestros actuales, sin escribir un origen ficticio.</summary>
public sealed class CargaComprobantesDirectaPreparador
{
    public async Task<(CargaComprobantesOrigen Origen, CrmCotizacionPricingContextDto Precios)> PrepararAsync(
        SqlConnection cn, CargaComprobantesContexto ctx, CargaComprobantesDirecta draft, CancellationToken ct, SqlTransaction? tx = null)
    {
        if (draft.Tc is not ("FC" or "NC" or "ND" or "FP")) throw new CargaComprobantesReglaException("Tipo de comprobante no admitido.");
        if (draft.Tc is "NC" or "ND" ? draft.FacturaAsociadaId is null or <= 0 : draft.FacturaAsociadaId is not null)
            throw new CargaComprobantesReglaException("Las notas requieren una factura asociada; las facturas nuevas y proformas no llevan asociado.");
        // Renglones de otros conceptos (sin artículo, como la grilla "Otros" de V_mv_vta): texto + importe sin IVA.
        var conceptos = draft.Lineas.Where(x => x.EsConcepto).ToList();
        var articulos = draft.Lineas.Where(x => !x.EsConcepto).ToList();
        if (draft.Borrador == Guid.Empty || string.IsNullOrWhiteSpace(draft.Cliente) || draft.Lineas.Count is < 1 or > 200 ||
            articulos.Any(x => string.IsNullOrWhiteSpace(x.Articulo) || x.Cantidad <= 0 || x.Cantidad > 9999999 || decimal.Round(x.Cantidad, 4) != x.Cantidad) ||
            articulos.GroupBy(x => x.Articulo.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new CargaComprobantesReglaException("Elegí un cliente y entre 1 y 200 artículos únicos, con cantidades positivas y hasta cuatro decimales.");
        if (conceptos.Any(x => string.IsNullOrWhiteSpace(x.Concepto) || x.Concepto!.Trim().Length > 250 || x.Cantidad != 1 ||
                x.PrecioUnitarioNeto is not { } importe || importe < 0 || importe > 999999999 || decimal.Round(importe, 4) != importe || x.Descuento.HasValue || x.Bultos.HasValue))
            throw new CargaComprobantesReglaException("Cada otro concepto necesita un texto de hasta 250 caracteres y un importe sin IVA de cero o más (cantidad 1, sin descuento).");
        if (conceptos.Count > 0 && draft.Tc is "NC" or "ND")
            throw new CargaComprobantesReglaException("Las notas de crédito y débito con otros conceptos todavía no están incorporadas.");
        if (draft.Observaciones.Length > 1000 || (!ctx.AdminCentral && draft.UnidadNegocio.Trim() != ctx.UnidadNegocio.Trim()))
            throw new CargaComprobantesReglaException("Revisá la unidad de negocio y las observaciones (hasta 1000 caracteres).");
        if (!string.IsNullOrWhiteSpace(draft.Vendedor) && await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.V_TA_VENDEDORES WHERE LTRIM(RTRIM(IdVendedor))=@Vendedor;",
                new { Vendedor = draft.Vendedor.Trim() }, tx, cancellationToken: ct)) == 0)
            throw new CargaComprobantesReglaException("El vendedor elegido no existe.");
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(CODIGO))=@Unidad;
            """, new { Unidad = draft.UnidadNegocio.Trim() }, tx, cancellationToken: ct)) != 1 ||
            await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.V_TA_Deposito WHERE LTRIM(RTRIM(IdDeposito))=@Deposito
                AND ISNULL(LTRIM(RTRIM(UnidadNegocio)),'') IN ('',@Unidad);
            """, new { Deposito = draft.Deposito.Trim(), Unidad = draft.UnidadNegocio.Trim() }, tx, cancellationToken: ct)) != 1)
            throw new CargaComprobantesReglaException("Elegí una unidad y un depósito compatibles.");
        var origen = await cn.QuerySingleOrDefaultAsync<CargaComprobantesOrigen>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) Cuenta,RAZON_SOCIAL Cliente,CONVERT(bit,ISNULL(ExentoIvaArticulos,0)) ExentoIvaArticulos,
                CONVERT(bit,CASE WHEN ISNULL(Descuento,0)<>0 THEN 1 ELSE 0 END) TieneAjustesGlobales,
                CONVERT(bit,CASE WHEN ISNULL(LTRIM(RTRIM(RETIBR_IdRetencion)),'')<>''
                    OR ISNULL(LTRIM(RTRIM(RETIBR_IdRetencionOnLine)),'')<>'' OR ISNULL(LTRIM(RTRIM(RETIVA_IdRetencion)),'')<>''
                    OR ISNULL(LTRIM(RTRIM(RETGAN_IdRetencion)),'')<>'' THEN 1 ELSE 0 END) TienePercepciones
            FROM dbo.VT_CLIENTES WHERE LTRIM(RTRIM(CODIGO))=@Cliente AND ISNULL(BLOQUEO,0)=0 AND ISNULL(Dada_De_Baja,0)=0;
            """, new { Cliente = draft.Cliente.Trim() }, tx, cancellationToken: ct))
            ?? throw new CargaComprobantesReglaException("El cliente no está disponible, está bloqueado o fue dado de baja.");
        var seleccionPrecios = new CargaComprobantesSeleccionPrecios();
        var pricing = await seleccionPrecios.ResolverAsync(cn, ctx.Usuario, draft, ct, tx);
        if (articulos.Count > 0)
            await seleccionPrecios.ValidarArticulosAsync(cn, ctx.Usuario, draft.Tc, pricing, articulos.Select(x => x.Articulo), ct, tx);
        var edicion = new CargaComprobantesEdicionPrecios();
        var politica = articulos.Any(x => x.PrecioUnitarioNeto.HasValue || x.Descuento.HasValue)
            ? await edicion.LeerAsync(cn, ctx.Usuario, draft.Cliente, draft.Tc, ct, tx) : null;
        var clase = Math.Clamp(pricing.ClasePrecio, 1, 8);
        var rows = articulos.Count == 0 ? new List<Articulo>() : (await cn.QueryAsync<Articulo>(new CommandDefinition($"""
            SELECT LTRIM(RTRIM(a.IDARTICULO)) Codigo,a.DESCRIPCION Descripcion,LTRIM(RTRIM(a.IDUNIDAD)) Unidad,
                ISNULL(a.TasaIVA,0) Iva,CONVERT(bit,ISNULL(a.EXENTO,0)) Exento,ISNULL(a.PRECIO{clase},0) PrecioMaestro,ISNULL(p.Precio{clase},0) PrecioLista,
                CONVERT(bit,CASE WHEN ISNULL(a.Suspendido,0)<>0 OR ISNULL(a.SuspendidoV,0)<>0 OR ISNULL(a.USASERIE,0)<>0
                    OR ISNULL(a.USALOTE,0)<>0 OR ISNULL(a.UsaTalle,0)<>0 OR ISNULL(a.PideEquivalencia,0)<>0
                    OR ISNULL(a.PideMedidas,0)<>0 OR ISNULL(a.EnComodato,0)<>0 OR ISNULL(a.IMPUESTOS,0)<>0 OR ISNULL(a.ITC,0)<>0
                    OR ISNULL(LTRIM(RTRIM(a.CuentaContable)),'')<>''
                    OR ISNULL(LTRIM(RTRIM(a.IdPercepcion)),'')<>'' OR ISNULL(LTRIM(RTRIM(a.Moneda)),'1') NOT IN ('','1')
                    OR ISNULL(a.Dto1,0)<>0 OR ISNULL(a.Dto2,0)<>0 OR ISNULL(a.Dto3,0)<>0 OR ISNULL(a.Dto4,0)<>0
                    OR ISNULL(a.Dto5,0)<>0 OR ISNULL(a.Dto6,0)<>0 OR ISNULL(a.Dto7,0)<>0 OR ISNULL(a.Dto8,0)<>0 OR ISNULL(a.Dto9,0)<>0
                    OR ISNULL(a.Rec1,0)<>0 OR ISNULL(a.Rec2,0)<>0 OR ISNULL(a.Rec3,0)<>0
                    OR ISNULL(p.Dto1,0)<>0 OR ISNULL(p.Dto2,0)<>0 OR ISNULL(p.Dto3,0)<>0 OR ISNULL(p.Dto4,0)<>0
                    OR ISNULL(p.Dto5,0)<>0 OR ISNULL(p.Dto6,0)<>0 OR ISNULL(p.Dto7,0)<>0 OR ISNULL(p.Dto8,0)<>0 OR ISNULL(p.Dto9,0)<>0
                    OR ISNULL(p.Rec1,0)<>0 OR ISNULL(p.Rec2,0)<>0 OR ISNULL(p.Rec3,0)<>0
                    OR ISNULL(a.EnOferta,0)<>0 OR ISNULL(p.CantidadDesde,0)<>0 OR ISNULL(p.CantidadOf2,0)<>0
                    OR ISNULL(p.CantidadOf3,0)<>0 THEN 1 ELSE 0 END) Especial
            FROM dbo.V_MA_ARTICULOS a LEFT JOIN dbo.V_MA_PRECIOS p ON p.IdArticulo=a.IDARTICULO
                AND LTRIM(RTRIM(p.IdLista))=@Lista AND p.TipoLista='V'
            WHERE LTRIM(RTRIM(a.IDARTICULO)) IN @Codigos;
            """, new { Lista = pricing.UsaListas ? pricing.IdLista : "", Codigos = articulos.Select(x => x.Articulo.Trim()).ToArray() }, tx, cancellationToken: ct))).ToList();
        if (rows.Count != articulos.Count || rows.Any(x => x.Especial || string.IsNullOrWhiteSpace(x.Unidad)))
            throw new CargaComprobantesReglaException("Un artículo no está disponible o requiere equivalencias, impuestos, ofertas, descuentos o un circuito especial pendiente de incorporar.");
        var lineas = new List<CargaComprobantesLineaOrigen>();
        var unidades = articulos.Count == 0 ? null : await CargaComprobantesUnidades.LeerAsync(cn, rows.Select(x => x.Codigo), ct, tx);
        foreach (var item in draft.Lineas)
        {
            if (item.EsConcepto)
            {
                // Otros conceptos: importe sin IVA tal cual; IVA a la alícuota general salvo "no gravado"
                // (clsTotales.AcumulaOtrosConceptos). En factura C el calculador no discrimina IVA.
                var importe = item.PrecioUnitarioNeto!.Value;
                var tasaConcepto = item.ConceptoNoGravado ? 0 : ctx.AlicuotaGeneral;
                lineas.Add(new() { Id = lineas.Count + 1, Secuencia = lineas.Count + 1, EsConcepto = true,
                    Descripcion = item.Concepto!.Trim(), Deposito = draft.Deposito.Trim(),
                    CantidadOriginal = 1, Pendiente = 1, CoeficienteUnidad = 1, RenglonesArticulo = 1,
                    PrecioSinIva = importe, Precio = decimal.Round(importe * (1 + tasaConcepto / 100), 4, MidpointRounding.ToEven),
                    AlicuotaIva = tasaConcepto, Exento = item.ConceptoNoGravado });
                continue;
            }
            var a = rows.Single(x => string.Equals(x.Codigo, item.Articulo.Trim(), StringComparison.OrdinalIgnoreCase));
            if (draft.Tc == "NC" && item.Bultos.HasValue)
                throw new CargaComprobantesReglaException("La nota de crédito conserva las unidades y cantidades de su factura; ingresá la cantidad a acreditar.");
            var medidas = unidades!.Resolver(a.Codigo,a.Unidad,item.Cantidad,item.Bultos);
            var tasa = a.Exento ? 0 : a.Iva == 0 ? ctx.AlicuotaGeneral : a.Iva;
            var bruto = pricing.UsaListas && a.PrecioLista > 0 ? a.PrecioLista : a.PrecioMaestro;
            var (neto, final) = ArticuloPrecioResolverService.SplitPrice(bruto, tasa, pricing.PreciosConIva);
            if (neto <= 0) throw new CargaComprobantesReglaException($"El artículo {a.Codigo} no tiene un precio de venta positivo.");
            var referencia = neto;
            var descuento = 0m;
            var editada = item.PrecioUnitarioNeto.HasValue || item.Descuento.HasValue;
            if (editada)
                (neto, final, descuento) = CargaComprobantesEdicionPrecios.Aplicar(new()
                { Articulo=item.Articulo,Cantidad=medidas.Unidades,PrecioUnitarioNeto=item.PrecioUnitarioNeto,Descuento=item.Descuento }, draft.Tc, politica!, neto, final, tasa, ctx.Decimales);
            lineas.Add(new() { Id = lineas.Count + 1, Secuencia = lineas.Count + 1, Articulo = a.Codigo, Descripcion = a.Descripcion,
                Unidad = a.Unidad, Deposito = draft.Deposito.Trim(), CantidadOriginal = medidas.Unidades, Pendiente = medidas.Unidades,
                CoeficienteUnidad = 1, RenglonesArticulo = 1, PrecioSinIva = neto, Precio = final, AlicuotaIva = tasa,
                CantidadReferencia = medidas.Bultos != 0 || medidas.Kilos != 0 || medidas.MetrosCubicos != 0 ? medidas.Unidades : 0,
                BultosReferencia=medidas.Bultos,KilosReferencia=medidas.Kilos,MetrosCubicosReferencia=medidas.MetrosCubicos,
                Exento = a.Exento, Descuento = descuento, PrecioReferenciaNeto = editada ? referencia : null });
        }
        origen.Tc = draft.Tc == "FP" ? "FP" : "DIRECTA"; origen.Moneda = "1"; origen.DatosCalculoDisponibles = true;
        origen.UnidadNegocio = draft.UnidadNegocio.Trim(); origen.Deposito = draft.Deposito.Trim();
        origen.Lista = pricing.UsaListas ? pricing.IdLista : ""; origen.Observaciones = draft.Observaciones; origen.Lineas = lineas;
        return (origen, pricing);
    }

    private sealed class Articulo
    {
        public string Codigo { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Unidad { get; set; } = "";
        public decimal Iva { get; set; }
        public bool Exento { get; set; }
        public decimal PrecioMaestro { get; set; }
        public decimal PrecioLista { get; set; }
        public bool Especial { get; set; }
    }
}
