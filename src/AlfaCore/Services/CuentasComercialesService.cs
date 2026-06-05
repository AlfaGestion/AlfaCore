using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class CuentasComercialesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    ICuentasComercialesValidator validator) : ICuentasComercialesService
{
    private const string ModuleName = "CuentasComerciales";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<CuentaComercialGridItemDto>> SearchAsync(CuentaComercialTipo tipo, CuentaComercialFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "Search", async token =>
        {
            filters ??= new CuentaComercialFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;
            var descriptor = ResolveDescriptor(tipo);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sql = $"""
                SELECT
                    ISNULL(base.CODIGO, ''),
                    ISNULL(base.RAZON_SOCIAL, ''),
                    ISNULL(base.CONTACTO, ''),
                    ISNULL(base.LOCALIDAD, ''),
                    ISNULL(base.PROVINCIA, ''),
                    ISNULL(prov.DESCRIPCION, ''),
                    ISNULL(base.PAIS, ''),
                    ISNULL(pai.DESCRIPCION, ''),
                    ISNULL(base.TELEFONO, ''),
                    ISNULL(base.MAIL, ''),
                    ISNULL(base.NUMERO_DOCUMENTO, ''),
                    ISNULL(base.IVA, ''),
                    ISNULL(iva.DESCRIPCION, ''),
                    ISNULL(base.IdVendedor, ''),
                    ISNULL(vd.Nombre, ''),
                    ISNULL(base.IdLista, ''),
                    ISNULL(base.BLOQUEO, 0),
                    CASE WHEN ISNULL(base.Dada_De_Baja, 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
                FROM dbo.{descriptor.ViewName} base
                LEFT JOIN dbo.TA_ESTADOS prov
                    ON UPPER(LTRIM(RTRIM(prov.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(base.PROVINCIA, ''))))
                LEFT JOIN dbo.TA_PAISES pai
                    ON UPPER(LTRIM(RTRIM(pai.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(base.PAIS, ''))))
                LEFT JOIN dbo.TA_CONDIVA iva
                    ON UPPER(LTRIM(RTRIM(iva.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(base.IVA, ''))))
                LEFT JOIN dbo.V_TA_VENDEDORES vd
                    ON UPPER(LTRIM(RTRIM(vd.IdVendedor))) = UPPER(LTRIM(RTRIM(ISNULL(base.IdVendedor, ''))))
                WHERE (
                        @TextoLike = ''
                        OR ISNULL(base.CODIGO, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.RAZON_SOCIAL, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.CONTACTO, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.LOCALIDAD, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.TELEFONO, '') LIKE @TextoLike
                        OR ISNULL(base.MAIL, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.NUMERO_DOCUMENTO, '') LIKE @TextoLike
                      )
                  AND (@Activo IS NULL OR CASE WHEN ISNULL(base.Dada_De_Baja, 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = @Activo)
                  AND (@Bloqueado IS NULL OR ISNULL(base.BLOQUEO, 0) = @Bloqueado)
                  AND (@ProvinciaCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(base.PROVINCIA, '')))) = @ProvinciaCodigo)
                  AND (@PaisCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(base.PAIS, '')))) = @PaisCodigo)
                ORDER BY
                    CASE WHEN ISNULL(base.Dada_De_Baja, 0) = 0 THEN 0 ELSE 1 END,
                    ISNULL(base.RAZON_SOCIAL, ''),
                    ISNULL(base.CODIGO, '')
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.{descriptor.ViewName} base
                WHERE (
                        @TextoLike = ''
                        OR ISNULL(base.CODIGO, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.RAZON_SOCIAL, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.CONTACTO, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.LOCALIDAD, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.TELEFONO, '') LIKE @TextoLike
                        OR ISNULL(base.MAIL, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(base.NUMERO_DOCUMENTO, '') LIKE @TextoLike
                      )
                  AND (@Activo IS NULL OR CASE WHEN ISNULL(base.Dada_De_Baja, 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = @Activo)
                  AND (@Bloqueado IS NULL OR ISNULL(base.BLOQUEO, 0) = @Bloqueado)
                  AND (@ProvinciaCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(base.PROVINCIA, '')))) = @ProvinciaCodigo)
                  AND (@PaisCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(base.PAIS, '')))) = @PaisCodigo);
                """;

            var rows = new List<CuentaComercialGridItemDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(filters.Texto));
            cmd.Parameters.AddWithValue("@Activo", filters.Activo.HasValue ? filters.Activo.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Bloqueado", filters.Bloqueado.HasValue ? filters.Bloqueado.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ProvinciaCodigo", (filters.ProvinciaCodigo ?? string.Empty).Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@PaisCodigo", (filters.PaisCodigo ?? string.Empty).Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new CuentaComercialGridItemDto
                {
                    Codigo = GetString(rd, 0),
                    RazonSocial = GetString(rd, 1),
                    Contacto = GetString(rd, 2),
                    Localidad = GetString(rd, 3),
                    ProvinciaCodigo = GetString(rd, 4),
                    ProvinciaDescripcion = GetString(rd, 5),
                    PaisCodigo = GetString(rd, 6),
                    PaisDescripcion = GetString(rd, 7),
                    Telefono = GetString(rd, 8),
                    Mail = GetString(rd, 9),
                    NumeroDocumento = GetString(rd, 10),
                    IvaCodigo = GetString(rd, 11),
                    IvaDescripcion = GetString(rd, 12),
                    IdVendedor = GetString(rd, 13),
                    NombreVendedor = GetString(rd, 14),
                    IdLista = GetString(rd, 15),
                    Bloqueado = GetBool(rd, 16),
                    Activo = GetBool(rd, 17)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = GetInt(rd, 0);

            return new PagedResult<CuentaComercialGridItemDto>
            {
                Items = rows,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, $"No se pudieron cargar los {GetPluralLabel(tipo).ToLowerInvariant()}.", ct);

    public Task<CuentaComercialDetailDto?> GetByIdAsync(CuentaComercialTipo tipo, string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "GetById", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return null;

            var descriptor = ResolveDescriptor(tipo);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasManualColumn = await ColumnExistsAsync(cn, null, "MA_CUENTAS", "MANUAL", token);
            var hasClasificacionColumn = await ColumnExistsAsync(cn, null, descriptor.ViewName, "Clasificacion", token);
            var manualSelect = hasManualColumn
                ? "ISNULL(CAST(acc.MANUAL AS nvarchar(max)), '')"
                : "CAST('' AS nvarchar(max))";
            var clasificacionSelect = hasClasificacionColumn
                ? "ISNULL(base.Clasificacion, '')"
                : "CAST('' AS nvarchar(4))";

            var sql = $"""
                SELECT
                    ISNULL(base.CODIGO, ''),
                    ISNULL(base.RAZON_SOCIAL, ''),
                    ISNULL(base.CONTACTO, ''),
                    ISNULL(base.CALLE, ''),
                    ISNULL(base.NUMERO, ''),
                    ISNULL(base.PISO, ''),
                    ISNULL(base.DEPARTAMENTO, ''),
                    ISNULL(base.CPOSTAL, ''),
                    ISNULL(base.LOCALIDAD, ''),
                    ISNULL(base.PROVINCIA, ''),
                    ISNULL(base.PAIS, ''),
                    ISNULL(base.TELEFONO, ''),
                    ISNULL(base.FAX, ''),
                    ISNULL(base.MAIL, ''),
                    ISNULL(base.WEB, ''),
                    ISNULL(base.DOCUMENTO_TIPO, ''),
                    ISNULL(base.NUMERO_DOCUMENTO, ''),
                    ISNULL(base.IVA, ''),
                    COALESCE(NULLIF(ISNULL(base.OBSERVACIONES, ''), ''), NULLIF(ISNULL(adic.OBSERVACIONES, ''), ''), ''),
                    base.Limite_Credito,
                    ISNULL(base.idCond_Cpra_Vta, ''),
                    ISNULL(base.IdLista, ''),
                    base.Clase,
                    ISNULL(base.IdVendedor, ''),
                    ISNULL(base.CuentaPrincipal, ''),
                    ISNULL(base.CodigoImputacion, ''),
                    ISNULL(base.CodigoOpcional, ''),
                    ISNULL(base.Moneda, ''),
                    {clasificacionSelect},
                    ISNULL(base.LugarEntrega, ''),
                    ISNULL(base.DatosPago, ''),
                    {manualSelect},
                    ISNULL(base.BLOQUEO, 0),
                    ISNULL(base.ExentoIvaServicios, 0),
                    ISNULL(base.ExentoIvaArticulos, 0),
                    ISNULL(base.ExentoIVAOtros, 0),
                    ISNULL(base.ProveedorLocal, 0),
                    ISNULL(base.ProveedorCompartido, 0),
                    ISNULL(base.TipoVista, ''),
                    CASE WHEN ISNULL(base.Dada_De_Baja, 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                    base.FechaHora_Grabacion,
                    base.FechaHora_Modificacion
                FROM dbo.{descriptor.ViewName} base
                LEFT JOIN dbo.MA_CUENTAS acc
                    ON UPPER(LTRIM(RTRIM(acc.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(base.CODIGO, ''))))
                LEFT JOIN dbo.MA_CUENTASADIC adic
                    ON UPPER(LTRIM(RTRIM(adic.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(base.CODIGO, ''))))
                WHERE UPPER(LTRIM(RTRIM(base.CODIGO))) = @Codigo;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Codigo", codigo.Trim().ToUpperInvariant());
            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (!await rd.ReadAsync(token))
                return null;

            var detail = new CuentaComercialDetailDto
            {
                CodigoOriginal = GetString(rd, 0),
                Codigo = GetString(rd, 0),
                RazonSocial = GetString(rd, 1),
                Contacto = GetString(rd, 2),
                Calle = GetString(rd, 3),
                Numero = GetString(rd, 4),
                Piso = GetString(rd, 5),
                Departamento = GetString(rd, 6),
                CodigoPostal = GetString(rd, 7),
                Localidad = GetString(rd, 8),
                ProvinciaCodigo = GetString(rd, 9),
                PaisCodigo = GetString(rd, 10),
                Telefono = GetString(rd, 11),
                Fax = GetString(rd, 12),
                Mail = GetString(rd, 13),
                Web = GetString(rd, 14),
                DocumentoTipoCodigo = GetString(rd, 15),
                NumeroDocumento = GetString(rd, 16),
                IvaCodigo = GetString(rd, 17),
                Observaciones = GetString(rd, 18),
                LimiteCredito = rd.IsDBNull(19) ? null : rd.GetDecimal(19),
                CondicionComercialCodigo = GetString(rd, 20),
                ListaPrecioCodigo = GetString(rd, 21),
                Clase = rd.IsDBNull(22) ? null : Convert.ToInt16(rd.GetValue(22)),
                VendedorCodigo = GetString(rd, 23),
                CuentaPrincipal = GetString(rd, 24),
                CodigoImputacion = GetString(rd, 25),
                CodigoOpcional = GetString(rd, 26),
                MonedaCodigo = GetString(rd, 27),
                ClasificacionCodigo = GetString(rd, 28),
                LugarEntrega = GetString(rd, 29),
                DatosPago = GetString(rd, 30),
                Manual = GetString(rd, 31),
                Bloqueado = GetBool(rd, 32),
                ExentoIvaServicios = GetBool(rd, 33),
                ExentoIvaArticulos = GetBool(rd, 34),
                ExentoIvaOtros = GetBool(rd, 35),
                ProveedorLocal = GetBool(rd, 36),
                ProveedorCompartido = GetBool(rd, 37),
                TipoVista = GetString(rd, 38),
                Activo = GetBool(rd, 39),
                FechaHoraGrabacion = rd.IsDBNull(40) ? null : rd.GetDateTime(40),
                FechaHoraModificacion = rd.IsDBNull(41) ? null : rd.GetDateTime(41)
            };

            await rd.CloseAsync();
            await LoadCuentaObservacionesAsync(cn, detail, token);
            if (tipo == CuentaComercialTipo.Proveedor)
                detail.DescuentosCondicion = (await LoadDescuentosProveedorAsync(cn, detail.Codigo, token)).ToList();

            return detail;
        }, $"No se pudo cargar el {GetSingularLabel(tipo).ToLowerInvariant()} seleccionado.", ct);

    public Task<string> GetSuggestedCodigoAsync(CuentaComercialTipo tipo, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "GetSuggestedCodigo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await GenerateNextCodigoAsync(cn, null, tipo, token);
        }, $"No se pudo preparar el próximo código de {GetSingularLabel(tipo).ToLowerInvariant()}.", ct);

    public Task<CuentaComercialLookupDataDto> GetLookupDataAsync(CuentaComercialTipo tipo, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "GetLookupData", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var descriptor = ResolveDescriptor(tipo);
            var usesPriceLists = await ReadConfigValueAsync(cn, null, descriptor.UsaListasConfigKey, token);
            var title = await ReadConfigValueAsync(cn, null, descriptor.TituloConfigKey, token);
            var tipoLista = descriptor.PriceListType;
            var habilitado = descriptor.CondicionHabilitado;

            var sql = $"""
                SELECT ISNULL(CODIGO, ''), ISNULL(DESCRIPCION, '') FROM dbo.TA_PAISES ORDER BY DESCRIPCION, CODIGO;
                SELECT ISNULL(CODIGO, ''), ISNULL(DESCRIPCION, '') FROM dbo.TA_ESTADOS ORDER BY DESCRIPCION, CODIGO;
                SELECT ISNULL(CODIGO, ''), ISNULL(DESCRIPCION, '') FROM dbo.TA_CONDIVA ORDER BY DESCRIPCION, CODIGO;
                SELECT ISNULL(CODIGO, ''), ISNULL(DESCRIPCION, '') FROM dbo.TA_TIPODOCUMENTO ORDER BY DESCRIPCION, CODIGO;
                SELECT ISNULL(IDCond_Cpra_Vta, ''), ISNULL(Descripcion, '')
                FROM dbo.V_TA_Cpra_Vta
                WHERE @Habilitado = ''
                   OR ISNULL(HabilitadoPara, '') = ''
                   OR HabilitadoPara LIKE '%' + @Habilitado + '%'
                ORDER BY Descripcion, IDCond_Cpra_Vta;
                SELECT ISNULL(IdVendedor, ''), ISNULL(Nombre, '')
                FROM dbo.V_TA_VENDEDORES
                ORDER BY Nombre, IdVendedor;
                SELECT ISNULL(IdLista, ''), ISNULL(Nombre, '')
                FROM dbo.V_MA_PreciosCab
                WHERE ISNULL(TipoLista, '') = @TipoLista
                ORDER BY Nombre, IdLista;
                SELECT ISNULL(CODIGO, ''), ISNULL(DESCRIPCION, '')
                FROM dbo.TA_MONEDAS
                ORDER BY DESCRIPCION, CODIGO;
                SELECT ISNULL(Codigo, ''), ISNULL(Descripcion, '')
                FROM dbo.TA_CLASIFICACIONES
                ORDER BY Descripcion, Codigo;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Habilitado", habilitado);
            cmd.Parameters.AddWithValue("@TipoLista", tipoLista);

            var data = new CuentaComercialLookupDataDto
            {
                TituloCuenta = title,
                UsaListasDePrecio = string.Equals(usesPriceLists, "SI", StringComparison.OrdinalIgnoreCase)
            };

            await using var rd = await cmd.ExecuteReaderAsync(token);
            data.Paises = (await ReadLookupListAsync(rd, token)).ToList();
            await rd.NextResultAsync(token);
            data.Provincias = (await ReadLookupListAsync(rd, token)).ToList();
            await rd.NextResultAsync(token);
            data.CondicionesIva = (await ReadLookupListAsync(rd, token)).ToList();
            await rd.NextResultAsync(token);
            data.TiposDocumento = (await ReadLookupListAsync(rd, token)).ToList();
            await rd.NextResultAsync(token);
            data.CondicionesComerciales = (await ReadLookupListAsync(rd, token)).ToList();
            await rd.NextResultAsync(token);
            data.Vendedores = (await ReadLookupListAsync(rd, token)).ToList();
            await rd.NextResultAsync(token);
            data.ListasPrecio = (await ReadLookupListAsync(rd, token)).ToList();
            await rd.NextResultAsync(token);
            data.Monedas = (await ReadLookupListAsync(rd, token)).ToList();
            await rd.NextResultAsync(token);
            data.Clasificaciones = (await ReadLookupListAsync(rd, token)).ToList();

            return data;
        }, $"No se pudieron cargar las referencias de {GetPluralLabel(tipo).ToLowerInvariant()}.", ct);

    public Task<string> SaveAsync(CuentaComercialTipo tipo, CuentaComercialSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "Save", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var normalized = NormalizeRequest(request);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            var descriptor = ResolveDescriptor(tipo);
            var isNew = string.IsNullOrWhiteSpace(normalized.CodigoOriginal);
            var hasManualColumn = await ColumnExistsAsync(cn, (SqlTransaction)tx, "MA_CUENTAS", "MANUAL", token);

            if (string.IsNullOrWhiteSpace(normalized.Codigo))
                normalized.Codigo = await GenerateNextCodigoAsync(cn, (SqlTransaction)tx, tipo, token);

            var validation = await validator.ValidateForSaveAsync(tipo, normalized, token);
            if (!validation.IsValid)
                throw new AppValidationException($"Revisá los datos del {GetSingularLabel(tipo).ToLowerInvariant()} antes de guardar.", validation);

            var tipoVista = await ResolveTipoVistaAsync(cn, (SqlTransaction)tx, normalized.Codigo, descriptor, token);
            var defaultDocumentoTipo = await ReadConfigurableDefaultAsync(cn, (SqlTransaction)tx, normalized.DocumentoTipoCodigo, "1", token);
            var defaultIva = await ReadConfigurableDefaultAsync(cn, (SqlTransaction)tx, normalized.IvaCodigo, "1", token);
            var defaultPais = await ReadConfigurableDefaultAsync(cn, (SqlTransaction)tx, normalized.PaisCodigo, "1", token);
            var defaultProvincia = await ReadConfigurableDefaultAsync(cn, (SqlTransaction)tx, normalized.ProvinciaCodigo, "1", token);
            var defaultCond = await ReadConfigurableDefaultAsync(cn, (SqlTransaction)tx, normalized.CondicionComercialCodigo, "1", token);
            var defaultClase = normalized.Clase ?? (tipo == CuentaComercialTipo.Cliente ? (short)1 : (short)0);
            var defaultMotivo = "1";

            var accountExists = await ExistsAsync(cn, (SqlTransaction)tx, "MA_CUENTAS", "CODIGO", normalized.Codigo, token);
            if (!accountExists)
            {
                var insertAccountSql = hasManualColumn
                    ? """
                    INSERT INTO dbo.MA_CUENTAS
                    (
                        CODIGO,
                        DESCRIPCION,
                        TITULO,
                        PideVencimiento,
                        TipoVista,
                        MANUAL,
                        CuentaPrincipal,
                        Libro_Iva_Ventas,
                        Libro_Iva_Compras,
                        Moneda,
                        BLOQUEO,
                        Dada_De_Baja,
                        CodigoOpcional,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Codigo,
                        @Descripcion,
                        0,
                        1,
                        @TipoVista,
                        @Manual,
                        @CuentaPrincipal,
                        @LibroVentas,
                        @LibroCompras,
                        @Moneda,
                        @Bloqueo,
                        0,
                        @CodigoOpcional,
                        GETDATE(),
                        GETDATE()
                    );
                    """
                    : """
                    INSERT INTO dbo.MA_CUENTAS
                    (
                        CODIGO,
                        DESCRIPCION,
                        TITULO,
                        PideVencimiento,
                        TipoVista,
                        CuentaPrincipal,
                        Libro_Iva_Ventas,
                        Libro_Iva_Compras,
                        Moneda,
                        BLOQUEO,
                        Dada_De_Baja,
                        CodigoOpcional,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Codigo,
                        @Descripcion,
                        0,
                        1,
                        @TipoVista,
                        @CuentaPrincipal,
                        @LibroVentas,
                        @LibroCompras,
                        @Moneda,
                        @Bloqueo,
                        0,
                        @CodigoOpcional,
                        GETDATE(),
                        GETDATE()
                    );
                    """;

                await using var cmd = new SqlCommand(insertAccountSql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@Codigo", normalized.Codigo);
                cmd.Parameters.AddWithValue("@Descripcion", normalized.RazonSocial);
                cmd.Parameters.AddWithValue("@TipoVista", tipoVista);
                if (hasManualColumn)
                    cmd.Parameters.AddWithValue("@Manual", DbNullable(normalized.Manual));
                cmd.Parameters.AddWithValue("@CuentaPrincipal", DbNullable(normalized.CuentaPrincipal));
                cmd.Parameters.AddWithValue("@LibroVentas", tipo == CuentaComercialTipo.Cliente);
                cmd.Parameters.AddWithValue("@LibroCompras", tipo == CuentaComercialTipo.Proveedor);
                cmd.Parameters.AddWithValue("@Moneda", DbNullable(normalized.MonedaCodigo));
                cmd.Parameters.AddWithValue("@Bloqueo", normalized.Bloqueado);
                cmd.Parameters.AddWithValue("@CodigoOpcional", DbNullable(normalized.CodigoOpcional));
                await cmd.ExecuteNonQueryAsync(token);
            }
            else
            {
                var updateAccountSql = hasManualColumn
                    ? """
                    UPDATE dbo.MA_CUENTAS
                    SET
                        DESCRIPCION = @Descripcion,
                        MANUAL = @Manual,
                        CuentaPrincipal = @CuentaPrincipal,
                        Moneda = @Moneda,
                        BLOQUEO = @Bloqueo,
                        Dada_De_Baja = 0,
                        CodigoOpcional = @CodigoOpcional,
                        FechaHora_Modificacion = GETDATE()
                    WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));
                    """
                    : """
                    UPDATE dbo.MA_CUENTAS
                    SET
                        DESCRIPCION = @Descripcion,
                        CuentaPrincipal = @CuentaPrincipal,
                        Moneda = @Moneda,
                        BLOQUEO = @Bloqueo,
                        Dada_De_Baja = 0,
                        CodigoOpcional = @CodigoOpcional,
                        FechaHora_Modificacion = GETDATE()
                    WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));
                    """;

                await using var cmd = new SqlCommand(updateAccountSql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@Codigo", normalized.Codigo.Trim().ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Descripcion", normalized.RazonSocial);
                if (hasManualColumn)
                    cmd.Parameters.AddWithValue("@Manual", DbNullable(normalized.Manual));
                cmd.Parameters.AddWithValue("@CuentaPrincipal", DbNullable(normalized.CuentaPrincipal));
                cmd.Parameters.AddWithValue("@Moneda", DbNullable(normalized.MonedaCodigo));
                cmd.Parameters.AddWithValue("@Bloqueo", normalized.Bloqueado);
                cmd.Parameters.AddWithValue("@CodigoOpcional", DbNullable(normalized.CodigoOpcional));
                await cmd.ExecuteNonQueryAsync(token);
            }

            var accountAdicExists = await ExistsAsync(cn, (SqlTransaction)tx, "MA_CUENTASADIC", "CODIGO", normalized.Codigo, token);
            if (!accountAdicExists)
            {
                const string insertAdicSql = """
                    INSERT INTO dbo.MA_CUENTASADIC
                    (
                        CODIGO,
                        CONTACTO,
                        CALLE,
                        NUMERO,
                        PISO,
                        DEPARTAMENTO,
                        CPOSTAL,
                        LOCALIDAD,
                        PROVINCIA,
                        PAIS,
                        TELEFONO,
                        FAX,
                        MAIL,
                        DOCUMENTO_TIPO,
                        NUMERO_DOCUMENTO,
                        IVA,
                        OBSERVACIONES,
                        Limite_Credito,
                        idCond_Cpra_Vta,
                        IdLista,
                        Clase,
                        IdVendedor,
                        IdMotivoVta,
                        IdMotivoCpra,
                        ExentoIvaServicios,
                        ExentoIvaArticulos,
                        ExentoIVAOtros,
                        LugarEntrega,
                        CodigoImputacion,
                        DatosPago,
                        WEB,
                        ProveedorLocal,
                        ProveedorCompartido,
                        Clasificacion,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Codigo,
                        @Contacto,
                        @Calle,
                        @Numero,
                        @Piso,
                        @Departamento,
                        @CodigoPostal,
                        @Localidad,
                        @Provincia,
                        @Pais,
                        @Telefono,
                        @Fax,
                        @Mail,
                        @DocumentoTipo,
                        @NumeroDocumento,
                        @Iva,
                        @Observaciones,
                        @LimiteCredito,
                        @CondicionComercial,
                        @IdLista,
                        @Clase,
                        @IdVendedor,
                        @IdMotivoVta,
                        @IdMotivoCpra,
                        @ExentoIvaServicios,
                        @ExentoIvaArticulos,
                        @ExentoIvaOtros,
                        @LugarEntrega,
                        @CodigoImputacion,
                        @DatosPago,
                        @Web,
                        @ProveedorLocal,
                        @ProveedorCompartido,
                        @Clasificacion,
                        GETDATE(),
                        GETDATE()
                    );
                    """;

                await using var cmd = new SqlCommand(insertAdicSql, cn, (SqlTransaction)tx);
                FillCuentaAdicParameters(cmd, normalized, defaultDocumentoTipo, defaultIva, defaultPais, defaultProvincia, defaultCond, defaultClase, defaultMotivo, tipo);
                await cmd.ExecuteNonQueryAsync(token);
            }
            else
            {
                const string updateAdicSql = """
                    UPDATE dbo.MA_CUENTASADIC
                    SET
                        CONTACTO = @Contacto,
                        CALLE = @Calle,
                        NUMERO = @Numero,
                        PISO = @Piso,
                        DEPARTAMENTO = @Departamento,
                        CPOSTAL = @CodigoPostal,
                        LOCALIDAD = @Localidad,
                        PROVINCIA = @Provincia,
                        PAIS = @Pais,
                        TELEFONO = @Telefono,
                        FAX = @Fax,
                        MAIL = @Mail,
                        DOCUMENTO_TIPO = @DocumentoTipo,
                        NUMERO_DOCUMENTO = @NumeroDocumento,
                        IVA = @Iva,
                        OBSERVACIONES = @Observaciones,
                        Limite_Credito = @LimiteCredito,
                        idCond_Cpra_Vta = @CondicionComercial,
                        IdLista = @IdLista,
                        Clase = @Clase,
                        IdVendedor = @IdVendedor,
                        IdMotivoVta = @IdMotivoVta,
                        IdMotivoCpra = @IdMotivoCpra,
                        ExentoIvaServicios = @ExentoIvaServicios,
                        ExentoIvaArticulos = @ExentoIvaArticulos,
                        ExentoIVAOtros = @ExentoIvaOtros,
                        LugarEntrega = @LugarEntrega,
                        CodigoImputacion = @CodigoImputacion,
                        DatosPago = @DatosPago,
                        WEB = @Web,
                        ProveedorLocal = @ProveedorLocal,
                        ProveedorCompartido = @ProveedorCompartido,
                        Clasificacion = @Clasificacion,
                        FechaHora_Modificacion = GETDATE()
                    WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));
                    """;

                await using var cmd = new SqlCommand(updateAdicSql, cn, (SqlTransaction)tx);
                FillCuentaAdicParameters(cmd, normalized, defaultDocumentoTipo, defaultIva, defaultPais, defaultProvincia, defaultCond, defaultClase, defaultMotivo, tipo);
                var affected = await cmd.ExecuteNonQueryAsync(token);
                if (affected == 0)
                    throw new InvalidOperationException("No se pudo actualizar MA_CUENTASADIC para la cuenta indicada.");
            }

            await UpsertCuentaObsAsync(cn, (SqlTransaction)tx, normalized, token);
            if (tipo == CuentaComercialTipo.Proveedor)
                await ReplaceDescuentosProveedorAsync(cn, (SqlTransaction)tx, normalized, token);

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                GetModuleLabel(tipo),
                isNew ? "Create" : "Update",
                "MA_CUENTAS",
                normalized.Codigo,
                isNew ? $"{GetSingularLabel(tipo)} creado." : $"{GetSingularLabel(tipo)} actualizado.",
                new
                {
                    Codigo = normalized.Codigo,
                    normalized.RazonSocial,
                    normalized.NumeroDocumento,
                    normalized.ProvinciaCodigo,
                    normalized.PaisCodigo,
                    Activo = true
                },
                token);

            return normalized.Codigo;
        }, $"No se pudo guardar el {GetSingularLabel(tipo).ToLowerInvariant()}.", ct);

    public Task DeactivateAsync(CuentaComercialTipo tipo, string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "Deactivate", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException($"No se recibió el {GetSingularLabel(tipo).ToLowerInvariant()} a dar de baja.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string sql = """
                UPDATE dbo.MA_CUENTAS
                SET
                    Dada_De_Baja = 1,
                    FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Codigo", codigo.Trim().ToUpperInvariant());
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException($"El {GetSingularLabel(tipo).ToLowerInvariant()} seleccionado ya no existe en la base activa.");

            await appEvents.LogAuditAsync(
                GetModuleLabel(tipo),
                "Deactivate",
                "MA_CUENTAS",
                codigo.Trim(),
                $"{GetSingularLabel(tipo)} dado de baja.",
                new { Codigo = codigo.Trim(), Activo = false },
                token);
        }, $"No se pudo dar de baja el {GetSingularLabel(tipo).ToLowerInvariant()}.", ct);

    public Task<IReadOnlyList<CuentaComercialContactoDto>> GetContactosAsync(CuentaComercialTipo tipo, string cuentaCodigo, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<CuentaComercialContactoDto>>(GetModuleLabel(tipo), "GetContactos", async token =>
        {
            if (string.IsNullOrWhiteSpace(cuentaCodigo))
                return [];

            var cuenta = cuentaCodigo.Trim().ToUpperInvariant();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivoColumn = await ColumnExistsAsync(cn, null, "MA_CONTACTOS", "Activo", token);
            var activoSelect = hasActivoColumn ? "ISNULL(c.Activo, 1)" : "CAST(1 AS bit)";
            var activoFilter = hasActivoColumn ? "AND ISNULL(c.Activo, 1) = 1" : string.Empty;

            var sql = $"""
                SELECT
                    c.id AS Id,
                    CONVERT(int, ISNULL(NULLIF(c.idContacto, 0), c.id)) AS IdContacto,
                    ISNULL(c.Nombre_y_Apellido, '') AS NombreApellido,
                    ISNULL(c.Cargo, '') AS Cargo,
                    ISNULL(c.email, '') AS Email,
                    ISNULL(c.Telefono, '') AS Telefono,
                    ISNULL(c.Celular, '') AS Celular,
                    {activoSelect} AS Activo,
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM dbo.MA_CONTACTOS_CUENTAS rel
                        WHERE rel.IdContacto = ISNULL(NULLIF(c.idContacto, 0), c.id)
                          AND UPPER(LTRIM(RTRIM(rel.Cuenta))) = @Cuenta
                    ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS VinculadoPorTabla,
                    CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.CuentaRel, '')))) = @Cuenta THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS VinculadoPorCuentaRel
                FROM dbo.MA_CONTACTOS c
                WHERE (
                        EXISTS (
                            SELECT 1
                            FROM dbo.MA_CONTACTOS_CUENTAS rel
                            WHERE rel.IdContacto = ISNULL(NULLIF(c.idContacto, 0), c.id)
                              AND UPPER(LTRIM(RTRIM(rel.Cuenta))) = @Cuenta
                        )
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.CuentaRel, '')))) = @Cuenta
                    )
                    {activoFilter}
                ORDER BY ISNULL(c.Nombre_y_Apellido, ''), c.id;
                """;

            var contactos = await cn.QueryAsync<CuentaComercialContactoDto>(
                new CommandDefinition(sql, new { Cuenta = cuenta }, cancellationToken: token));
            return contactos.ToList();
        }, "No se pudieron cargar los contactos vinculados.", ct);

    public Task<IReadOnlyList<CuentaComercialContactoCandidateDto>> SearchContactosParaVincularAsync(CuentaComercialTipo tipo, string cuentaCodigo, string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<CuentaComercialContactoCandidateDto>>(GetModuleLabel(tipo), "SearchContactosParaVincular", async token =>
        {
            if (string.IsNullOrWhiteSpace(cuentaCodigo) || string.IsNullOrWhiteSpace(texto))
                return [];

            var cuenta = cuentaCodigo.Trim().ToUpperInvariant();
            var search = SearchTextHelper.Normalize(texto);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivoColumn = await ColumnExistsAsync(cn, null, "MA_CONTACTOS", "Activo", token);
            var activoFilter = hasActivoColumn ? "AND ISNULL(c.Activo, 1) = 1" : string.Empty;

            var sql = $"""
                SELECT TOP (20)
                    c.id AS Id,
                    CONVERT(int, ISNULL(NULLIF(c.idContacto, 0), c.id)) AS IdContacto,
                    ISNULL(c.Nombre_y_Apellido, '') AS NombreApellido,
                    ISNULL(c.Cargo, '') AS Cargo,
                    ISNULL(c.email, '') AS Email,
                    ISNULL(c.Telefono, '') AS Telefono,
                    ISNULL(c.Celular, '') AS Celular,
                    ISNULL(c.CuentaRel, '') AS CuentaRel
                FROM dbo.MA_CONTACTOS c
                WHERE (
                        ISNULL(c.Nombre_y_Apellido, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(c.email, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(c.Telefono, '') LIKE @TextoLike
                        OR ISNULL(c.Celular, '') LIKE @TextoLike
                        OR ISNULL(c.Cargo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    )
                    {activoFilter}
                    AND NOT EXISTS (
                        SELECT 1
                        FROM dbo.MA_CONTACTOS_CUENTAS rel
                        WHERE rel.IdContacto = ISNULL(NULLIF(c.idContacto, 0), c.id)
                          AND UPPER(LTRIM(RTRIM(rel.Cuenta))) = @Cuenta
                    )
                    AND UPPER(LTRIM(RTRIM(ISNULL(c.CuentaRel, '')))) <> @Cuenta
                ORDER BY
                    CASE WHEN ISNULL(c.Nombre_y_Apellido, '') LIKE @Texto + '%' THEN 0 ELSE 1 END,
                    ISNULL(c.Nombre_y_Apellido, ''),
                    c.id;
                """;

            var contactos = await cn.QueryAsync<CuentaComercialContactoCandidateDto>(
                new CommandDefinition(sql, new { Cuenta = cuenta, Texto = search, TextoLike = SearchTextHelper.LikeContains(search) }, cancellationToken: token));
            return contactos.ToList();
        }, "No se pudieron buscar contactos existentes para vincular.", ct);

    public Task<int> CreateContactoAsync(CuentaComercialTipo tipo, CuentaComercialContactoCreateRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "CreateContacto", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var normalized = NormalizeContactoRequest(request);
            if (string.IsNullOrWhiteSpace(normalized.CuentaCodigo))
                throw new InvalidOperationException($"No se recibió el {GetSingularLabel(tipo).ToLowerInvariant()} al que se vincula el contacto.");
            if (string.IsNullOrWhiteSpace(normalized.NombreApellido))
                throw new InvalidOperationException("Ingresá el nombre del contacto antes de guardar.");

            var descriptor = ResolveDescriptor(tipo);
            var cuenta = normalized.CuentaCodigo.Trim().ToUpperInvariant();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            var sqlTx = (SqlTransaction)tx;

            var accountExists = await ExistsAsync(cn, sqlTx, descriptor.ViewName, "CODIGO", cuenta, token);
            if (!accountExists)
                throw new InvalidOperationException($"El {GetSingularLabel(tipo).ToLowerInvariant()} seleccionado ya no existe en la base activa.");

            var hasActivoColumn = await ColumnExistsAsync(cn, sqlTx, "MA_CONTACTOS", "Activo", token);
            var insertSql = hasActivoColumn
                ? """
                INSERT INTO dbo.MA_CONTACTOS
                (
                    Nombre_y_Apellido,
                    Telefono,
                    Celular,
                    email,
                    Observaciones,
                    Cargo,
                    CuentaRel,
                    mailPGCB,
                    mailOT,
                    mailOC,
                    Activo
                )
                VALUES
                (
                    @NombreApellido,
                    @Telefono,
                    @Celular,
                    @Email,
                    @Observaciones,
                    @Cargo,
                    @Cuenta,
                    0,
                    0,
                    0,
                    1
                );
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """
                : """
                INSERT INTO dbo.MA_CONTACTOS
                (
                    Nombre_y_Apellido,
                    Telefono,
                    Celular,
                    email,
                    Observaciones,
                    Cargo,
                    CuentaRel,
                    mailPGCB,
                    mailOT,
                    mailOC
                )
                VALUES
                (
                    @NombreApellido,
                    @Telefono,
                    @Celular,
                    @Email,
                    @Observaciones,
                    @Cargo,
                    @Cuenta,
                    0,
                    0,
                    0
                );
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            var contactoParams = new
            {
                NombreApellido = normalized.NombreApellido,
                Telefono = DbNullable(normalized.Telefono),
                Celular = DbNullable(normalized.Celular),
                Email = DbNullable(normalized.Email),
                Observaciones = DbNullable(normalized.Observaciones),
                Cargo = DbNullable(normalized.Cargo),
                Cuenta = cuenta
            };
            var contactoId = await cn.ExecuteScalarAsync<int>(
                new CommandDefinition(insertSql, contactoParams, sqlTx, cancellationToken: token));

            const string linkSql = """
                UPDATE dbo.MA_CONTACTOS
                SET idContacto = @IdContacto
                WHERE id = @Id
                  AND ISNULL(idContacto, 0) = 0;

                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.MA_CONTACTOS_CUENTAS
                    WHERE IdContacto = @IdContacto
                      AND UPPER(LTRIM(RTRIM(Cuenta))) = @Cuenta
                )
                BEGIN
                    INSERT INTO dbo.MA_CONTACTOS_CUENTAS (IdContacto, Cuenta)
                    VALUES (@IdContacto, @Cuenta);
                END;
                """;

            await cn.ExecuteAsync(
                new CommandDefinition(
                    linkSql,
                    new { Id = contactoId, IdContacto = Convert.ToDouble(contactoId), Cuenta = cuenta },
                    sqlTx,
                    cancellationToken: token));

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                GetModuleLabel(tipo),
                "CreateContacto",
                "MA_CONTACTOS_CUENTAS",
                cuenta,
                $"Contacto vinculado al {GetSingularLabel(tipo).ToLowerInvariant()}.",
                new
                {
                    Cuenta = cuenta,
                    ContactoId = contactoId,
                    normalized.NombreApellido,
                    normalized.Email
                },
                token);

            return contactoId;
        }, "No se pudo crear el contacto vinculado.", ct);

    public Task LinkContactoAsync(CuentaComercialTipo tipo, CuentaComercialContactoLinkRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "LinkContacto", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var cuenta = request.CuentaCodigo?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cuenta))
                throw new InvalidOperationException($"No se recibió el {GetSingularLabel(tipo).ToLowerInvariant()} al que se vincula el contacto.");
            if (request.ContactoId <= 0)
                throw new InvalidOperationException("No se recibió el contacto a vincular.");

            var descriptor = ResolveDescriptor(tipo);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            var sqlTx = (SqlTransaction)tx;

            var accountExists = await ExistsAsync(cn, sqlTx, descriptor.ViewName, "CODIGO", cuenta, token);
            if (!accountExists)
                throw new InvalidOperationException($"El {GetSingularLabel(tipo).ToLowerInvariant()} seleccionado ya no existe en la base activa.");

            const string resolveSql = """
                SELECT TOP (1)
                    c.id AS Id,
                    CONVERT(int, ISNULL(NULLIF(c.idContacto, 0), c.id)) AS IdContacto,
                    ISNULL(c.Nombre_y_Apellido, '') AS NombreApellido
                FROM dbo.MA_CONTACTOS c
                WHERE c.id = @ContactoId;
                """;

            var contacto = await cn.QuerySingleOrDefaultAsync<CuentaComercialContactoCandidateDto>(
                new CommandDefinition(resolveSql, new { request.ContactoId }, sqlTx, cancellationToken: token));
            if (contacto is null || contacto.Id <= 0)
                throw new InvalidOperationException("El contacto seleccionado ya no existe en la base activa.");

            const string linkSql = """
                UPDATE dbo.MA_CONTACTOS
                SET idContacto = @IdContacto
                WHERE id = @Id
                  AND ISNULL(idContacto, 0) = 0;

                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.MA_CONTACTOS_CUENTAS
                    WHERE IdContacto = @IdContacto
                      AND UPPER(LTRIM(RTRIM(Cuenta))) = @Cuenta
                )
                BEGIN
                    INSERT INTO dbo.MA_CONTACTOS_CUENTAS (IdContacto, Cuenta)
                    VALUES (@IdContacto, @Cuenta);
                END;
                """;

            await cn.ExecuteAsync(
                new CommandDefinition(
                    linkSql,
                    new { Id = contacto.Id, IdContacto = Convert.ToDouble(contacto.IdContacto), Cuenta = cuenta },
                    sqlTx,
                    cancellationToken: token));

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                GetModuleLabel(tipo),
                "LinkContacto",
                "MA_CONTACTOS_CUENTAS",
                cuenta,
                $"Contacto existente vinculado al {GetSingularLabel(tipo).ToLowerInvariant()}.",
                new { Cuenta = cuenta, ContactoId = contacto.Id, contacto.IdContacto, contacto.NombreApellido },
                token);
        }, "No se pudo vincular el contacto existente.", ct);

    public Task UpdateContactoQuickAsync(CuentaComercialTipo tipo, CuentaComercialContactoQuickUpdateRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "UpdateContactoQuick", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var normalized = NormalizeContactoQuickRequest(request);
            if (normalized.Id <= 0)
                throw new InvalidOperationException("No se recibió el contacto a actualizar.");
            if (string.IsNullOrWhiteSpace(normalized.CuentaCodigo))
                throw new InvalidOperationException($"No se recibió el {GetSingularLabel(tipo).ToLowerInvariant()} vinculado al contacto.");

            var cuenta = normalized.CuentaCodigo.Trim().ToUpperInvariant();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var sql = """
                UPDATE c
                SET
                    c.email = @Email,
                    c.Telefono = @Telefono,
                    c.Celular = @Celular
                FROM dbo.MA_CONTACTOS c
                WHERE c.id = @Id
                  AND (
                        EXISTS (
                            SELECT 1
                            FROM dbo.MA_CONTACTOS_CUENTAS rel
                            WHERE rel.IdContacto = ISNULL(NULLIF(c.idContacto, 0), c.id)
                              AND UPPER(LTRIM(RTRIM(rel.Cuenta))) = @Cuenta
                        )
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.CuentaRel, '')))) = @Cuenta
                    );
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Id", normalized.Id);
            cmd.Parameters.AddWithValue("@Cuenta", cuenta);
            cmd.Parameters.AddWithValue("@Email", DbNullable(normalized.Email));
            cmd.Parameters.AddWithValue("@Telefono", DbNullable(normalized.Telefono));
            cmd.Parameters.AddWithValue("@Celular", DbNullable(normalized.Celular));
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("El contacto seleccionado ya no está vinculado a esta cuenta o no existe.");

            await appEvents.LogAuditAsync(
                GetModuleLabel(tipo),
                "UpdateContactoQuick",
                "MA_CONTACTOS",
                normalized.Id.ToString(CultureInfo.InvariantCulture),
                $"Datos rápidos de contacto actualizados desde {GetSingularLabel(tipo).ToLowerInvariant()}.",
                new { normalized.Id, Cuenta = cuenta, normalized.Email, normalized.Telefono, normalized.Celular },
                token);
        }, "No se pudieron guardar los datos rápidos del contacto.", ct);

    public Task<CuentaComercialViewSettingsDto> GetViewSettingsAsync(CuentaComercialTipo tipo, string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "GetViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return CreateDefaultViewSettings(tipo);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildViewConfigKey(tipo, userName);
            var sql = $"""
                SELECT TOP (1)
                    ISNULL(VALOR, ''),
                    ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Clave", configKey.ToUpperInvariant());
            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (!await rd.ReadAsync(token))
                return CreateDefaultViewSettings(tipo);

            var raw = ResolveStoredValue(GetString(rd, 0), GetString(rd, 1));
            if (string.IsNullOrWhiteSpace(raw))
                return CreateDefaultViewSettings(tipo);

            var parsed = JsonSerializer.Deserialize<CuentaComercialViewSettingsDto>(raw, JsonOptions);
            return NormalizeViewSettings(tipo, parsed);
        }, "No se pudo cargar la configuración de vista.", ct);

    public Task SaveViewSettingsAsync(CuentaComercialTipo tipo, string userName, CuentaComercialViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync(GetModuleLabel(tipo), "SaveViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la vista.");

            var normalized = NormalizeViewSettings(tipo, settings);
            var serialized = JsonSerializer.Serialize(normalized, JsonOptions);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var stored = SplitStoredValue(serialized);
            var configKey = BuildViewConfigKey(tipo, userName);
            var sql = $"""
                UPDATE dbo.TA_CONFIGURACION
                SET
                    VALOR = @Valor,
                    {detailColumn} = @ValorAux,
                    GRUPO = @Grupo,
                    FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION
                    (
                        CLAVE,
                        VALOR,
                        {detailColumn},
                        GRUPO,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Clave,
                        @Valor,
                        @ValorAux,
                        @Grupo,
                        GETDATE(),
                        GETDATE()
                    );
                END;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@ClaveNormalizada", configKey.ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Clave", configKey);
            cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
            cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
            cmd.Parameters.AddWithValue("@Grupo", tipo == CuentaComercialTipo.Cliente ? "CLIENTES" : "PROVEEDORES");
            await cmd.ExecuteNonQueryAsync(token);

            await appEvents.LogAuditAsync(
                GetModuleLabel(tipo),
                "SaveViewSettings",
                "TA_CONFIGURACION",
                configKey,
                $"Configuración de vista de {GetPluralLabel(tipo).ToLowerInvariant()} actualizada.",
                new { UserName = userName.Trim(), normalized.AgruparPor, Columnas = normalized.Columnas },
                token);
        }, "No se pudo guardar la configuración de vista.", ct);

    private static CuentaComercialContactoCreateRequest NormalizeContactoRequest(CuentaComercialContactoCreateRequest request)
        => new()
        {
            CuentaCodigo = request.CuentaCodigo?.Trim() ?? string.Empty,
            NombreApellido = request.NombreApellido?.Trim() ?? string.Empty,
            Cargo = request.Cargo?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty,
            Telefono = request.Telefono?.Trim() ?? string.Empty,
            Celular = request.Celular?.Trim() ?? string.Empty,
            Observaciones = request.Observaciones?.Trim() ?? string.Empty
        };

    private static CuentaComercialContactoQuickUpdateRequest NormalizeContactoQuickRequest(CuentaComercialContactoQuickUpdateRequest request)
        => new()
        {
            Id = request.Id,
            CuentaCodigo = request.CuentaCodigo?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty,
            Telefono = request.Telefono?.Trim() ?? string.Empty,
            Celular = request.Celular?.Trim() ?? string.Empty
        };

    private static CuentaComercialSaveRequest NormalizeRequest(CuentaComercialSaveRequest request)
        => new()
        {
            CodigoOriginal = request.CodigoOriginal?.Trim() ?? string.Empty,
            Codigo = request.Codigo?.Trim() ?? string.Empty,
            RazonSocial = request.RazonSocial?.Trim() ?? string.Empty,
            Contacto = request.Contacto?.Trim() ?? string.Empty,
            Calle = request.Calle?.Trim() ?? string.Empty,
            Numero = request.Numero?.Trim() ?? string.Empty,
            Piso = request.Piso?.Trim() ?? string.Empty,
            Departamento = request.Departamento?.Trim() ?? string.Empty,
            CodigoPostal = request.CodigoPostal?.Trim() ?? string.Empty,
            Localidad = request.Localidad?.Trim() ?? string.Empty,
            ProvinciaCodigo = request.ProvinciaCodigo?.Trim() ?? string.Empty,
            PaisCodigo = request.PaisCodigo?.Trim() ?? string.Empty,
            Telefono = request.Telefono?.Trim() ?? string.Empty,
            Fax = request.Fax?.Trim() ?? string.Empty,
            Mail = request.Mail?.Trim() ?? string.Empty,
            Web = request.Web?.Trim() ?? string.Empty,
            DocumentoTipoCodigo = request.DocumentoTipoCodigo?.Trim() ?? string.Empty,
            NumeroDocumento = request.NumeroDocumento?.Trim() ?? string.Empty,
            IvaCodigo = request.IvaCodigo?.Trim() ?? string.Empty,
            Observaciones = request.Observaciones?.Trim() ?? string.Empty,
            LimiteCredito = request.LimiteCredito,
            CondicionComercialCodigo = request.CondicionComercialCodigo?.Trim() ?? string.Empty,
            ListaPrecioCodigo = request.ListaPrecioCodigo?.Trim() ?? string.Empty,
            Clase = request.Clase,
            VendedorCodigo = request.VendedorCodigo?.Trim() ?? string.Empty,
            CuentaPrincipal = request.CuentaPrincipal?.Trim() ?? string.Empty,
            CodigoImputacion = request.CodigoImputacion?.Trim() ?? string.Empty,
            CodigoOpcional = request.CodigoOpcional?.Trim() ?? string.Empty,
            MonedaCodigo = request.MonedaCodigo?.Trim() ?? string.Empty,
            ClasificacionCodigo = request.ClasificacionCodigo?.Trim() ?? string.Empty,
            LugarEntrega = request.LugarEntrega?.Trim() ?? string.Empty,
            DatosPago = request.DatosPago?.Trim() ?? string.Empty,
            Manual = request.Manual?.Trim() ?? string.Empty,
            NotaCuenta = request.NotaCuenta?.Trim() ?? string.Empty,
            SituacionIb = request.SituacionIb?.Trim() ?? string.Empty,
            ConvenioIb = request.ConvenioIb?.Trim() ?? string.Empty,
            ConceptoIb = request.ConceptoIb?.Trim() ?? string.Empty,
            NroConstanciaIb = request.NroConstanciaIb?.Trim() ?? string.Empty,
            Bloqueado = request.Bloqueado,
            ExentoIvaServicios = request.ExentoIvaServicios,
            ExentoIvaArticulos = request.ExentoIvaArticulos,
            ExentoIvaOtros = request.ExentoIvaOtros,
            ProveedorLocal = request.ProveedorLocal,
            ProveedorCompartido = request.ProveedorCompartido,
            DescuentosCondicion = (request.DescuentosCondicion ?? [])
                .Where(item => item is not null)
                .Select(item => new CuentaComercialCondicionDescuentoDto
                {
                    CondicionCodigo = item.CondicionCodigo?.Trim() ?? string.Empty,
                    CondicionDescripcion = item.CondicionDescripcion?.Trim() ?? string.Empty,
                    Descuento1 = item.Descuento1,
                    Descuento2 = item.Descuento2,
                    Descuento3 = item.Descuento3,
                    Descuento4 = item.Descuento4,
                    Descuento5 = item.Descuento5
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.CondicionCodigo))
                .ToList()
        };

    private static void FillCuentaAdicParameters(
        SqlCommand cmd,
        CuentaComercialSaveRequest request,
        string documentoTipo,
        string iva,
        string pais,
        string provincia,
        string condicion,
        short clase,
        string motivoDefault,
        CuentaComercialTipo tipo)
    {
        cmd.Parameters.AddWithValue("@Codigo", request.Codigo.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Contacto", DbNullable(request.Contacto));
        cmd.Parameters.AddWithValue("@Calle", DbNullable(request.Calle));
        cmd.Parameters.AddWithValue("@Numero", DbNullable(request.Numero));
        cmd.Parameters.AddWithValue("@Piso", DbNullable(request.Piso));
        cmd.Parameters.AddWithValue("@Departamento", DbNullable(request.Departamento));
        cmd.Parameters.AddWithValue("@CodigoPostal", DbNullable(request.CodigoPostal));
        cmd.Parameters.AddWithValue("@Localidad", DbNullable(request.Localidad));
        cmd.Parameters.AddWithValue("@Provincia", provincia);
        cmd.Parameters.AddWithValue("@Pais", pais);
        cmd.Parameters.AddWithValue("@Telefono", DbNullable(request.Telefono));
        cmd.Parameters.AddWithValue("@Fax", DbNullable(request.Fax));
        cmd.Parameters.AddWithValue("@Mail", DbNullable(request.Mail));
        cmd.Parameters.AddWithValue("@DocumentoTipo", documentoTipo);
        cmd.Parameters.AddWithValue("@NumeroDocumento", request.NumeroDocumento);
        cmd.Parameters.AddWithValue("@Iva", iva);
        cmd.Parameters.AddWithValue("@Observaciones", DbNullable(request.Observaciones));
        cmd.Parameters.AddWithValue("@LimiteCredito", request.LimiteCredito.HasValue ? request.LimiteCredito.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@CondicionComercial", condicion);
        cmd.Parameters.AddWithValue("@IdLista", DbNullable(request.ListaPrecioCodigo));
        cmd.Parameters.AddWithValue("@Clase", clase);
        cmd.Parameters.AddWithValue("@IdVendedor", DbNullable(request.VendedorCodigo));
        cmd.Parameters.AddWithValue("@IdMotivoVta", tipo == CuentaComercialTipo.Cliente ? motivoDefault : DBNull.Value);
        cmd.Parameters.AddWithValue("@IdMotivoCpra", tipo == CuentaComercialTipo.Proveedor ? motivoDefault : DBNull.Value);
        cmd.Parameters.AddWithValue("@ExentoIvaServicios", request.ExentoIvaServicios);
        cmd.Parameters.AddWithValue("@ExentoIvaArticulos", request.ExentoIvaArticulos);
        cmd.Parameters.AddWithValue("@ExentoIvaOtros", request.ExentoIvaOtros);
        cmd.Parameters.AddWithValue("@LugarEntrega", DbNullable(request.LugarEntrega));
        cmd.Parameters.AddWithValue("@CodigoImputacion", DbNullable(request.CodigoImputacion));
        cmd.Parameters.AddWithValue("@DatosPago", DbNullable(request.DatosPago));
        cmd.Parameters.AddWithValue("@Web", DbNullable(request.Web));
        cmd.Parameters.AddWithValue("@ProveedorLocal", request.ProveedorLocal);
        cmd.Parameters.AddWithValue("@ProveedorCompartido", request.ProveedorCompartido);
        cmd.Parameters.AddWithValue("@Clasificacion", DbNullable(request.ClasificacionCodigo));
    }

    private static async Task LoadCuentaObservacionesAsync(SqlConnection cn, CuentaComercialDetailDto detail, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ISNULL(CAST(Nota AS nvarchar(max)), ''),
                ISNULL(SituacionIB, ''),
                ISNULL(Convenio, ''),
                ISNULL(Concepto, ''),
                ISNULL(NroConstancia, '')
            FROM dbo.MA_CUENTASOBS
            WHERE UPPER(LTRIM(RTRIM(Codigo))) = @Codigo
              AND ISNULL(Sucursal, 0) = 0;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Codigo", detail.Codigo.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return;

        detail.NotaCuenta = GetString(rd, 0);
        detail.SituacionIb = GetString(rd, 1);
        detail.ConvenioIb = GetString(rd, 2);
        detail.ConceptoIb = GetString(rd, 3);
        detail.NroConstanciaIb = GetString(rd, 4);
    }

    private static async Task<IReadOnlyList<CuentaComercialCondicionDescuentoDto>> LoadDescuentosProveedorAsync(SqlConnection cn, string codigo, CancellationToken ct)
    {
        const string sql = """
            SELECT
                ISNULL(d.IdCond_Cpra_Vta, ''),
                ISNULL(c.Descripcion, ''),
                d.PorDto1,
                d.PorDto2,
                d.PorDto3,
                d.PorDto4,
                d.PorDto5
            FROM dbo.C_PROVEEDORES_CCOMPRA d
            LEFT JOIN dbo.V_TA_Cpra_Vta c
                ON UPPER(LTRIM(RTRIM(c.IDCond_Cpra_Vta))) = UPPER(LTRIM(RTRIM(ISNULL(d.IdCond_Cpra_Vta, ''))))
            WHERE UPPER(LTRIM(RTRIM(d.IdProveedor))) = @Codigo
            ORDER BY ISNULL(c.Descripcion, ''), ISNULL(d.IdCond_Cpra_Vta, '');
            """;

        var items = new List<CuentaComercialCondicionDescuentoDto>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Codigo", codigo.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new CuentaComercialCondicionDescuentoDto
            {
                CondicionCodigo = GetString(rd, 0),
                CondicionDescripcion = GetString(rd, 1),
                Descuento1 = GetNullableDecimal(rd, 2),
                Descuento2 = GetNullableDecimal(rd, 3),
                Descuento3 = GetNullableDecimal(rd, 4),
                Descuento4 = GetNullableDecimal(rd, 5),
                Descuento5 = GetNullableDecimal(rd, 6)
            });
        }

        return items;
    }

    private static async Task UpsertCuentaObsAsync(SqlConnection cn, SqlTransaction tx, CuentaComercialSaveRequest request, CancellationToken ct)
    {
        var hasValues =
            !string.IsNullOrWhiteSpace(request.NotaCuenta) ||
            !string.IsNullOrWhiteSpace(request.SituacionIb) ||
            !string.IsNullOrWhiteSpace(request.ConvenioIb) ||
            !string.IsNullOrWhiteSpace(request.ConceptoIb) ||
            !string.IsNullOrWhiteSpace(request.NroConstanciaIb);

        if (!hasValues)
        {
            const string deleteSql = """
                DELETE FROM dbo.MA_CUENTASOBS
                WHERE UPPER(LTRIM(RTRIM(Codigo))) = @Codigo
                  AND ISNULL(Sucursal, 0) = 0;
                """;

            await using var deleteCmd = new SqlCommand(deleteSql, cn, tx);
            deleteCmd.Parameters.AddWithValue("@Codigo", request.Codigo.Trim().ToUpperInvariant());
            await deleteCmd.ExecuteNonQueryAsync(ct);
            return;
        }

        const string existsSql = """
            SELECT COUNT(1)
            FROM dbo.MA_CUENTASOBS
            WHERE UPPER(LTRIM(RTRIM(Codigo))) = @Codigo
              AND ISNULL(Sucursal, 0) = 0;
            """;

        await using var existsCmd = new SqlCommand(existsSql, cn, tx);
        existsCmd.Parameters.AddWithValue("@Codigo", request.Codigo.Trim().ToUpperInvariant());
        var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync(ct)) > 0;

        var sql = exists
            ? """
                UPDATE dbo.MA_CUENTASOBS
                SET
                    Nota = @Nota,
                    SituacionIB = @SituacionIb,
                    Convenio = @ConvenioIb,
                    Concepto = @ConceptoIb,
                    NroConstancia = @NroConstanciaIb
                WHERE UPPER(LTRIM(RTRIM(Codigo))) = @Codigo
                  AND ISNULL(Sucursal, 0) = 0;
                """
            : """
                INSERT INTO dbo.MA_CUENTASOBS
                (
                    Codigo,
                    Sucursal,
                    Nota,
                    SituacionIB,
                    Convenio,
                    Concepto,
                    NroConstancia
                )
                VALUES
                (
                    @Codigo,
                    0,
                    @Nota,
                    @SituacionIb,
                    @ConvenioIb,
                    @ConceptoIb,
                    @NroConstanciaIb
                );
                """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@Codigo", request.Codigo.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Nota", DbNullable(request.NotaCuenta));
        cmd.Parameters.AddWithValue("@SituacionIb", DbNullable(request.SituacionIb));
        cmd.Parameters.AddWithValue("@ConvenioIb", DbNullable(request.ConvenioIb));
        cmd.Parameters.AddWithValue("@ConceptoIb", DbNullable(request.ConceptoIb));
        cmd.Parameters.AddWithValue("@NroConstanciaIb", DbNullable(request.NroConstanciaIb));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReplaceDescuentosProveedorAsync(SqlConnection cn, SqlTransaction tx, CuentaComercialSaveRequest request, CancellationToken ct)
    {
        const string deleteSql = """
            DELETE FROM dbo.C_PROVEEDORES_CCOMPRA
            WHERE UPPER(LTRIM(RTRIM(IdProveedor))) = @Codigo;
            """;

        await using (var deleteCmd = new SqlCommand(deleteSql, cn, tx))
        {
            deleteCmd.Parameters.AddWithValue("@Codigo", request.Codigo.Trim().ToUpperInvariant());
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        if (request.DescuentosCondicion.Count == 0)
            return;

        const string insertSql = """
            INSERT INTO dbo.C_PROVEEDORES_CCOMPRA
            (
                IdProveedor,
                IdCond_Cpra_Vta,
                PorDto1,
                PorDto2,
                PorDto3,
                PorDto4,
                PorDto5
            )
            VALUES
            (
                @Codigo,
                @CondicionCodigo,
                @Descuento1,
                @Descuento2,
                @Descuento3,
                @Descuento4,
                @Descuento5
            );
            """;

        foreach (var item in request.DescuentosCondicion)
        {
            await using var insertCmd = new SqlCommand(insertSql, cn, tx);
            insertCmd.Parameters.AddWithValue("@Codigo", request.Codigo.Trim().ToUpperInvariant());
            insertCmd.Parameters.AddWithValue("@CondicionCodigo", item.CondicionCodigo.Trim());
            insertCmd.Parameters.AddWithValue("@Descuento1", DbNullable(item.Descuento1));
            insertCmd.Parameters.AddWithValue("@Descuento2", DbNullable(item.Descuento2));
            insertCmd.Parameters.AddWithValue("@Descuento3", DbNullable(item.Descuento3));
            insertCmd.Parameters.AddWithValue("@Descuento4", DbNullable(item.Descuento4));
            insertCmd.Parameters.AddWithValue("@Descuento5", DbNullable(item.Descuento5));
            await insertCmd.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<string> GenerateNextCodigoAsync(SqlConnection cn, SqlTransaction? tx, CuentaComercialTipo tipo, CancellationToken ct)
    {
        var descriptor = ResolveDescriptor(tipo);
        var titulo = await ReadConfigValueAsync(cn, tx, descriptor.TituloConfigKey, ct);
        if (string.IsNullOrWhiteSpace(titulo))
            throw new InvalidOperationException($"No está configurada la clave {descriptor.TituloConfigKey} en TA_CONFIGURACION.");

        var sql = $"""
            SELECT MAX(CODIGO)
            FROM dbo.{descriptor.ViewName}
            WHERE CODIGO LIKE @Titulo + '%';
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@Titulo", titulo);
        var raw = Convert.ToString(await cmd.ExecuteScalarAsync(ct)) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return titulo + "0001";

        var suffix = raw.Length > titulo.Length ? raw[titulo.Length..] : "0";
        if (!long.TryParse(suffix, out var current))
            current = 0;

        var next = current + 1;
        var digits = Math.Max(4, suffix.Length);
        return titulo + next.ToString(new string('0', digits));
    }

    private async Task<string> ResolveTipoVistaAsync(SqlConnection cn, SqlTransaction? tx, string codigo, CuentaComercialDescriptor descriptor, CancellationToken ct)
    {
        var sql = """
            SELECT TOP (1)
                ISNULL(v.TipoVista, '')
            FROM dbo.TA_RANGOS_DATOS_ADIC r
            LEFT JOIN dbo.TA_VISTAS v
                ON UPPER(LTRIM(RTRIM(v.Nombre))) = UPPER(LTRIM(RTRIM(ISNULL(r.Vista, ''))))
            WHERE @Codigo >= ISNULL(r.[CUENTA-DESDE], '')
              AND @Codigo <= ISNULL(r.[CUENTA-HASTA], '')
            ORDER BY r.[CUENTA-DESDE];
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@Codigo", codigo);
        var tipoVista = Convert.ToString(await cmd.ExecuteScalarAsync(ct)) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(tipoVista))
            return tipoVista.Trim();

        return descriptor.TipoVistaFallback;
    }

    private static async Task<IReadOnlyList<CuentaComercialLookupOptionDto>> ReadLookupListAsync(SqlDataReader rd, CancellationToken ct)
    {
        var items = new List<CuentaComercialLookupOptionDto>();
        while (await rd.ReadAsync(ct))
        {
            items.Add(new CuentaComercialLookupOptionDto
            {
                Codigo = GetString(rd, 0),
                Descripcion = GetString(rd, 1)
            });
        }

        return items;
    }

    private static CuentaComercialDescriptor ResolveDescriptor(CuentaComercialTipo tipo)
        => tipo == CuentaComercialTipo.Cliente
            ? new CuentaComercialDescriptor("VT_CLIENTES", "TituloClientes", "UsaListasDePrecios", "V", "V", "CL", "Clientes", "Cliente")
            : new CuentaComercialDescriptor("VT_PROVEEDORES", "TituloProveedores", "UsaListasDePreciosProveedores", "C", "C", "PR", "Proveedores", "Proveedor");

    private static string GetModuleLabel(CuentaComercialTipo tipo)
        => tipo == CuentaComercialTipo.Cliente ? "Clientes" : "Proveedores";

    private static string GetPluralLabel(CuentaComercialTipo tipo)
        => tipo == CuentaComercialTipo.Cliente ? "Clientes" : "Proveedores";

    private static string GetSingularLabel(CuentaComercialTipo tipo)
        => tipo == CuentaComercialTipo.Cliente ? "Cliente" : "Proveedor";

    private static string BuildViewConfigKey(CuentaComercialTipo tipo, string userName)
    {
        var normalized = userName.Trim().ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        var prefix = tipo == CuentaComercialTipo.Cliente ? "USUVIEW-CLIENTES-" : "USUVIEW-PROVEEDORES-";
        return $"{prefix}{hash[..24]}";
    }

    private static CuentaComercialViewSettingsDto CreateDefaultViewSettings(CuentaComercialTipo tipo)
        => new()
        {
            AgruparPor = CuentaComercialViewGroupKeys.None,
            Columnas =
            [
                new() { Key = CuentaComercialViewColumnKeys.Codigo, Label = "Código", Visible = true, Order = 0 },
                new() { Key = CuentaComercialViewColumnKeys.RazonSocial, Label = tipo == CuentaComercialTipo.Cliente ? "Cliente" : "Proveedor", Visible = true, Order = 1 },
                new() { Key = CuentaComercialViewColumnKeys.Contacto, Label = "Contacto", Visible = true, Order = 2 },
                new() { Key = CuentaComercialViewColumnKeys.Localidad, Label = "Localidad", Visible = true, Order = 3 },
                new() { Key = CuentaComercialViewColumnKeys.Provincia, Label = "Provincia", Visible = true, Order = 4 },
                new() { Key = CuentaComercialViewColumnKeys.Telefono, Label = "Teléfono", Visible = true, Order = 5 },
                new() { Key = CuentaComercialViewColumnKeys.Mail, Label = "Email", Visible = true, Order = 6 },
                new() { Key = CuentaComercialViewColumnKeys.Documento, Label = "CUIT / Doc.", Visible = true, Order = 7 },
                new() { Key = CuentaComercialViewColumnKeys.Iva, Label = "Cond. IVA", Visible = false, Order = 8 },
                new() { Key = CuentaComercialViewColumnKeys.Vendedor, Label = "Vendedor", Visible = false, Order = 9 },
                new() { Key = CuentaComercialViewColumnKeys.Lista, Label = "Lista", Visible = false, Order = 10 },
                new() { Key = CuentaComercialViewColumnKeys.Bloqueado, Label = "Bloqueado", Visible = true, Order = 11 }
            ]
        };

    private static CuentaComercialViewSettingsDto NormalizeViewSettings(CuentaComercialTipo tipo, CuentaComercialViewSettingsDto? settings)
    {
        var defaults = CreateDefaultViewSettings(tipo);
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var normalized = new CuentaComercialViewSettingsDto
        {
            AgruparPor = settings.AgruparPor switch
            {
                CuentaComercialViewGroupKeys.Activo => CuentaComercialViewGroupKeys.Activo,
                CuentaComercialViewGroupKeys.Provincia => CuentaComercialViewGroupKeys.Provincia,
                CuentaComercialViewGroupKeys.Bloqueado => CuentaComercialViewGroupKeys.Bloqueado,
                _ => CuentaComercialViewGroupKeys.None
            },
            Columnas = defaults.Columnas
                .Select(defaultCol =>
                {
                    if (!incoming.TryGetValue(defaultCol.Key, out var source))
                    {
                        return new CuentaComercialViewColumnDto
                        {
                            Key = defaultCol.Key,
                            Label = defaultCol.Label,
                            Visible = defaultCol.Visible,
                            Order = defaultCol.Order
                        };
                    }

                    return new CuentaComercialViewColumnDto
                    {
                        Key = defaultCol.Key,
                        Label = defaultCol.Label,
                        Visible = source.Visible,
                        Order = source.Order
                    };
                })
                .OrderBy(c => c.Order)
                .Select((column, index) =>
                {
                    column.Order = index;
                    return column;
                })
                .ToList()
        };

        if (!normalized.Columnas.Any(c => c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150 ? (string.Empty, normalized) : (normalized, string.Empty);
    }

    private async Task<string> ReadConfigValueAsync(SqlConnection cn, SqlTransaction? tx, string key, CancellationToken ct)
    {
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@Clave", key.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return string.Empty;

        return ResolveStoredValue(GetString(rd, 0), GetString(rd, 1));
    }

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END, name;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static async Task<bool> ExistsAsync(SqlConnection cn, SqlTransaction? tx, string tableName, string fieldName, string value, CancellationToken ct)
    {
        var sql = $"""
            SELECT COUNT(1)
            FROM dbo.{tableName}
            WHERE UPPER(LTRIM(RTRIM({fieldName}))) = @Value;
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@Value", value.Trim().ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, SqlTransaction? tx, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@ObjectName)
              AND UPPER(name) = @ColumnName;
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@ObjectName", $"dbo.{tableName}");
        cmd.Parameters.AddWithValue("@ColumnName", columnName.Trim().ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<string> ReadConfigurableDefaultAsync(SqlConnection cn, SqlTransaction? tx, string requestedValue, string fallbackValue, CancellationToken ct)
    {
        await Task.CompletedTask;
        return string.IsNullOrWhiteSpace(requestedValue) ? fallbackValue : requestedValue.Trim();
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index));

    private static int GetInt(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index));

    private static decimal? GetNullableDecimal(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? null : Convert.ToDecimal(rd.GetValue(index));

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static object DbNullable(decimal? value)
        => value.HasValue ? value.Value : DBNull.Value;

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ExecuteLoggedAsync(
        string module,
        string action,
        Func<CancellationToken, Task> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private sealed record CuentaComercialDescriptor(
        string ViewName,
        string TituloConfigKey,
        string UsaListasConfigKey,
        string PriceListType,
        string CondicionHabilitado,
        string TipoVistaFallback,
        string ModulePlural,
        string ModuleSingular);
}
