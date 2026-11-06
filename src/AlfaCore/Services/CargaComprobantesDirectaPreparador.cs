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
        if (!CargaComprobantesTipos.EsValido(draft.Tc)) throw new CargaComprobantesReglaException("Tipo de comprobante no admitido.");
        if (CargaComprobantesTipos.EsProforma(draft.Tc) && !ctx.UsaFacturaProforma)
            throw new CargaComprobantesReglaException("La factura proforma no está habilitada en la configuración de esta base (UsaFacturaProforma).");
        if (CargaComprobantesTipos.RequiereAsociado(draft.Tc) ? draft.FacturaAsociadaId is null or <= 0 : draft.FacturaAsociadaId is not null)
            throw new CargaComprobantesReglaException(draft.Tc == "NCFP" ? "La nota de crédito proforma requiere la factura proforma asociada."
                : "Las notas requieren una factura asociada; las facturas, proformas, presupuestos, pedidos y remitos no llevan asociado.");
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
        // V_mv_vta (CalculaPercepciones) no liquida percepciones en RM/PR/FP/NCFP/NP: solo los electrónicos las requieren.
        if (!CargaComprobantesTipos.EsElectronico(draft.Tc)) origen.TienePercepciones = false;
        var seleccionPrecios = new CargaComprobantesSeleccionPrecios();
        var (pricing, preciosLista) = await seleccionPrecios.ResolverConConfigAsync(cn, ctx.Usuario, draft, ct, tx);
        if (articulos.Count > 0)
            await seleccionPrecios.ValidarArticulosAsync(cn, ctx.Usuario, draft.Tc, pricing, articulos.Select(x => x.Articulo), ct, tx);
        var edicion = new CargaComprobantesEdicionPrecios();
        var politica = articulos.Any(x => x.PrecioUnitarioNeto.HasValue || x.Descuento.HasValue)
            ? await edicion.LeerAsync(cn, ctx.Usuario, draft.Cliente, draft.Tc, ct, tx) : null;
        var clase = Math.Clamp(pricing.ClasePrecio, 1, 8);
        var rows = articulos.Count == 0 ? new List<Articulo>() : (await cn.QueryAsync<Articulo>(new CommandDefinition($"""
            SELECT LTRIM(RTRIM(a.IDARTICULO)) Codigo,a.DESCRIPCION Descripcion,LTRIM(RTRIM(a.IDUNIDAD)) Unidad,
                ISNULL(a.TasaIVA,0) Iva,CONVERT(bit,ISNULL(a.EXENTO,0)) Exento,ISNULL(a.PRECIO{clase},0) PrecioMaestro,ISNULL(p.Precio{clase},0) PrecioLista,
                ISNULL(a.IMPUESTOS,0) Internos,
                CASE WHEN ISNULL(a.Suspendido,0)<>0 OR ISNULL(a.SuspendidoV,0)<>0 THEN 'está suspendido para la venta'
                    WHEN ISNULL(LTRIM(RTRIM(a.IDUNIDAD)),'')='' THEN 'no tiene unidad de medida cargada'
                    WHEN ISNULL(a.USASERIE,0)<>0 THEN 'usa números de serie'
                    WHEN ISNULL(a.USALOTE,0)<>0 THEN 'usa lotes'
                    WHEN ISNULL(a.UsaTalle,0)<>0 THEN 'usa talles'
                    WHEN ISNULL(a.PideEquivalencia,0)<>0 THEN 'pide equivalencia de unidades'
                    WHEN ISNULL(a.PideMedidas,0)<>0 THEN 'pide medidas'
                    WHEN ISNULL(a.EnComodato,0)<>0 THEN 'está en comodato'
                    WHEN ISNULL(a.IMPUESTOS,0)<>0 AND @PreciosFinales=0 THEN 'tiene impuestos internos (requiere PreciosFinales=SI)'
                    WHEN ISNULL(a.ITC,0)<>0 THEN 'tiene impuesto a los combustibles (ITC)'
                    WHEN ISNULL(LTRIM(RTRIM(a.CuentaContable)),'')<>'' THEN 'tiene cuenta contable propia ('+LTRIM(RTRIM(a.CuentaContable))+')'
                    WHEN ISNULL(LTRIM(RTRIM(a.IdPercepcion)),'')<>'' THEN 'tiene percepción propia ('+LTRIM(RTRIM(a.IdPercepcion))+')'
                    WHEN ISNULL(LTRIM(RTRIM(a.Moneda)),'1') NOT IN ('','1') THEN 'está en moneda extranjera'
                    WHEN @ConLista=0 AND (ISNULL(a.Dto1,0)<>0 OR ISNULL(a.Dto2,0)<>0 OR ISNULL(a.Dto3,0)<>0 OR ISNULL(a.Dto4,0)<>0
                        OR ISNULL(a.Dto5,0)<>0 OR ISNULL(a.Dto6,0)<>0 OR ISNULL(a.Dto7,0)<>0 OR ISNULL(a.Dto8,0)<>0 OR ISNULL(a.Dto9,0)<>0
                        OR ISNULL(a.Rec1,0)<>0 OR ISNULL(a.Rec2,0)<>0 OR ISNULL(a.Rec3,0)<>0) THEN 'tiene descuentos o recargos en el maestro de artículos'
                    WHEN @ConLista=0 AND (ISNULL(p.Dto1,0)<>0 OR ISNULL(p.Dto2,0)<>0 OR ISNULL(p.Dto3,0)<>0 OR ISNULL(p.Dto4,0)<>0
                        OR ISNULL(p.Dto5,0)<>0 OR ISNULL(p.Dto6,0)<>0 OR ISNULL(p.Dto7,0)<>0 OR ISNULL(p.Dto8,0)<>0 OR ISNULL(p.Dto9,0)<>0
                        OR ISNULL(p.Rec1,0)<>0 OR ISNULL(p.Rec2,0)<>0 OR ISNULL(p.Rec3,0)<>0) THEN 'tiene descuentos o recargos en la lista de precios'
                    WHEN ISNULL(p.CantidadDesde,0)<>0 THEN 'tiene precios por cantidad (CantidadDesde) en la lista'
                    WHEN @ConLista=0 AND (ISNULL(a.EnOferta,0)<>0 OR ISNULL(p.CantidadOf2,0)<>0 OR ISNULL(p.CantidadOf3,0)<>0)
                        THEN 'está en oferta o tiene precios por cantidad'
                    ELSE '' END Motivo
            FROM dbo.V_MA_ARTICULOS a LEFT JOIN dbo.V_MA_PRECIOS p ON p.IdArticulo=a.IDARTICULO
                AND LTRIM(RTRIM(p.IdLista))=@Lista AND p.TipoLista='V'
            WHERE LTRIM(RTRIM(a.IDARTICULO)) IN @Codigos;
            """, new { Lista = pricing.UsaListas ? pricing.IdLista : "", Codigos = articulos.Select(x => x.Articulo.Trim()).ToArray(),
                ConLista = pricing.UsaListas, ctx.PreciosFinales }, tx, cancellationToken: ct))).ToList();
        var filasLista = pricing.UsaListas
            ? await CargaComprobantesPreciosLista.LeerAsync(cn, pricing.IdLista, rows.Select(x => x.Codigo), ct, tx)
            : new Dictionary<string, CargaComprobantesPrecioListaFila>(StringComparer.OrdinalIgnoreCase);
        var fechaPrecio = (draft.Fecha ?? DateTime.Today).Date;
        var problemas = new List<string>();
        for (var i = 0; i < draft.Lineas.Count; i++)
        {
            var item = draft.Lineas[i];
            if (item.EsConcepto) continue;
            var a = rows.FirstOrDefault(x => string.Equals(x.Codigo, item.Articulo.Trim(), StringComparison.OrdinalIgnoreCase));
            if (a is null) problemas.Add($"Renglón {i + 1} · artículo {item.Articulo.Trim()}: no existe en el maestro de artículos.");
            else if (a.Motivo.Length > 0) problemas.Add($"Renglón {i + 1} · {a.Codigo} {a.Descripcion.Trim()}: {a.Motivo}.");
            // Retail: los precios del maestro son costos; sin precio de venta en la lista no se factura.
            else if (preciosLista.Retail && (!filasLista.TryGetValue(a.Codigo, out var filaRetail) || filaRetail.Precio(clase) <= 0))
                problemas.Add($"Renglón {i + 1} · {a.Codigo} {a.Descripcion.Trim()}: no tiene precio de {preciosLista.NombreClase(clase)} en la lista {pricing.IdLista.Trim()}.");
        }
        if (problemas.Count > 0)
            throw new CargaComprobantesReglaException(string.Join(" ", problemas)
                + " La carga web todavía no admite ese circuito; quitá el renglón o cargalo desde el sistema de escritorio.");
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
            if (CargaComprobantesTipos.EsCredito(draft.Tc) && item.Bultos.HasValue)
                throw new CargaComprobantesReglaException("La nota de crédito conserva las unidades y cantidades de su factura; ingresá la cantidad a acreditar.");
            var medidas = unidades!.Resolver(a.Codigo,a.Unidad,item.Cantidad,item.Bultos);
            var tasa = a.Exento ? 0 : a.Iva == 0 ? ctx.AlicuotaGeneral : a.Iva;
            // Precio de lista (clsTotales.ImporteLista): oferta por fecha y ofertas por cantidad; sin fila, el maestro.
            // PreciosFinales: el interno (importe fijo por unidad del maestro) está dentro del precio final y se separa antes del IVA.
            var interno = ctx.PreciosFinales ? a.Internos : 0;
            var precio = CargaComprobantesPreciosLista.Calcular(filasLista.GetValueOrDefault(a.Codigo), a.PrecioMaestro, clase,
                medidas.Unidades, fechaPrecio, preciosLista, tasa, pricing.PreciosConIva, interno);
            var (neto, final) = (precio.Neto, precio.Final - interno);
            var descuentoLista = precio.Descuento;
            var costoLista = precio.Costo;
            if (neto <= 0) throw new CargaComprobantesReglaException($"El artículo {a.Codigo} no tiene un precio de venta positivo.");
            var referencia = neto;
            var descuento = descuentoLista;
            var editada = item.PrecioUnitarioNeto.HasValue || item.Descuento.HasValue;
            if (editada)
                (neto, final, descuento) = CargaComprobantesEdicionPrecios.Aplicar(new()
                { Articulo=item.Articulo,Cantidad=medidas.Unidades,PrecioUnitarioNeto=item.PrecioUnitarioNeto,Descuento=item.Descuento }, draft.Tc, politica!, neto, final, tasa, ctx.Decimales, descuentoLista);
            final += interno;
            lineas.Add(new() { Id = lineas.Count + 1, Secuencia = lineas.Count + 1, Articulo = a.Codigo, Descripcion = a.Descripcion,
                Unidad = a.Unidad, Deposito = draft.Deposito.Trim(), CantidadOriginal = medidas.Unidades, Pendiente = medidas.Unidades,
                CoeficienteUnidad = 1, RenglonesArticulo = 1, PrecioSinIva = neto, Precio = final, AlicuotaIva = tasa,
                CantidadReferencia = medidas.Bultos != 0 || medidas.Kilos != 0 || medidas.MetrosCubicos != 0 ? medidas.Unidades : 0,
                BultosReferencia=medidas.Bultos,KilosReferencia=medidas.Kilos,MetrosCubicosReferencia=medidas.MetrosCubicos,
                Exento = a.Exento, Descuento = descuento, PrecioReferenciaNeto = editada ? referencia : null,
                ImpuestoInternoUnitario = interno, CostoReferencia = costoLista });
        }
        origen.Tc = CargaComprobantesTipos.EsProforma(draft.Tc) ? draft.Tc : "DIRECTA"; origen.Moneda = "1"; origen.DatosCalculoDisponibles = true;
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
        public decimal Internos { get; set; }
        public string Motivo { get; set; } = "";
    }
}
