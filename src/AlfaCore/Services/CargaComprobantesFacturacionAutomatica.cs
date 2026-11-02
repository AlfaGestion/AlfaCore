using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Clientes en condiciones de facturación automática a una fecha, con las mismas reglas que
/// FrmFCAutoCptes.CmdCargar (v_FacturacionAutomatica.vbp). Solo consulta: no emite ni modifica nada.
/// </summary>
public static class CargaComprobantesFacturacionAutomatica
{
    public const string TipoOrigen = "FA";
    private const int Maximo = 2000;

    // Reglas del VB6 por periodicidad (FechaUltMov vacía o 01/01/1900 = nunca facturó, siempre entra):
    //   Diaria >= 1 día · Semanal >= 1 semana · Quincenal >= 15 días · Mensual: antes del día 1 del mes
    //   Bimestral/Trimestral/Semestral >= 2/3/6 meses · Anual >= 1 año.
    // Importe = conceptos sin cuotas o con cuota pendiente (NroUltimaCuota <= Cuotas); con importe 0 no se lista.
    // Una fila por cliente (el VB6 no repite la cuenta) y sin clientes dados de baja o bloqueados.
    private const string Sql = """
        DECLARE @F datetime = @Fecha;
        WITH Cab AS (
            SELECT a.Cuenta, a.Concepto, a.TC, a.Periodicidad, a.FechaUltMov, a.UNEGOCIO,
                UPPER(LTRIM(RTRIM(ISNULL(a.Periodicidad,'')))) Per,
                CASE WHEN a.FechaUltMov IS NULL OR YEAR(a.FechaUltMov) <= 1900 THEN NULL ELSE a.FechaUltMov END UltMov
            FROM dbo.MA_CUENTAS_AUTOCPTES a
            WHERE a.Activo = 1
        ), Aptos AS (
            SELECT c.*, ROW_NUMBER() OVER (PARTITION BY c.Cuenta ORDER BY
                    CASE c.Per WHEN 'DIARIA' THEN 1 WHEN 'SEMANAL' THEN 2 WHEN 'QUINCENAL' THEN 3 WHEN 'MENSUAL' THEN 4
                        WHEN 'BIMESTRAL' THEN 5 WHEN 'TRIMESTRAL' THEN 6 WHEN 'SEMESTRAL' THEN 7 ELSE 8 END, c.UNEGOCIO) Orden
            FROM Cab c
            WHERE c.Per IN ('DIARIA','SEMANAL','QUINCENAL','MENSUAL','BIMESTRAL','TRIMESTRAL','SEMESTRAL','ANUAL')
              AND (c.UltMov IS NULL OR
                   (c.Per = 'DIARIA' AND DATEDIFF(day, c.UltMov, @F) >= 1) OR
                   (c.Per = 'SEMANAL' AND DATEDIFF(week, c.UltMov, @F) >= 1) OR
                   (c.Per = 'QUINCENAL' AND DATEDIFF(day, c.UltMov, @F) >= 15) OR
                   (c.Per = 'MENSUAL' AND c.UltMov < DATEADD(month, DATEDIFF(month, 0, @F), 0)) OR
                   (c.Per = 'BIMESTRAL' AND DATEDIFF(month, c.UltMov, @F) >= 2) OR
                   (c.Per = 'TRIMESTRAL' AND DATEDIFF(month, c.UltMov, @F) >= 3) OR
                   (c.Per = 'SEMESTRAL' AND DATEDIFF(month, c.UltMov, @F) >= 6) OR
                   (c.Per = 'ANUAL' AND DATEDIFF(year, c.UltMov, @F) >= 1))
        )
        SELECT TOP(@Maximo) LTRIM(RTRIM(cl.CODIGO)) Cuenta, LTRIM(RTRIM(ISNULL(cl.CTACODIGO,''))) CtaCodigo,
            ISNULL(cl.RAZON_SOCIAL,'') Cliente,
            ISNULL(CONVERT(nvarchar(500), a.Concepto),'') Concepto, LTRIM(RTRIM(ISNULL(a.TC,''))) Tc,
            LTRIM(RTRIM(ISNULL(a.Periodicidad,''))) Periodicidad, a.UltMov FechaUltimoMovimiento,
            LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) UnidadNegocio,
            LTRIM(RTRIM(ISNULL(cl.IdVendedor,''))) Vendedor, ISNULL(vd.Nombre,'') VendedorNombre,
            det.Importe, det.Conceptos
        FROM Aptos a
        JOIN dbo.VT_CLIENTES cl ON cl.CODIGO = a.Cuenta
        CROSS APPLY (SELECT SUM(ISNULL(d.Importe,0)) Importe, COUNT(*) Conceptos
                     FROM dbo.MA_CUENTAS_AUTOCPTESDET d
                     WHERE d.Cuenta = a.Cuenta AND (ISNULL(d.Cuotas,0) <= 0 OR ISNULL(d.NroUltimaCuota,0) <= d.Cuotas)) det
        LEFT JOIN dbo.V_TA_VENDEDORES vd ON LTRIM(RTRIM(vd.IdVendedor)) = LTRIM(RTRIM(cl.IdVendedor))
        WHERE a.Orden = 1 AND ISNULL(cl.Dada_De_Baja,0) = 0 AND ISNULL(cl.BLOQUEO,0) = 0 AND det.Importe > 0
          AND (@AdminCentral = 1 OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) IN ('', @UnidadUsuario))
          AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) = @Unidad)
          AND (@Vendedor = '' OR LTRIM(RTRIM(ISNULL(cl.IdVendedor,''))) = @Vendedor)
          AND (@Zona = '' OR LTRIM(RTRIM(ISNULL(cl.ZONA,''))) = @Zona)
          AND (@Texto = '' OR LTRIM(RTRIM(cl.CODIGO)) LIKE @Like ESCAPE '\' OR LTRIM(RTRIM(ISNULL(cl.CTACODIGO,''))) LIKE @Like ESCAPE '\'
               OR cl.RAZON_SOCIAL LIKE @Like ESCAPE '\')
        ORDER BY LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))), LTRIM(RTRIM(cl.CODIGO));
        """;

    public static async Task<IReadOnlyList<CargaComprobantesFacturacionAutomaticaFila>> BuscarAsync(SqlConnection cn,
        CargaComprobantesContexto contexto, CargaComprobantesFiltro filtro, CancellationToken ct)
    {
        var fecha = (filtro.FechaFacturacion ?? DateTime.Today).Date;
        var texto = (filtro.Busqueda ?? string.Empty).Trim();
        static string Literal(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        var filas = (await cn.QueryAsync<CargaComprobantesFacturacionAutomaticaFila>(new CommandDefinition(Sql, new
        {
            Fecha = fecha, Maximo,
            contexto.AdminCentral, UnidadUsuario = contexto.UnidadNegocio.Trim(),
            Unidad = filtro.UnidadNegocio.Trim(), Vendedor = filtro.Vendedor.Trim(), Zona = filtro.Zona.Trim(),
            Texto = texto, Like = "%" + Literal(texto) + "%"
        }, commandTimeout: 60, cancellationToken: ct))).ToList();
        // Mismo reemplazo que FrmFCAutoCptes.GenerarCpte: <PERIODO> = mes/año de la fecha a facturar, sin ceros.
        var periodo = $"{fecha.Month}/{fecha.Year}";
        foreach (var fila in filas)
            fila.ConceptoFactura = fila.Concepto.Replace("<PERIODO>", periodo, StringComparison.OrdinalIgnoreCase);
        return filas;
    }
}
