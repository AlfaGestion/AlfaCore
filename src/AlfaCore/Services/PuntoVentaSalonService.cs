using System.Data;
using System.Security.Cryptography;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaSalonService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IPuntoVentaService puntoVentaService,
    ILogger<PuntoVentaSalonService> logger) : IPuntoVentaSalonService
{
    private const string ModuleName = "PuntoVentaSalon";

    // Claves heredadas del salon VB6 (V_MV_CtrolMesas): se respetan para no duplicar configuracion.
    private const string CfgArticuloCubiertos = "GOUR_ArtCubiertos";
    private const string CfgRequiereMozo = "GOUR_RequiereMozo";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    // ------------------------------------------------------------------ lectura

    public Task<PuntoVentaSalonDto> GetSalonAsync(int idPuntoVenta, bool incluirInactivas = false, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetSalon", async token =>
        {
            await using var cn = await OpenAsync(token);

            var pv = await cn.QuerySingleOrDefaultAsync<(int Id, string Nombre, string QrModo)>(new CommandDefinition(
                "SELECT ID, NOMBRE, ISNULL(QR_MODO, 'NO') FROM dbo.POS_PUNTOVENTA WHERE ID = @Id;", new { Id = idPuntoVenta }, cancellationToken: token));
            if (pv.Id <= 0)
                throw new InvalidOperationException("No se encontró el punto de venta indicado.");

            var sectores = (await cn.QueryAsync<PuntoVentaSalonSectorDto>(new CommandDefinition("""
                SELECT ID AS Id, NOMBRE AS Nombre, ISNULL(ICONO, '') AS Icono, ORDEN AS Orden
                FROM dbo.POS_SECTOR
                WHERE IDPUNTOVENTA = @IdPuntoVenta AND ACTIVO = 1
                ORDER BY ORDEN, NOMBRE;
                """, new { IdPuntoVenta = idPuntoVenta }, cancellationToken: token))).ToList();

            const string mesasSql = """
                SELECT
                    m.ID AS Id, m.IDSECTOR AS IdSector, m.CODIGO AS Codigo, m.NOMBRE AS Nombre,
                    m.CAPACIDAD AS Capacidad, m.POSX AS PosX, m.POSY AS PosY, m.WIDTH AS Width, m.HEIGHT AS Height,
                    ISNULL(m.FORMATO, 'CUADRADA') AS Formato,
                    m.ACTIVO AS Activo,
                    ISNULL(m.ICONO, '') AS Icono,
                    p.ID AS IdPedido,
                    ISNULL(p.ESTADO, 'LIBRE') AS Estado,
                    p.FECHAHORA_ALTA AS FechaHoraApertura,
                    ISNULL(LTRIM(RTRIM(p.MOZO)), '') AS Mozo,
                    ISNULL(LTRIM(RTRIM(v.Nombre)), '') AS MozoNombre,
                    p.CUBIERTOS AS Cubiertos,
                    ISNULL(t.Total, 0) AS Total,
                    ISNULL(t.Pendientes, 0) AS LineasPendientes,
                    ISNULL(t.EnCocina, 0) AS LineasEnCocina,
                    ISNULL(t.Listas, 0) AS LineasListas,
                    ISNULL(t.QrPorConfirmar, 0) AS LineasQrPorConfirmar,
                    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.POS_MESA_LLAMADO l WHERE l.IDMESA = m.ID AND l.ATENDIDO = 0 AND l.TIPO = 'MOZO' AND l.IDPEDIDO = p.ID) THEN 1 ELSE 0 END AS bit) AS LlamaMozo,
                    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.POS_MESA_LLAMADO l WHERE l.IDMESA = m.ID AND l.ATENDIDO = 0 AND l.TIPO = 'CUENTA' AND l.IDPEDIDO = p.ID) THEN 1 ELSE 0 END AS bit) AS PideCuenta,
                    t.UltimoEnvio
                FROM dbo.POS_MESA m
                INNER JOIN dbo.POS_SECTOR s ON s.ID = m.IDSECTOR
                LEFT JOIN dbo.POS_PEDIDO p
                    ON p.IDMESA = m.ID AND p.MODALIDAD = 'SALON' AND p.ESTADO IN ('ABIERTA', 'CUENTA')
                LEFT JOIN dbo.V_TA_VENDEDORES v ON LTRIM(RTRIM(v.IdVendedor)) = LTRIM(RTRIM(p.MOZO))
                LEFT JOIN (
                    SELECT d.IDPEDIDO,
                        SUM(CASE WHEN d.ESTADO <> 'ANULADO' THEN d.CANTIDAD * d.PRECIO ELSE 0 END) AS Total,
                        SUM(CASE WHEN d.ESTADO = 'PENDIENTE' THEN 1 ELSE 0 END) AS Pendientes,
                        SUM(CASE WHEN d.ESTADO IN ('ENVIADO', 'EN_PREPARACION') THEN 1 ELSE 0 END) AS EnCocina,
                        SUM(CASE WHEN d.ESTADO = 'LISTO' THEN 1 ELSE 0 END) AS Listas,
                        SUM(CASE WHEN d.ESTADO = 'PENDIENTE' AND d.ORIGEN = 'QR' THEN 1 ELSE 0 END) AS QrPorConfirmar,
                        MAX(d.FECHAHORA_ENVIO) AS UltimoEnvio
                    FROM dbo.POS_PEDIDO_DET d
                    GROUP BY d.IDPEDIDO
                ) t ON t.IDPEDIDO = p.ID
                WHERE s.IDPUNTOVENTA = @IdPuntoVenta AND s.ACTIVO = 1
                  AND (m.ACTIVO = 1 OR @IncluirInactivas = 1)
                ORDER BY m.IDSECTOR, m.CODIGO, m.ID;
                """;

            var mesas = (await cn.QueryAsync<PuntoVentaSalonMesaDto>(new CommandDefinition(
                mesasSql, new { IdPuntoVenta = idPuntoVenta, IncluirInactivas = incluirInactivas ? 1 : 0 }, cancellationToken: token))).ToList();

            // Si una mesa quedo con dos pedidos abiertos (no deberia: AbrirMesa lo bloquea), se muestra el mas viejo.
            foreach (var sector in sectores)
            {
                sector.Mesas = mesas
                    .Where(m => m.IdSector == sector.Id)
                    .GroupBy(m => m.Id)
                    .Select(g => g.OrderBy(x => x.IdPedido ?? int.MaxValue).First())
                    .ToList();
            }

            var mozos = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IdVendedor)) AS Valor, LTRIM(RTRIM(ISNULL(Nombre, ''))) AS Descripcion
                FROM dbo.V_TA_VENDEDORES
                WHERE LTRIM(RTRIM(IdVendedor)) <> ''
                ORDER BY Nombre, IdVendedor;
                """, cancellationToken: token))).ToList();

            var llamados = (await cn.QueryAsync<PuntoVentaSalonLlamadoDto>(new CommandDefinition("""
                SELECT l.ID AS Id, l.IDMESA AS IdMesa, l.IDPEDIDO AS IdPedido, ISNULL(m.NOMBRE, '') AS NombreMesa,
                    l.TIPO AS Tipo, l.FECHAHORA AS FechaHora
                FROM dbo.POS_MESA_LLAMADO l
                LEFT JOIN dbo.POS_MESA m ON m.ID = l.IDMESA
                WHERE l.IDPUNTOVENTA = @IdPuntoVenta AND l.ATENDIDO = 0
                  AND EXISTS (SELECT 1 FROM dbo.POS_PEDIDO pp WHERE pp.ID = l.IDPEDIDO AND pp.ESTADO IN ('ABIERTA', 'CUENTA'))
                ORDER BY l.FECHAHORA;
                """, new { IdPuntoVenta = idPuntoVenta }, cancellationToken: token))).ToList();

            return new PuntoVentaSalonDto
            {
                IdPuntoVenta = pv.Id,
                Llamados = llamados,
                QrModo = string.IsNullOrWhiteSpace(pv.QrModo) ? PuntoVentaQrModoKeys.No : pv.QrModo.Trim(),
                NombrePuntoVenta = pv.Nombre?.Trim() ?? string.Empty,
                Sectores = sectores,
                Mozos = mozos,
                RequiereMozo = EsVerdadero(await GetConfigAsync(cn, null, CfgRequiereMozo, token)),
                ArticuloCubiertos = (await GetConfigAsync(cn, null, CfgArticuloCubiertos, token)).Trim()
            };
        }, "No se pudo cargar el salón.", ct);

    public Task<PuntoVentaSalonMesaDetalleDto?> GetMesaDetalleAsync(int idPedido, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetMesaDetalle", async token =>
        {
            await using var cn = await OpenAsync(token);
            return await LoadDetalleAsync(cn, null, idPedido, token);
        }, "No se pudo cargar la mesa.", ct);

    private static async Task<PuntoVentaSalonMesaDetalleDto?> LoadDetalleAsync(SqlConnection cn, SqlTransaction? tx, int idPedido, CancellationToken ct)
    {
        var detalle = await cn.QuerySingleOrDefaultAsync<PuntoVentaSalonMesaDetalleDto>(new CommandDefinition("""
            SELECT p.ID AS IdPedido, p.IDPUNTOVENTA AS IdPuntoVenta, ISNULL(p.IDMESA, 0) AS IdMesa,
                ISNULL(m.NOMBRE, '') AS NombreMesa, ISNULL(s.NOMBRE, '') AS NombreSector,
                p.ESTADO AS Estado, ISNULL(p.FECHAHORA_ALTA, p.FECHA) AS FechaHoraApertura,
                ISNULL(LTRIM(RTRIM(p.MOZO)), '') AS Mozo, ISNULL(LTRIM(RTRIM(v.Nombre)), '') AS MozoNombre,
                p.CUBIERTOS AS Cubiertos, ISNULL(p.PIN, '') AS Pin, ISNULL(p.OBSERVACIONES, '') AS Observaciones
            FROM dbo.POS_PEDIDO p
            LEFT JOIN dbo.POS_MESA m ON m.ID = p.IDMESA
            LEFT JOIN dbo.POS_SECTOR s ON s.ID = m.IDSECTOR
            LEFT JOIN dbo.V_TA_VENDEDORES v ON LTRIM(RTRIM(v.IdVendedor)) = LTRIM(RTRIM(p.MOZO))
            WHERE p.ID = @Id AND p.MODALIDAD = 'SALON';
            """, new { Id = idPedido }, tx, cancellationToken: ct));
        if (detalle is null)
            return null;

        detalle.Lineas = (await cn.QueryAsync<PuntoVentaSalonLineaDto>(new CommandDefinition("""
            SELECT ID AS Id, IDARTICULO AS IdArticulo, ISNULL(DESCRIPCION, '') AS Descripcion,
                CAST(CANTIDAD AS decimal(18, 4)) AS Cantidad, PRECIO AS Precio,
                ISNULL(OBSERVACIONES, '') AS Observaciones,
                ISNULL(ESTADO, 'PENDIENTE') AS Estado, ISNULL(DESTINO, '') AS Destino,
                ISNULL(ORIGEN, '') AS Origen, ISNULL(USUARIO, '') AS Usuario,
                FECHAHORA_ALTA AS FechaHoraAlta, FECHAHORA_ENVIO AS FechaHoraEnvio,
                ISNULL(MOTIVOANULACION, '') AS MotivoAnulacion
            FROM dbo.POS_PEDIDO_DET
            WHERE IDPEDIDO = @Id
            ORDER BY ID;
            """, new { Id = idPedido }, tx, cancellationToken: ct))).ToList();

        return detalle;
    }

    // ------------------------------------------------------------------ apertura

    public Task<PuntoVentaSalonAbrirMesaResult> AbrirMesaAsync(PuntoVentaSalonAbrirMesaRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("AbrirMesa", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var usuario = NormalizeUser(request.UsuarioAccion);
            var mozo = (request.Mozo ?? string.Empty).Trim();
            var cubiertos = request.Cubiertos is > 0 ? request.Cubiertos : null;

            await using var cn = await OpenAsync(token);

            var mesa = await cn.QuerySingleOrDefaultAsync<(int IdMesa, string NombreMesa, int IdPuntoVenta, string Unegocio, string IdCaja)>(new CommandDefinition("""
                SELECT m.ID, m.NOMBRE, s.IDPUNTOVENTA, ISNULL(pv.UNEGOCIO, ''), ISNULL(pv.IDCAJA, '')
                FROM dbo.POS_MESA m
                INNER JOIN dbo.POS_SECTOR s ON s.ID = m.IDSECTOR
                INNER JOIN dbo.POS_PUNTOVENTA pv ON pv.ID = s.IDPUNTOVENTA
                WHERE m.ID = @IdMesa AND m.ACTIVO = 1;
                """, new { request.IdMesa }, cancellationToken: token));
            if (mesa.IdMesa <= 0)
                throw new InvalidOperationException("La mesa no existe o está inactiva.");

            if (string.IsNullOrEmpty(mozo) && EsVerdadero(await GetConfigAsync(cn, null, CfgRequiereMozo, token)))
                throw new InvalidOperationException("Indicá el mozo que atiende la mesa.");

            // Articulo de cubiertos (si esta configurado): se resuelve antes de la transaccion.
            PuntoVentaArticleDto? articuloCubiertos = null;
            if (cubiertos.HasValue)
            {
                var codigo = (await GetConfigAsync(cn, null, CfgArticuloCubiertos, token)).Trim();
                if (codigo.Length > 0)
                    articuloCubiertos = await puntoVentaService.GetArticuloPorCodigoAsync(codigo, token);
            }

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, token);
            try
            {
                // UPDLOCK + HOLDLOCK: dos mozos que abren la misma mesa a la vez no generan dos pedidos.
                var existente = await cn.ExecuteScalarAsync<int?>(new CommandDefinition("""
                    SELECT TOP (1) ID FROM dbo.POS_PEDIDO WITH (UPDLOCK, HOLDLOCK)
                    WHERE IDMESA = @IdMesa AND MODALIDAD = 'SALON' AND ESTADO IN ('ABIERTA', 'CUENTA')
                    ORDER BY ID;
                    """, new { request.IdMesa }, tx, cancellationToken: token));
                if (existente is > 0)
                {
                    await tx.CommitAsync(token);
                    return new PuntoVentaSalonAbrirMesaResult { IdPedido = existente.Value, YaEstabaAbierta = true };
                }

                var idPedido = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    INSERT INTO dbo.POS_PEDIDO
                        (IDPUNTOVENTA, MODALIDAD, IDCLIENTE, NOMBRECLIENTE, FECHA, ESTADO, OBSERVACIONES,
                         UNEGOCIO, IDCAJA, USUARIO, FECHAHORA_ALTA, IDMESA, MOZO, CUBIERTOS, PIN)
                    VALUES
                        (@IdPuntoVenta, 'SALON', '', @NombreCliente, CAST(GETDATE() AS date), 'ABIERTA', @Observaciones,
                         @Unegocio, @IdCaja, @Usuario, GETDATE(), @IdMesa, @Mozo, @Cubiertos, @Pin);
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """, new
                {
                    mesa.IdPuntoVenta,
                    NombreCliente = Truncate(NombrePedidoMesa(mesa.NombreMesa), 100),
                    Observaciones = Truncate((request.Observaciones ?? string.Empty).Trim(), 500),
                    Unegocio = NullIfEmpty(mesa.Unegocio),
                    IdCaja = NullIfEmpty(mesa.IdCaja),
                    Usuario = usuario,
                    request.IdMesa,
                    Mozo = NullIfEmpty(mozo),
                    Cubiertos = cubiertos,
                    Pin = GenerarPin()
                }, tx, cancellationToken: token));

                if (articuloCubiertos is not null && cubiertos.HasValue)
                {
                    await InsertLineaAsync(cn, tx, idPedido, CrearLineaCubiertos(articuloCubiertos, cubiertos.Value), PuntoVentaSalonLineaEstadoKeys.Servido, PuntoVentaSalonOrigenKeys.Mozo, usuario, token);
                }

                await tx.CommitAsync(token);

                await AuditAsync("AbrirMesa", idPedido, $"Mesa {mesa.NombreMesa?.Trim()} abierta.", new { request.IdMesa, mozo, cubiertos });
                return new PuntoVentaSalonAbrirMesaResult { IdPedido = idPedido };
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo abrir la mesa.", ct);

    public Task ActualizarMesaAsync(int idPedido, string mozo, int? cubiertos, string observaciones, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("ActualizarMesa", async token =>
        {
            var usuario = NormalizeUser(usuarioAccion);
            var nuevosCubiertos = cubiertos is > 0 ? cubiertos : null;

            await using var cn = await OpenAsync(token);
            var codigo = (await GetConfigAsync(cn, null, CfgArticuloCubiertos, token)).Trim();
            var articuloCubiertos = codigo.Length > 0 && nuevosCubiertos.HasValue
                ? await puntoVentaService.GetArticuloPorCodigoAsync(codigo, token)
                : null;

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                await RequireAbiertaAsync(cn, tx, idPedido, token);

                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.POS_PEDIDO
                    SET MOZO = @Mozo, CUBIERTOS = @Cubiertos, OBSERVACIONES = @Observaciones,
                        USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                    WHERE ID = @Id;
                    """, new
                {
                    Id = idPedido,
                    Mozo = NullIfEmpty((mozo ?? string.Empty).Trim()),
                    Cubiertos = nuevosCubiertos,
                    Observaciones = Truncate((observaciones ?? string.Empty).Trim(), 500),
                    Usuario = usuario
                }, tx, cancellationToken: token));

                // La linea de cubiertos sigue a la cantidad de la cabecera.
                if (codigo.Length > 0)
                {
                    if (nuevosCubiertos.HasValue)
                    {
                        var actualizadas = await cn.ExecuteAsync(new CommandDefinition("""
                            UPDATE dbo.POS_PEDIDO_DET SET CANTIDAD = @Cantidad
                            WHERE IDPEDIDO = @Id AND LTRIM(RTRIM(IDARTICULO)) = @Codigo AND ESTADO <> 'ANULADO';
                            """, new { Id = idPedido, Cantidad = nuevosCubiertos.Value, Codigo = codigo }, tx, cancellationToken: token));

                        if (actualizadas == 0 && articuloCubiertos is not null)
                        {
                            await InsertLineaAsync(cn, tx, idPedido, CrearLineaCubiertos(articuloCubiertos, nuevosCubiertos.Value), PuntoVentaSalonLineaEstadoKeys.Servido, PuntoVentaSalonOrigenKeys.Mozo, usuario, token);
                        }
                    }
                    else
                    {
                        await cn.ExecuteAsync(new CommandDefinition("""
                            UPDATE dbo.POS_PEDIDO_DET SET ESTADO = 'ANULADO', MOTIVOANULACION = 'Sin cubiertos'
                            WHERE IDPEDIDO = @Id AND LTRIM(RTRIM(IDARTICULO)) = @Codigo AND ESTADO <> 'ANULADO';
                            """, new { Id = idPedido, Codigo = codigo }, tx, cancellationToken: token));
                    }
                }

                await tx.CommitAsync(token);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudieron actualizar los datos de la mesa.", ct);

    // ------------------------------------------------------------------ items / comanda

    public Task AgregarItemsAsync(int idPedido, IReadOnlyList<PuntoVentaPedidoItemDto> items, bool enviar, string origen, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("AgregarItems", async token =>
        {
            ArgumentNullException.ThrowIfNull(items);
            var validos = items.Where(i => !string.IsNullOrWhiteSpace(i.IdArticulo) && i.Cantidad > 0).ToList();
            if (validos.Count == 0)
                return true;

            var usuario = NormalizeUser(usuarioAccion);
            var origenNormalizado = string.Equals(origen, PuntoVentaSalonOrigenKeys.Qr, StringComparison.OrdinalIgnoreCase)
                ? PuntoVentaSalonOrigenKeys.Qr
                : PuntoVentaSalonOrigenKeys.Mozo;

            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var estadoMesa = await RequireAbiertaAsync(cn, tx, idPedido, token);
                await RequireNoAgotadosAsync(cn, tx, idPedido, validos.Select(i => i.IdArticulo), token);

                foreach (var item in validos)
                    await InsertLineaAsync(cn, tx, idPedido, item, PuntoVentaSalonLineaEstadoKeys.Pendiente, origenNormalizado, usuario, token);

                await AsignarDestinosAsync(cn, tx, idPedido, token);

                // Si habia pedido la cuenta y agregan algo, la mesa vuelve a estar abierta.
                if (estadoMesa == PuntoVentaSalonMesaEstadoKeys.Cuenta)
                    await SetEstadoMesaAsync(cn, tx, idPedido, PuntoVentaSalonMesaEstadoKeys.Abierta, usuario, token);

                if (enviar)
                    await EnviarPendientesCoreAsync(cn, tx, idPedido, usuario, token);

                await tx.CommitAsync(token);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudieron agregar los artículos a la mesa.", ct);

    public Task<int> EnviarPendientesAsync(int idPedido, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("EnviarPendientes", async token =>
        {
            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                await RequireAbiertaAsync(cn, tx, idPedido, token);
                var enviadas = await EnviarPendientesCoreAsync(cn, tx, idPedido, NormalizeUser(usuarioAccion), token);
                await tx.CommitAsync(token);
                return enviadas;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo enviar la comanda.", ct);

    /// <summary>
    /// Envia lo PENDIENTE: genera una POS_COMANDA por destino (lo que ve la pantalla de cocina y lo
    /// que imprime la comandera) y pasa las lineas a ENVIADO; lo que no se prepara (sin destino:
    /// cubiertos, bebidas embotelladas...) pasa directo a SERVIDO.
    /// </summary>
    internal static async Task<int> EnviarPendientesCoreAsync(SqlConnection cn, SqlTransaction tx, int idPedido, string usuario, CancellationToken ct)
    {
        const string sql = """
            DECLARE @APreparar int;
            SELECT @APreparar = COUNT(*) FROM dbo.POS_PEDIDO_DET
            WHERE IDPEDIDO = @Id AND ESTADO = 'PENDIENTE' AND ISNULL(DESTINO, '') <> '';

            INSERT INTO dbo.POS_COMANDA (IDPEDIDO, IDPUNTOVENTA, DESTINO, FECHAHORA, USUARIO)
            SELECT @Id, p.IDPUNTOVENTA, d.DESTINO, GETDATE(), @Usuario
            FROM (SELECT DISTINCT DESTINO FROM dbo.POS_PEDIDO_DET
                  WHERE IDPEDIDO = @Id AND ESTADO = 'PENDIENTE' AND ISNULL(DESTINO, '') <> '') d
            CROSS JOIN dbo.POS_PEDIDO p
            WHERE p.ID = @Id;

            UPDATE det
            SET IDCOMANDA = (SELECT MAX(c.ID) FROM dbo.POS_COMANDA c WHERE c.IDPEDIDO = @Id AND c.DESTINO = det.DESTINO)
            FROM dbo.POS_PEDIDO_DET det
            WHERE det.IDPEDIDO = @Id AND det.ESTADO = 'PENDIENTE' AND ISNULL(det.DESTINO, '') <> '';

            UPDATE dbo.POS_PEDIDO_DET
            SET ESTADO = CASE WHEN ISNULL(DESTINO, '') = '' THEN 'SERVIDO' ELSE 'ENVIADO' END,
                FECHAHORA_ENVIO = GETDATE()
            WHERE IDPEDIDO = @Id AND ESTADO = 'PENDIENTE';

            SELECT @APreparar;
            """;
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Id = idPedido, Usuario = usuario }, tx, cancellationToken: ct));
    }

    /// <summary>Rechaza la carga si algun articulo esta marcado como agotado en el punto de venta del pedido.</summary>
    internal static async Task RequireNoAgotadosAsync(SqlConnection cn, SqlTransaction tx, int idPedido, IEnumerable<string> idsArticulo, CancellationToken ct)
    {
        var ids = idsArticulo.Select(i => (i ?? string.Empty).Trim()).Where(i => i.Length > 0).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var agotados = (await cn.QueryAsync<string>(new CommandDefinition("""
            SELECT ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), x.IDARTICULO)
            FROM dbo.POS_ARTICULO_AGOTADO x
            INNER JOIN dbo.POS_PEDIDO p ON p.IDPUNTOVENTA = x.IDPUNTOVENTA
            LEFT JOIN dbo.V_MA_ARTICULOS a ON LTRIM(RTRIM(a.IdArticulo)) = x.IDARTICULO
            WHERE p.ID = @IdPedido AND x.IDARTICULO IN @Ids;
            """, new { IdPedido = idPedido, Ids = ids }, tx, cancellationToken: ct))).ToList();

        if (agotados.Count > 0)
            throw new InvalidOperationException($"No hay más: {string.Join(", ", agotados)}. Quitalo del pedido.");
    }

    public Task AnularLineaAsync(int idLinea, string motivo, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("AnularLinea", async token =>
        {
            var usuario = NormalizeUser(usuarioAccion);
            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var linea = await cn.QuerySingleOrDefaultAsync<(int IdPedido, string Estado, string Descripcion)>(new CommandDefinition("""
                    SELECT IDPEDIDO, ISNULL(ESTADO, 'PENDIENTE'), ISNULL(DESCRIPCION, '')
                    FROM dbo.POS_PEDIDO_DET WITH (UPDLOCK) WHERE ID = @Id;
                    """, new { Id = idLinea }, tx, cancellationToken: token));
                if (linea.IdPedido <= 0)
                    throw new InvalidOperationException("No se encontró el artículo indicado.");

                await RequireAbiertaAsync(cn, tx, linea.IdPedido, token);

                if (linea.Estado == PuntoVentaSalonLineaEstadoKeys.Pendiente)
                {
                    // Nunca salio a cocina: se borra sin dejar rastro.
                    await cn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM dbo.POS_PEDIDO_DET WHERE ID = @Id;", new { Id = idLinea }, tx, cancellationToken: token));
                }
                else
                {
                    var motivoNormalizado = Truncate((motivo ?? string.Empty).Trim(), 100);
                    if (motivoNormalizado.Length == 0)
                        throw new InvalidOperationException("Indicá el motivo: el artículo ya se envió a preparar.");

                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.POS_PEDIDO_DET
                        SET ESTADO = 'ANULADO', MOTIVOANULACION = @Motivo, USUARIO = @Usuario
                        WHERE ID = @Id;
                        """, new { Id = idLinea, Motivo = motivoNormalizado, Usuario = usuario }, tx, cancellationToken: token));
                }

                await tx.CommitAsync(token);

                if (linea.Estado != PuntoVentaSalonLineaEstadoKeys.Pendiente)
                    await AuditAsync("AnularLinea", linea.IdPedido, $"Anulado después de enviado: {linea.Descripcion}.", new { idLinea, motivo });
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo anular el artículo.", ct);

    public Task CambiarEstadoLineaAsync(int idLinea, string estado, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("CambiarEstadoLinea", async token =>
        {
            var nuevo = (estado ?? string.Empty).Trim().ToUpperInvariant();
            if (!PuntoVentaSalonLineaEstadoKeys.All.Contains(nuevo)
                || nuevo is PuntoVentaSalonLineaEstadoKeys.Pendiente or PuntoVentaSalonLineaEstadoKeys.Anulado)
                throw new InvalidOperationException("El estado indicado no es válido.");

            await using var cn = await OpenAsync(token);
            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.POS_PEDIDO_DET
                SET ESTADO = @Estado,
                    FECHAHORA_LISTO = CASE WHEN @Estado = 'LISTO' THEN GETDATE() ELSE FECHAHORA_LISTO END
                WHERE ID = @Id AND ESTADO NOT IN ('PENDIENTE', 'ANULADO');
                """, new { Id = idLinea, Estado = nuevo }, cancellationToken: token));
            if (affected <= 0)
                throw new InvalidOperationException("El artículo no está enviado a preparar.");
            return true;
        }, "No se pudo actualizar el artículo.", ct);

    // ------------------------------------------------------------------ cuenta / mesas

    public Task PedirCuentaAsync(int idPedido, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("PedirCuenta", async token =>
        {
            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                await RequireAbiertaAsync(cn, tx, idPedido, token);
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.POS_PEDIDO
                    SET ESTADO = 'CUENTA', FECHAHORA_CUENTA = GETDATE(), USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                    WHERE ID = @Id;
                    """, new { Id = idPedido, Usuario = NormalizeUser(usuarioAccion) }, tx, cancellationToken: token));
                await tx.CommitAsync(token);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo pedir la cuenta.", ct);

    public Task ReabrirAsync(int idPedido, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("Reabrir", async token =>
        {
            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                await RequireAbiertaAsync(cn, tx, idPedido, token);
                await SetEstadoMesaAsync(cn, tx, idPedido, PuntoVentaSalonMesaEstadoKeys.Abierta, NormalizeUser(usuarioAccion), token);
                await tx.CommitAsync(token);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo reabrir la mesa.", ct);

    public Task CambiarMesaAsync(int idPedido, int idMesaDestino, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("CambiarMesa", async token =>
        {
            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, token);
            try
            {
                await RequireAbiertaAsync(cn, tx, idPedido, token);

                var nombreDestino = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
                    SELECT m.NOMBRE FROM dbo.POS_MESA m
                    INNER JOIN dbo.POS_SECTOR s ON s.ID = m.IDSECTOR
                    WHERE m.ID = @IdMesa AND m.ACTIVO = 1
                      AND s.IDPUNTOVENTA = (SELECT IDPUNTOVENTA FROM dbo.POS_PEDIDO WHERE ID = @IdPedido);
                    """, new { IdMesa = idMesaDestino, IdPedido = idPedido }, tx, cancellationToken: token));
                if (nombreDestino is null)
                    throw new InvalidOperationException("La mesa destino no pertenece a este salón.");

                var ocupada = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT COUNT(*) FROM dbo.POS_PEDIDO WITH (UPDLOCK, HOLDLOCK)
                    WHERE IDMESA = @IdMesa AND MODALIDAD = 'SALON' AND ESTADO IN ('ABIERTA', 'CUENTA');
                    """, new { IdMesa = idMesaDestino }, tx, cancellationToken: token));
                if (ocupada > 0)
                    throw new InvalidOperationException("La mesa destino está ocupada. Para juntar las cuentas usá \"Unir mesas\".");

                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.POS_PEDIDO
                    SET IDMESA = @IdMesa,
                        NOMBRECLIENTE = @NombreCliente,
                        USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                    WHERE ID = @Id;
                    """, new
                {
                    Id = idPedido,
                    IdMesa = idMesaDestino,
                    NombreCliente = Truncate(NombrePedidoMesa(nombreDestino), 100),
                    Usuario = NormalizeUser(usuarioAccion)
                }, tx, cancellationToken: token));

                await tx.CommitAsync(token);
                await AuditAsync("CambiarMesa", idPedido, "Mesa cambiada.", new { idMesaDestino });
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo cambiar la mesa.", ct);

    public Task UnirMesasAsync(int idPedidoOrigen, int idPedidoDestino, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("UnirMesas", async token =>
        {
            if (idPedidoOrigen == idPedidoDestino)
                throw new InvalidOperationException("Elegí una mesa distinta para unir.");

            var usuario = NormalizeUser(usuarioAccion);
            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, token);
            try
            {
                await RequireAbiertaAsync(cn, tx, idPedidoOrigen, token);
                await RequireAbiertaAsync(cn, tx, idPedidoDestino, token);

                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.POS_PEDIDO_DET SET IDPEDIDO = @Destino WHERE IDPEDIDO = @Origen;

                    UPDATE d
                    SET CUBIERTOS = CASE WHEN ISNULL(d.CUBIERTOS, 0) + ISNULL(o.CUBIERTOS, 0) > 0
                                         THEN ISNULL(d.CUBIERTOS, 0) + ISNULL(o.CUBIERTOS, 0) END,
                        ESTADO = 'ABIERTA', USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                    FROM dbo.POS_PEDIDO d
                    CROSS JOIN dbo.POS_PEDIDO o
                    WHERE d.ID = @Destino AND o.ID = @Origen;

                    UPDATE dbo.POS_PEDIDO
                    SET ESTADO = 'ANULADO',
                        OBSERVACIONES = LEFT(ISNULL(OBSERVACIONES, '') + ' [Unida al pedido ' + CAST(@Destino AS nvarchar(12)) + ']', 500),
                        USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                    WHERE ID = @Origen;
                    """, new { Origen = idPedidoOrigen, Destino = idPedidoDestino, Usuario = usuario }, tx, cancellationToken: token));

                await tx.CommitAsync(token);
                await AuditAsync("UnirMesas", idPedidoDestino, $"Se unió el pedido {idPedidoOrigen}.", new { idPedidoOrigen, idPedidoDestino });
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudieron unir las mesas.", ct);

    public Task AnularMesaAsync(int idPedido, string motivo, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("AnularMesa", async token =>
        {
            var usuario = NormalizeUser(usuarioAccion);
            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                await RequireAbiertaAsync(cn, tx, idPedido, token);

                var enviadas = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT COUNT(*) FROM dbo.POS_PEDIDO_DET
                    WHERE IDPEDIDO = @Id AND ESTADO NOT IN ('PENDIENTE', 'ANULADO') AND ISNULL(DESTINO, '') <> '';
                    """, new { Id = idPedido }, tx, cancellationToken: token));
                var motivoNormalizado = Truncate((motivo ?? string.Empty).Trim(), 100);
                if (enviadas > 0 && motivoNormalizado.Length == 0)
                    throw new InvalidOperationException("La mesa tiene artículos enviados a preparar: indicá el motivo para liberarla sin cobrar.");

                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.POS_PEDIDO_DET
                    SET ESTADO = 'ANULADO', MOTIVOANULACION = ISNULL(MOTIVOANULACION, @Motivo)
                    WHERE IDPEDIDO = @Id AND ESTADO <> 'ANULADO';

                    UPDATE dbo.POS_PEDIDO
                    SET ESTADO = 'ANULADO',
                        OBSERVACIONES = LEFT(ISNULL(OBSERVACIONES, '') + CASE WHEN @Motivo = '' THEN '' ELSE ' [Anulada: ' + @Motivo + ']' END, 500),
                        USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                    WHERE ID = @Id;
                    """, new { Id = idPedido, Motivo = motivoNormalizado, Usuario = usuario }, tx, cancellationToken: token));

                await tx.CommitAsync(token);
                await AuditAsync("AnularMesa", idPedido, "Mesa liberada sin cobrar.", new { motivo = motivoNormalizado, enviadas });
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo liberar la mesa.", ct);

    public Task AtenderLlamadoAsync(int idLlamado, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("AtenderLlamado", async token =>
        {
            await using var cn = await OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.POS_MESA_LLAMADO
                SET ATENDIDO = 1, FECHAHORA_ATENCION = GETDATE(), USUARIO_ATENCION = @Usuario
                WHERE ID = @Id AND ATENDIDO = 0;
                """, new { Id = idLlamado, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
            return true;
        }, "No se pudo marcar el llamado como atendido.", ct);

    public Task GuardarPosicionesAsync(IReadOnlyList<PuntoVentaMesaDto> mesas, CancellationToken ct = default)
        => ExecuteLoggedAsync("GuardarPosiciones", async token =>
        {
            if (mesas.Count == 0)
                return true;

            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                foreach (var mesa in mesas)
                {
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.POS_MESA
                        SET POSX = @PosX, POSY = @PosY, WIDTH = @Width, HEIGHT = @Height, FECHAHORA_MODIFICACION = GETDATE()
                        WHERE ID = @Id;
                        """, new { mesa.Id, mesa.PosX, mesa.PosY, mesa.Width, mesa.Height }, tx, cancellationToken: token));
                }

                await tx.CommitAsync(token);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar el plano del salón.", ct);

    // ------------------------------------------------------------------ helpers

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    private static async Task<string> RequireAbiertaAsync(SqlConnection cn, SqlTransaction tx, int idPedido, CancellationToken ct)
    {
        var estado = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT ESTADO FROM dbo.POS_PEDIDO WITH (UPDLOCK)
            WHERE ID = @Id AND MODALIDAD = 'SALON';
            """, new { Id = idPedido }, tx, cancellationToken: ct));

        if (estado is null)
            throw new InvalidOperationException("No se encontró la mesa indicada.");
        if (!PuntoVentaSalonMesaEstadoKeys.Abiertos.Contains(estado.Trim()))
            throw new InvalidOperationException("La mesa ya fue cobrada o liberada. Actualizá el salón.");
        return estado.Trim();
    }

    private static Task SetEstadoMesaAsync(SqlConnection cn, SqlTransaction tx, int idPedido, string estado, string usuario, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.POS_PEDIDO SET ESTADO = @Estado, USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
            WHERE ID = @Id;
            """, new { Id = idPedido, Estado = estado, Usuario = usuario }, tx, cancellationToken: ct));

    private static PuntoVentaPedidoItemDto CrearLineaCubiertos(PuntoVentaArticleDto articulo, int cantidad) => new()
    {
        IdArticulo = articulo.IdArticulo,
        Descripcion = articulo.Descripcion,
        IdUnidad = articulo.IdUnidad,
        Presentacion = articulo.Presentacion,
        Cantidad = cantidad,
        Precio = articulo.PrecioUnitario,
        TasaIva = articulo.TasaIva,
        Exento = articulo.Exento,
        Familia = articulo.Familia,
        Observaciones = "Cubiertos"
    };

    private static Task InsertLineaAsync(SqlConnection cn, SqlTransaction tx, int idPedido, PuntoVentaPedidoItemDto item, string estado, string origen, string usuario, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.POS_PEDIDO_DET
                (IDPEDIDO, IDARTICULO, DESCRIPCION, IDUNIDAD, PRESENTACION, CANTIDAD, PRECIO, TASAIVA, EXENTO, FAMILIA, OBSERVACIONES,
                 ESTADO, ORIGEN, USUARIO, FECHAHORA_ALTA, FECHAHORA_ENVIO)
            VALUES
                (@IdPedido, @IdArticulo, @Descripcion, @IdUnidad, @Presentacion, @Cantidad, @Precio, @TasaIva, @Exento, @Familia, @Observaciones,
                 @Estado, @Origen, @Usuario, GETDATE(), CASE WHEN @Estado = 'PENDIENTE' THEN NULL ELSE GETDATE() END);
            """, new
        {
            IdPedido = idPedido,
            IdArticulo = item.IdArticulo.Trim(),
            Descripcion = Truncate(item.Descripcion ?? string.Empty, 200),
            IdUnidad = Truncate(item.IdUnidad ?? string.Empty, 4),
            Presentacion = Truncate(item.Presentacion ?? string.Empty, 50),
            item.Cantidad,
            item.Precio,
            item.TasaIva,
            item.Exento,
            Familia = Truncate(item.Familia ?? string.Empty, 100),
            Observaciones = Truncate(item.Observaciones ?? string.Empty, 250),
            Estado = estado,
            Origen = origen,
            Usuario = usuario
        }, tx, cancellationToken: ct));

    /// <summary>
    /// Destino de preparacion segun V_MA_ARTICULOS.TalleDefault, igual que el salon VB6:
    /// contiene 'C' = cocina, 'B' = barra, 'R' = barra 2. Sin columna o sin letra = no se prepara.
    /// </summary>
    private static async Task AsignarDestinosAsync(SqlConnection cn, SqlTransaction tx, int idPedido, CancellationToken ct)
    {
        var tieneColumna = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'TalleDefault') IS NULL THEN 0 ELSE 1 END;",
            transaction: tx, cancellationToken: ct));
        if (tieneColumna == 0)
            return;

        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE d
            SET DESTINO = CASE
                    WHEN UPPER(ISNULL(a.TalleDefault, '')) LIKE '%C%' THEN 'COCINA'
                    WHEN UPPER(ISNULL(a.TalleDefault, '')) LIKE '%B%' THEN 'BARRA'
                    WHEN UPPER(ISNULL(a.TalleDefault, '')) LIKE '%R%' THEN 'BARRA2'
                END
            FROM dbo.POS_PEDIDO_DET d
            INNER JOIN dbo.V_MA_ARTICULOS a ON LTRIM(RTRIM(a.IdArticulo)) = LTRIM(RTRIM(d.IDARTICULO))
            WHERE d.IDPEDIDO = @Id AND d.ESTADO = 'PENDIENTE' AND d.DESTINO IS NULL;
            """, new { Id = idPedido }, tx, cancellationToken: ct));
    }

    private static async Task<string> GetConfigAsync(SqlConnection cn, SqlTransaction? tx, string clave, CancellationToken ct)
    {
        var valor = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
                SELECT TOP (1) LTRIM(RTRIM(ISNULL(VALOR, ''))) FROM dbo.TA_CONFIGURACION WHERE CLAVE = @Clave;
            """, new { Clave = clave }, tx, cancellationToken: ct));
        return valor ?? string.Empty;
    }

    private static bool EsVerdadero(string valor)
        => valor.Trim().ToUpperInvariant() is "1" or "S" or "SI" or "SÍ" or "TRUE" or "VERDADERO";

    private static string GenerarPin()
        => RandomNumberGenerator.GetInt32(0, 10000).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"5" -> "Mesa 5"; "Mesa 5" o "Barra 2" quedan igual.</summary>
    private static string NombrePedidoMesa(string? nombreMesa)
    {
        var nombre = (nombreMesa ?? string.Empty).Trim();
        return nombre.Length > 0 && nombre.All(char.IsDigit) ? $"Mesa {nombre}" : nombre;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private async Task AuditAsync(string action, int idPedido, string message, object? data)
    {
        try
        {
            await appEvents.LogAuditAsync(ModuleName, action, "POS_PEDIDO", idPedido.ToString(System.Globalization.CultureInfo.InvariantCulture), message, data);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo auditar {Action} del pedido {IdPedido}.", action, idPedido);
        }
    }

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Operacion cancelada {Module}.{Action}", ModuleName, action);
            throw;
        }
        catch (InvalidOperationException)
        {
            // Incluye AppValidationException y AppUserFacingException.
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }
}
