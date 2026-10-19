using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesHistoricosTests
{
    [CargaComprobantesSqlFact]
    public async Task ContrastaLiquidacionDeArticulosConFacturasHistoricasSinAjustesGlobales()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        var contexto = await new CargaComprobantesRepository().GetContextoAsync(cn,
            Environment.GetEnvironmentVariable("ALFACORE_TEST_USUARIO") ?? "Administrador", default);
        var muestra = (await cn.QueryAsync<Historico>("""
            SELECT TOP (100) c.ID, c.FECHA Fecha, LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante,
                ISNULL(c.NetoGravado,0) NetoGravado,ISNULL(c.NetoNoGravado,0) NetoNoGravado,
                ISNULL(c.ImporteIva,0)+ISNULL(c.ImporteIva2,0)+ISNULL(c.ImpIVA3,0) Iva,
                ISNULL(c.ImporteImpuestosInternos,0) ImpuestosInternos,c.IMPORTE Total,
                CONVERT(bit,ISNULL(c.ExentoIVAArticulos,0)) ExentoIvaArticulos
            FROM dbo.V_MV_CPTE c WHERE LTRIM(RTRIM(c.TC))='FC' AND c.IDCOMPLEMENTO=0 AND c.ANULADA=0
                AND ISNULL(LTRIM(RTRIM(c.Moneda)),'1') IN ('','1')
                AND ISNULL(c.PorcDescuento1,0)=0 AND ISNULL(c.PorcDescuento2,0)=0 AND ISNULL(c.PorcDescuento3,0)=0 AND ISNULL(c.PorcDescuento4,0)=0
                AND ISNULL(c.ImpDescuento1,0)=0 AND ISNULL(c.ImpDescuento2,0)=0 AND ISNULL(c.ImpDescuento3,0)=0 AND ISNULL(c.ImpDescuento4,0)=0
                AND ISNULL(c.PorcRecargo,0)=0 AND ISNULL(c.ImpAnticipo,0)=0
                AND ISNULL(c.RETIBR_Importe,0)=0 AND ISNULL(c.RETIVA_Importe,0)=0 AND ISNULL(c.RETGAN_Importe,0)=0
                AND ISNULL(c.ImporteServicios,0)=0 AND ISNULL(c.ImporteOtrosConceptos,0)=0 AND ISNULL(c.ITC,0)=0 AND ISNULL(c.ImporteIvaRec,0)=0
                AND NOT EXISTS(SELECT 1 FROM dbo.V_MV_CPTETAREAS t WHERE t.TC=c.TC AND t.IDCOMPROBANTE=c.IDCOMPROBANTE)
                AND NOT EXISTS(SELECT 1 FROM dbo.V_MV_CPTE_OBSERV o WHERE o.TC=c.TC AND o.IDCOMPROBANTE=c.IDCOMPROBANTE AND ISNULL(o.Importe,0)<>0)
                AND EXISTS(SELECT 1 FROM dbo.V_MV_CPTEINSUMOS i WHERE i.TC=c.TC AND i.IDCOMPROBANTE=c.IDCOMPROBANTE AND i.IDCOMPLEMENTO=0)
                AND NOT EXISTS(SELECT 1 FROM dbo.V_MV_CPTEINSUMOS i WHERE i.TC=c.TC AND i.IDCOMPROBANTE=c.IDCOMPROBANTE AND
                    (ISNULL(i.CANTIDAD,0)<=0 OR ISNULL(i.ITC,0)<>0 OR ISNULL(i.AlicIvaRec,0)<>0))
            ORDER BY c.FECHA DESC,c.ID DESC;
            """)).ToList();
        Assert.NotEmpty(muestra);
        var coincidencias = 0; var diferencias = 0; var fueraAlcance = 0;
        var diferenciasTotal = 0; var diferenciasNeto = 0; var diferenciasIva = 0;
        var casos = new List<object>();
        var diferenciasConNetoIgual = 0;
        var diferenciasConIvaSobreNetoGuardado = 0;
        var cierreSinAjusteCoincidencias = 0; var cierreConAjusteCoincidencias = 0; var cierresEvaluados = 0;
        decimal maximaDiferencia = 0;
        foreach (var historico in muestra)
        {
            var lineas = (await cn.QueryAsync<CargaComprobantesLineaOrigen>("""
                SELECT ID Id,SECUENCIA Secuencia,LTRIM(RTRIM(IDARTICULO)) Articulo,
                    CONVERT(decimal(28,8),CANTIDAD) CantidadOriginal,CONVERT(decimal(28,8),CANTIDAD) Pendiente,
                    IMPORTE Precio,IMPORTE_S_IVA PrecioSinIva,ISNULL(PorcDto,0) Descuento,
                    ISNULL(AlicIVA,0) AlicuotaIva,ISNULL(AlicIvaRec,0) AlicuotaIvaRecargo,
                    ISNULL(IMPUESTOS,0) ImpuestoInternoUnitario,ISNULL(ITC,0) ItcUnitario,
                    CONVERT(bit,ISNULL(EXENTO,0)) Exento,TotalFinal TotalOriginal,
                    COUNT(*) OVER(PARTITION BY IDARTICULO) RenglonesArticulo
                FROM dbo.V_MV_CPTEINSUMOS WHERE LTRIM(RTRIM(TC))='FC' AND LTRIM(RTRIM(IDCOMPROBANTE))=@Comprobante AND IDCOMPLEMENTO=0
                ORDER BY SECUENCIA,ID;
                """, new { historico.Comprobante })).ToList();
            var origen = new CargaComprobantesOrigen { Tc = "FC", Moneda = "1", DatosCalculoDisponibles = true,
                ExentoIvaArticulos = historico.ExentoIvaArticulos, Lineas = lineas };
            CargaComprobantesLiquidacion calculo;
            try { calculo = CargaComprobantesCalculador.Calcular(origen, contexto,
                lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.CantidadOriginal }).ToList()); }
            catch (CargaComprobantesReglaException ex)
            {
                fueraAlcance++;
                casos.Add(new { historico.Id, historico.Comprobante, Estado = "FUERA_ALCANCE", Motivo = ex.Message });
                continue;
            }
            var delta = Math.Abs(calculo.Total - historico.Total);
            maximaDiferencia = Math.Max(maximaDiferencia, delta);
            bool Igual(decimal a, decimal b) => decimal.Round(a, contexto.Decimales, MidpointRounding.ToEven) ==
                decimal.Round(b, contexto.Decimales, MidpointRounding.ToEven);
            if (Igual(calculo.Total, historico.Total) && Igual(calculo.NetoGravado, historico.NetoGravado) &&
                Igual(calculo.NetoNoGravado, historico.NetoNoGravado) && Igual(calculo.Iva, historico.Iva) &&
                Igual(calculo.ImpuestosInternos, historico.ImpuestosInternos)) coincidencias++;
            else diferencias++;
            if (!Igual(calculo.Total, historico.Total)) diferenciasTotal++;
            if (!Igual(calculo.NetoGravado, historico.NetoGravado) || !Igual(calculo.NetoNoGravado, historico.NetoNoGravado)) diferenciasNeto++;
            if (!Igual(calculo.Iva, historico.Iva)) diferenciasIva++;
            var coincide = Igual(calculo.Total, historico.Total) && Igual(calculo.NetoGravado, historico.NetoGravado) &&
                Igual(calculo.NetoNoGravado, historico.NetoNoGravado) && Igual(calculo.Iva, historico.Iva) &&
                Igual(calculo.ImpuestosInternos, historico.ImpuestosInternos);
            bool? ivaSobreNetoGuardado = calculo.Alicuotas.Count == 1 && historico.NetoNoGravado == 0
                ? Igual(historico.Iva, historico.NetoGravado * calculo.Alicuotas[0].Alicuota / 100) : null;
            if (!coincide && Igual(calculo.NetoGravado, historico.NetoGravado)) diferenciasConNetoIgual++;
            if (!coincide && ivaSobreNetoGuardado == true) diferenciasConIvaSobreNetoGuardado++;
            var letra = historico.Comprobante[^1..];
            var cierreSinAjuste = CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, letra, false);
            var cierreConAjuste = CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, letra, true);
            bool CoincideCierre(CierreLegacy cierre) => Igual(cierre.Total, historico.Total) &&
                Igual(cierre.NetoGravado, historico.NetoGravado) && Igual(cierre.NetoNoGravado, historico.NetoNoGravado) &&
                Igual(cierre.Iva, historico.Iva) && Igual(historico.ImpuestosInternos, 0);
            if (cierreSinAjuste is not null)
            {
                cierresEvaluados++;
                if (CoincideCierre(cierreSinAjuste)) cierreSinAjusteCoincidencias++;
                if (CoincideCierre(cierreConAjuste!)) cierreConAjusteCoincidencias++;
            }
            casos.Add(new
            {
                historico.Id, historico.Fecha, historico.Comprobante,
                Estado = coincide ? "COINCIDE" : "DIFIERE",
                Indicadores = new { IvaGuardadoCoincideConNetoGuardadoPorTasa = ivaSobreNetoGuardado,
                    NetoCoincideAPrecisionConfigurada = Igual(calculo.NetoGravado, historico.NetoGravado) },
                historico.ExentoIvaArticulos,
                Guardado = new { historico.Total, historico.NetoGravado, historico.NetoNoGravado, historico.Iva, historico.ImpuestosInternos },
                Calculado = new { calculo.Total, calculo.NetoGravado, calculo.NetoNoGravado, calculo.Iva, calculo.ImpuestosInternos },
                Delta = new { Total = calculo.Total - historico.Total, NetoGravado = calculo.NetoGravado - historico.NetoGravado,
                    NetoNoGravado = calculo.NetoNoGravado - historico.NetoNoGravado, Iva = calculo.Iva - historico.Iva },
                Lineas = lineas.Select(x => new { x.Id, x.Secuencia, x.Articulo, x.CantidadOriginal, x.Precio,
                    x.PrecioSinIva, x.Descuento, x.AlicuotaIva, x.Exento, x.ImpuestoInternoUnitario, x.TotalOriginal }),
                LiquidacionLineas = calculo.Lineas, calculo.Alicuotas,
                CierresDiagnosticos = new { SinAjusteFinal = cierreSinAjuste, ConAjusteFinal = cierreConAjuste,
                    CoincideSinAjuste = cierreSinAjuste is null ? (bool?)null : CoincideCierre(cierreSinAjuste),
                    CoincideConAjuste = cierreConAjuste is null ? (bool?)null : CoincideCierre(cierreConAjuste) }
            });
        }
        var resultado = new { Muestra = muestra.Count, Coincidencias = coincidencias, Diferencias = diferencias,
            FueraAlcance = fueraAlcance, MaximaDiferenciaTotal = maximaDiferencia, contexto.Decimales,
            diferenciasTotal, diferenciasNeto, diferenciasIva, contexto.AjustaRedondeoTotal,
            Limitacion = "Comparación retrospectiva con configuración actual; no ejecuta VB6 ni certifica emisión." };
        var json = JsonSerializer.Serialize(resultado, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        var destino = Environment.GetEnvironmentVariable("ALFACORE_CALCULO_REPORTE");
        if (!string.IsNullOrWhiteSpace(destino)) await File.WriteAllTextAsync(destino, json);
        // Los casos identificables son locales y opt-in; el agregado público conserva su contrato.
        var detalle = Environment.GetEnvironmentVariable("ALFACORE_CALCULO_CASOS");
        if (!string.IsNullOrWhiteSpace(detalle))
        {
            var reporte = new { Version = 1, FechaUtc = DateTime.UtcNow, Base = cn.Database,
                Advertencia = "Retrospectiva con configuración actual. No ejecuta VB6 ni certifica equivalencia.",
                Configuracion = new { contexto.Decimales, contexto.PreciosFinales, contexto.PreciosFinalesNeto,
                    contexto.AlicuotaGeneral, contexto.AjustaRedondeoTotal }, Resumen = resultado,
                Diagnostico = new { DiferenciasConNetoIgual = diferenciasConNetoIgual,
                    DiferenciasConIvaSobreNetoGuardado = diferenciasConIvaSobreNetoGuardado,
                    CierresEvaluados = cierresEvaluados, CierreSinAjusteCoincidencias = cierreSinAjusteCoincidencias,
                    CierreConAjusteCoincidencias = cierreConAjusteCoincidencias,
                    Limite = "Coincidencia numérica para investigar; no demuestra la rama VB6 ejecutada ni justifica cambiar la fórmula." },
                Casos = casos };
            await File.WriteAllTextAsync(detalle, JsonSerializer.Serialize(reporte,
                new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
        }
        // Las diferencias históricas son evidencia para investigar, nunca se borran ni se ajusta la base.
        Assert.True(coincidencias > 0, "No se encontró ningún caso histórico compatible con el cálculo incorporado.");
        Assert.Equal(muestra.Count, coincidencias + diferencias + fueraAlcance);
    }

    private sealed class Historico
    {
        public int Id { get; set; }
        public string Comprobante { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public decimal NetoGravado { get; set; }
        public decimal NetoNoGravado { get; set; }
        public decimal Iva { get; set; }
        public decimal ImpuestosInternos { get; set; }
        public decimal Total { get; set; }
        public bool ExentoIvaArticulos { get; set; }
    }
}
