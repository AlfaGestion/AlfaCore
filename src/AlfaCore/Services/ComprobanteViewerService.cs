using AlfaCore.Models;
using Dapper;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class ComprobanteViewerService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IComprobanteViewerService
{
    private const string ModuleName = "ComprobanteViewer";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    // TCs especiales: siempre en V_MV_Cpte pero con formato diferente
    private static readonly HashSet<string> TcReciboCobros =
        new(StringComparer.OrdinalIgnoreCase) { "CB", "CBFP", "CBCT" };
    private static readonly HashSet<string> TcReciboPagos =
        new(StringComparer.OrdinalIgnoreCase) { "PG", "OPG" };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<ComprobanteViewerDto?> GetAsync(string tc, string idComprobante, int idComplemento = 0, CancellationToken ct = default)
        => ExecuteLoggedAsync("Get", async token =>
        {
            if (string.IsNullOrWhiteSpace(tc) || string.IsNullOrWhiteSpace(idComprobante))
                return null;

            var tcNorm = tc.Trim().ToUpperInvariant();
            var idNorm = idComprobante.Trim().ToUpperInvariant();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // Determinar sistema desde V_TA_Cpte
            var sistema = await cn.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(
                    "SELECT TOP(1) ISNULL(UPPER(LTRIM(RTRIM(SISTEMA))), '') FROM dbo.V_TA_Cpte WHERE CODIGO = @Tc",
                    new { Tc = tcNorm },
                    cancellationToken: token)) ?? string.Empty;

            var tipoFormato = TcReciboCobros.Contains(tcNorm) ? "ReciboCobros"
                            : TcReciboPagos.Contains(tcNorm) ? "ReciboPagos"
                            : string.Empty;

            return sistema == "COMPRAS"
                ? await GetComprasAsync(cn, tcNorm, idNorm, tipoFormato, token)
                : await GetVentasAsync(cn, tcNorm, idNorm, idComplemento, tipoFormato, token);
        }, "No se pudo cargar el comprobante solicitado.", new { tc, idComprobante, idComplemento }, ct);

    public Task<IReadOnlyList<ComprobanteAsientoDto>> GetAsientosAsync(string tc, string idComprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetAsientos", async token =>
        {
            if (string.IsNullOrWhiteSpace(tc) || string.IsNullOrWhiteSpace(idComprobante))
                return (IReadOnlyList<ComprobanteAsientoDto>)[];

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var idNorm = idComprobante.Trim().ToUpperInvariant();
            if (idNorm.Length != 13)
                return (IReadOnlyList<ComprobanteAsientoDto>)[];

            var sucursal = idNorm[..4];
            var numero = idNorm.Substring(4, 8);
            var letra = idNorm[^1..];

            var rows = await cn.QueryAsync<ComprobanteAsientoDto>(new CommandDefinition(
                """
                SELECT
                    ISNULL(a.[NUMERO ASIENTO], 0) AS NumeroAsiento,
                    a.FECHA AS Fecha,
                    a.FECHAHORA_GRABACION AS FechaHoraGrabacion,
                    ISNULL(LTRIM(RTRIM(a.CUENTA)), '') AS Cuenta,
                    ISNULL(LTRIM(RTRIM(mc.DESCRIPCION)), '') AS DescripcionCuenta,
                    ISNULL(LTRIM(RTRIM(a.DETALLE)), '') AS Detalle,
                    ISNULL(a.[DEBE-HABER], '') AS DebeHaber,
                    ISNULL(a.IMPORTE, 0) AS Importe,
                    CASE WHEN a.[DEBE-HABER] = 'D' THEN ISNULL(a.IMPORTE, 0) ELSE 0 END AS Debe,
                    CASE WHEN a.[DEBE-HABER] = 'H' THEN ISNULL(a.IMPORTE, 0) ELSE 0 END AS Haber,
                    ISNULL(a.NroComprobanteBancario, '') AS Cheque,
                    a.VENCIMIENTO AS Vencimiento,
                    ISNULL(a.TJ_Cuotas, 0) AS Cuotas,
                    ISNULL(LTRIM(RTRIM(a.UNEGOCIO)), '') AS UnidadNegocio,
                    ISNULL(LTRIM(RTRIM(a.USUARIO_LOGEADO)), '') AS Usuario
                FROM dbo.MV_ASIENTOS a
                LEFT JOIN dbo.MA_CUENTAS mc ON mc.CODIGO = a.CUENTA
                WHERE a.TC = @Tc
                  AND a.SUCURSAL = @Sucursal
                  AND a.NUMERO = @Numero
                  AND a.LETRA = @Letra
                ORDER BY ISNULL(a.[NUMERO ASIENTO], 0), ISNULL(a.SECUENCIA, 0)
                """,
                new { Tc = tc.Trim().ToUpperInvariant(), Sucursal = sucursal, Numero = numero, Letra = letra },
                cancellationToken: token));

            return (IReadOnlyList<ComprobanteAsientoDto>)rows.AsList();
        }, "No se pudo cargar el asiento del comprobante.", new { tc, idComprobante }, ct);

    // ── Ventas (V_MV_*) ──────────────────────────────────────────────────────

    private static async Task<ComprobanteViewerDto?> GetVentasAsync(
        SqlConnection cn, string tc, string idComprobante, int idComplemento, string tipoFormato, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ISNULL(c.TC, '') AS Tc,
                ISNULL(c.IDCOMPROBANTE, '') AS IdComprobante,
                ISNULL(c.IDCOMPLEMENTO, 0) AS IdComplemento,
                'VENTAS' AS SistemaOrigen,
                @TipoFormato AS TipoFormato,
                c.FECHA AS Fecha,
                ISNULL(c.CUENTA, '') AS Cuenta,
                ISNULL(c.NOMBRE, '') AS Nombre,
                ISNULL(c.DOMICILIO, '') AS Domicilio,
                ISNULL(c.LOCALIDAD, '') AS Localidad,
                ISNULL(c.TELEFONO, '') AS Telefono,
                ISNULL(c.CODIGOPOSTAL, '') AS CodigoPostal,
                ISNULL(c.IDCOND_CPRA_VTA, '') AS CondicionComercialCodigo,
                ISNULL(cv.Descripcion, '') AS CondicionComercialDescripcion,
                ISNULL(c.IdVendedor, '') AS VendedorCodigo,
                ISNULL(vd.Nombre, '') AS VendedorNombre,
                ISNULL(c.IDTECNICO, '') AS TecnicoCodigo,
                ISNULL(te.Nombre, '') AS TecnicoNombre,
                ISNULL(c.Usuario, '') AS Usuario,
                ISNULL(LTRIM(RTRIM(c.UNEGOCIO)), '') AS UnidadNegocio,
                ISNULL(NULLIF(LTRIM(RTRIM(un.Descripcion)), ''), LTRIM(RTRIM(CONVERT(varchar(50), c.UNEGOCIO)))) AS UnidadNegocioDescripcion,
                ISNULL(c.IMPORTE, 0) AS ImporteTotal,
                ISNULL(c.IMPORTE_S_IVA, 0) AS ImporteSinIva,
                ISNULL(c.ImporteInsumos, 0) AS ImporteInsumos,
                ISNULL(c.ImporteServicios, 0) AS ImporteServicios,
                ISNULL(c.ImporteOtrosConceptos, 0) AS ImporteOtrosConceptos,
                ISNULL(c.ImporteImpuestosInternos, 0) AS ImporteImpuestosInternos,
                ISNULL(c.ImporteIva, 0) AS Iva,
                ISNULL(c.ImporteIvaRec, 0) AS IvaRecargo,
                ISNULL(c.ImpDescuento1, 0) AS Descuento1,
                ISNULL(c.ImpDescuento2, 0) AS Descuento2,
                ISNULL(c.ImpDescuento3, 0) AS Descuento3,
                ISNULL(c.ImpDescuento4, 0) AS Descuento4,
                ISNULL(c.NetoGravado, 0) AS NetoGravado,
                ISNULL(c.NetoNoGravado, 0) AS NetoNoGravado,
                CAST(ISNULL(c.ANULADA, 0) AS bit) AS Anulada,
                CAST(ISNULL(c.FINALIZADA, 0) AS bit) AS Finalizada,
                CAST(ISNULL(c.APROBADO, 0) AS bit) AS Aprobada,
                CAST(ISNULL(c.Impreso, 0) AS bit) AS Impresa,
                CAST(ISNULL(c.BLOQUEADA, 0) AS bit) AS Bloqueada,
                CAST(ISNULL(c.CERRADA, 0) AS bit) AS Cerrada,
                CAST(ISNULL(c.OBSERVACIONES, '') AS nvarchar(max)) AS ObservacionesGenerales,
                CAST(ISNULL(c.COMENTARIOS, '') AS nvarchar(max)) AS Comentarios
            FROM dbo.V_MV_Cpte c
            LEFT JOIN dbo.V_TA_Cpra_Vta cv
                ON cv.IDCond_Cpra_Vta = c.IDCOND_CPRA_VTA
            LEFT JOIN dbo.V_TA_VENDEDORES vd
                ON vd.IdVendedor = c.IdVendedor
            LEFT JOIN dbo.V_TA_Tecnicos te
                ON te.IdTecnico = c.IDTECNICO
            LEFT JOIN dbo.V_TA_UnidadNegocio un
                ON LTRIM(RTRIM(un.Codigo)) = LTRIM(RTRIM(CONVERT(varchar(50), c.UNEGOCIO)))
            WHERE c.TC = @Tc
              AND c.IDCOMPROBANTE = @IdComprobante
              AND ISNULL(c.IDCOMPLEMENTO, 0) = @IdComplemento
            ORDER BY c.ID DESC;

            SELECT
                ISNULL(i.IDARTICULO, '') AS CodigoArticulo,
                ISNULL(i.DESCRIPCION, '') AS Descripcion,
                ISNULL(i.IDUNIDAD, '') AS Unidad,
                ISNULL(i.CANTIDAD, 0) AS Cantidad,
                ISNULL(i.IMPORTE, 0) AS PrecioImporte,
                ISNULL(i.ImporteDto, 0) AS DescuentoImporte,
                ISNULL(i.AlicIVA, 0) AS Iva,
                ISNULL(i.TOTAL, 0) AS Total,
                ISNULL(i.NRO_SERIE, '') AS NumeroSerie,
                ISNULL(i.NRO_LOTE, '') AS NumeroLote,
                ISNULL(i.IdDeposito, '') AS Deposito,
                ISNULL(i.CODIGOBARRA, '') AS CodigoBarra,
                ISNULL(i.SECUENCIA, 0) AS Secuencia
            FROM dbo.V_MV_CpteInsumos i
            WHERE i.TC = @Tc
              AND i.IDCOMPROBANTE = @IdComprobante
              AND i.IDCOMPLEMENTO = @IdComplemento
            ORDER BY ISNULL(i.SECUENCIA, 0), ISNULL(i.ID, 0);

            SELECT
                ISNULL(t.IDTAREA, '') AS CodigoTarea,
                ISNULL(t.DESCRIPCION, '') AS Descripcion,
                ISNULL(t.HORAS, 0) AS Horas,
                ISNULL(t.VALORHORA, 0) AS ValorHora,
                ISNULL(t.TOTAL, 0) AS Total,
                ISNULL(t.IdTecnico, '') AS TecnicoCodigo,
                ISNULL(te.Nombre, '') AS TecnicoNombre,
                t.FechaEstInicio AS FechaEstimadaInicio,
                t.FechaEstFin AS FechaEstimadaFin,
                t.FechaRealInicio AS FechaRealInicio,
                t.FechaRealFin AS FechaRealFin,
                ISNULL(CONVERT(nvarchar(50), t.Prioridad), '') AS Prioridad,
                ISNULL(t.Usuario, '') AS Usuario,
                ISNULL(t.SECUENCIA, 0) AS Secuencia
            FROM dbo.V_MV_CpteTareas t
            LEFT JOIN dbo.V_TA_Tecnicos te
                ON te.IdTecnico = t.IdTecnico
            WHERE t.TC = @Tc
              AND t.IDCOMPROBANTE = @IdComprobante
              AND t.IDCOMPLEMENTO = @IdComplemento
            ORDER BY ISNULL(t.SECUENCIA, 0), ISNULL(t.ID, 0);

            SELECT
                ISNULL(o.TIPO_OBS, '') AS TipoObservacion,
                CAST(ISNULL(o.OBSERVACION, '') AS nvarchar(max)) AS Observacion,
                ISNULL(o.IMPORTE, 0) AS Importe,
                ISNULL(o.SECUENCIA, 0) AS Secuencia
            FROM dbo.V_MV_CPTE_OBSERV o
            WHERE o.TC = @Tc
              AND o.IDCOMPROBANTE = @IdComprobante
              AND o.IDCOMPLEMENTO = @IdComplemento
            ORDER BY ISNULL(o.SECUENCIA, 0), ISNULL(o.ID, 0);

            SELECT
                ISNULL(d.DOCUMENTO, '') AS Documento
            FROM dbo.V_MV_CpteDoc d
            WHERE d.TC = @Tc
              AND d.IDCOMPROBANTE = @IdComprobante
              AND d.IDCOMPLEMENTO = @IdComplemento
            ORDER BY ISNULL(d.ID, 0);

            """ + AplicadoPorQuery + ";" + AccionesVentasQuery + ";" + TieneAsientoQuery;

        var parameters = new
        {
            Tc = tc,
            IdComprobante = idComprobante,
            IdComplemento = idComplemento,
            TipoFormato = tipoFormato
        };

        var definition = new CommandDefinition(sql, parameters, commandType: CommandType.Text, cancellationToken: ct);
        using var multi = await cn.QueryMultipleAsync(definition);
        return await ReadMultiAsync(multi);
    }

    // ── Compras (C_MV_*) ─────────────────────────────────────────────────────

    private static async Task<ComprobanteViewerDto?> GetComprasAsync(
        SqlConnection cn, string tc, string idComprobante, string tipoFormato, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ISNULL(c.TC, '') AS Tc,
                ISNULL(c.IDCOMPROBANTE, '') AS IdComprobante,
                0 AS IdComplemento,
                'COMPRAS' AS SistemaOrigen,
                @TipoFormato AS TipoFormato,
                c.FECHA AS Fecha,
                ISNULL(c.CUENTA, '') AS Cuenta,
                ISNULL(c.NOMBRE, '') AS Nombre,
                ISNULL(c.DOMICILIO, '') AS Domicilio,
                ISNULL(c.LOCALIDAD, '') AS Localidad,
                ISNULL(c.TELEFONO, '') AS Telefono,
                ISNULL(c.CODIGOPOSTAL, '') AS CodigoPostal,
                ISNULL(c.IDCOND_CPRA_VTA, '') AS CondicionComercialCodigo,
                ISNULL(cv.Descripcion, '') AS CondicionComercialDescripcion,
                '' AS VendedorCodigo,
                '' AS VendedorNombre,
                '' AS TecnicoCodigo,
                '' AS TecnicoNombre,
                ISNULL(c.USUARIO, '') AS Usuario,
                ISNULL(LTRIM(RTRIM(c.UNEGOCIO)), '') AS UnidadNegocio,
                ISNULL(NULLIF(LTRIM(RTRIM(un.Descripcion)), ''), LTRIM(RTRIM(CONVERT(varchar(50), c.UNEGOCIO)))) AS UnidadNegocioDescripcion,
                ISNULL(c.IMPORTE, 0) AS ImporteTotal,
                ISNULL(c.IMPORTE_S_IVA, 0) AS ImporteSinIva,
                ISNULL(c.ImporteInsumos, 0) AS ImporteInsumos,
                ISNULL(c.ImporteServicios, 0) AS ImporteServicios,
                ISNULL(c.ImporteOtrosConceptos, 0) AS ImporteOtrosConceptos,
                ISNULL(c.ImporteImpuestosInternos, 0) AS ImporteImpuestosInternos,
                ISNULL(c.ImporteIva, 0) AS Iva,
                ISNULL(c.ImporteIvaRec, 0) AS IvaRecargo,
                ISNULL(c.ImpDescuento1, 0) AS Descuento1,
                ISNULL(c.ImpDescuento2, 0) AS Descuento2,
                ISNULL(c.ImpDescuento3, 0) AS Descuento3,
                ISNULL(c.ImpDescuento4, 0) AS Descuento4,
                ISNULL(c.NetoGravado, 0) AS NetoGravado,
                ISNULL(c.NetoNoGravado, 0) AS NetoNoGravado,
                CAST(ISNULL(c.ANULADA, 0) AS bit) AS Anulada,
                CAST(ISNULL(c.Finalizado, 0) AS bit) AS Finalizada,
                CAST(ISNULL(c.Aprobado, 0) AS bit) AS Aprobada,
                CAST(0 AS bit) AS Impresa,
                CAST(ISNULL(c.Bloqueado, 0) AS bit) AS Bloqueada,
                CAST(ISNULL(c.Cerrada, 0) AS bit) AS Cerrada,
                CAST(ISNULL(c.OBSERVACIONES, '') AS nvarchar(max)) AS ObservacionesGenerales,
                CAST(ISNULL(c.COMENTARIOS, '') AS nvarchar(max)) AS Comentarios
            FROM dbo.C_MV_Cpte c
            LEFT JOIN dbo.V_TA_Cpra_Vta cv
                ON cv.IDCond_Cpra_Vta = c.IDCOND_CPRA_VTA
            LEFT JOIN dbo.V_TA_UnidadNegocio un
                ON LTRIM(RTRIM(un.Codigo)) = LTRIM(RTRIM(CONVERT(varchar(50), c.UNEGOCIO)))
            WHERE c.TC = @Tc
              AND c.IDCOMPROBANTE = @IdComprobante
            ORDER BY c.ID DESC;

            SELECT
                ISNULL(i.IDARTICULO, '') AS CodigoArticulo,
                ISNULL(i.DESCRIPCION, '') AS Descripcion,
                ISNULL(i.IDUNIDAD, '') AS Unidad,
                ISNULL(i.CANTIDAD, 0) AS Cantidad,
                ISNULL(i.IMPORTE, 0) AS PrecioImporte,
                ISNULL(i.ImporteDto, 0) AS DescuentoImporte,
                ISNULL(i.AlicIva, 0) AS Iva,
                ISNULL(i.TOTAL, 0) AS Total,
                ISNULL(i.NRO_SERIE, '') AS NumeroSerie,
                ISNULL(i.NRO_LOTE, '') AS NumeroLote,
                '' AS Deposito,
                '' AS CodigoBarra,
                0 AS Secuencia
            FROM dbo.C_MV_CpteInsumos i
            WHERE i.TC = @Tc
              AND i.IDCOMPROBANTE = @IdComprobante
            ORDER BY ISNULL(i.ID, 0);

            SELECT '' AS CodigoTarea WHERE 1=0;
            SELECT '' AS TipoObservacion WHERE 1=0;
            SELECT '' AS Documento WHERE 1=0;

            """ + AplicadoPorQuery + ";" + AccionesVentasQuery + ";" + TieneAsientoQuery;

        var parameters = new
        {
            Tc = tc,
            IdComprobante = idComprobante,
            TipoFormato = tipoFormato
        };

        var definition = new CommandDefinition(sql, parameters, commandType: CommandType.Text, cancellationToken: ct);
        using var multi = await cn.QueryMultipleAsync(definition);
        return await ReadMultiAsync(multi);
    }

    // ── Queries compartidas (MV_APLICACION y acciones) ────────────────────────

    private const string AplicadoPorQuery = """
        SELECT
            ISNULL(ap.TCO_ORIGEN, '') AS TcOrigen,
            ISNULL(ap.IDComprobante_Origen, '') AS IdComprobanteOrigen,
            ISNULL(ap.IDCOMPLEMENTO_ORIGEN, 0) AS IdComplementoOrigen,
            ISNULL(ap.TC, '') AS TcRelacionado,
            ISNULL(ap.IDComprobante, '') AS IdComprobanteRelacionado,
            0 AS IdComplementoRelacionado
        FROM dbo.MV_APLICACION ap
        WHERE (
                (ap.TCO_ORIGEN = @Tc
                 AND ap.IDComprobante_Origen = @IdComprobante)
             OR (ap.TC = @Tc
                 AND ap.IDComprobante = @IdComprobante)
              )
        ORDER BY ISNULL(ap.IDComprobante, '')
        """;

    private const string AccionesVentasQuery = """
        SELECT
            ISNULL(a.TIPO_ACCION, '') AS TipoAccion,
            CAST(ISNULL(a.SYSTEMUSER, '') AS nvarchar(max)) AS Comentario,
            a.FECHAHORA AS FechaHora,
            ISNULL(a.USUARIO, '') AS Usuario,
            ISNULL(a.PC, '') AS Pc,
            ISNULL(a.Proceso, '') AS Proceso
        FROM dbo.V_MV_CpteAcciones a
        WHERE a.TC = @Tc
          AND a.IDCOMPROBANTE = @IdComprobante
        ORDER BY a.FECHAHORA DESC, a.ID DESC
        """;

    private const string TieneAsientoQuery = """
        SELECT CAST(CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.MV_ASIENTOS a
            WHERE a.TC = @Tc
              AND a.SUCURSAL = SUBSTRING(@IdComprobante, 1, 4)
              AND a.NUMERO = SUBSTRING(@IdComprobante, 5, 8)
              AND a.LETRA = RIGHT(@IdComprobante, 1)
        ) THEN 1 ELSE 0 END AS bit) AS TieneAsiento
        """;

    // ── Lectura del multi-resultset (orden fijo en ambos branches) ────────────

    private static async Task<ComprobanteViewerDto?> ReadMultiAsync(SqlMapper.GridReader multi)
    {
        var cabecera = await multi.ReadFirstOrDefaultAsync<ComprobanteCabeceraDto>();
        if (cabecera is null)
            return null;

        var insumos       = (await multi.ReadAsync<ComprobanteInsumoDto>()).AsList();
        var tareas        = (await multi.ReadAsync<ComprobanteTareaDto>()).AsList();
        var observaciones = (await multi.ReadAsync<ComprobanteObservacionDto>()).AsList();
        var documentosRaw = (await multi.ReadAsync<ComprobanteDocumentoRow>()).AsList();
        var aplicadoPor   = (await multi.ReadAsync<ComprobanteAplicacionDto>()).AsList();
        var acciones      = (await multi.ReadAsync<ComprobanteAccionDto>()).AsList();
        var tieneAsiento  = await multi.ReadFirstOrDefaultAsync<bool>();

        return new ComprobanteViewerDto
        {
            Cabecera      = cabecera,
            Insumos       = insumos,
            Tareas        = tareas,
            Observaciones = observaciones,
            Documentos    = documentosRaw.Select(MapDocumento).ToList(),
            AplicaA       = [],
            AplicadoPor   = aplicadoPor,
            Acciones      = acciones,
            TieneAsiento  = tieneAsiento
        };
    }

    // ── Documento archivo ─────────────────────────────────────────────────────

    public Task<ComprobanteDocumentoArchivoDto?> GetDocumentoArchivoAsync(string tc, string idComprobante, int idComplemento, string documento, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetDocumentoArchivo", async token =>
        {
            if (string.IsNullOrWhiteSpace(tc) || string.IsNullOrWhiteSpace(idComprobante) || string.IsNullOrWhiteSpace(documento))
                return null;

            const string sql = """
                SELECT TOP (1)
                    ISNULL(d.DOCUMENTO, '') AS Documento
                FROM dbo.V_MV_CpteDoc d
                WHERE d.TC = @Tc
                  AND d.IDCOMPROBANTE = @IdComprobante
                  AND d.IDCOMPLEMENTO = @IdComplemento
                  AND ISNULL(d.DOCUMENTO, '') = @Documento
                ORDER BY ISNULL(d.ID, 0);
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var row = await cn.QueryFirstOrDefaultAsync<ComprobanteDocumentoRow>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Tc = tc.Trim().ToUpperInvariant(),
                        IdComprobante = idComprobante.Trim().ToUpperInvariant(),
                        IdComplemento = idComplemento,
                        Documento = documento.Trim()
                    },
                    cancellationToken: token));

            if (row is null || string.IsNullOrWhiteSpace(row.Documento))
                return null;

            var path = row.Documento.Trim();
            if (IsExternalUrl(path))
                return null;
            if (!File.Exists(path))
                return null;

            var fileName = Path.GetFileName(path);
            if (!ContentTypeProvider.TryGetContentType(fileName, out var mimeType))
                mimeType = "application/octet-stream";

            return new ComprobanteDocumentoArchivoDto
            {
                RutaCompleta = path,
                NombreArchivo = fileName,
                MimeType = mimeType
            };
        }, "No se pudo abrir el documento relacionado.", new { tc, idComprobante, idComplemento, documento }, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, object? data, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, data, ct: ct);
            throw;
        }
    }

    private static ComprobanteDocumentoDto MapDocumento(ComprobanteDocumentoRow row)
    {
        var documento = (row.Documento ?? string.Empty).Trim();
        var esUrlExterna = IsExternalUrl(documento);
        var nombreArchivo = esUrlExterna
            ? documento
            : Path.GetFileName(documento);

        return new ComprobanteDocumentoDto
        {
            Documento = documento,
            NombreArchivo = string.IsNullOrWhiteSpace(nombreArchivo) ? documento : nombreArchivo,
            EsUrlExterna = esUrlExterna,
            EsAbrible = !string.IsNullOrWhiteSpace(documento)
        };
    }

    private static bool IsExternalUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private sealed class ComprobanteDocumentoRow
    {
        public string Documento { get; init; } = string.Empty;
    }
}
