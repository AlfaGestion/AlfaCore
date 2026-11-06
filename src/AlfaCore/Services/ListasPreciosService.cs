using System.Globalization;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IListasPreciosService
{
    Task<ListaPrecioContextoDto> GetContextoAsync(CancellationToken ct = default);
    Task<string> GetProximoCodigoAsync(CancellationToken ct = default);
    Task<ListaPrecioDto> GuardarListaAsync(ListaPrecioGuardarRequest request, CancellationToken ct = default);
    Task BorrarListaAsync(string idLista, CancellationToken ct = default);
    Task<ListaPrecioPaginaDto> BuscarAsync(ListaPrecioFiltro filtro, CancellationToken ct = default);
    Task<ListaPrecioFilaDto> GuardarFilaAsync(string idLista, ListaPrecioFilaDto fila, CancellationToken ct = default);
    Task<ListaPrecioOperacionResultado> QuitarAsync(string idLista, IReadOnlyList<int> ids, CancellationToken ct = default);
    Task<ListaPrecioOperacionResultado> AgregarAsync(ListaPrecioAgregarRequest request, CancellationToken ct = default);
    Task<ListaPrecioOperacionResultado> AumentarAsync(ListaPrecioAumentoRequest request, CancellationToken ct = default);
    Task<ListaPrecioOperacionResultado> RedondearAsync(ListaPrecioRedondeoRequest request, CancellationToken ct = default);
}

/// <summary>
/// Listas de precios de ventas, como el legacy V_MA_PRECIOS.exe (V) y V_MA_AUMENTOGLOBAL.exe.
/// Graba en V_MA_PRECIOSCAB / V_MA_PRECIOS igual que el legacy (DesdeTrigger=0 y USUARIO), para que los triggers
/// FMT_V_MA_PRECIOS / FMT_V_MA_PRECIOS_DEL registren el histórico y redondeen con DecimalesListaV{IdLista}.
/// Nunca actualiza V_MA_ARTICULOS (TR_BlockMassUpdate_Precios bloquea más de 50 filas).
/// </summary>
public sealed class ListasPreciosService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events) : IListasPreciosService
{
    /// <summary>Clave legacy "Listas de Precios (Ventas)" (Shell:V_MA_PRECIOS @PARAMETROADICIONAL=V).</summary>
    public const string MenuKey = "D010504";
    private const int TimeoutMasivo = 600;

    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public Task<ListaPrecioContextoDto> GetContextoAsync(CancellationToken ct = default)
        => EjecutarAsync("Contexto", async cn =>
        {
            var cfg = await CargaComprobantesConfigPrecios.LeerAsync(cn, ct);
            var piva = await LeerConfigAsync(cn, null, "PIVA", ct);
            static async Task<List<ArticuloLookupOptionDto>> Opciones(SqlConnection c, string sql, CancellationToken t)
                => (await c.QueryAsync<ArticuloLookupOptionDto>(new CommandDefinition(sql, cancellationToken: t))).ToList();
            return new ListaPrecioContextoDto
            {
                NombresClases = Enumerable.Range(1, 8).Select(cfg.NombreClase).ToList(),
                ClaseVenta = cfg.ClaseVenta, ClaseOferta = cfg.ClaseOferta, Retail = cfg.Retail,
                OfertaPorDescuento = cfg.OfertaPorDescuento, OfertaPrecioUnitario = cfg.OfertaPrecioUnitario,
                TasaIvaDefault = decimal.TryParse(piva, NumberStyles.Number, CultureInfo.InvariantCulture, out var t) && t > 0 ? t : 21,
                PuedeEditar = true,
                Rubros = await Opciones(cn, "SELECT LTRIM(RTRIM(IdRubro)) Codigo, ISNULL(Descripcion,'') Descripcion FROM dbo.v_ta_rubros ORDER BY Descripcion;", ct),
                Marcas = await Opciones(cn, "SELECT LTRIM(RTRIM(IdTipo)) Codigo, ISNULL(Descripcion,'') Descripcion FROM dbo.v_ta_tipoArticulo ORDER BY Descripcion;", ct),
                Familias = await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.V_TA_FAMILIAS') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 1
                    ? await Opciones(cn, "SELECT LTRIM(RTRIM(IdFamilia)) Codigo, ISNULL(Descripcion,'') Descripcion FROM dbo.V_TA_FAMILIAS ORDER BY IdFamilia;", ct) : [],
                Listas = (await LeerListasAsync(cn, null, null, ct)).ToList()
            };
        }, ct);

    public Task<string> GetProximoCodigoAsync(CancellationToken ct = default)
        => EjecutarAsync("ProximoCodigo", async cn => await ProximoCodigoAsync(cn, null, ct), ct);

