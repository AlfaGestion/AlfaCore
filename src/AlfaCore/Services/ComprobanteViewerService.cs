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

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<ComprobanteViewerDto?> GetAsync(string tc, string idComprobante, int idComplemento = 0, CancellationToken ct = default)
        => ExecuteLoggedAsync("Get", async token =>
        {
            if (string.IsNullOrWhiteSpace(tc) || string.IsNullOrWhiteSpace(idComprobante))
                return null;

            const string sql = """
                SELECT TOP (1)
                    ISNULL(c.TC, '') AS Tc,
                    ISNULL(c.IDCOMPROBANTE, '') AS IdComprobante,
                    ISNULL(c.IDCOMPLEMENTO, 0) AS IdComplemento,
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
                    ISNULL(c.IMPORTE, 0) AS ImporteTotal,
                    ISNULL(c.IMPORTE_S_IVA, 0) AS ImporteSinIva,
                    ISNULL(c.ImporteIva, 0) AS Iva,
                    ISNULL(c.ImporteIvaRec, 0) AS IvaRecargo,
                    ISNULL(c.ImpDescuento1, 0) + ISNULL(c.ImpDescuento2, 0) + ISNULL(c.ImpDescuento3, 0) + ISNULL(c.ImpDescuento4, 0) AS Descuentos,
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
                    ON UPPER(LTRIM(RTRIM(cv.IDCond_Cpra_Vta))) = UPPER(LTRIM(RTRIM(ISNULL(c.IDCOND_CPRA_VTA, ''))))
                LEFT JOIN dbo.V_TA_VENDEDORES vd
                    ON UPPER(LTRIM(RTRIM(vd.IdVendedor))) = UPPER(LTRIM(RTRIM(ISNULL(c.IdVendedor, ''))))
                LEFT JOIN dbo.V_TA_Tecnicos te
                    ON UPPER(LTRIM(RTRIM(te.IdTecnico))) = UPPER(LTRIM(RTRIM(ISNULL(c.IDTECNICO, ''))))
                WHERE UPPER(LTRIM(RTRIM(c.TC))) = @Tc
                  AND UPPER(LTRIM(RTRIM(c.IDCOMPROBANTE))) = @IdComprobante
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
                WHERE UPPER(LTRIM(RTRIM(i.TC))) = @Tc
                  AND UPPER(LTRIM(RTRIM(i.IDCOMPROBANTE))) = @IdComprobante
                  AND ISNULL(i.IDCOMPLEMENTO, 0) = @IdComplemento
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
                    ON UPPER(LTRIM(RTRIM(te.IdTecnico))) = UPPER(LTRIM(RTRIM(ISNULL(t.IdTecnico, ''))))
                WHERE UPPER(LTRIM(RTRIM(t.TC))) = @Tc
                  AND UPPER(LTRIM(RTRIM(t.IDCOMPROBANTE))) = @IdComprobante
                  AND ISNULL(t.IDCOMPLEMENTO, 0) = @IdComplemento
                ORDER BY ISNULL(t.SECUENCIA, 0), ISNULL(t.ID, 0);

                SELECT
                    ISNULL(o.TIPO_OBS, '') AS TipoObservacion,
                    CAST(ISNULL(o.OBSERVACION, '') AS nvarchar(max)) AS Observacion,
                    ISNULL(o.IMPORTE, 0) AS Importe,
                    ISNULL(o.SECUENCIA, 0) AS Secuencia
                FROM dbo.V_MV_CPTE_OBSERV o
                WHERE UPPER(LTRIM(RTRIM(o.TC))) = @Tc
                  AND UPPER(LTRIM(RTRIM(o.IDCOMPROBANTE))) = @IdComprobante
                  AND ISNULL(o.IDCOMPLEMENTO, 0) = @IdComplemento
                ORDER BY ISNULL(o.SECUENCIA, 0), ISNULL(o.ID, 0);

                SELECT
                    ISNULL(d.DOCUMENTO, '') AS Documento
                FROM dbo.V_MV_CpteDoc d
                WHERE UPPER(LTRIM(RTRIM(d.TC))) = @Tc
                  AND UPPER(LTRIM(RTRIM(d.IDCOMPROBANTE))) = @IdComprobante
                  AND ISNULL(d.IDCOMPLEMENTO, 0) = @IdComplemento
                ORDER BY ISNULL(d.ID, 0);

                SELECT
                    ISNULL(ap.TCO_ORIGEN, '') AS TcOrigen,
                    ISNULL(ap.IDComprobante_Origen, '') AS IdComprobanteOrigen,
                    ISNULL(ap.IDCOMPLEMENTO_ORIGEN, 0) AS IdComplementoOrigen,
                    ISNULL(ap.TC, '') AS TcRelacionado,
                    ISNULL(ap.IDComprobante, '') AS IdComprobanteRelacionado,
                    ISNULL(rel.IDCOMPLEMENTO, 0) AS IdComplementoRelacionado,
                    rel.FECHA AS FechaRelacionado,
                    ISNULL(ap.IMPORTE, 0) AS ImporteAplicado,
                    LTRIM(RTRIM(
                        CASE
                            WHEN ISNULL(ap.TC_Print, '') <> '' AND ISNULL(ap.IdComprobante_Print, '') <> '' THEN ap.TC_Print + ' ' + ap.IdComprobante_Print
                            WHEN ISNULL(ap.TC_Print, '') <> '' THEN ap.TC_Print
                            ELSE ISNULL(ap.IdComprobante_Print, '')
                        END
                    )) AS Recibo,
                    LTRIM(RTRIM(
                        CASE WHEN ISNULL(rel.ANULADA, 0) = 1 THEN 'ANULADO ' ELSE '' END +
                        CASE WHEN ISNULL(rel.FINALIZADA, 0) = 1 THEN 'FINALIZADO ' ELSE '' END +
                        CASE WHEN ISNULL(rel.APROBADO, 0) = 1 THEN 'APROBADO ' ELSE '' END +
                        CASE WHEN ISNULL(rel.BLOQUEADA, 0) = 1 THEN 'BLOQUEADO ' ELSE '' END +
                        CASE WHEN ISNULL(rel.CERRADA, 0) = 1 THEN 'CERRADO ' ELSE '' END
                    )) AS EstadoRelacionado,
                    ISNULL(rel.CUENTA, '') AS CuentaRelacionada,
                    ISNULL(rel.NOMBRE, '') AS NombreRelacionado
                FROM dbo.MV_APLICACION ap
                OUTER APPLY
                (
                    SELECT TOP (1)
                        c2.IDCOMPLEMENTO,
                        c2.FECHA,
                        c2.ANULADA,
                        c2.FINALIZADA,
                        c2.APROBADO,
                        c2.BLOQUEADA,
                        c2.CERRADA,
                        c2.CUENTA,
                        c2.NOMBRE
                    FROM dbo.V_MV_Cpte c2
                    WHERE UPPER(LTRIM(RTRIM(c2.TC))) = UPPER(LTRIM(RTRIM(ap.TCO_ORIGEN)))
                      AND UPPER(LTRIM(RTRIM(c2.IDCOMPROBANTE))) = UPPER(LTRIM(RTRIM(ap.IDComprobante_Origen)))
                      AND ISNULL(c2.IDCOMPLEMENTO, 0) = ISNULL(ap.IDCOMPLEMENTO_ORIGEN, 0)
                    ORDER BY c2.ID DESC
                ) rel
                WHERE UPPER(LTRIM(RTRIM(ap.TC))) = @Tc
                  AND UPPER(LTRIM(RTRIM(ap.IDComprobante))) = @IdComprobante
                ORDER BY ISNULL(rel.FECHA, '19000101'), ISNULL(ap.IDComprobante_Origen, '');

                SELECT
                    ISNULL(ap.TCO_ORIGEN, '') AS TcOrigen,
                    ISNULL(ap.IDComprobante_Origen, '') AS IdComprobanteOrigen,
                    ISNULL(ap.IDCOMPLEMENTO_ORIGEN, 0) AS IdComplementoOrigen,
                    ISNULL(ap.TC, '') AS TcRelacionado,
                    ISNULL(ap.IDComprobante, '') AS IdComprobanteRelacionado,
                    ISNULL(rel.IDCOMPLEMENTO, 0) AS IdComplementoRelacionado,
                    rel.FECHA AS FechaRelacionado,
                    ISNULL(ap.IMPORTE, 0) AS ImporteAplicado,
                    LTRIM(RTRIM(
                        CASE
                            WHEN ISNULL(ap.TC_Print, '') <> '' AND ISNULL(ap.IdComprobante_Print, '') <> '' THEN ap.TC_Print + ' ' + ap.IdComprobante_Print
                            WHEN ISNULL(ap.TC_Print, '') <> '' THEN ap.TC_Print
                            ELSE ISNULL(ap.IdComprobante_Print, '')
                        END
                    )) AS Recibo,
                    LTRIM(RTRIM(
                        CASE WHEN ISNULL(rel.ANULADA, 0) = 1 THEN 'ANULADO ' ELSE '' END +
                        CASE WHEN ISNULL(rel.FINALIZADA, 0) = 1 THEN 'FINALIZADO ' ELSE '' END +
                        CASE WHEN ISNULL(rel.APROBADO, 0) = 1 THEN 'APROBADO ' ELSE '' END +
                        CASE WHEN ISNULL(rel.BLOQUEADA, 0) = 1 THEN 'BLOQUEADO ' ELSE '' END +
                        CASE WHEN ISNULL(rel.CERRADA, 0) = 1 THEN 'CERRADO ' ELSE '' END
                    )) AS EstadoRelacionado,
                    ISNULL(rel.CUENTA, '') AS CuentaRelacionada,
                    ISNULL(rel.NOMBRE, '') AS NombreRelacionado
                FROM dbo.MV_APLICACION ap
                OUTER APPLY
                (
                    SELECT TOP (1)
                        c2.IDCOMPLEMENTO,
                        c2.FECHA,
                        c2.ANULADA,
                        c2.FINALIZADA,
                        c2.APROBADO,
                        c2.BLOQUEADA,
                        c2.CERRADA,
                        c2.CUENTA,
                        c2.NOMBRE
                    FROM dbo.V_MV_Cpte c2
                    WHERE UPPER(LTRIM(RTRIM(c2.TC))) = UPPER(LTRIM(RTRIM(ap.TC)))
                      AND UPPER(LTRIM(RTRIM(c2.IDCOMPROBANTE))) = UPPER(LTRIM(RTRIM(ap.IDComprobante)))
                    ORDER BY CASE WHEN ISNULL(c2.IDCOMPLEMENTO, 0) = 0 THEN 0 ELSE 1 END, c2.ID DESC
                ) rel
                WHERE UPPER(LTRIM(RTRIM(ap.TCO_ORIGEN))) = @Tc
                  AND UPPER(LTRIM(RTRIM(ap.IDComprobante_Origen))) = @IdComprobante
                  AND ISNULL(ap.IDCOMPLEMENTO_ORIGEN, 0) = @IdComplemento
                ORDER BY ISNULL(rel.FECHA, '19000101'), ISNULL(ap.IDComprobante, '');

                SELECT
                    ISNULL(a.TIPO_ACCION, '') AS TipoAccion,
                    CAST(ISNULL(a.SYSTEMUSER, '') AS nvarchar(max)) AS Comentario,
                    a.FECHAHORA AS FechaHora,
                    ISNULL(a.USUARIO, '') AS Usuario,
                    ISNULL(a.PC, '') AS Pc,
                    ISNULL(a.Proceso, '') AS Proceso
                FROM dbo.V_MV_CpteAcciones a
                WHERE UPPER(LTRIM(RTRIM(a.TC))) = @Tc
                  AND UPPER(LTRIM(RTRIM(a.IDCOMPROBANTE))) = @IdComprobante
                  AND ISNULL(a.IDCOMPLEMENTO, 0) = @IdComplemento
                ORDER BY a.FECHAHORA DESC, a.ID DESC;
                """;

            var parameters = new
            {
                Tc = tc.Trim().ToUpperInvariant(),
                IdComprobante = idComprobante.Trim().ToUpperInvariant(),
                IdComplemento = idComplemento
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var definition = new CommandDefinition(sql, parameters, commandType: CommandType.Text, cancellationToken: token);
            using var multi = await cn.QueryMultipleAsync(definition);

            var cabecera = await multi.ReadFirstOrDefaultAsync<ComprobanteCabeceraDto>();
            if (cabecera is null)
                return null;

            var insumos = (await multi.ReadAsync<ComprobanteInsumoDto>()).AsList();
            var tareas = (await multi.ReadAsync<ComprobanteTareaDto>()).AsList();
            var observaciones = (await multi.ReadAsync<ComprobanteObservacionDto>()).AsList();
            var documentosRaw = (await multi.ReadAsync<ComprobanteDocumentoRow>()).AsList();
            var aplicaA = (await multi.ReadAsync<ComprobanteAplicacionDto>()).AsList();
            var aplicadoPor = (await multi.ReadAsync<ComprobanteAplicacionDto>()).AsList();
            var acciones = (await multi.ReadAsync<ComprobanteAccionDto>()).AsList();

            return new ComprobanteViewerDto
            {
                Cabecera = cabecera,
                Insumos = insumos,
                Tareas = tareas,
                Observaciones = observaciones,
                Documentos = documentosRaw.Select(MapDocumento).ToList(),
                AplicaA = aplicaA,
                AplicadoPor = aplicadoPor,
                Acciones = acciones
            };
        }, "No se pudo cargar el comprobante solicitado.", new { tc, idComprobante, idComplemento }, ct);

    public Task<ComprobanteDocumentoArchivoDto?> GetDocumentoArchivoAsync(string tc, string idComprobante, int idComplemento, string documento, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetDocumentoArchivo", async token =>
        {
            if (string.IsNullOrWhiteSpace(tc) || string.IsNullOrWhiteSpace(idComprobante) || string.IsNullOrWhiteSpace(documento))
                return null;

            const string sql = """
                SELECT TOP (1)
                    ISNULL(d.DOCUMENTO, '') AS Documento
                FROM dbo.V_MV_CpteDoc d
                WHERE UPPER(LTRIM(RTRIM(d.TC))) = @Tc
                  AND UPPER(LTRIM(RTRIM(d.IDCOMPROBANTE))) = @IdComprobante
                  AND ISNULL(d.IDCOMPLEMENTO, 0) = @IdComplemento
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
