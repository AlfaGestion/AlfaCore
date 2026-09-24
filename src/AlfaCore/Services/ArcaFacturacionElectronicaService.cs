using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ArcaFacturacionElectronicaService(
    IArcaConfigService arcaConfig,
    IWsaaClient wsaaClient,
    IWsfev1Client wsfeClient,
    IAppEventService appEvents) : IArcaFacturacionElectronicaService
{
    private const string ModuleName = "ArcaFacturacionElectronica";
    private const int ConceptoProductos = 1;
    private const int ArcaSqlCommandTimeoutSeconds = 120;

    public async Task ValidarDisponibilidadAsync(SqlConnection cn, string? uNegocio, CancellationToken ct)
    {
        var emisor = await arcaConfig.ResolveEmisorAsync(cn, uNegocio, ct);
        if (emisor is not null)
            await wsaaClient.ObtenerTicketAsync(cn, emisor, ct);
    }

    public async Task<ArcaNumeracionPrevistaDto?> ResolverNumeracionAsync(SqlConnection cn, string? uNegocio, string letra, CancellationToken ct, Func<string, Task>? progreso = null)
    {
        var emisor = await arcaConfig.ResolveEmisorAsync(cn, uNegocio, ct);
        if (emisor is null)
            return null;

        var fiscal = TiposDocumentoCore.Fiscal(TiposDocumentoCore.ParaLetra(letra))
            ?? throw new InvalidOperationException($"La letra '{letra}' no tiene un tipo fiscal AFIP asociado.");

        if (progreso is not null) await progreso($"Autenticando con WSAA de ARCA ({EtiquetaAmbiente(emisor.Ambiente)})...");
        var ticket = await wsaaClient.ObtenerTicketAsync(cn, emisor, ct);
        if (progreso is not null) await progreso($"Consultando el próximo número autorizado en ARCA ({EtiquetaAmbiente(emisor.Ambiente)})...");
        var ultimo = await wsfeClient.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, fiscal.CodigoArca, emisor.Ambiente, emisor.WsfeUrl, ct);

        return new ArcaNumeracionPrevistaDto(emisor, fiscal.CodigoArca, ultimo + 1);
    }

    public async Task<ArcaCaeIntentoDto> SolicitarCaeYPersistirAsync(SqlConnection cn, PuntoVentaCaeContextoDto contexto, CancellationToken ct, Func<string, Task>? progreso = null)
    {
        var emisor = await arcaConfig.ResolveEmisorAsync(cn, contexto.UNegocio, ct);
        if (emisor is null)
            return ArcaCaeIntentoDto.NoAplica;

        if (!await ExistsAsync(cn, "dbo.V_MV_CPTE_ELECTRONICOS", ct))
            throw new InvalidOperationException("No existe dbo.V_MV_CPTE_ELECTRONICOS en esta base: no se puede registrar el resultado de AFIP.");

        var cabecera = await LeerCabeceraAsync(cn, contexto.Tc, contexto.IdComprobante, ct)
            ?? throw new InvalidOperationException($"No se encontró el comprobante {contexto.Tc}/{contexto.IdComprobante} para pedir su CAE.");

        var fiscal = TiposDocumentoCore.Fiscal(TiposDocumentoCore.ParaLetra(contexto.Letra))
            ?? throw new InvalidOperationException($"La letra '{contexto.Letra}' no tiene un tipo fiscal AFIP asociado.");

        var (docTipo, docNro) = ArcaCodigosAfip.ResolverDocumento(cabecera.DocumentoTipoDescripcion, cabecera.DocumentoNumero);
        var condicionIvaReceptorId = ArcaCodigosAfip.ResolverCondicionIvaReceptor(cabecera.CondicionIvaDescripcion);
        var (impNeto, impTotConc, impOpEx, ivas) = CalcularDesglose(contexto.Items, contexto.ImpTotal);

        var numero = long.Parse(contexto.Numero.TrimStart('0').Length == 0 ? "0" : contexto.Numero.TrimStart('0'));

        var solicitud = new ArcaCaeSolicitudDto(
            Cuit: emisor.Cuit,
            PtoVta: emisor.PuntoVentaElectronico,
            CbteTipo: fiscal.CodigoArca,
            Concepto: ConceptoProductos,
            DocTipo: docTipo,
            DocNro: docNro,
            CbteFch: cabecera.Fecha,
            CbteDesde: numero,
            CbteHasta: numero,
            ImpTotal: contexto.ImpTotal,
            ImpTotConc: impTotConc,
            ImpNeto: impNeto,
            ImpOpEx: impOpEx,
            ImpTrib: 0m,
            ImpIva: ivas.Sum(x => x.Importe),
            CondicionIvaReceptorId: condicionIvaReceptorId,
            MonId: ResolverMonId(cabecera.Moneda),
            MonCotiz: 1m,
            Ivas: ivas);

        ArcaCaeResultadoDto resultado;
        try
        {
            if (progreso is not null) await progreso($"Autenticando con WSAA de ARCA ({EtiquetaAmbiente(emisor.Ambiente)})...");
            var ticket = await wsaaClient.ObtenerTicketAsync(cn, emisor, ct);
            if (progreso is not null) await progreso($"Enviando la factura y obteniendo el CAE ({EtiquetaAmbiente(emisor.Ambiente)})...");
            resultado = await wsfeClient.SolicitarCaeAsync(ticket, solicitud, emisor.Ambiente, emisor.WsfeUrl, ct);
        }
        catch (Exception ex)
        {
            resultado = ArcaCaeResultadoDto.Fallo(ex.Message, numero, numero);
        }

        string? codigoBarra = null;
        if (resultado.Aprobado)
        {
            codigoBarra = ArcaCodigosAfip.CalcularCodigoBarraCae(emisor.Cuit, fiscal.CodigoArca, emisor.PuntoVentaElectronico, resultado.Cae!, resultado.CaeVto!.Value);
        }
        else
        {
            await appEvents.LogErrorAsync(
                ModuleName, "SolicitarCae",
                new InvalidOperationException(resultado.ErrorTecnico ?? "AFIP rechazó el comprobante."),
                "AFIP rechazó o no pudo procesar la solicitud de CAE.",
                new { contexto.Tc, contexto.IdComprobante, resultado.Resultado, Observaciones = resultado.Observaciones.Select(o => $"{o.Codigo}: {o.Mensaje}") },
                ct: ct);
        }

        await PersistirIntentoAsync(cn, contexto, fiscal.CodigoArca, docTipo, docNro, solicitud, resultado, codigoBarra, ct);

        var motivo = resultado.Aprobado
            ? null
            : resultado.ErrorTecnico ?? string.Join(" | ", resultado.Observaciones.Select(o => $"{o.Codigo}: {o.Mensaje}"));

        return resultado.Aprobado
            ? new ArcaCaeIntentoDto(ArcaCaeEstado.Aprobado, resultado.Cae, resultado.CaeVto, null)
            : new ArcaCaeIntentoDto(ArcaCaeEstado.Rechazado, null, null, motivo);
    }

    private static string EtiquetaAmbiente(ArcaAmbiente ambiente)
        => ambiente == ArcaAmbiente.Produccion
            ? "modo PRODUCCIÓN"
            : "modo PRUEBA / HOMOLOGACIÓN";

    /// <summary>Recalcula neto/IVA/exento desde el carrito (ver comentario en PuntoVentaCaeContextoDto
    /// sobre por qué no se usan las columnas AlicIva1..4 de la cabecera). Subtotal se asume precio
    /// final CON IVA (consistente con FN_PRECIO_SIN_IVA de sp_web_CpteInsumos). El residuo de
    /// redondeo contra ImpTotal se absorbe en el neto gravado -- WSFEv1 exige que
    /// ImpTotal = ImpNeto+ImpIVA+ImpOpEx+ImpTotConc+ImpTrib de forma exacta.</summary>
    private static (decimal ImpNeto, decimal ImpTotConc, decimal ImpOpEx, IReadOnlyList<ArcaIvaAlicuotaDto> Ivas) CalcularDesglose(IReadOnlyList<PuntoVentaCartItemDto> items, decimal impTotal)
    {
        var grupos = new Dictionary<int, (decimal Base, decimal Iva, decimal Tasa)>();
        var noGravado = 0m;
        var exento = 0m;

        foreach (var item in items)
        {
            var subtotal = item.Subtotal;
            if (item.NoGravado)
            {
                noGravado += subtotal;
                continue;
            }

            if (item.Exento || item.TasaIva <= 0)
            {
                exento += subtotal;
                continue;
            }

            var neto = Math.Round(subtotal / (1 + item.TasaIva / 100m), 2, MidpointRounding.AwayFromZero);
            var iva = subtotal - neto;
            var id = ArcaCodigosAfip.ResolverIdAlicuotaIva(item.TasaIva);
            var actual = grupos.TryGetValue(id, out var existente)
                ? existente
                : (Base: 0m, Iva: 0m, Tasa: item.TasaIva);
            grupos[id] = (actual.Base + neto, actual.Iva + iva, actual.Tasa);
        }

        var impNeto = grupos.Values.Sum(v => v.Base);
        var impIva = grupos.Values.Sum(v => v.Iva);
        var residuo = impTotal - (impNeto + impIva + noGravado + exento);
        if (Math.Abs(residuo) >= 0.01m)
        {
            if (grupos.Count == 0)
            {
                // Si el comprobante solo tiene conceptos exentos, el recargo
                // no puede crear un ImpNeto gravado sin su objeto IVA: ARCA
                // rechaza esa combinación con el error 10070.
                exento += residuo;
            }
            else
            {
                // El recargo de Point se distribuye sobre el grupo gravado
                // de mayor alícuota, generando simultáneamente base e IVA.
                // Así ImpTotal siempre coincide con ImpNeto + ImpIVA + Exento.
                var idGrupo = grupos.OrderByDescending(x => x.Value.Tasa).First().Key;
                var grupo = grupos[idGrupo];
                var netoRecargo = Math.Round(residuo / (1 + grupo.Tasa / 100m), 2, MidpointRounding.AwayFromZero);
                var ivaRecargo = residuo - netoRecargo;
                grupos[idGrupo] = (grupo.Base + netoRecargo, grupo.Iva + ivaRecargo, grupo.Tasa);
            }
        }

        var ivas = grupos.Select(kv => new ArcaIvaAlicuotaDto(kv.Key, kv.Value.Base, kv.Value.Iva)).ToList();
        impNeto = grupos.Values.Sum(v => v.Base);
        return (impNeto, noGravado, exento, ivas);
    }

    private static string ResolverMonId(string? codigoMoneda) => codigoMoneda?.Trim().ToUpperInvariant() switch
    {
        null or "" or "0" or "1" or "PES" => "PES",
        "2" or "DOL" => "DOL",
        "3" or "EUR" => "EUR",
        _ => "PES"
    };

    private static async Task<CabeceraRow?> LeerCabeceraAsync(SqlConnection cn, string tc, string idComprobante, CancellationToken ct)
    {
        var tieneCondIva = await ExistsAsync(cn, "dbo.TA_CONDIVA", ct);
        var condIvaJoin = tieneCondIva ? "LEFT JOIN dbo.TA_CONDIVA ci ON UPPER(LTRIM(RTRIM(ci.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(v.CONDICIONIVA, ''))))" : string.Empty;
        var condIvaSelect = tieneCondIva ? "ISNULL(ci.DESCRIPCION, '')" : "''";

        var sql = $"""
            SELECT v.FECHA AS Fecha, LTRIM(RTRIM(ISNULL(v.MONEDA, ''))) AS Moneda,
                   ISNULL(LTRIM(RTRIM(v.DOCUMENTONUMERO)), '') AS DocumentoNumero,
                   ISNULL(td.DESCRIPCION, '') AS DocumentoTipoDescripcion,
                   {condIvaSelect} AS CondicionIvaDescripcion
            FROM dbo.V_MV_Cpte v
            LEFT JOIN dbo.TA_TIPODOCUMENTO td ON UPPER(LTRIM(RTRIM(td.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(v.DOCUMENTOTIPO, ''))))
            {condIvaJoin}
            WHERE v.TC = @Tc AND v.IDCOMPROBANTE = @IdComprobante;
            """;

        await using var cmd = new SqlCommand(sql, cn)
        {
            CommandTimeout = ArcaSqlCommandTimeoutSeconds
        };
        cmd.Parameters.AddWithValue("@Tc", tc);
        cmd.Parameters.AddWithValue("@IdComprobante", idComprobante);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new CabeceraRow(
            rd.GetDateTime(0),
            rd.IsDBNull(1) ? string.Empty : rd.GetString(1),
            rd.IsDBNull(2) ? string.Empty : rd.GetString(2),
            rd.IsDBNull(3) ? string.Empty : rd.GetString(3),
            rd.IsDBNull(4) ? string.Empty : rd.GetString(4));
    }

    private static async Task PersistirIntentoAsync(
        SqlConnection cn,
        PuntoVentaCaeContextoDto contexto,
        int tipoCpte,
        int docTipo,
        string docNro,
        ArcaCaeSolicitudDto solicitud,
        ArcaCaeResultadoDto resultado,
        string? codigoBarra,
        CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);

        await using (var archivarCmd = new SqlCommand(
            """
            UPDATE dbo.V_MV_CPTE_ELECTRONICOS SET Archivado = 1
            WHERE TC = @Tc AND IdComprobante = @IdComprobante AND ISNULL(Archivado, 0) = 0;
            """, cn, tx)
        {
            CommandTimeout = ArcaSqlCommandTimeoutSeconds
        })
        {
            archivarCmd.Parameters.AddWithValue("@Tc", contexto.Tc);
            archivarCmd.Parameters.AddWithValue("@IdComprobante", contexto.IdComprobante);
            await archivarCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var countCmd = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.V_MV_CPTE_ELECTRONICOS WHERE TC = @Tc AND IdComprobante = @IdComprobante;", cn, tx)
        {
            CommandTimeout = ArcaSqlCommandTimeoutSeconds
        })
        {
            countCmd.Parameters.AddWithValue("@Tc", contexto.Tc);
            countCmd.Parameters.AddWithValue("@IdComprobante", contexto.IdComprobante);
            var intentoNro = (int)await countCmd.ExecuteScalarAsync(ct)! + 1;
            var idRequerimiento = $"{contexto.Tc}{contexto.IdComprobante}-{intentoNro}";

            await using var insertCmd = new SqlCommand(
                """
                INSERT INTO dbo.V_MV_CPTE_ELECTRONICOS
                (IdRequerimiento, CantReg, Presta_Serv, TC, IdComprobante,
                 Tipo_Doc, Nro_Doc, Tipo_Cpte, Punto_Vta, Cpte_Desde, Cpte_Hasta,
                 Imp_Total, Imp_NoGrav, Imp_NetoGrav, Imp_ImpuestoLiquidado, Imp_Impuestos_Rni, Imp_OPExentas,
                 Fecha_Cpte, Resultado, Motivo, CAE, Error, VtoCAE, CodigoBarraCAE, Archivado)
                VALUES
                (@IdRequerimiento, 1, 0, @Tc, @IdComprobante,
                 @TipoDoc, @NroDoc, @TipoCpte, @PuntoVta, @CbteDesde, @CbteHasta,
                 @ImpTotal, @ImpNoGrav, @ImpNetoGrav, @ImpIva, 0, @ImpOpEx,
                 @FechaCpte, @Resultado, @Motivo, @Cae, @Error, @VtoCae, @CodigoBarraCae, 0);
                """, cn, tx)
            {
                CommandTimeout = ArcaSqlCommandTimeoutSeconds
            };

            insertCmd.Parameters.AddWithValue("@IdRequerimiento", idRequerimiento);
            insertCmd.Parameters.AddWithValue("@Tc", contexto.Tc);
            insertCmd.Parameters.AddWithValue("@IdComprobante", contexto.IdComprobante);
            insertCmd.Parameters.AddWithValue("@TipoDoc", docTipo);
            insertCmd.Parameters.AddWithValue("@NroDoc", docNro);
            insertCmd.Parameters.AddWithValue("@TipoCpte", tipoCpte);
            insertCmd.Parameters.AddWithValue("@PuntoVta", solicitud.PtoVta);
            insertCmd.Parameters.AddWithValue("@CbteDesde", solicitud.CbteDesde.ToString());
            insertCmd.Parameters.AddWithValue("@CbteHasta", solicitud.CbteHasta.ToString());
            insertCmd.Parameters.AddWithValue("@ImpTotal", solicitud.ImpTotal);
            insertCmd.Parameters.AddWithValue("@ImpNoGrav", solicitud.ImpTotConc);
            insertCmd.Parameters.AddWithValue("@ImpNetoGrav", solicitud.ImpNeto);
            insertCmd.Parameters.AddWithValue("@ImpIva", solicitud.ImpIva);
            insertCmd.Parameters.AddWithValue("@ImpOpEx", solicitud.ImpOpEx);
            insertCmd.Parameters.AddWithValue("@FechaCpte", solicitud.CbteFch);
            insertCmd.Parameters.AddWithValue("@Resultado", (object?)resultado.Resultado ?? "R");
            insertCmd.Parameters.AddWithValue("@Motivo", (object?)FormatearMotivo(resultado) ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@Cae", (object?)resultado.Cae ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@Error", (object?)resultado.ErrorTecnico ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@VtoCae", (object?)resultado.CaeVto ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@CodigoBarraCae", (object?)codigoBarra ?? DBNull.Value);
            await insertCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static string? FormatearMotivo(ArcaCaeResultadoDto resultado)
    {
        if (resultado.Observaciones.Count == 0) return null;
        var texto = string.Join(" | ", resultado.Observaciones.Select(o => o.Codigo.ToString()));
        return texto.Length > 10 ? texto[..10] : texto;
    }

    private static async Task<bool> ExistsAsync(SqlConnection cn, string objeto, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT OBJECT_ID(N'{objeto}');", cn)
        {
            CommandTimeout = ArcaSqlCommandTimeoutSeconds
        };
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null and not DBNull;
    }

    private sealed record CabeceraRow(DateTime Fecha, string Moneda, string DocumentoNumero, string DocumentoTipoDescripcion, string CondicionIvaDescripcion);
}
