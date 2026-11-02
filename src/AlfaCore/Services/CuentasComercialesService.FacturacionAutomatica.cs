using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Facturación automática de servicios por cliente: mismas tablas que la solapa Cptes-Automáticos
/// de Alfa Gestión (V_MA_Clientes). El comprobante lo sigue generando Alfa Gestión; acá solo se
/// configura. Siempre SucursalCuenta = 0, como hace el VB6 cuando la cuenta no tiene sucursal.
/// </summary>
public sealed partial class CuentasComercialesService
{
    /// <summary>Comprobantes que ofrece el combo de Alfa Gestión (VT_TA_CPTE).</summary>
    internal static readonly IReadOnlyList<string> TiposComprobanteAutomatico = ["FC", "FP", "RM", "NP"];

    /// <summary>Cliente con facturación automática: cabecera activa con al menos un concepto.
    /// (Alfa Gestión crea cabeceras vacías al grabar cualquier cliente, por eso no alcanza con que exista.)</summary>
    internal const string FacturacionAutomaticaActivaSql = """
        EXISTS (SELECT 1 FROM dbo.MA_CUENTAS_AUTOCPTES fa
                WHERE fa.Cuenta = base.CODIGO AND ISNULL(fa.Activo, 0) = 1
                  AND EXISTS (SELECT 1 FROM dbo.MA_CUENTAS_AUTOCPTESDET fd
                              WHERE fd.Cuenta = fa.Cuenta AND ISNULL(fd.SucursalCuenta, 0) = fa.SucursalCuenta))
        """;

    private static string BuildFiltroFacturacionAutomatica(CuentaComercialTipo tipo, CuentaComercialFilters filters)
        => tipo != CuentaComercialTipo.Cliente || filters.FacturacionAutomatica is null
            ? string.Empty
            : filters.FacturacionAutomatica.Value
                ? $"AND {FacturacionAutomaticaActivaSql}"
                : $"AND NOT {FacturacionAutomaticaActivaSql}";

    private static async Task<bool> TablasFacturacionAutomaticaExistenAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.MA_CUENTAS_AUTOCPTES', N'U') IS NOT NULL
                         AND OBJECT_ID(N'dbo.MA_CUENTAS_AUTOCPTESDET', N'U') IS NOT NULL THEN 1 ELSE 0 END;
            """, transaction: tx, cancellationToken: ct)) == 1;

    private static async Task<CuentaComercialFacturacionAutomaticaDto> LoadFacturacionAutomaticaAsync(SqlConnection cn, string codigo, CancellationToken ct)
    {
        var resultado = new CuentaComercialFacturacionAutomaticaDto();
        if (!await TablasFacturacionAutomaticaExistenAsync(cn, null, ct))
            return resultado;

        using var grid = await cn.QueryMultipleAsync(new CommandDefinition("""
            SELECT TOP (1) ISNULL(Activo, 0) AS Activo, LTRIM(RTRIM(ISNULL(TC, ''))) AS TC, ISNULL(Concepto, '') AS Concepto,
                LTRIM(RTRIM(ISNULL(Periodicidad, ''))) AS Periodicidad, FechaUltMov, LTRIM(RTRIM(ISNULL(UNEGOCIO, ''))) AS UNegocio
            FROM dbo.MA_CUENTAS_AUTOCPTES
            WHERE Cuenta = @Cuenta AND SucursalCuenta = 0;

