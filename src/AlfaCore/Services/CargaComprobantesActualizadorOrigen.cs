using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Efectos locales de ActualizaInsumosComprobante (V_mv_vta.frm 12547-12590).
/// Componente de la futura transacción de factura; no expone grabación independiente.
/// La finalización la determina MV_APLICACION y sus triggers al insertar la aplicación.
/// </summary>
public sealed class CargaComprobantesActualizadorOrigen
{
    public async Task ActualizarAsync(SqlConnection cn, SqlTransaction transaction,
        CargaComprobantesOrigen origen, IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Connection != cn)
            throw new ArgumentException("La actualización de origen requiere la misma conexión y transacción de la factura.", nameof(transaction));
        CargaComprobantesSimulador.ValidarSeleccion(origen, cantidades);
        if (origen.Tc is not ("NP" or "RM"))
            throw new CargaComprobantesReglaException("La actualización admite únicamente pedidos o remitos individuales.");
        var lineas = origen.Lineas.ToDictionary(x => x.Id);
        // Serializa operaciones del nuevo módulo sobre la cabecera; no reserva fuera del commit.
        var disponible = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK)
            WHERE ID=@Id AND LTRIM(RTRIM(TC))=@Tc AND LTRIM(RTRIM(IDCOMPROBANTE))=@Comprobante
                AND IDCOMPLEMENTO=0 AND ANULADA=0 AND FINALIZADA=0
                AND ISNULL(BLOQUEADA,0)=0 AND ISNULL(CERRADA,0)=0;
            """, new { origen.Id, origen.Tc, origen.Comprobante }, transaction, cancellationToken: ct));
        if (disponible != 1)
            throw new CargaComprobantesReglaException("El origen cambió antes de grabar. Volvé a consultar sus pendientes.");

        foreach (var item in cantidades.Where(x => x.Cantidad > 0).OrderBy(x => x.LineaId))
        {
            var linea = lineas[item.LineaId];
            var cantidad = decimal.Round(item.Cantidad, 4, MidpointRounding.ToEven);
            if (cantidad <= 0 || cantidad != item.Cantidad)
                throw new CargaComprobantesReglaException("La grabación legacy requiere cantidades representables con cuatro decimales.");
            // VB6 resta/suma AuxCoeficiente: no multiplicar por la cantidad ni por la equivalencia.
            var coeficiente = decimal.Round(linea.CoeficienteUnidad, 4, MidpointRounding.ToEven);
            var sql = origen.Tc == "RM" ? """
                UPDATE dbo.V_MV_CPTEINSUMOS WITH(UPDLOCK,HOLDLOCK)
                SET CANTIDADAFACTURAR=CASE WHEN CANTIDADAFACTURAR-@Cantidad<0 THEN 0 ELSE CANTIDADAFACTURAR-@Cantidad END,
                    CANTIDADAFACTURARUD=CASE WHEN CANTIDADAFACTURARUD-@Coeficiente<0 THEN 0 ELSE CANTIDADAFACTURARUD-@Coeficiente END,
                    RECEPCION=-999
                """ : """
                UPDATE dbo.V_MV_CPTEINSUMOS WITH(UPDLOCK,HOLDLOCK)
                SET CANTIDADPREPARADA=ISNULL(CANTIDADPREPARADA,0)+@Cantidad,
                    CANTIDADPREPARADAUD=ISNULL(CANTIDADPREPARADAUD,0)+@Coeficiente,
                    CANTIDADAFACTURAR=0,CANTIDADAFACTURARUD=0,RECEPCION=-999
                """;
            // Los triggers legacy no siempre usan NOCOUNT: contar únicamente filas del UPDATE,
            // sin confundir sus escrituras internas con renglones actualizados por el comando.
            sql = "DECLARE @Actualizados TABLE (Id int);\n" + sql + "\nOUTPUT inserted.ID INTO @Actualizados\n";
            // Pendiente operativo oficial y snapshot de cantidad/coefficient revalidados en la transacción.
            sql += """

                WHERE ID=@LineaId AND LTRIM(RTRIM(TC))=@Tc AND LTRIM(RTRIM(IDCOMPROBANTE))=@Comprobante
                    AND IDCOMPLEMENTO=0 AND SECUENCIA=@Secuencia
                    AND CONVERT(decimal(28,8),ISNULL(CANTIDAD,0))=@CantidadOriginal
                    AND CONVERT(decimal(28,8),ISNULL(CANTIDADUD,0))=@CoeficienteOriginal
                    AND @Cantidad <= (SELECT ISNULL(SUM(v.SALDO),0) FROM dbo.VT_V_MV_INSUMOS_SALDOSRM v
                        WHERE LTRIM(RTRIM(v.TC))=@Tc AND LTRIM(RTRIM(v.IDCOMPROBANTE))=@Comprobante
                            AND LTRIM(RTRIM(v.IDARTICULO))=@Articulo AND ISNULL(LTRIM(RTRIM(v.IdDeposito)),'')=@Deposito);
                """;
            sql += "\nSELECT COUNT(*) FROM @Actualizados;";
            var updated = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
            {
                LineaId = item.LineaId, origen.Tc, origen.Comprobante, linea.Secuencia, linea.Articulo, linea.Deposito,
                Cantidad = cantidad, Coeficiente = coeficiente, linea.CantidadOriginal, CoeficienteOriginal = linea.CoeficienteUnidad
            }, transaction, cancellationToken: ct));
            if (updated != 1)
                throw new CargaComprobantesReglaException("Cambió un renglón o su pendiente. Se debe revertir la transacción y recargar el origen.");
        }
    }
}
