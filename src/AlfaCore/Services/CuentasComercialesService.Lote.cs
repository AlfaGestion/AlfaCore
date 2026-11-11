using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Accion por lote del listado de clientes. Usa las mismas columnas que graba el editor:
/// MA_CUENTASADIC (IdVendedor, ZONA, IdLista, idCond_Cpra_Vta, Clase, Clasificacion, Limite_Credito)
/// y MA_CUENTAS.BLOQUEO. La zona es texto libre, como en V_MA_Clientes.frm.
/// </summary>
public sealed partial class CuentasComercialesService
{
    public const int MaxClientesLote = 5000;

    public Task<IReadOnlyList<string>> GetZonasClientesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Clientes", "GetZonas", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var zonas = await cn.QueryAsync<string>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(ZONA)) FROM dbo.MA_CUENTASADIC
                WHERE LTRIM(RTRIM(ISNULL(ZONA, ''))) <> ''
                GROUP BY LTRIM(RTRIM(ZONA))
                ORDER BY LTRIM(RTRIM(ZONA));
                """, cancellationToken: token));
            return (IReadOnlyList<string>)zonas.ToList();
        }, "No se pudieron cargar las zonas.", ct);

    public Task<CuentaComercialLoteResultadoDto> AplicarLoteClientesAsync(CuentaComercialLoteRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Clientes", "AccionLote", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var codigos = request.Codigos
                .Select(c => (c ?? string.Empty).Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (codigos.Count == 0)
                throw new InvalidOperationException("Selecciona al menos un cliente.");
            if (codigos.Count > MaxClientesLote)
                throw new InvalidOperationException($"Se pueden procesar hasta {MaxClientesLote} clientes por vez.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var accion = (request.Accion ?? string.Empty).Trim().ToLowerInvariant();
            var cambio = await ResolverCambioLoteAsync(cn, accion, (request.Valor ?? string.Empty).Trim(), request, token);

            var resultado = new CuentaComercialLoteResultadoDto();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            // Por bloques para no pasar el limite de parametros de SQL Server.
            foreach (var bloque in codigos.Chunk(500))
            {
                var claves = bloque.Select(c => c.ToUpperInvariant()).ToList();
                // El conteo de filas del UPDATE incluye lo que tocan los triggers: se cuentan los
                // clientes encontrados antes de actualizar.
                var existentes = (await cn.QueryAsync<string>(new CommandDefinition($"""
                    SELECT UPPER(LTRIM(RTRIM(CODIGO))) FROM dbo.{cambio.Tabla} WITH (UPDLOCK)
                    WHERE UPPER(LTRIM(RTRIM(CODIGO))) IN @Claves;
                    """, new { Claves = claves }, tx, cancellationToken: token))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (existentes.Count > 0)
                    await cn.ExecuteAsync(new CommandDefinition($"""
                        UPDATE dbo.{cambio.Tabla}
                        SET {cambio.Set}, FechaHora_Modificacion = GETDATE()
                        WHERE UPPER(LTRIM(RTRIM(CODIGO))) IN @Claves;
                        """, new { cambio.Valor, Claves = existentes.ToList() }, tx, cancellationToken: token));
                resultado.Actualizados += existentes.Count;
                resultado.Observaciones.AddRange(bloque
                    .Where(c => !existentes.Contains(c))
                    .Select(c => cambio.Tabla == "MA_CUENTASADIC"
                        ? $"{c}: no tiene datos comerciales cargados (abrilo y grabalo una vez desde el editor)."
                        : $"{c}: no existe."));
            }
            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync("Clientes", "AccionLote", cambio.Tabla, accion,
                $"Accion por lote '{accion}' sobre {resultado.Actualizados} cliente(s).",
                new { accion, cambio.Valor, request.EsPorcentaje, Cantidad = codigos.Count }, CancellationToken.None);
            return resultado;
        }, "No se pudo aplicar la accion a los clientes seleccionados.", ct);

    /// <summary>Tabla, expresion SET (con @Valor) y valor para cada accion. Se guarda el codigo tal como
    /// esta en la tabla de referencia (con sus espacios), igual que lo deja el editor. Vacio = quitar.</summary>
    private static async Task<(string Tabla, string Set, object? Valor)> ResolverCambioLoteAsync(
        SqlConnection cn, string accion, string valor, CuentaComercialLoteRequest request, CancellationToken ct)
    {
        const string Adic = "MA_CUENTASADIC";
        switch (accion)
        {
            case CuentaComercialLoteAcciones.Vendedor:
                return (Adic, "IdVendedor = @Valor", valor.Length == 0 ? null
                    : await CodigoExistenteAsync(cn, "V_TA_VENDEDORES", "IdVendedor", valor, null, ct)
                      ?? throw new InvalidOperationException("Elegi un vendedor existente."));
            case CuentaComercialLoteAcciones.Lista:
                return (Adic, "IdLista = @Valor", valor.Length == 0 ? null
                    : await CodigoExistenteAsync(cn, "V_MA_PreciosCab", "IdLista", valor, ResolveDescriptor(CuentaComercialTipo.Cliente).PriceListType, ct)
                      ?? throw new InvalidOperationException("Elegi una lista de precios de venta existente."));
            case CuentaComercialLoteAcciones.Condicion:
                if (valor.Length == 0)
                    throw new InvalidOperationException("Elegi la condicion de pago.");
                return (Adic, "idCond_Cpra_Vta = @Valor",
                    await CodigoExistenteAsync(cn, "V_TA_Cpra_Vta", "IDCond_Cpra_Vta", valor, null, ct)
                    ?? throw new InvalidOperationException("Elegi una condicion de pago existente."));
            case CuentaComercialLoteAcciones.Zona:
                if (valor.Length > 30)
                    throw new InvalidOperationException("La zona puede tener hasta 30 caracteres.");
                return (Adic, "ZONA = @Valor", valor.Length == 0 ? null : valor);
            case CuentaComercialLoteAcciones.Clase:
                // Mismo rango que el editor de clientes (0 a 8).
                if (!short.TryParse(valor, out var clase) || clase is < 0 or > 8)
                    throw new InvalidOperationException("Elegi una clase de precio de 0 a 8.");
                return (Adic, "Clase = @Valor", clase);
            case CuentaComercialLoteAcciones.Clasificacion:
                if (!await ColumnExistsAsync(cn, null, "MA_CUENTASADIC", "Clasificacion", ct))
                    throw new InvalidOperationException("Esta base no tiene clasificacion de clientes.");
                return (Adic, "Clasificacion = @Valor", valor.Length == 0 ? null
                    : await CodigoExistenteAsync(cn, "TA_CLASIFICACIONES", "Codigo", valor, null, ct)
                      ?? throw new InvalidOperationException("Elegi una clasificacion existente."));
            case CuentaComercialLoteAcciones.LimiteCredito:
                if (request.EsPorcentaje)
                {
                    if (request.Numero == 0 || request.Numero <= -100 || request.Numero > 1000)
                        throw new InvalidOperationException("El porcentaje tiene que estar entre -99,99 y 1000 (distinto de 0).");
                    // Solo cambia los que ya tienen limite; sin limite (0) siguen igual.
                    return (Adic, "Limite_Credito = ROUND(ISNULL(Limite_Credito, 0) * @Valor, 2)", 1 + request.Numero / 100m);
                }
                if (request.Numero < 0)
                    throw new InvalidOperationException("El limite de credito no puede ser negativo.");
                return (Adic, "Limite_Credito = @Valor", request.Numero);
            case CuentaComercialLoteAcciones.Bloqueo:
                if (valor is not ("1" or "0"))
                    throw new InvalidOperationException("Elegi bloquear o desbloquear.");
                return ("MA_CUENTAS", "BLOQUEO = @Valor", valor == "1");
            default:
                throw new InvalidOperationException("Elegi la accion a aplicar.");
        }
    }

    private static Task<string?> CodigoExistenteAsync(SqlConnection cn, string tabla, string columna, string codigo, string? tipoLista, CancellationToken ct)
        => cn.ExecuteScalarAsync<string?>(new CommandDefinition($"""
            SELECT TOP (1) {columna} FROM dbo.{tabla}
            WHERE LTRIM(RTRIM({columna})) = @Codigo
              {(tipoLista is null ? string.Empty : "AND ISNULL(TipoLista, '') = @TipoLista")};
            """, new { Codigo = codigo.Trim(), TipoLista = tipoLista }, cancellationToken: ct));
}