            SELECT ID AS Id, ISNULL(Detalle, '') AS Detalle, ISNULL(Importe, 0) AS Importe,
                CONVERT(int, ROUND(ISNULL(NroUltimaCuota, 0), 0)) AS Cuota, CONVERT(int, ROUND(ISNULL(Cuotas, 0), 0)) AS Cuotas
            FROM dbo.MA_CUENTAS_AUTOCPTESDET
            WHERE Cuenta = @Cuenta AND ISNULL(SucursalCuenta, 0) = 0
            ORDER BY ID;
            """, new { Cuenta = codigo.Trim() }, cancellationToken: ct));

        var cabecera = await grid.ReadFirstOrDefaultAsync<FacturacionAutomaticaCabeceraRow>();
        if (cabecera is not null)
        {
            resultado.Configurada = true;
            resultado.Activo = cabecera.Activo;
            resultado.TipoComprobante = cabecera.TC.Length > 0 ? cabecera.TC : "FC";
            resultado.Concepto = cabecera.Concepto;
            resultado.Periodicidad = cabecera.Periodicidad.Length > 0 ? cabecera.Periodicidad : "Mensual";
            // Alfa Gestión deja 01/01/1900 cuando nunca facturó: se muestra vacío.
            resultado.FechaUltimoMovimiento = cabecera.FechaUltMov is { Year: > 1900 } fecha ? fecha : null;
            resultado.UnidadNegocio = cabecera.UNegocio;
        }

        resultado.Conceptos = (await grid.ReadAsync<CuentaComercialFacturacionConceptoDto>()).ToList();
        return resultado;
    }

    private static async Task SaveFacturacionAutomaticaAsync(SqlConnection cn, SqlTransaction tx, string codigo, CuentaComercialFacturacionAutomaticaDto config, CancellationToken ct)
    {
        var cuenta = codigo.Trim();
        var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.MA_CUENTAS_AUTOCPTES WHERE Cuenta = @Cuenta AND SucursalCuenta = 0;",
            new { Cuenta = cuenta }, tx, cancellationToken: ct)) > 0;

        // A diferencia del VB6, no se crea una cabecera vacía para cada cliente que se graba.
        if (!existe && !config.TieneDatos)
            return;

        var cabecera = new
        {
            Cuenta = cuenta,
            config.Concepto,
            config.Periodicidad,
            TC = config.TipoComprobante,
            config.Activo,
            Fecha = config.FechaUltimoMovimiento?.Date,
            UNegocio = config.UnidadNegocio
        };

        if (existe)
        {
            // Si la fecha se deja vacía se conserva la grabada: la usa Alfa Gestión para el próximo período.
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.MA_CUENTAS_AUTOCPTES
                SET Concepto = @Concepto, Periodicidad = @Periodicidad, TC = @TC, Activo = @Activo,
                    FechaUltMov = COALESCE(@Fecha, FechaUltMov), UNEGOCIO = @UNegocio
                WHERE Cuenta = @Cuenta AND SucursalCuenta = 0;
                """, cabecera, tx, cancellationToken: ct));
        }
        else
        {
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.MA_CUENTAS_AUTOCPTES (Cuenta, Concepto, Periodicidad, TC, Activo, SucursalCuenta, FechaUltMov, UNEGOCIO)
                VALUES (@Cuenta, @Concepto, @Periodicidad, @TC, @Activo, 0, @Fecha, @UNegocio);
                """, cabecera, tx, cancellationToken: ct));
        }

        // Conceptos: se actualiza cada renglón por su ID (el VB6 borra y vuelve a insertar). Así se
        // conservan ImporteOld/FechaCambio, que usa "Aumentar valores de facturación automática".
        var existentes = (await cn.QueryAsync<int>(new CommandDefinition(
            "SELECT ID FROM dbo.MA_CUENTAS_AUTOCPTESDET WHERE Cuenta = @Cuenta AND ISNULL(SucursalCuenta, 0) = 0;",
            new { Cuenta = cuenta }, tx, cancellationToken: ct))).ToHashSet();

        var conceptos = config.Conceptos.Where(c => !string.IsNullOrWhiteSpace(c.Detalle)).ToList();
        var conservados = conceptos.Where(c => c.Id is int id && existentes.Contains(id)).Select(c => c.Id!.Value).ToHashSet();

        foreach (var id in existentes.Where(id => !conservados.Contains(id)))
        {
            await cn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.MA_CUENTAS_AUTOCPTESDET WHERE ID = @Id AND Cuenta = @Cuenta;",
                new { Id = id, Cuenta = cuenta }, tx, cancellationToken: ct));
        }

        foreach (var concepto in conceptos)
        {
            var parametros = new
            {
                Id = concepto.Id ?? 0,
                Cuenta = cuenta,
                concepto.Detalle,
                concepto.Importe,
                Cuota = (double)concepto.Cuota,
                Cuotas = (double)concepto.Cuotas
            };

            if (concepto.Id is int id && conservados.Contains(id))
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.MA_CUENTAS_AUTOCPTESDET
                    SET ImporteOld = CASE WHEN Importe <> @Importe THEN Importe ELSE ImporteOld END,
                        FechaCambio = CASE WHEN Importe <> @Importe THEN GETDATE() ELSE FechaCambio END,
                        Detalle = @Detalle, Importe = @Importe, NroUltimaCuota = @Cuota, Cuotas = @Cuotas
                    WHERE ID = @Id AND Cuenta = @Cuenta;
                    """, parametros, tx, cancellationToken: ct));
            }
            else
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.MA_CUENTAS_AUTOCPTESDET (Cuenta, Detalle, Importe, NroUltimaCuota, Cuotas, SucursalCuenta)
                    VALUES (@Cuenta, @Detalle, @Importe, @Cuota, @Cuotas, 0);
                    """, parametros, tx, cancellationToken: ct));
            }
        }
    }

    private static async Task LoadFacturacionAutomaticaLookupsAsync(SqlConnection cn, CuentaComercialLookupDataDto data, CancellationToken ct)
    {
        data.TiposComprobanteAutomatico = (await cn.QueryAsync<CuentaComercialLookupOptionDto>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) AS Codigo, ISNULL(DESCRIPCION, '') AS Descripcion
            FROM dbo.VT_TA_CPTE WHERE LTRIM(RTRIM(CODIGO)) IN @Codigos;
            """, new { Codigos = TiposComprobanteAutomatico }, cancellationToken: ct)))
            .OrderBy(x => IndexOfTipo(x.Codigo))
            .ToList();
        // Si la vista no trae alguno, igual se ofrece con su código.
        foreach (var codigo in TiposComprobanteAutomatico.Where(c => data.TiposComprobanteAutomatico.All(x => !string.Equals(x.Codigo, c, StringComparison.OrdinalIgnoreCase))))
            data.TiposComprobanteAutomatico.Add(new CuentaComercialLookupOptionDto { Codigo = codigo, Descripcion = codigo });

        data.UnidadesNegocio = (await cn.QueryAsync<CuentaComercialLookupOptionDto>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(Codigo)) AS Codigo, MAX(ISNULL(Descripcion, '')) AS Descripcion
            FROM dbo.V_TA_UnidadNegocio
            WHERE ISNULL(LTRIM(RTRIM(Codigo)), '') <> ''
            GROUP BY LTRIM(RTRIM(Codigo))
            ORDER BY LTRIM(RTRIM(Codigo));
            """, cancellationToken: ct))).ToList();

        static int IndexOfTipo(string codigo)
        {
            for (var i = 0; i < TiposComprobanteAutomatico.Count; i++)
                if (string.Equals(TiposComprobanteAutomatico[i], codigo, StringComparison.OrdinalIgnoreCase))
                    return i;
            return int.MaxValue;
        }
    }

    private static CuentaComercialFacturacionAutomaticaDto? NormalizeFacturacionAutomatica(CuentaComercialFacturacionAutomaticaDto? config)
        => config is null ? null : new CuentaComercialFacturacionAutomaticaDto
        {
            Configurada = config.Configurada,
            Activo = config.Activo,
            TipoComprobante = (config.TipoComprobante ?? string.Empty).Trim().ToUpperInvariant(),
            Concepto = (config.Concepto ?? string.Empty).Trim(),
            Periodicidad = (config.Periodicidad ?? string.Empty).Trim(),
            FechaUltimoMovimiento = config.FechaUltimoMovimiento,
            UnidadNegocio = (config.UnidadNegocio ?? string.Empty).Trim(),
            Conceptos = (config.Conceptos ?? [])
                .Where(c => c is not null)
                .Select(c => new CuentaComercialFacturacionConceptoDto
                {
                    Id = c.Id is > 0 ? c.Id : null,
                    Detalle = (c.Detalle ?? string.Empty).Trim(),
                    Importe = c.Importe,
                    Cuota = c.Cuota,
                    Cuotas = c.Cuotas
                })
                .ToList()
        };

    /// <summary>
    /// Vistas fijas de la lista de clientes, iguales a las del VB6 (NwDbGrid, proceso V_MA_Clientes_C):
    /// "Facturación Automática por Cliente" (VT_PL_Clientes_FacturacionAutCli) y "Datos de Facturación
    /// Automática" (VT_PL_Clientes_FacturacionAut). Se calculan acá, sin depender de que esas vistas
    /// existan en la base. Solo clientes no dados de baja con la facturación activa.
    /// </summary>
    public Task<IReadOnlyList<FacturacionAutomaticaVistaFilaDto>> GetFacturacionAutomaticaVistaAsync(string vista, string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync("Clientes", "FacturacionAutomaticaVista", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TablasFacturacionAutomaticaExistenAsync(cn, null, token))
                return (IReadOnlyList<FacturacionAutomaticaVistaFilaDto>)[];

            var porConcepto = string.Equals(vista, CuentaComercialVistaFijaKeys.FacturacionPorConcepto, StringComparison.OrdinalIgnoreCase);
            const string comunes = """
                LTRIM(RTRIM(ISNULL(A.CODIGO, ''))) AS Codigo, LTRIM(RTRIM(ISNULL(A.CTACODIGO, ''))) AS CtaCodigo,
                ISNULL(B.SucursalCuenta, 0) AS Sucursal, ISNULL(A.RAZON_SOCIAL, '') AS RazonSocial, ISNULL(A.CONTACTO, '') AS Contacto,
                ISNULL(A.MAIL, '') AS Mail, B.FechaUltMov AS FechaUltMov, LTRIM(RTRIM(ISNULL(B.Periodicidad, ''))) AS Periodicidad,
                LTRIM(RTRIM(ISNULL(B.TC, ''))) AS TC, LTRIM(RTRIM(ISNULL(B.UNEGOCIO, ''))) AS UnidadNegocio,
                LTRIM(RTRIM(ISNULL(A.IdVendedor, ''))) AS VendedorCodigo, ISNULL(vd.Nombre, '') AS VendedorNombre,
                ISNULL(B.Concepto, '') AS Concepto
                """;
            const string filtroTexto = """
                (@Texto = '' OR ISNULL(A.CODIGO, '') COLLATE Latin1_General_CI_AI LIKE @Texto
                    OR ISNULL(A.RAZON_SOCIAL, '') COLLATE Latin1_General_CI_AI LIKE @Texto
                    OR ISNULL(A.CONTACTO, '') COLLATE Latin1_General_CI_AI LIKE @Texto
                """;

            var sql = porConcepto
                ? $"""
                    SELECT TOP (5000) {comunes},
                        ISNULL(C.Detalle, '') AS Detalle, ISNULL(C.Importe, 0) AS Importe,
                        ISNULL(C.ImporteOld, 0) AS ImporteAnterior, C.FechaCambio AS FechaCambio,
                        CONVERT(int, ROUND(ISNULL(C.NroUltimaCuota, 0), 0)) AS Cuota, CONVERT(int, ROUND(ISNULL(C.Cuotas, 0), 0)) AS Cuotas
                    FROM dbo.VT_CLIENTES A
                    INNER JOIN dbo.MA_CUENTAS_AUTOCPTES B ON A.CODIGO = B.Cuenta
                    LEFT JOIN dbo.MA_CUENTAS_AUTOCPTESDET C ON C.Cuenta = B.Cuenta AND ISNULL(C.SucursalCuenta, 0) = B.SucursalCuenta
                    LEFT JOIN dbo.V_TA_VENDEDORES vd ON LTRIM(RTRIM(vd.IdVendedor)) = LTRIM(RTRIM(A.IdVendedor))
                    WHERE ISNULL(A.Dada_De_Baja, 0) = 0 AND ISNULL(B.Activo, 0) = 1
                      AND {filtroTexto} OR ISNULL(C.Detalle, '') COLLATE Latin1_General_CI_AI LIKE @Texto)
                    ORDER BY A.CTACODIGO, C.ID;
                    """
                : $"""
                    SELECT TOP (5000) {comunes},
                        ISNULL(D.Abono, 0) AS Abono, ISNULL(D.Total, 0) - ISNULL(D.Abono, 0) AS Otros,
                        ISNULL(D.Total, 0) AS Total, ISNULL(D.Conceptos, 0) AS Conceptos
                    FROM dbo.VT_CLIENTES A
                    INNER JOIN dbo.MA_CUENTAS_AUTOCPTES B ON A.CODIGO = B.Cuenta
                    LEFT JOIN (
                        SELECT Cuenta, ISNULL(SucursalCuenta, 0) AS SucursalCuenta, COUNT(*) AS Conceptos,
                            SUM(CASE WHEN UPPER(Detalle) LIKE '%<PERIODO>%' AND (ISNULL(Cuotas, 0) <= 0 OR ISNULL(NroUltimaCuota, 0) <= Cuotas)
                                     THEN ISNULL(Importe, 0) ELSE 0 END) AS Abono,
                            SUM(CASE WHEN ISNULL(Cuotas, 0) <= 0 OR ISNULL(NroUltimaCuota, 0) <= Cuotas THEN ISNULL(Importe, 0) ELSE 0 END) AS Total
                        FROM dbo.MA_CUENTAS_AUTOCPTESDET
                        GROUP BY Cuenta, ISNULL(SucursalCuenta, 0)
                    ) D ON D.Cuenta = B.Cuenta AND D.SucursalCuenta = ISNULL(B.SucursalCuenta, 0)
                    LEFT JOIN dbo.V_TA_VENDEDORES vd ON LTRIM(RTRIM(vd.IdVendedor)) = LTRIM(RTRIM(A.IdVendedor))
                    WHERE ISNULL(A.Dada_De_Baja, 0) = 0 AND ISNULL(B.Activo, 0) = 1
                      AND {filtroTexto})
                    ORDER BY A.CTACODIGO;
                    """;

            var filas = await cn.QueryAsync<FacturacionAutomaticaVistaFilaDto>(new CommandDefinition(
                sql, new { Texto = SearchTextHelper.LikeContains(texto) }, commandTimeout: 60, cancellationToken: token));
            return (IReadOnlyList<FacturacionAutomaticaVistaFilaDto>)filas.ToList();
        }, "No se pudo cargar la vista de facturación automática.", ct);

    private sealed class FacturacionAutomaticaCabeceraRow
    {
        public bool Activo { get; set; }
        public string TC { get; set; } = string.Empty;
        public string Concepto { get; set; } = string.Empty;
        public string Periodicidad { get; set; } = string.Empty;
        public DateTime? FechaUltMov { get; set; }
        public string UNegocio { get; set; } = string.Empty;
    }
}