    private static async Task<string> ProximoCodigoAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
    {
        // Como el legacy (BuscaMaximo + 1), con el formato de las listas de ventas: 4 dígitos con ceros.
        var maximo = await cn.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT MAX(CASE WHEN LTRIM(RTRIM(IdLista)) NOT LIKE '%[^0-9]%' AND LTRIM(RTRIM(IdLista)) <> ''
                            THEN CONVERT(int, LTRIM(RTRIM(IdLista))) END)
            FROM dbo.V_MA_PRECIOSCAB WHERE TipoLista = 'V';
            """, transaction: tx, cancellationToken: ct)) ?? 0;
        return (maximo + 1).ToString("0000", CultureInfo.InvariantCulture);
    }

    public Task<ListaPrecioDto> GuardarListaAsync(ListaPrecioGuardarRequest request, CancellationToken ct = default)
        => EjecutarAsync("GuardarLista", async cn =>
        {
            var nombre = (request.Nombre ?? "").Trim();
            if (nombre.Length == 0) throw new ReglaException("Ingresá el nombre de la lista.");
            if (nombre.Length > 50) throw new ReglaException("El nombre de la lista admite hasta 50 caracteres.");
            if (request.Decimales is < 0 or > 4) throw new ReglaException("Los decimales de redondeo van de 0 a 4.");
            if (request.VigenciaDesde is { } d && request.VigenciaHasta is { } h && h.Date < d.Date)
                throw new ReglaException("La vigencia hasta no puede ser anterior a la vigencia desde.");
            var usuario = Usuario();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var esAlta = string.IsNullOrWhiteSpace(request.IdListaOriginal);
            string idLista;
            if (esAlta)
            {
                var pedido = (request.IdLista ?? "").Trim();
                idLista = pedido.Length == 0 ? await ProximoCodigoAsync(cn, tx, ct)
                    : pedido.All(char.IsDigit) && pedido.Length <= 4 ? pedido.PadLeft(4, '0') : pedido;
                if (idLista.Length > 4) throw new ReglaException("El código de la lista admite hasta 4 caracteres.");
                // Como el legacy: el mismo código no puede ser lista de ventas y de compras a la vez.
                var existe = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
                    SELECT TOP (1) TipoLista FROM dbo.V_MA_PRECIOSCAB WITH (UPDLOCK, HOLDLOCK) WHERE LTRIM(RTRIM(IdLista)) = LTRIM(RTRIM(@IdLista));
                    """, new { IdLista = idLista }, tx, cancellationToken: ct));
                if (existe is not null)
                    throw new ReglaException(existe.Trim() == "V" ? $"Ya existe la lista {idLista}." : $"El código {idLista} ya es una lista de compras.");
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.V_MA_PRECIOSCAB (IdLista, Nombre, TipoLista, VigenciaDesde, VigenciaHasta) VALUES (@IdLista, @Nombre, 'V', @Desde, @Hasta);
                    """, new { IdLista = idLista, Nombre = nombre, Desde = request.VigenciaDesde?.Date, Hasta = request.VigenciaHasta?.Date }, tx, cancellationToken: ct));
            }
            else
            {
                idLista = await IdListaExistenteAsync(cn, tx, request.IdListaOriginal, ct);
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.V_MA_PRECIOSCAB SET Nombre = @Nombre, VigenciaDesde = @Desde, VigenciaHasta = @Hasta
                    WHERE IdLista = @IdLista AND TipoLista = 'V';
                    """, new { IdLista = idLista, Nombre = nombre, Desde = request.VigenciaDesde?.Date, Hasta = request.VigenciaHasta?.Date }, tx, cancellationToken: ct));
                // Nombre y con/sin IVA se repiten en cada fila (legacy). DesdeTrigger=1: no son cambios de precio,
                // no pasan por el histórico ni el redondeo.
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.V_MA_PRECIOS SET Nombre = @Nombre, ConIVA = @ConIva, DesdeTrigger = 1
                    WHERE IdLista = @IdLista AND TipoLista = 'V' AND (Nombre <> @Nombre OR ConIVA <> @ConIva);
                    """, new { IdLista = idLista, Nombre = nombre, request.ConIva }, tx, commandTimeout: TimeoutMasivo, cancellationToken: ct));
            }
            await GrabarConfigAsync(cn, tx, $"DecimalesListaV{idLista}", request.Decimales.ToString(CultureInfo.InvariantCulture),
                $"Decimales de redondeo de la lista de ventas {idLista}", ct);
            await GrabarConfigAsync(cn, tx, $"ConIvaListaV{idLista}", request.ConIva ? "SI" : "NO",
                $"Precios con IVA de la lista de ventas {idLista} (lista sin artículos)", ct);
            if (esAlta && !string.IsNullOrWhiteSpace(request.CopiarDeLista))
            {
                var origen = await IdListaExistenteAsync(cn, tx, request.CopiarDeLista, ct);
                await CopiarDeListaAsync(cn, tx, idLista, nombre, request.ConIva, origen, usuario, ct);
            }
            await tx.CommitAsync(ct);
            await events.LogAuditAsync("ListasPrecios", esAlta ? "AltaLista" : "ModificaLista", "V_MA_PRECIOSCAB", idLista,
                $"{(esAlta ? "Alta" : "Modificación")} de la lista de ventas {idLista} {nombre}", new { request.ConIva, request.Decimales, request.CopiarDeLista }, CancellationToken.None);
            return (await LeerListasAsync(cn, null, idLista, ct)).First();
        }, ct);

    public Task BorrarListaAsync(string idLista, CancellationToken ct = default)
        => EjecutarAsync("BorrarLista", async cn =>
        {
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var id = await IdListaExistenteAsync(cn, tx, idLista, ct);
            var clientes = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.Vt_Clientes WHERE LTRIM(RTRIM(ISNULL(IdLista,''))) = LTRIM(RTRIM(@IdLista));",
                new { IdLista = id }, tx, cancellationToken: ct));
            if (clientes > 0)
                throw new ReglaException($"La lista {id} la tienen asignada {clientes} cliente(s). Cambiales la lista antes de borrarla.");
            var usuario = Usuario();
            // USUARIO antes del DELETE: FMT_V_MA_PRECIOS_DEL lo registra en el histórico ("BAJA").
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.V_MA_PRECIOS SET USUARIO = @Usuario, DesdeTrigger = 1 WHERE IdLista = @IdLista AND TipoLista = 'V';
                DELETE FROM dbo.V_MA_PRECIOS WHERE IdLista = @IdLista AND TipoLista = 'V';
                DELETE FROM dbo.V_MA_PRECIOSCAB WHERE IdLista = @IdLista AND TipoLista = 'V';
                DELETE FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (UPPER('DecimalesListaV' + @IdLista), UPPER('ConIvaListaV' + @IdLista));
                """, new { IdLista = id, Usuario = usuario }, tx, commandTimeout: TimeoutMasivo, cancellationToken: ct));
            await tx.CommitAsync(ct);
            await events.LogAuditAsync("ListasPrecios", "BajaLista", "V_MA_PRECIOSCAB", id, $"Baja de la lista de ventas {id}", null, CancellationToken.None);
            return true;
        }, ct);

    public Task<ListaPrecioPaginaDto> BuscarAsync(ListaPrecioFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Buscar", async cn =>
        {
            var id = await IdListaExistenteAsync(cn, null, filtro.IdLista, ct);
            var lista = (await LeerListasAsync(cn, null, id, ct)).First();
            var p = new DynamicParameters();
            var where = FiltroSql(filtro, id, p);
            var pageSize = Math.Clamp(filtro.PageSize, 10, 500);
            var page = Math.Max(1, filtro.PageNumber);
            p.Add("Desde_", (page - 1) * pageSize + 1);
            p.Add("Hasta_", page * pageSize);
            var filas = (await cn.QueryAsync<ListaPrecioFilaTotal>(new CommandDefinition($"""
                WITH q AS (
                    SELECT p.ID Id, LTRIM(RTRIM(p.IdArticulo)) IdArticulo, ISNULL(a.DESCRIPCION, p.DESCRIPCIONARTICULO) Descripcion,
                        ISNULL(LTRIM(RTRIM(a.CODIGOBARRA)),'') CodigoBarra, ISNULL(r.Descripcion,'') Rubro, ISNULL(t.Descripcion,'') Marca,
                        CONVERT(decimal(9,4), CASE WHEN ISNULL(a.EXENTO,0)=1 THEN 0 WHEN ISNULL(a.TasaIVA,0)=0 THEN @Piva ELSE a.TasaIVA END) TasaIva,
                        CONVERT(bit, ISNULL(a.EXENTO,0)) Exento,
                        CONVERT(bit, CASE WHEN ISNULL(a.SUSPENDIDO,0)=1 OR ISNULL(a.SuspendidoV,0)=1 THEN 1 ELSE 0 END) Suspendido,
                        p.ConIVA ConIva, CONVERT(decimal(19,4), ISNULL(a.COSTO, p.COSTO)) Costo,
                        CONVERT(decimal(19,4),p.Precio1) Precio1, CONVERT(decimal(19,4),p.Precio2) Precio2, CONVERT(decimal(19,4),p.Precio3) Precio3,
                        CONVERT(decimal(19,4),p.Precio4) Precio4, CONVERT(decimal(19,4),p.Precio5) Precio5, CONVERT(decimal(19,4),ISNULL(p.PRECIO6,0)) Precio6,
                        CONVERT(decimal(19,4),ISNULL(p.PRECIO7,0)) Precio7, CONVERT(decimal(19,4),ISNULL(p.PRECIO8,0)) Precio8,
                        CONVERT(decimal(19,4),p.PRECIO9) Precio9, CONVERT(decimal(19,4),p.PRECIO10) Precio10,
                        CONVERT(decimal(19,4),p.CantidadOf2) CantidadOf2, CONVERT(decimal(19,4),p.CantidadOf3) CantidadOf3,
                        p.FhOfertaDesde OfertaDesde, p.FhOfertaHasta OfertaHasta, ISNULL(p.ModificaPrecios,'') ClasesDelMaestro,
                        ROW_NUMBER() OVER (ORDER BY ISNULL(a.DESCRIPCION, p.DESCRIPCIONARTICULO), p.IdArticulo) Fila,
                        COUNT(*) OVER () Total
                    FROM dbo.V_MA_PRECIOS p
                    LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = p.IdArticulo
                    LEFT JOIN dbo.v_ta_rubros r ON LTRIM(RTRIM(r.IdRubro)) = LTRIM(RTRIM(a.IDRUBRO))
                    LEFT JOIN dbo.v_ta_tipoArticulo t ON LTRIM(RTRIM(t.IdTipo)) = LTRIM(RTRIM(a.IDTIPO))
                    WHERE {where}
                )
                SELECT * FROM q WHERE Fila BETWEEN @Desde_ AND @Hasta_ ORDER BY Fila;
                """, Con(p, "Piva", await TasaGeneralAsync(cn, null, ct)), commandTimeout: 60, cancellationToken: ct))).ToList();
            return new ListaPrecioPaginaDto
            {
                Lista = lista, Filas = filas.Select(x => (ListaPrecioFilaDto)x).ToList(),
                Total = filas.FirstOrDefault()?.Total ?? 0, PageNumber = page, PageSize = pageSize
            };
        }, ct);

    public Task<ListaPrecioFilaDto> GuardarFilaAsync(string idLista, ListaPrecioFilaDto fila, CancellationToken ct = default)
        => EjecutarAsync("GuardarFila", async cn =>
        {
            ValidarPrecios(fila.IdArticulo, Enumerable.Range(1, 10).Select(fila.Precio), fila.OfertaDesde, fila.OfertaHasta,
                fila.CantidadOf2, fila.Precio9, fila.CantidadOf3, fila.Precio10);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var id = await IdListaExistenteAsync(cn, tx, idLista, ct);
            var n = await cn.ExecuteScalarAsync<int>(new CommandDefinition(ActualizarPreciosSql("p.ID = @Id"), ParametrosPrecios(PreciosFila.De(fila), Usuario(), id, fila.Id),
                tx, cancellationToken: ct));
            if (n == 0) throw new ReglaException($"El artículo {fila.IdArticulo} ya no está en la lista {id}.");
            await tx.CommitAsync(ct);
            // Se relee: el trigger redondea a los decimales de la lista.
            var p = new DynamicParameters();
            p.Add("IdLista", id); p.Add("IdFila", fila.Id);
            var leidas = await LeerFilasAsync(cn, "p.IdLista = @IdLista AND p.TipoLista = 'V' AND p.ID = @IdFila", p, ct);
            return leidas.FirstOrDefault() ?? fila;
        }, ct);

    public Task<ListaPrecioOperacionResultado> QuitarAsync(string idLista, IReadOnlyList<int> ids, CancellationToken ct = default)
        => EjecutarAsync("Quitar", async cn =>
        {
            if (ids.Count == 0) throw new ReglaException("Elegí los artículos a quitar de la lista.");
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var id = await IdListaExistenteAsync(cn, tx, idLista, ct);
            var quitados = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                SET NOCOUNT ON;
                UPDATE dbo.V_MA_PRECIOS SET USUARIO = @Usuario, DesdeTrigger = 1 WHERE IdLista = @IdLista AND TipoLista = 'V' AND ID IN @Ids;
                DELETE FROM dbo.V_MA_PRECIOS WHERE IdLista = @IdLista AND TipoLista = 'V' AND ID IN @Ids;
                SELECT @@ROWCOUNT;
                """, new { IdLista = id, Ids = ids.ToArray(), Usuario = Usuario() }, tx, commandTimeout: TimeoutMasivo, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return new ListaPrecioOperacionResultado { Filas = quitados, Mensaje = $"Se quitaron {quitados} artículo(s) de la lista {id}." };
        }, ct);

    public Task<ListaPrecioOperacionResultado> AgregarAsync(ListaPrecioAgregarRequest request, CancellationToken ct = default)
        => EjecutarAsync("Agregar", async cn =>
        {
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var id = await IdListaExistenteAsync(cn, tx, request.IdLista, ct);
            var lista = (await LeerListasAsync(cn, tx, id, ct)).First();
            var valor = (request.Valor ?? "").Trim();
            if (request.Modo != ListaPrecioAgregarModo.Todos && valor.Length == 0)
                throw new ReglaException("Elegí qué artículos agregar.");
            var usuario = Usuario();
            int agregados;
            if (request.Modo == ListaPrecioAgregarModo.OtraLista)
            {
                var origen = await IdListaExistenteAsync(cn, tx, valor, ct);
                if (origen == id) throw new ReglaException("Elegí otra lista para copiar.");
                agregados = await CopiarDeListaAsync(cn, tx, id, lista.Nombre, lista.ConIva, origen, usuario, ct);
            }
            else
            {
                var p = new DynamicParameters();
                var criterio = request.Modo switch
                {
                    ListaPrecioAgregarModo.Todos => "1=1",
                    ListaPrecioAgregarModo.Rubro => "LTRIM(RTRIM(ISNULL(a.IDRUBRO,''))) = @Valor",
                    ListaPrecioAgregarModo.Marca => "LTRIM(RTRIM(ISNULL(a.IDTIPO,''))) = @Valor",
                    ListaPrecioAgregarModo.Proveedor => "LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR,''))) = @Valor",
                    ListaPrecioAgregarModo.Familia => "LTRIM(RTRIM(ISNULL(a.IdFamilia,''))) LIKE @ValorLike ESCAPE '\\'",
                    ListaPrecioAgregarModo.Articulos => "LTRIM(RTRIM(a.IDARTICULO)) IN @Codigos",
                    _ => throw new ReglaException("Modo de carga desconocido.")
                };
                p.Add("Valor", valor);
                p.Add("ValorLike", Escapar(valor) + "%");
                if (request.Modo == ListaPrecioAgregarModo.Articulos)
                    p.Add("Codigos", valor.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                p.Add("IdLista", id); p.Add("Nombre", lista.Nombre); p.Add("ConIva", lista.ConIva);
                p.Add("Mp", await ClasesDelMaestroAsync(cn, tx, id, ct)); p.Add("Usuario", usuario);
                p.Add("Suspendidos", request.IncluirSuspendidos);
                // Precios del maestro, como "Insertar" del legacy (Ma_art / V_MA_Precios.frm). Los que ya están no se tocan.
                agregados = await cn.ExecuteScalarAsync<int>(new CommandDefinition($"""
                    SET NOCOUNT ON;
                    INSERT INTO dbo.V_MA_PRECIOS (IdLista, Nombre, IdArticulo, ConIVA, TipoLista, Precio1, Precio2, Precio3, Precio4, Precio5,
                        PRECIO6, PRECIO7, PRECIO8, COSTO, ModificaPrecios, DESCRIPCIONARTICULO, RUBRO, TIPO, IdUnidad, CUENTAPROVEEDOR,
                        UTILIDAD, ActualizaBase, DesdeTrigger, USUARIO, FHALTA)
                    SELECT @IdLista, @Nombre, a.IDARTICULO, @ConIva, 'V', ISNULL(a.PRECIO1,0), ISNULL(a.PRECIO2,0), ISNULL(a.PRECIO3,0),
                        ISNULL(a.PRECIO4,0), ISNULL(a.PRECIO5,0), ISNULL(a.PRECIO6,0), ISNULL(a.PRECIO7,0), ISNULL(a.PRECIO8,0), ISNULL(a.COSTO,0),
                        @Mp, LEFT(a.DESCRIPCION,100), LEFT(a.IDRUBRO,4), LEFT(a.IDTIPO,4), LEFT(a.IDUNIDAD,4), LEFT(a.CUENTAPROVEEDOR,15),
                        ISNULL(a.UTILIDAD,0), 0, 0, @Usuario, GETDATE()
                    FROM dbo.V_MA_ARTICULOS a
                    WHERE {criterio}
                      AND (@Suspendidos = 1 OR (ISNULL(a.SUSPENDIDO,0) = 0 AND ISNULL(a.SuspendidoV,0) = 0))
                      AND ISNULL(a.EnComodato,0) = 0
                      AND NOT EXISTS (SELECT 1 FROM dbo.V_MA_PRECIOS x WHERE x.IdLista = @IdLista AND x.TipoLista = 'V' AND x.IdArticulo = a.IDARTICULO);
                    SELECT @@ROWCOUNT;
                    """, p, tx, commandTimeout: TimeoutMasivo, cancellationToken: ct));
            }
            await tx.CommitAsync(ct);
            await events.LogAuditAsync("ListasPrecios", "AgregarArticulos", "V_MA_PRECIOS", id,
                $"Se agregaron {agregados} artículo(s) a la lista {id} ({request.Modo} {valor})", null, CancellationToken.None);
            return new ListaPrecioOperacionResultado
            {
                Filas = agregados,
                Mensaje = agregados == 0 ? "No había artículos nuevos para agregar (los que ya están en la lista no se tocan)."
                    : $"Se agregaron {agregados} artículo(s) a la lista {id}" + (request.Modo == ListaPrecioAgregarModo.OtraLista
                        ? " con los precios de la otra lista." : " con los precios del maestro de artículos.")
            };
        }, ct);

    public Task<ListaPrecioOperacionResultado> AumentarAsync(ListaPrecioAumentoRequest request, CancellationToken ct = default)
        => EjecutarAsync("Aumentar", async cn =>
        {
            if (request.Porcentaje == 0) throw new ReglaException("Ingresá el porcentaje (negativo para bajar precios).");
            if (request.Porcentaje <= -100) throw new ReglaException("El porcentaje tiene que ser mayor a -100.");
            if (request.Origen is < 0 or > 8) throw new ReglaException("Elegí el precio de origen.");
            var destinos = request.Destinos.Distinct().Where(x => x is >= 1 and <= 8).OrderBy(x => x).ToList();
            if (destinos.Count == 0) throw new ReglaException("Elegí al menos una clase de destino.");
            if (request.RedondearA < 0) throw new ReglaException("El redondeo no puede ser negativo.");
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var id = await IdListaExistenteAsync(cn, tx, request.Filtro.IdLista, ct);
            var mp = await ClasesDelMaestroAsync(cn, tx, id, ct);
            var bloqueadas = destinos.Where(d => mp.Contains(d.ToString(CultureInfo.InvariantCulture))).ToList();
            destinos = destinos.Except(bloqueadas).ToList();
            if (destinos.Count == 0)
                throw new ReglaException("Las clases elegidas las copia la lista del maestro de artículos: cambialas en el maestro.");
            var p = new DynamicParameters();
            var where = FiltroSql(request.Filtro, id, p);
            p.Add("Factor", 1 + request.Porcentaje / 100m);
            p.Add("Redondeo", request.RedondearA);
            p.Add("Usuario", Usuario());
            p.Add("Piva", await TasaGeneralAsync(cn, tx, ct));
            // Desde el costo del maestro: si la lista es con IVA se le suma el IVA del artículo (V_MA_AUMENTOGLOBAL, CalculaAumento).
            var origen = request.Origen == 0
                ? "ISNULL(a.COSTO,0) * CASE WHEN p.ConIVA = 1 AND ISNULL(a.EXENTO,0) = 0 THEN 1 + (CASE WHEN ISNULL(a.TasaIVA,0)=0 THEN @Piva ELSE a.TasaIVA END) / 100.0 ELSE 1 END"
                : "p." + Columna(request.Origen);
            string Nuevo() => $"CASE WHEN @Redondeo > 0 THEN CEILING(ROUND({origen} * @Factor, 4) / @Redondeo) * @Redondeo ELSE {origen} * @Factor END";
            var sets = string.Join(", ", destinos.Select(d => $"p.{Columna(d)} = {Nuevo()}"));
            var n = await cn.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SET NOCOUNT ON;
                UPDATE p SET {sets}, p.DesdeTrigger = 0, p.USUARIO = @Usuario
                FROM dbo.V_MA_PRECIOS p
                LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = p.IdArticulo
                WHERE {where} AND ({origen}) > 0;
                SELECT @@ROWCOUNT;
                """, p, tx, commandTimeout: TimeoutMasivo, cancellationToken: ct));
            await tx.CommitAsync(ct);
            var desc = $"{(request.Porcentaje > 0 ? "+" : "")}{request.Porcentaje.ToString("0.##", CultureInfo.GetCultureInfo("es-AR"))}%";
            await events.LogAuditAsync("ListasPrecios", "Aumento", "V_MA_PRECIOS", id,
                $"Aumento {desc} en la lista {id}: {n} artículo(s), origen {(request.Origen == 0 ? "costo" : $"clase {request.Origen}")}, destino {string.Join(",", destinos)}",
                new { request.Filtro, request.RedondearA }, CancellationToken.None);
            return new ListaPrecioOperacionResultado
            {
                Filas = n,
                Mensaje = $"Se actualizaron {n} artículo(s) ({desc})."
                    + (bloqueadas.Count > 0 ? $" Las clases {string.Join(", ", bloqueadas)} no se tocaron: la lista las copia del maestro." : "")
            };
        }, ct);

    public Task<ListaPrecioOperacionResultado> RedondearAsync(ListaPrecioRedondeoRequest request, CancellationToken ct = default)
        => EjecutarAsync("Redondear", async cn =>
        {
            if (request.Multiplo <= 0) throw new ReglaException("Ingresá a qué múltiplo redondear (ej. 1, 10, 0,50).");
            var clases = request.Clases.Distinct().Where(x => x is >= 1 and <= 8).OrderBy(x => x).ToList();
            if (clases.Count == 0) throw new ReglaException("Elegí al menos una clase a redondear.");
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var id = await IdListaExistenteAsync(cn, tx, request.Filtro.IdLista, ct);
            var mp = await ClasesDelMaestroAsync(cn, tx, id, ct);
            var bloqueadas = clases.Where(d => mp.Contains(d.ToString(CultureInfo.InvariantCulture))).ToList();
            clases = clases.Except(bloqueadas).ToList();
            if (clases.Count == 0)
                throw new ReglaException("Las clases elegidas las copia la lista del maestro de artículos: cambialas en el maestro.");
            var p = new DynamicParameters();
            var where = FiltroSql(request.Filtro, id, p);
            p.Add("Multiplo", request.Multiplo);
            p.Add("Usuario", Usuario());
            var sets = string.Join(", ", clases.Select(c => $"p.{Columna(c)} = CASE WHEN p.{Columna(c)} > 0 THEN CEILING(ROUND(p.{Columna(c)}, 4) / @Multiplo) * @Multiplo ELSE p.{Columna(c)} END"));
            var cambia = string.Join(" OR ", clases.Select(c => $"(p.{Columna(c)} > 0 AND CEILING(ROUND(p.{Columna(c)}, 4) / @Multiplo) * @Multiplo <> p.{Columna(c)})"));
            var n = await cn.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SET NOCOUNT ON;
                UPDATE p SET {sets}, p.DesdeTrigger = 0, p.USUARIO = @Usuario
                FROM dbo.V_MA_PRECIOS p
                LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = p.IdArticulo
                WHERE {where} AND ({cambia});
                SELECT @@ROWCOUNT;
                """, p, tx, commandTimeout: TimeoutMasivo, cancellationToken: ct));
            await tx.CommitAsync(ct);
            await events.LogAuditAsync("ListasPrecios", "Redondeo", "V_MA_PRECIOS", id,
                $"Redondeo a {request.Multiplo} en la lista {id}: {n} artículo(s), clases {string.Join(",", clases)}", new { request.Filtro }, CancellationToken.None);
            return new ListaPrecioOperacionResultado
            {
                Filas = n,
                Mensaje = $"Se redondearon {n} artículo(s)."
                    + (bloqueadas.Count > 0 ? $" Las clases {string.Join(", ", bloqueadas)} no se tocaron: la lista las copia del maestro." : "")
            };
        }, ct);

    // ---------------- Lectura y escritura compartidas (también las usa el maestro de artículos) ----------------

    internal static async Task<IReadOnlyList<ListaPrecioDto>> LeerListasAsync(SqlConnection cn, SqlTransaction? tx, string? idLista, CancellationToken ct)
    {
        var listas = (await cn.QueryAsync<ListaPrecioDto>(new CommandDefinition("""
            SELECT c.IdLista, LTRIM(RTRIM(c.Nombre)) Nombre, c.VigenciaDesde, c.VigenciaHasta,
                ISNULL(s.Articulos,0) Articulos,
                CONVERT(bit, CASE WHEN ISNULL(s.Articulos,0) = 0 THEN CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(ci.VALOR,'')))) IN ('SI','S','1') THEN 1 ELSE 0 END
                                  WHEN s.ConIva * 2 >= s.Articulos THEN 1 ELSE 0 END) ConIva,
                CONVERT(bit, CASE WHEN s.ConIva > 0 AND s.ConIva < s.Articulos THEN 1 ELSE 0 END) ConIvaMezclado,
                ISNULL(s.Mp,'') ClasesDelMaestro,
                CASE WHEN ISNUMERIC(dl.VALOR) = 1 THEN CONVERT(int, CONVERT(float, dl.VALOR)) ELSE 2 END Decimales,
                (SELECT COUNT(*) FROM dbo.Vt_Clientes cl WHERE LTRIM(RTRIM(ISNULL(cl.IdLista,''))) = LTRIM(RTRIM(c.IdLista))) Clientes
            FROM dbo.V_MA_PRECIOSCAB c
            OUTER APPLY (SELECT COUNT(*) Articulos, SUM(CONVERT(int, p.ConIVA)) ConIva, MAX(ISNULL(p.ModificaPrecios,'')) Mp
                         FROM dbo.V_MA_PRECIOS p WHERE p.IdLista = c.IdLista AND p.TipoLista = 'V') s
            OUTER APPLY (SELECT TOP (1) CONVERT(nvarchar(20), VALOR) VALOR FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER('DecimalesListaV' + c.IdLista)) dl
            OUTER APPLY (SELECT TOP (1) CONVERT(nvarchar(20), VALOR) VALOR FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER('ConIvaListaV' + c.IdLista)) ci
            WHERE c.TipoLista = 'V' AND (@IdLista IS NULL OR c.IdLista = @IdLista)
            ORDER BY c.IdLista;
            """, new { IdLista = idLista }, tx, commandTimeout: 60, cancellationToken: ct))).ToList();
        foreach (var l in listas) l.Decimales = Math.Clamp(l.Decimales, 0, 4);
        return listas;
    }

    /// <summary>Listas de ventas del artículo para la solapa "Listas" del maestro: todas, tildadas las que lo tienen.</summary>
    internal static async Task<List<ArticuloListaPrecioDto>> LeerListasArticuloAsync(SqlConnection cn, string idArticulo, CancellationToken ct)
    {
        var listas = await LeerListasAsync(cn, null, null, ct);
        var filas = (await cn.QueryAsync<ArticuloListaPrecioDto>(new CommandDefinition("""
            SELECT p.IdLista, p.ConIVA ConIva, ISNULL(p.ModificaPrecios,'') ClasesDelMaestro,
                CONVERT(decimal(19,4),p.Precio1) Precio1, CONVERT(decimal(19,4),p.Precio2) Precio2, CONVERT(decimal(19,4),p.Precio3) Precio3,
                CONVERT(decimal(19,4),p.Precio4) Precio4, CONVERT(decimal(19,4),p.Precio5) Precio5, CONVERT(decimal(19,4),ISNULL(p.PRECIO6,0)) Precio6,
                CONVERT(decimal(19,4),ISNULL(p.PRECIO7,0)) Precio7, CONVERT(decimal(19,4),ISNULL(p.PRECIO8,0)) Precio8,
                CONVERT(decimal(19,4),p.PRECIO9) Precio9, CONVERT(decimal(19,4),p.PRECIO10) Precio10,
                CONVERT(decimal(19,4),p.CantidadOf2) CantidadOf2, CONVERT(decimal(19,4),p.CantidadOf3) CantidadOf3,
                p.FhOfertaDesde OfertaDesde, p.FhOfertaHasta OfertaHasta
            FROM dbo.V_MA_PRECIOS p
            WHERE p.TipoLista = 'V' AND LTRIM(RTRIM(p.IdArticulo)) = @IdArticulo;
            """, new { IdArticulo = idArticulo.Trim() }, cancellationToken: ct)))
            .GroupBy(x => x.IdLista).ToDictionary(x => x.Key, x => x.First());
        return listas.Select(l =>
        {
            if (filas.TryGetValue(l.IdLista, out var f))
            {
                f.Nombre = l.Nombre; f.Incluido = true; f.Existia = true;
                return f;
            }
            return new ArticuloListaPrecioDto { IdLista = l.IdLista, Nombre = l.Nombre, ConIva = l.ConIva, ClasesDelMaestro = l.ClasesDelMaestro };
        }).ToList();
    }

    /// <summary>
    /// Graba la solapa "Listas" del artículo dentro de la transacción del artículo (Ma_art.frm GrabaLP / Agregar_a_ListasVentas):
    /// alta en las listas tildadas, precios en las que ya estaba y baja en las destildadas (con histórico).
    /// incluirEnTodas (alta del artículo): agrega en todas las listas de ventas las que no vinieron en <paramref name="listas"/>.
    /// </summary>
    internal static async Task GuardarListasArticuloAsync(SqlConnection cn, SqlTransaction tx, string idArticulo, ArticuloSaveRequest articulo,
        IReadOnlyList<ArticuloListaPrecioDto>? listas, bool incluirEnTodas, string usuario, CancellationToken ct)
    {
        var existentes = await LeerListasAsync(cn, tx, null, ct);
        var porId = (listas ?? []).GroupBy(x => x.IdLista).ToDictionary(x => x.Key, x => x.First());
        if (incluirEnTodas)
            foreach (var l in existentes.Where(l => !porId.ContainsKey(l.IdLista)))
                porId[l.IdLista] = DesdeMaestro(l, articulo);
        if (porId.Count == 0) return;
        var actuales = (await cn.QueryAsync<(int Id, string IdLista)>(new CommandDefinition(
            "SELECT ID, IdLista FROM dbo.V_MA_PRECIOS WHERE TipoLista = 'V' AND IdArticulo = @IdArticulo;",
            new { IdArticulo = idArticulo }, tx, cancellationToken: ct))).GroupBy(x => x.IdLista).ToDictionary(x => x.Key, x => x.First().Id);
        foreach (var (id, item) in porId)
        {
            var lista = existentes.FirstOrDefault(x => x.IdLista == id)
                ?? throw new InvalidOperationException($"La lista de precios {id.Trim()} ya no existe.");
            var esta = actuales.TryGetValue(id, out var idFila);
            if (!item.Incluido)
            {
                if (!esta) continue;
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.V_MA_PRECIOS SET USUARIO = @Usuario, DesdeTrigger = 1 WHERE ID = @Id;
                    DELETE FROM dbo.V_MA_PRECIOS WHERE ID = @Id;
                    """, new { Id = idFila, Usuario = usuario }, tx, cancellationToken: ct));
                continue;
            }
            ValidarPrecios($"{idArticulo.Trim()} en la lista {lista.Nombre}", Enumerable.Range(1, 10).Select(item.Precio), item.OfertaDesde, item.OfertaHasta,
                item.CantidadOf2, item.Precio9, item.CantidadOf3, item.Precio10);
            if (!esta)
            {
                // Mismo alta que el legacy (Agregar_a_ListasVentas): ConIVA y ModificaPrecios de la lista; DesdeTrigger=0 para que
                // FMT_V_MA_PRECIOS registre el histórico y redondee. Las clases que la lista copia del maestro arrancan con las del artículo.
                // Tildada sin cargarle precios: toma todos los del artículo (como "Agregar a todas las listas").
                var sinPrecios = Enumerable.Range(1, 8).All(c => item.Precio(c) == 0);
                foreach (var c in Enumerable.Range(1, 8).Where(c => sinPrecios || lista.ClasesDelMaestro.Contains(c.ToString(CultureInfo.InvariantCulture))))
                    item.SetPrecio(c, PrecioMaestro(articulo, c));
                var pi =ParametrosPrecios(PreciosFila.De(item), usuario, id, 0);
                pi.Add("Nombre", lista.Nombre); pi.Add("ConIva", lista.ConIva); pi.Add("Mp", lista.ClasesDelMaestro); pi.Add("IdArticulo", idArticulo);
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.V_MA_PRECIOS (IdLista, Nombre, IdArticulo, ConIVA, TipoLista, Precio1, Precio2, Precio3, Precio4, Precio5,
                        PRECIO6, PRECIO7, PRECIO8, PRECIO9, PRECIO10, CantidadOf2, CantidadOf3, FhOfertaDesde, FhOfertaHasta, COSTO, ModificaPrecios,
                        DESCRIPCIONARTICULO, RUBRO, TIPO, IdUnidad, CUENTAPROVEEDOR, UTILIDAD, ActualizaBase, DesdeTrigger, USUARIO, FHALTA)
                    SELECT @IdLista, @Nombre, a.IDARTICULO, @ConIva, 'V', @P1, @P2, @P3, @P4, @P5, @P6, @P7, @P8, @P9, @P10,
                        @CantidadOf2, @CantidadOf3, @OfertaDesde, @OfertaHasta, ISNULL(a.COSTO,0), @Mp, LEFT(a.DESCRIPCION,100),
                        LEFT(a.IDRUBRO,4), LEFT(a.IDTIPO,4), LEFT(a.IDUNIDAD,4), LEFT(a.CUENTAPROVEEDOR,15), ISNULL(a.UTILIDAD,0), 0, 0, @Usuario, GETDATE()
                    FROM dbo.V_MA_ARTICULOS a WHERE a.IDARTICULO = @IdArticulo;
                    """, pi, tx, cancellationToken: ct));
            }
            else
            {
                await cn.ExecuteAsync(new CommandDefinition(ActualizarPreciosSql("p.ID = @Id"), ParametrosPrecios(PreciosFila.De(item), usuario, id, idFila), tx, cancellationToken: ct));
            }
        }
    }

    private static ArticuloListaPrecioDto DesdeMaestro(ListaPrecioDto lista, ArticuloSaveRequest a)
    {
        var item = new ArticuloListaPrecioDto { IdLista = lista.IdLista, Nombre = lista.Nombre, Incluido = true, ConIva = lista.ConIva, ClasesDelMaestro = lista.ClasesDelMaestro };
        for (var c = 1; c <= 8; c++) item.SetPrecio(c, PrecioMaestro(a, c));
        return item;
    }

    private static decimal PrecioMaestro(ArticuloSaveRequest a, int clase) => clase switch
    {
        1 => a.Precio1, 2 => a.Precio2, 3 => a.Precio3, 4 => a.Precio4, 5 => a.Precio5, 6 => a.Precio6, 7 => a.Precio7, 8 => a.Precio8, _ => 0
    };

    /// <summary>UPDATE de precios de una fila; las clases que la lista copia del maestro (ModificaPrecios) se conservan.</summary>
    private static string ActualizarPreciosSql(string where)
    {
        var sb = new StringBuilder("UPDATE p SET ");
        for (var c = 1; c <= 10; c++)
        {
            var col = Columna(c);
            sb.Append(c <= 8
                ? $"p.{col} = CASE WHEN CHARINDEX('{c}', ISNULL(p.ModificaPrecios,'')) > 0 THEN p.{col} ELSE @P{c} END, "
                : $"p.{col} = @P{c}, ");
        }
        sb.Append("p.CantidadOf2 = @CantidadOf2, p.CantidadOf3 = @CantidadOf3, p.FhOfertaDesde = @OfertaDesde, p.FhOfertaHasta = @OfertaHasta, ");
        sb.Append("p.DesdeTrigger = 0, p.USUARIO = @Usuario FROM dbo.V_MA_PRECIOS p WHERE p.IdLista = @IdLista AND p.TipoLista = 'V' AND ");
        sb.Append(where).Append("; SELECT @@ROWCOUNT;");
        return "SET NOCOUNT ON; " + sb;
    }

    /// <summary>Precios editables de una fila (de la grilla de la lista o de la solapa del artículo).</summary>
    private sealed record PreciosFila(decimal[] Clases, decimal? Precio9, decimal? Precio10, decimal? CantidadOf2, decimal? CantidadOf3,
        DateTime? OfertaDesde, DateTime? OfertaHasta)
    {
        public static PreciosFila De(ListaPrecioFilaDto f) => new(Enumerable.Range(1, 8).Select(f.Precio).ToArray(), f.Precio9, f.Precio10,
            f.CantidadOf2, f.CantidadOf3, f.OfertaDesde, f.OfertaHasta);
        public static PreciosFila De(ArticuloListaPrecioDto f) => new(Enumerable.Range(1, 8).Select(f.Precio).ToArray(), f.Precio9, f.Precio10,
            f.CantidadOf2, f.CantidadOf3, f.OfertaDesde, f.OfertaHasta);
    }

    private static DynamicParameters ParametrosPrecios(PreciosFila fila, string usuario, string idLista, int id)
    {
        var p = new DynamicParameters();
        for (var c = 1; c <= 8; c++) p.Add($"P{c}", decimal.Round(fila.Clases[c - 1], 4));
        p.Add("P9", fila.Precio9 is { } p9 && p9 != 0 ? decimal.Round(p9, 4) : null);
        p.Add("P10", fila.Precio10 is { } p10 && p10 != 0 ? decimal.Round(p10, 4) : null);
        p.Add("CantidadOf2", fila.CantidadOf2 is { } c2 && c2 > 0 ? (double?)(double)c2 : null);
        p.Add("CantidadOf3", fila.CantidadOf3 is { } c3 && c3 > 0 ? (double?)(double)c3 : null);
        p.Add("OfertaDesde", fila.OfertaDesde?.Date);
        p.Add("OfertaHasta", fila.OfertaHasta?.Date);
        p.Add("Usuario", usuario);
        p.Add("IdLista", idLista);
        p.Add("Id", id);
        return p;
    }

    private static void ValidarPrecios(string quien, IEnumerable<decimal> precios, DateTime? desde, DateTime? hasta,
        decimal? cant2, decimal? precio9, decimal? cant3, decimal? precio10)
    {
        if (precios.Any(x => x < 0)) throw new ReglaException($"Los precios de {quien} no pueden ser negativos.");
        if (desde is not null && hasta is not null && hasta.Value.Date < desde.Value.Date)
            throw new ReglaException($"La oferta de {quien} termina antes de empezar: revisá las fechas.");
        if ((precio9 ?? 0) != 0 && (cant2 ?? 0) <= 0)
            throw new ReglaException($"Falta la cantidad desde la que rige el primer precio por cantidad de {quien}.");
        if ((precio10 ?? 0) != 0 && (cant3 ?? 0) <= 0)
            throw new ReglaException($"Falta la cantidad desde la que rige el segundo precio por cantidad de {quien}.");
        if ((cant2 ?? 0) > 0 && (cant3 ?? 0) > 0 && cant3 <= cant2)
            throw new ReglaException($"La segunda cantidad de {quien} tiene que ser mayor que la primera.");
    }

    private static async Task<int> CopiarDeListaAsync(SqlConnection cn, SqlTransaction tx, string destino, string nombre, bool conIva,
        string origen, string usuario, CancellationToken ct)
    {
        var origenLista = (await LeerListasAsync(cn, tx, origen, ct)).First();
        if (origenLista.Articulos > 0 && origenLista.ConIva != conIva)
            throw new ReglaException($"La lista {origenLista.Nombre} es {(origenLista.ConIva ? "con" : "sin")} IVA y esta es {(conIva ? "con" : "sin")} IVA: no se pueden copiar los precios.");
        var mp = await ClasesDelMaestroAsync(cn, tx, destino, ct);
        if (mp.Length == 0) mp = origenLista.ClasesDelMaestro;
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SET NOCOUNT ON;
            INSERT INTO dbo.V_MA_PRECIOS (IdLista, Nombre, IdArticulo, ConIVA, TipoLista, Precio1, Precio2, Precio3, Precio4, Precio5,
                PRECIO6, PRECIO7, PRECIO8, PRECIO9, PRECIO10, CantidadOf2, CantidadOf3, FhOfertaDesde, FhOfertaHasta, COSTO, ModificaPrecios,
                DESCRIPCIONARTICULO, RUBRO, TIPO, IdUnidad, CUENTAPROVEEDOR, UTILIDAD, IdMoneda, ActualizaBase, DesdeTrigger, USUARIO, FHALTA)
            SELECT @Destino, @Nombre, s.IdArticulo, @ConIva, 'V', s.Precio1, s.Precio2, s.Precio3, s.Precio4, s.Precio5,
                s.PRECIO6, s.PRECIO7, s.PRECIO8, s.PRECIO9, s.PRECIO10, s.CantidadOf2, s.CantidadOf3, s.FhOfertaDesde, s.FhOfertaHasta, s.COSTO, @Mp,
                s.DESCRIPCIONARTICULO, s.RUBRO, s.TIPO, s.IdUnidad, s.CUENTAPROVEEDOR, s.UTILIDAD, s.IdMoneda, 0, 0, @Usuario, GETDATE()
            FROM dbo.V_MA_PRECIOS s
            WHERE s.IdLista = @Origen AND s.TipoLista = 'V'
              AND NOT EXISTS (SELECT 1 FROM dbo.V_MA_PRECIOS x WHERE x.IdLista = @Destino AND x.TipoLista = 'V' AND x.IdArticulo = s.IdArticulo);
            SELECT @@ROWCOUNT;
            """, new { Destino = destino, Nombre = nombre, ConIva = conIva, Origen = origen, Mp = mp, Usuario = usuario }, tx,
            commandTimeout: TimeoutMasivo, cancellationToken: ct));
    }

    private static async Task<string> ClasesDelMaestroAsync(SqlConnection cn, SqlTransaction? tx, string idLista, CancellationToken ct)
    {
        var mp = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT MAX(ISNULL(ModificaPrecios,'')) FROM dbo.V_MA_PRECIOS WHERE IdLista = @IdLista AND TipoLista = 'V';",
            new { IdLista = idLista }, tx, cancellationToken: ct));
        if (!string.IsNullOrEmpty(mp)) return mp.Trim();
        // Lista vacía: en Retail el legacy (Ma_art_Sup) copia costo y clases 1..3 del maestro ('0123').
        var cfg = await CargaComprobantesConfigPrecios.LeerAsync(cn, ct, tx);
        return cfg.Retail ? "0123" : "";
    }

    /// <summary>Filtro de la grilla y de las operaciones en bloque (alias p = V_MA_PRECIOS, a = V_MA_ARTICULOS).</summary>
    private static string FiltroSql(ListaPrecioFiltro f, string idLista, DynamicParameters p)
    {
        var sb = new StringBuilder("p.IdLista = @IdLista AND p.TipoLista = 'V'");
        p.Add("IdLista", idLista);
        var palabras = (f.Texto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < palabras.Length && i < 6; i++)
        {
            p.Add($"T{i}", "%" + Escapar(palabras[i]) + "%");
            sb.Append($" AND (p.IdArticulo LIKE @T{i} ESCAPE '\\' OR ISNULL(a.DESCRIPCION, p.DESCRIPCIONARTICULO) LIKE @T{i} ESCAPE '\\' OR ISNULL(a.CODIGOBARRA,'') LIKE @T{i} ESCAPE '\\')");
        }
        if (!string.IsNullOrWhiteSpace(f.RubroCodigo)) { p.Add("Rubro", f.RubroCodigo.Trim()); sb.Append(" AND LTRIM(RTRIM(ISNULL(a.IDRUBRO,''))) = @Rubro"); }
        if (!string.IsNullOrWhiteSpace(f.MarcaCodigo)) { p.Add("Marca", f.MarcaCodigo.Trim()); sb.Append(" AND LTRIM(RTRIM(ISNULL(a.IDTIPO,''))) = @Marca"); }
        if (!string.IsNullOrWhiteSpace(f.ProveedorCodigo)) { p.Add("Proveedor", f.ProveedorCodigo.Trim()); sb.Append(" AND LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR,''))) = @Proveedor"); }
        if (!string.IsNullOrWhiteSpace(f.FamiliaCodigo)) { p.Add("Familia", Escapar(f.FamiliaCodigo.Trim()) + "%"); sb.Append(" AND LTRIM(RTRIM(ISNULL(a.IdFamilia,''))) LIKE @Familia ESCAPE '\\'"); }
        if (f.SoloOfertas) sb.Append(" AND p.FhOfertaDesde <= GETDATE() AND (p.FhOfertaHasta IS NULL OR p.FhOfertaHasta >= CONVERT(date, GETDATE()))");
        if (!f.IncluirSuspendidos) sb.Append(" AND ISNULL(a.SUSPENDIDO,0) = 0 AND ISNULL(a.SuspendidoV,0) = 0");
        return sb.ToString();
    }

    private static async Task<List<ListaPrecioFilaDto>> LeerFilasAsync(SqlConnection cn, string where, DynamicParameters p, CancellationToken ct)
    {
        p.Add("Piva", await TasaGeneralAsync(cn, null, ct));
        return (await cn.QueryAsync<ListaPrecioFilaDto>(new CommandDefinition($"""
            SELECT p.ID Id, LTRIM(RTRIM(p.IdArticulo)) IdArticulo, ISNULL(a.DESCRIPCION, p.DESCRIPCIONARTICULO) Descripcion,
                ISNULL(LTRIM(RTRIM(a.CODIGOBARRA)),'') CodigoBarra, ISNULL(r.Descripcion,'') Rubro, ISNULL(t.Descripcion,'') Marca,
                CONVERT(decimal(9,4), CASE WHEN ISNULL(a.EXENTO,0)=1 THEN 0 WHEN ISNULL(a.TasaIVA,0)=0 THEN @Piva ELSE a.TasaIVA END) TasaIva,
                CONVERT(bit, ISNULL(a.EXENTO,0)) Exento,
                CONVERT(bit, CASE WHEN ISNULL(a.SUSPENDIDO,0)=1 OR ISNULL(a.SuspendidoV,0)=1 THEN 1 ELSE 0 END) Suspendido,
                p.ConIVA ConIva, CONVERT(decimal(19,4), ISNULL(a.COSTO, p.COSTO)) Costo,
                CONVERT(decimal(19,4),p.Precio1) Precio1, CONVERT(decimal(19,4),p.Precio2) Precio2, CONVERT(decimal(19,4),p.Precio3) Precio3,
                CONVERT(decimal(19,4),p.Precio4) Precio4, CONVERT(decimal(19,4),p.Precio5) Precio5, CONVERT(decimal(19,4),ISNULL(p.PRECIO6,0)) Precio6,
                CONVERT(decimal(19,4),ISNULL(p.PRECIO7,0)) Precio7, CONVERT(decimal(19,4),ISNULL(p.PRECIO8,0)) Precio8,
                CONVERT(decimal(19,4),p.PRECIO9) Precio9, CONVERT(decimal(19,4),p.PRECIO10) Precio10,
                CONVERT(decimal(19,4),p.CantidadOf2) CantidadOf2, CONVERT(decimal(19,4),p.CantidadOf3) CantidadOf3,
                p.FhOfertaDesde OfertaDesde, p.FhOfertaHasta OfertaHasta, ISNULL(p.ModificaPrecios,'') ClasesDelMaestro
            FROM dbo.V_MA_PRECIOS p
            LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = p.IdArticulo
            LEFT JOIN dbo.v_ta_rubros r ON LTRIM(RTRIM(r.IdRubro)) = LTRIM(RTRIM(a.IDRUBRO))
            LEFT JOIN dbo.v_ta_tipoArticulo t ON LTRIM(RTRIM(t.IdTipo)) = LTRIM(RTRIM(a.IDTIPO))
            WHERE {where};
            """, p, commandTimeout: 60, cancellationToken: ct))).ToList();
    }

    private static string Columna(int clase) => clase switch
    {
        >= 1 and <= 5 => $"Precio{clase}",
        >= 6 and <= 10 => $"PRECIO{clase}",
        _ => throw new ArgumentOutOfRangeException(nameof(clase))
    };

    private static string Escapar(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    private static DynamicParameters Con(DynamicParameters p, string nombre, object valor) { p.Add(nombre, valor); return p; }

    private static async Task<decimal> TasaGeneralAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => decimal.TryParse(await LeerConfigAsync(cn, tx, "PIVA", ct), NumberStyles.Number, CultureInfo.InvariantCulture, out var t) && t > 0 ? t : 21m;

    private static async Task<string> IdListaExistenteAsync(SqlConnection cn, SqlTransaction? tx, string? idLista, CancellationToken ct)
    {
        var buscado = (idLista ?? "").Trim();
        if (buscado.Length == 0) throw new ReglaException("Elegí la lista de precios.");
        var id = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT TOP (1) IdLista FROM dbo.V_MA_PRECIOSCAB WHERE TipoLista = 'V' AND LTRIM(RTRIM(IdLista)) = @Id;
            """, new { Id = buscado }, tx, cancellationToken: ct));
        return id ?? throw new ReglaException($"La lista de precios {buscado} no existe.");
    }

    private static Task<string?> LeerConfigAsync(SqlConnection cn, SqlTransaction? tx, string clave, CancellationToken ct)
        => cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) LTRIM(RTRIM(CONVERT(nvarchar(200), VALOR))) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);",
            new { Clave = clave }, tx, cancellationToken: ct));

    private static Task GrabarConfigAsync(SqlConnection cn, SqlTransaction tx, string clave, string valor, string descripcion, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion) VALUES (N'PRECIOS', @Clave, @Valor, @Descripcion, GETDATE());
            """, new { Clave = clave, Valor = valor, Descripcion = descripcion }, tx, cancellationToken: ct));

    private string Usuario()
    {
        var u = users.GetCurrentUserName().Trim();
        if (u.Length == 0) u = "AlfaCore";
        return u.Length > 50 ? u[..50] : u;
    }

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var sesion)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            if (!await permissions.HasAccessAsync(MenuKey, ct))
                throw new ReglaException("Tu usuario no tiene permiso para las listas de precios.");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            var r = await operacion(cn);
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != sesion)
                throw new ReglaException("Cambió la sesión. Volvé a abrir las listas de precios en la base activa.");
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (SqlException ex)
        {
            // Triggers del legacy, campos largos, datos faltantes: decir qué corregir.
            var mensaje = CargaComprobantesErroresSql.Explicar(ex);
            var codigo = await events.LogErrorAsync("ListasPrecios", accion, ex, mensaje, ct: CancellationToken.None);
            throw new AppUserFacingException(mensaje, codigo, ex);
        }
        catch (Exception ex)
        {
            const string mensaje = "No se pudo completar la operación con las listas de precios.";
            var codigo = await events.LogErrorAsync("ListasPrecios", accion, ex, mensaje, ct: CancellationToken.None);
            throw new AppUserFacingException(mensaje, codigo, ex);
        }
    }

    private sealed class ListaPrecioFilaTotal
    {
        public int Id { get; set; }
        public string IdArticulo { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string CodigoBarra { get; set; } = "";
        public string Rubro { get; set; } = "";
        public string Marca { get; set; } = "";
        public decimal TasaIva { get; set; }
        public bool Exento { get; set; }
        public bool Suspendido { get; set; }
        public bool ConIva { get; set; }
        public decimal Costo { get; set; }
        public decimal Precio1 { get; set; }
        public decimal Precio2 { get; set; }
        public decimal Precio3 { get; set; }
        public decimal Precio4 { get; set; }
        public decimal Precio5 { get; set; }
        public decimal Precio6 { get; set; }
        public decimal Precio7 { get; set; }
        public decimal Precio8 { get; set; }
        public decimal? Precio9 { get; set; }
        public decimal? Precio10 { get; set; }
        public decimal? CantidadOf2 { get; set; }
        public decimal? CantidadOf3 { get; set; }
        public DateTime? OfertaDesde { get; set; }
        public DateTime? OfertaHasta { get; set; }
        public string ClasesDelMaestro { get; set; } = "";
        public long Fila { get; set; }
        public int Total { get; set; }

        public static explicit operator ListaPrecioFilaDto(ListaPrecioFilaTotal f) => new()
        {
            Id = f.Id, IdArticulo = f.IdArticulo, Descripcion = f.Descripcion, CodigoBarra = f.CodigoBarra, Rubro = f.Rubro, Marca = f.Marca,
            TasaIva = f.TasaIva, Exento = f.Exento, Suspendido = f.Suspendido, ConIva = f.ConIva, Costo = f.Costo,
            Precio1 = f.Precio1, Precio2 = f.Precio2, Precio3 = f.Precio3, Precio4 = f.Precio4, Precio5 = f.Precio5,
            Precio6 = f.Precio6, Precio7 = f.Precio7, Precio8 = f.Precio8, Precio9 = f.Precio9, Precio10 = f.Precio10,
            CantidadOf2 = f.CantidadOf2, CantidadOf3 = f.CantidadOf3, OfertaDesde = f.OfertaDesde, OfertaHasta = f.OfertaHasta,
            ClasesDelMaestro = f.ClasesDelMaestro
        };
    }
}
