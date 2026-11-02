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

    // ── Acciones por lote (vista de facturación automática) ─────────────────────────────────────────
    private const int MaximoLote = 1000;
    // Abono = renglones con la marca <PERIODO> (igual que FrmPrincipalCliNew "Agregar concepto Facturacion Automatica").
    private const string FiltroAbono = "UPPER(D.Detalle) LIKE '%<PERIODO>%'";
    // Cuotas cumplidas: igual criterio que la facturación (se factura mientras NroUltimaCuota <= Cuotas).
    private const string FiltroCumplido = "ISNULL(D.Cuotas,0) > 0 AND ISNULL(D.NroUltimaCuota,0) > D.Cuotas";

    private static string[] NormalizarCodigos(IReadOnlyList<string> codigos)
    {
        var lista = (codigos ?? []).Select(c => (c ?? string.Empty).Trim()).Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (lista.Length == 0) throw new InvalidOperationException("Seleccioná al menos un cliente.");
        if (lista.Length > MaximoLote) throw new InvalidOperationException($"Seleccioná hasta {MaximoLote} clientes por vez.");
        return lista;
    }

    public Task<IReadOnlyList<FacturacionAutomaticaConceptoUsoDto>> GetConceptosFacturacionAutomaticaAsync(IReadOnlyList<string> codigos, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<FacturacionAutomaticaConceptoUsoDto>>("Clientes", "FacturacionAutomaticaConceptos", async token =>
        {
            var lista = NormalizarCodigos(codigos);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return (IReadOnlyList<FacturacionAutomaticaConceptoUsoDto>)(await cn.QueryAsync<FacturacionAutomaticaConceptoUsoDto>(new CommandDefinition($"""
                SELECT LTRIM(RTRIM(D.Detalle)) AS Detalle, COUNT(DISTINCT D.Cuenta) AS Clientes, COUNT(*) AS Renglones,
                       SUM(CASE WHEN {FiltroCumplido} THEN 1 ELSE 0 END) AS Cumplidos
                FROM dbo.MA_CUENTAS_AUTOCPTESDET D
                WHERE D.Cuenta IN @Codigos AND LTRIM(RTRIM(D.Detalle)) <> ''
                GROUP BY LTRIM(RTRIM(D.Detalle))
                ORDER BY COUNT(DISTINCT D.Cuenta) DESC, LTRIM(RTRIM(D.Detalle));
                """, new { Codigos = lista }, commandTimeout: 60, cancellationToken: token))).ToList();
        }, "No se pudieron leer los conceptos de facturación automática.", ct);

    public Task<FacturacionAutomaticaLoteResultadoDto> AgregarConceptoFacturacionAutomaticaAsync(IReadOnlyList<string> codigos, string detalle,
        decimal importe, bool esPorcentaje, int cuota, int cuotas, CancellationToken ct = default)
        => ExecuteLoggedAsync<FacturacionAutomaticaLoteResultadoDto>("Clientes", "FacturacionAutomaticaAgregarConcepto", async token =>
        {
            var lista = NormalizarCodigos(codigos);
            detalle = (detalle ?? string.Empty).Trim();
            if (detalle.Length is 0 or > 1000) throw new InvalidOperationException("Ingresá el detalle del concepto (hasta 1000 caracteres).");
            if (importe < 0 || (esPorcentaje && importe > 1000)) throw new InvalidOperationException("Revisá el importe o el porcentaje.");
            if (cuotas < 0 || cuotas > 999 || (cuotas > 0 && (cuota < 1 || cuota > cuotas)))
                throw new InvalidOperationException("Revisá las cuotas: la cuota inicial tiene que estar entre 1 y la cantidad de cuotas.");
            if (cuotas == 0) cuota = 0;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            var sqlTx = (SqlTransaction)tx;
            // Igual que el VB6: una línea por cada cabecera (sucursal) de la cuenta, sin repetir un detalle
            // que ya tenga. Con porcentaje, sobre la suma de las líneas de abono (<PERIODO>) de esa sucursal.
            var importeSql = esPorcentaje ? "ROUND(A.Abono * @Importe / 100, 2)" : "@Importe";
            var joinAbono = esPorcentaje ? $"""
                INNER JOIN (SELECT D.Cuenta, ISNULL(D.SucursalCuenta,0) AS SucursalCuenta, SUM(ISNULL(D.Importe,0)) AS Abono
                            FROM dbo.MA_CUENTAS_AUTOCPTESDET D WHERE {FiltroAbono}
                            GROUP BY D.Cuenta, ISNULL(D.SucursalCuenta,0)) A
                    ON A.Cuenta = C.Cuenta AND A.SucursalCuenta = ISNULL(C.SucursalCuenta,0) AND A.Abono <> 0
                """ : string.Empty;
            var insertados = await cn.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.MA_CUENTAS_AUTOCPTESDET (Cuenta, Detalle, Importe, NroUltimaCuota, Cuotas, SucursalCuenta)
                SELECT C.Cuenta, @Detalle, {importeSql}, @Cuota, @Cuotas, ISNULL(C.SucursalCuenta,0)
                FROM dbo.MA_CUENTAS_AUTOCPTES C
                {joinAbono}
                WHERE C.Cuenta IN @Codigos
                  AND NOT EXISTS (SELECT 1 FROM dbo.MA_CUENTAS_AUTOCPTESDET D WHERE D.Cuenta = C.Cuenta
                                  AND ISNULL(D.SucursalCuenta,0) = ISNULL(C.SucursalCuenta,0) AND LTRIM(RTRIM(D.Detalle)) = @Detalle);
                """, new { Codigos = lista, Detalle = detalle, Importe = importe, Cuota = cuota, Cuotas = cuotas }, sqlTx, 120, cancellationToken: token));
            var conConcepto = (await cn.QueryAsync<string>(new CommandDefinition("""
                SELECT DISTINCT LTRIM(RTRIM(Cuenta)) FROM dbo.MA_CUENTAS_AUTOCPTESDET WHERE Cuenta IN @Codigos AND LTRIM(RTRIM(Detalle)) = @Detalle;
                """, new { Codigos = lista, Detalle = detalle }, sqlTx, cancellationToken: token))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await sqlTx.CommitAsync(token);

            var resultado = new FacturacionAutomaticaLoteResultadoDto
            {
                Renglones = insertados,
                Clientes = conConcepto.Count,
                NoModificados = lista.Where(c => !conConcepto.Contains(c)).ToList()
            };
            await appEvents.LogAuditAsync("Clientes", "FacturacionAutomaticaAgregarConcepto", "MA_CUENTAS_AUTOCPTESDET", string.Join(",", lista.Take(50)),
                $"Agregó «{detalle}» a {insertados} renglón(es) de facturación automática.",
                new { Detalle = detalle, Importe = importe, EsPorcentaje = esPorcentaje, Cuota = cuota, Cuotas = cuotas, Seleccionados = lista.Length, Renglones = insertados },
                CancellationToken.None);
            return resultado;
        }, "No se pudo agregar el concepto de facturación automática.", ct);

    public Task<FacturacionAutomaticaLoteResultadoDto> QuitarConceptoFacturacionAutomaticaAsync(IReadOnlyList<string> codigos, string detalle,
        bool soloCumplidos, CancellationToken ct = default)
        => ExecuteLoggedAsync<FacturacionAutomaticaLoteResultadoDto>("Clientes", "FacturacionAutomaticaQuitarConcepto", async token =>
        {
            var lista = NormalizarCodigos(codigos);
            detalle = (detalle ?? string.Empty).Trim();
            if (detalle.Length == 0) throw new InvalidOperationException("Elegí el concepto a quitar.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            var sqlTx = (SqlTransaction)tx;
            var filtro = $"D.Cuenta IN @Codigos AND LTRIM(RTRIM(D.Detalle)) = @Detalle{(soloCumplidos ? $" AND {FiltroCumplido}" : string.Empty)}";
            var afectadas = (await cn.QueryAsync<string>(new CommandDefinition(
                $"SELECT DISTINCT LTRIM(RTRIM(D.Cuenta)) FROM dbo.MA_CUENTAS_AUTOCPTESDET D WHERE {filtro};",
                new { Codigos = lista, Detalle = detalle }, sqlTx, cancellationToken: token))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var borrados = await cn.ExecuteAsync(new CommandDefinition(
                $"DELETE D FROM dbo.MA_CUENTAS_AUTOCPTESDET D WHERE {filtro};",
                new { Codigos = lista, Detalle = detalle }, sqlTx, 120, cancellationToken: token));
            await sqlTx.CommitAsync(token);

            await appEvents.LogAuditAsync("Clientes", "FacturacionAutomaticaQuitarConcepto", "MA_CUENTAS_AUTOCPTESDET", string.Join(",", lista.Take(50)),
                $"Quitó «{detalle}» de {borrados} renglón(es) de facturación automática{(soloCumplidos ? " (solo cuotas cumplidas)" : "")}.",
                new { Detalle = detalle, SoloCumplidos = soloCumplidos, Seleccionados = lista.Length, Renglones = borrados },
                CancellationToken.None);
            return new FacturacionAutomaticaLoteResultadoDto
            {
                Renglones = borrados, Clientes = afectadas.Count,
                NoModificados = lista.Where(c => !afectadas.Contains(c)).ToList()
            };
        }, "No se pudo quitar el concepto de facturación automática.", ct);

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
