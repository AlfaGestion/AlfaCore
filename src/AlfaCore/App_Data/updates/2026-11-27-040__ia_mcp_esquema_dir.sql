-- MCP de consulta con IA (AlfaCore.Mcp): esquema `dir` de SOLO LECTURA para que Claude / ChatGPT consulten esta base.
-- Reune los scripts manuales 001-004, 012, 013 y 015 de docs/base-datos/sql-referencia/dir-mcp (generado a partir de ellos).
-- Corre en TODAS las bases, como cualquier actualizacion; es inofensivo: crea el esquema dir con vistas de lectura, la tabla de
-- auditoria IA_LOG_CONSULTAS y claves IA_MCP_* que no pisan valores existentes. Nadie las usa hasta que ALFA NET habilita la base
-- para el MCP (login de solo lectura + habilitar-base): el login, por llevar contrasena, NO va aca (005_login_solo_lectura.sql).
-- Si una vista no se puede crear en esta base (version vieja sin alguna tabla o columna), se saltea SIN frenar la actualizacion
-- y el motivo queda en TA_CONFIGURACION, clave IA_MCP_ESQUEMA_ERRORES (vacia = todo bien). Compatible con nivel 80/100.
SET NOCOUNT ON;
GO

-- Esquema dir, propiedad de dbo: las vistas heredan el ownership chaining y el login del MCP lee sin permisos sobre las tablas.
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'dir')
    EXEC(N'CREATE SCHEMA dir AUTHORIZATION dbo');
GO

-- Auditoria de consultas del MCP (en dbo a proposito: consultar_sql, limitado a dir.*, no la puede leer).
IF OBJECT_ID(N'dbo.IA_LOG_CONSULTAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IA_LOG_CONSULTAS
    (
        Id          bigint IDENTITY(1,1) NOT NULL,
        Fecha       datetime       NOT NULL CONSTRAINT DF_IA_LOG_CONSULTAS_Fecha DEFAULT (GETDATE()),
        Usuario     nvarchar(100)  NULL,      -- hoy: usuario fijo del MCP; con OAuth (etapa 5): claim del token
        Herramienta nvarchar(60)   NOT NULL,  -- ventas_resumen, ranking_articulos, consultar_sql, ...
        Parametros  nvarchar(max)  NULL,      -- JSON de argumentos (se omite si IA_MCP_LOG_PARAMETROS <> SI)
        DuracionMs  int            NULL,
        Filas       int            NULL,
        Error       nvarchar(max)  NULL,      -- NULL = ejecucion correcta
        CONSTRAINT PK_IA_LOG_CONSULTAS PRIMARY KEY CLUSTERED (Id)
    );
    CREATE NONCLUSTERED INDEX IX_IA_LOG_CONSULTAS_Fecha ON dbo.IA_LOG_CONSULTAS (Fecha DESC);
END
GO

-- Claves IA_MCP_* (insertan solo las que faltan; nunca pisan).
DECLARE @k TABLE (Clave nvarchar(100) PRIMARY KEY, Valor nvarchar(300), Descripcion nvarchar(500));
INSERT INTO @k (Clave, Valor, Descripcion) VALUES
 (N'IA_MCP_HABILITADO',                N'SI',      N'Interruptor general del servidor MCP. NO = rechaza todas las consultas.'),
 (N'IA_MCP_MAX_FILAS',                 N'500',     N'Tope duro de filas que devuelve cualquier herramienta (incluida consultar_sql).'),
 (N'IA_MCP_TOP_MAXIMO',                N'100',     N'Maximo permitido para el parametro top de los rankings.'),
 (N'IA_MCP_TIMEOUT_SEG',               N'60',      N'Segundos maximos por consulta SQL. cuenta corriente y stock tardan 10-15 s en bases grandes.'),
 (N'IA_MCP_TC_PROFORMA',               N'FP,NCFP', N'Tipos de comprobante proforma (afectan cuenta corriente pero no el libro de IVA), separados por coma. Los usa dir.vw_Ventas.'),
 (N'IA_MCP_INCLUIR_PROFORMAS',         N'SI',      N'SI = las herramientas de ventas incluyen proformas (son ventas reales no fiscales). NO = solo comprobantes del libro de IVA.'),
 (N'IA_MCP_CONSULTAR_SQL_HABILITADO',  N'SI',      N'SI = permite la herramienta consultar_sql (solo SELECT sobre el esquema dir).'),
 (N'IA_MCP_LOG_PARAMETROS',            N'SI',      N'SI = IA_LOG_CONSULTAS guarda los parametros de cada llamada. NO = solo herramienta, duracion y filas.'),
 (N'IA_MCP_ADVERTIR_SIN_COSTO_PCT',    N'5',       N'Si el porcentaje de lineas sin costo supera este valor, las herramientas de margen agregan una advertencia.'),
 (N'IA_MCP_LECTURA_SIN_BLOQUEOS',      N'SI',      N'SI = el MCP lee sin tomar bloqueos (READ UNCOMMITTED) para no frenar las escrituras de Alfa Gestion/AlfaCore; puede ver un comprobante a medio grabar. NO = aislamiento normal. En ambos casos pierde siempre ante un deadlock.');

INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
SELECT N'IA_MCP', k.Clave, k.Valor, k.Descripcion, GETDATE()
FROM @k k
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE c.CLAVE = k.Clave);
GO
DECLARE @k TABLE (Clave nvarchar(100) PRIMARY KEY, Valor nvarchar(300), Descripcion nvarchar(500), Aux nvarchar(max));
INSERT INTO @k (Clave, Valor, Descripcion, Aux) VALUES
 (N'IA_MCP_OAUTH_ACCESS_TOKEN_MIN', N'60',  N'Vida del access token en minutos (5-1440). Un usuario desactivado deja de poder renovar al vencer.', NULL),
 (N'IA_MCP_OAUTH_REFRESH_DIAS',     N'30',  N'Vida del refresh token en dias (1-365). Pasado ese tiempo sin renovar hay que volver a iniciar sesion.', NULL),
 (N'IA_MCP_OAUTH_CODIGO_SEG',       N'60',  N'Segundos de validez del codigo de autorizacion (10-300).', NULL),
 (N'IA_MCP_OAUTH_MAX_INTENTOS',     N'5',   N'Intentos de login fallidos antes de bloquear el usuario (3-20).', NULL),
 (N'IA_MCP_OAUTH_BLOQUEO_MIN',      N'15',  N'Minutos de bloqueo del usuario tras superar los intentos (1-1440).', NULL),
 (N'IA_MCP_OAUTH_MAX_CLIENTES',     N'500', N'Maximo de clientes OAuth que se pueden registrar dinamicamente.', NULL),
 (N'IA_MCP_OAUTH_REQUIERE_TOTP',    N'SI',  N'SI = el login exige segundo factor (codigo de la app autenticadora). Un usuario sin segundo factor configurado NO puede ingresar. NO solo para pruebas.', NULL),
 (N'IA_MCP_OAUTH_REDIRECTS',        N'',    N'Lista blanca de redirect URIs que un cliente puede registrar (una por linea o separadas por coma). http solo en loopback; puerto comodin solo en loopback (http://localhost:*/callback).',
  N'https://claude.ai/api/mcp/auth_callback
https://claude.com/api/mcp/auth_callback
https://chatgpt.com/connector_platform_oauth_redirect
http://localhost:*/callback
http://127.0.0.1:*/callback');

INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, ValorAux, FechaHora_Grabacion)
SELECT N'IA_MCP', k.Clave, k.Valor, k.Descripcion, k.Aux, GETDATE()
FROM @k k
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE c.CLAVE = k.Clave);
GO
DECLARE @k TABLE (Clave nvarchar(100) PRIMARY KEY, Valor nvarchar(300), Descripcion nvarchar(500));
INSERT INTO @k (Clave, Valor, Descripcion) VALUES
 (N'IA_MCP_OAUTH_EMAIL_HABILITADO',     N'SI', N'SI = la pantalla de conexion pide el mail y manda un codigo de 6 digitos (clientes). NO = solo usuario y clave. Requiere el SMTP del servidor (Mcp__Email__*).'),
 (N'IA_MCP_OAUTH_EMAIL_CODIGO_MIN',     N'10', N'Minutos de validez del codigo enviado por mail.'),
 (N'IA_MCP_OAUTH_EMAIL_MAX_ENVIOS',     N'3',  N'Maximo de codigos por mail cada 15 minutos (evita usar el formulario para llenar casillas).'),
 (N'IA_MCP_OAUTH_EMAIL_AVISO_CONEXION', N'SI', N'SI = cada conexion nueva manda un mail de aviso al usuario (si no fue el, avisa a ALFA NET).');

INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
SELECT N'IA_MCP', k.Clave, k.Valor, k.Descripcion, GETDATE()
FROM @k k
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE c.CLAVE = k.Clave);
GO
INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
SELECT N'IA_MCP', N'IA_MCP_TC_PROFORMA_COMPRAS', N'FPC,NCCP',
       N'Tipos de comprobante de COMPRA proforma (no fiscales), separados por coma. Los usa dir.vw_Compras; se incluyen segun IA_MCP_INCLUIR_PROFORMAS.',
       GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE = N'IA_MCP_TC_PROFORMA_COMPRAS');
GO

IF OBJECT_ID(N'tempdb..#mcp_errores') IS NOT NULL DROP TABLE #mcp_errores;
CREATE TABLE #mcp_errores (Vista nvarchar(128) NOT NULL, Error nvarchar(2000) NULL);
GO

-- dir.vw_Config: unica puerta del MCP a TA_CONFIGURACION (solo claves IA_MCP_*).
-- Normaliza VALOR / ValorAux (ntext) para que el MCP no repita esa logica.
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_Config', N'V') IS NOT NULL DROP VIEW dir.vw_Config;
    EXEC(N'CREATE VIEW dir.vw_Config
AS
SELECT
    LTRIM(RTRIM(CLAVE)) AS Clave,
    CASE WHEN LTRIM(RTRIM(ISNULL(VALOR, N''''))) <> N'''' THEN LTRIM(RTRIM(VALOR))
         ELSE LTRIM(RTRIM(ISNULL(CAST(ValorAux AS nvarchar(max)), N''''))) END AS Valor,
    LTRIM(RTRIM(GRUPO)) AS Grupo
FROM dbo.TA_CONFIGURACION
WHERE CLAVE LIKE N''IA[_]MCP[_]%''');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_Config', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_Ventas: UNA FILA POR LINEA VENDIDA, con signo aplicado.
--   Venta = comprobante de V_TA_Cpte.SISTEMA='VENTAS' con debe/haber, con lineas de articulos, no anulado, y
--           (figura en el libro de IVA: MV_ASIENTOS.LIVA_TIPO='VENTAS')  o  (es proforma segun IA_MCP_TC_PROFORMA).
--   Quedan afuera cobranzas (sin lineas), remitos, pedidos, presupuestos y anulados.
--   Signo: V_TA_Cpte.DEBEHABER 'H' => -1 (notas de credito), si no +1. Sin codigos de TC hardcodeados.
--   Cantidad, NetoSinIva, CostoTotal y Margen ya vienen con signo.
--   SinCosto=1 marca lineas sin costo registrado (su margen no es confiable).
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_Ventas', N'V') IS NOT NULL DROP VIEW dir.vw_Ventas;
    EXEC(N'CREATE VIEW dir.vw_Ventas
AS
SELECT
    x.Tc, x.TipoComprobante, x.Comprobante, x.Fecha, x.Anio, x.Mes, x.AnioMes,
    x.UnidadNegocio, x.ClienteCodigo, x.ClienteNombre, x.VendedorCodigo, x.VendedorNombre,
    x.ArticuloCodigo, x.ArticuloDescripcion, x.RubroCodigo, x.RubroNombre,
    x.EsNotaCredito, x.EsProforma, x.Signo, x.Moneda, x.Unidad,
    CAST(x.Signo * x.CantidadRaw AS decimal(18,4)) AS Cantidad,
    CAST(x.Signo * x.CantidadRaw * x.PrecioNetoUnit * (1 - x.PorcDto / 100.0) AS decimal(18,2)) AS NetoSinIva,
    CAST(x.Signo * x.CantidadRaw * x.CostoUnit AS decimal(18,2)) AS CostoTotal,
    CAST(x.Signo * x.CantidadRaw * (x.PrecioNetoUnit * (1 - x.PorcDto / 100.0) - x.CostoUnit) AS decimal(18,2)) AS Margen,
    x.SinCosto
FROM (
    SELECT
        LTRIM(RTRIM(c.TC))                              AS Tc,
        LTRIM(RTRIM(t.Descripcion))                     AS TipoComprobante,
        LTRIM(RTRIM(c.IDCOMPROBANTE))                   AS Comprobante,
        c.FECHA                                         AS Fecha,
        YEAR(c.FECHA)                                   AS Anio,
        MONTH(c.FECHA)                                  AS Mes,
        CONVERT(char(7), c.FECHA, 120)                  AS AnioMes,
        LTRIM(RTRIM(ISNULL(c.UNEGOCIO, N'''')))           AS UnidadNegocio,
        LTRIM(RTRIM(c.CUENTA))                          AS ClienteCodigo,
        LTRIM(RTRIM(c.NOMBRE))                          AS ClienteNombre,
        LTRIM(RTRIM(ISNULL(c.IdVendedor, N'''')))         AS VendedorCodigo,
        ISNULL(LTRIM(RTRIM(v.Nombre)), N''Sin vendedor'') AS VendedorNombre,
        LTRIM(RTRIM(i.IDARTICULO))                      AS ArticuloCodigo,
        LTRIM(RTRIM(i.DESCRIPCION))                     AS ArticuloDescripcion,
        LTRIM(RTRIM(ISNULL(a.IDRUBRO, N'''')))            AS RubroCodigo,
        ISNULL(LTRIM(RTRIM(r.Descripcion)), N''Sin rubro'') AS RubroNombre,
        CASE WHEN t.DebeHaber = N''H'' THEN 1 ELSE 0 END  AS EsNotaCredito,
        CASE WHEN CHARINDEX(N'','' + LTRIM(RTRIM(c.TC)) + N'','', cfg.Lista) > 0 THEN 1 ELSE 0 END AS EsProforma,
        CASE WHEN t.DebeHaber = N''H'' THEN -1 ELSE 1 END AS Signo,
        LTRIM(RTRIM(ISNULL(c.Moneda, N'''')))             AS Moneda,
        LTRIM(RTRIM(ISNULL(i.IDUNIDAD, N'''')))           AS Unidad,
        ISNULL(i.CANTIDAD, 0)                           AS CantidadRaw,
        ISNULL(i.IMPORTE_S_IVA, 0)                      AS PrecioNetoUnit,
        ISNULL(i.PorcDto, 0)                            AS PorcDto,
        ISNULL(i.COSTO, 0)                              AS CostoUnit,
        CASE WHEN ISNULL(i.COSTO, 0) > 0 THEN 0 ELSE 1 END AS SinCosto
    FROM dbo.V_MV_Cpte c
    INNER JOIN dbo.V_MV_CpteInsumos i
            ON i.TC = c.TC AND i.IDCOMPROBANTE = c.IDCOMPROBANTE AND i.IDCOMPLEMENTO = c.IDCOMPLEMENTO
    INNER JOIN (SELECT CODIGO, MAX(DEBEHABER) AS DebeHaber, MAX(DESCRIPCION) AS Descripcion
                FROM dbo.V_TA_Cpte
                WHERE UPPER(SISTEMA) = N''VENTAS'' AND DEBEHABER IN (N''D'', N''H'')
                GROUP BY CODIGO) t
            ON t.CODIGO = c.TC
    LEFT JOIN (SELECT TOP 1 N'','' + REPLACE(ISNULL(VALOR, N''''), N'' '', N'''') + N'','' AS Lista
               FROM dbo.TA_CONFIGURACION WHERE CLAVE = N''IA_MCP_TC_PROFORMA'') cfg
            ON 1 = 1
    LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = i.IDARTICULO
    LEFT JOIN dbo.V_TA_Rubros r    ON r.IdRubro = a.IDRUBRO
    LEFT JOIN dbo.V_TA_VENDEDORES v ON v.IdVendedor = c.IdVendedor
    WHERE c.ANULADA = 0
      AND ( CHARINDEX(N'','' + LTRIM(RTRIM(c.TC)) + N'','', cfg.Lista) > 0
            OR EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS m
                       WHERE m.TC = c.TC AND m.CUENTA = c.CUENTA
                         AND m.SUCURSAL = c.SUCURSAL AND m.NUMERO = c.NUMERO AND m.LETRA = c.LETRA
                         AND m.LIVA_TIPO = N''VENTAS'') )
) x');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_Ventas', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_Articulos: maestro de articulos. CostoActual es el costo vigente HOY (no el historico de venta).
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_Articulos', N'V') IS NOT NULL DROP VIEW dir.vw_Articulos;
    EXEC(N'CREATE VIEW dir.vw_Articulos
AS
SELECT
    LTRIM(RTRIM(a.IDARTICULO))                        AS ArticuloCodigo,
    LTRIM(RTRIM(a.DESCRIPCION))                       AS Descripcion,
    LTRIM(RTRIM(ISNULL(a.IDRUBRO, N'''')))              AS RubroCodigo,
    ISNULL(LTRIM(RTRIM(r.Descripcion)), N''Sin rubro'') AS RubroNombre,
    LTRIM(RTRIM(ISNULL(a.IdFamilia, N'''')))            AS FamiliaCodigo,
    LTRIM(RTRIM(ISNULL(a.IDUNIDAD, N'''')))             AS Unidad,
    LTRIM(RTRIM(ISNULL(a.Moneda, N'''')))               AS Moneda,
    ISNULL(a.COSTO, 0)                                AS CostoActual,
    ISNULL(a.PRECIO1, 0)                              AS Precio1,
    ISNULL(a.PRECIO2, 0)                              AS Precio2,
    ISNULL(a.PRECIO3, 0)                              AS Precio3,
    ISNULL(a.PUNTOPEDIDO, 0)                          AS PuntoPedido,
    ISNULL(a.PUNTOMAXIMO, 0)                          AS PuntoMaximo,
    a.SUSPENDIDO                                      AS Suspendido
FROM dbo.V_MA_ARTICULOS a
LEFT JOIN dbo.V_TA_Rubros r ON r.IdRubro = a.IDRUBRO');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_Articulos', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_Clientes: clientes (y particulares). Sin telefono ni mail a proposito (no hacen falta para analisis).
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_Clientes', N'V') IS NOT NULL DROP VIEW dir.vw_Clientes;
    EXEC(N'CREATE VIEW dir.vw_Clientes
AS
SELECT
    LTRIM(RTRIM(cl.CODIGO))                           AS ClienteCodigo,
    LTRIM(RTRIM(cl.RAZON_SOCIAL))                     AS RazonSocial,
    LTRIM(RTRIM(ISNULL(cl.NUMERO_DOCUMENTO, N'''')))    AS Documento,
    LTRIM(RTRIM(ISNULL(cl.IVA, N'''')))                 AS CondicionIva,
    LTRIM(RTRIM(ISNULL(cl.LOCALIDAD, N'''')))           AS Localidad,
    LTRIM(RTRIM(ISNULL(cl.PROVINCIA, N'''')))           AS Provincia,
    LTRIM(RTRIM(ISNULL(cl.IdVendedor, N'''')))          AS VendedorCodigo,
    ISNULL(LTRIM(RTRIM(v.Nombre)), N''Sin vendedor'')   AS VendedorNombre,
    ISNULL(cl.Limite_Credito, 0)                      AS LimiteCredito,
    cl.Dada_De_Baja                                   AS DadoDeBaja
FROM dbo.Vt_Clientes cl
LEFT JOIN dbo.V_TA_VENDEDORES v ON v.IdVendedor = cl.IdVendedor');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_Clientes', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_CuentaCorriente: saldos abiertos por comprobante (fuente oficial VE_CPTES_SALDOS_VENTAS).
-- Saldo > 0 = el cliente debe; creditos pendientes (NC, anticipos) vienen con signo negativo.
-- DiasVencido > 0 = vencido hace esos dias; <= 0 = todavia no vence.
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_CuentaCorriente', N'V') IS NOT NULL DROP VIEW dir.vw_CuentaCorriente;
    EXEC(N'CREATE VIEW dir.vw_CuentaCorriente
AS
SELECT
    y.ClienteCodigo, y.ClienteNombre, y.VendedorCodigo, y.Tc, y.Comprobante, y.Fecha, y.Vencimiento,
    y.Saldo, y.DiasVencido,
    CASE WHEN y.DiasVencido <= 0  THEN N''A vencer''
         WHEN y.DiasVencido <= 30 THEN N''Vencido 1-30''
         WHEN y.DiasVencido <= 60 THEN N''Vencido 31-60''
         WHEN y.DiasVencido <= 90 THEN N''Vencido 61-90''
         ELSE N''Vencido mas de 90'' END AS Tramo
FROM (
    SELECT
        LTRIM(RTRIM(s.CUENTA))                              AS ClienteCodigo,
        ISNULL(LTRIM(RTRIM(cl.RAZON_SOCIAL)), N'''')          AS ClienteNombre,
        LTRIM(RTRIM(ISNULL(cl.IdVendedor, N'''')))            AS VendedorCodigo,
        LTRIM(RTRIM(s.TC))                                  AS Tc,
        LTRIM(RTRIM(s.SUCURSAL)) + N''-'' + LTRIM(RTRIM(s.NUMERO)) + LTRIM(RTRIM(s.LETRA)) AS Comprobante,
        s.FECHA                                             AS Fecha,
        s.VENCIMIENTO                                       AS Vencimiento,
        CAST(s.SALDO AS decimal(18,2))                      AS Saldo,
        DATEDIFF(day, s.VENCIMIENTO, GETDATE())             AS DiasVencido
    FROM dbo.VE_CPTES_SALDOS_VENTAS s
    LEFT JOIN dbo.Vt_Clientes cl ON cl.CODIGO = s.CUENTA
) y');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_CuentaCorriente', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_Stock: existencia por articulo y deposito (V_MV_Stock.Cantidad ya viene con signo, en unidad base).
-- PuntoPedido es por articulo: para "stock critico" hay que sumar el Stock de todos los depositos del articulo.
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_Stock', N'V') IS NOT NULL DROP VIEW dir.vw_Stock;
    EXEC(N'CREATE VIEW dir.vw_Stock
AS
SELECT
    LTRIM(RTRIM(s.IDArticulo))                          AS ArticuloCodigo,
    LTRIM(RTRIM(a.DESCRIPCION))                         AS Descripcion,
    ISNULL(LTRIM(RTRIM(r.Descripcion)), N''Sin rubro'')   AS RubroNombre,
    LTRIM(RTRIM(ISNULL(s.IdDeposito, N'''')))             AS DepositoCodigo,
    ISNULL(LTRIM(RTRIM(d.Descripcion)), N'''')            AS DepositoNombre,
    CAST(SUM(ISNULL(s.Cantidad, 0)) AS decimal(18,4))   AS Stock,
    ISNULL(a.PUNTOPEDIDO, 0)                            AS PuntoPedido,
    ISNULL(a.PUNTOMAXIMO, 0)                            AS PuntoMaximo,
    ISNULL(a.COSTO, 0)                                  AS CostoActual,
    a.SUSPENDIDO                                        AS Suspendido
FROM dbo.V_MV_Stock s
INNER JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = s.IDArticulo
LEFT JOIN dbo.V_TA_Rubros r     ON r.IdRubro = a.IDRUBRO
LEFT JOIN dbo.V_TA_Deposito d   ON d.IdDeposito = s.IdDeposito
WHERE s.Anulado = 0
GROUP BY s.IDArticulo, a.DESCRIPCION, r.Descripcion, s.IdDeposito, d.Descripcion,
         a.PUNTOPEDIDO, a.PUNTOMAXIMO, a.COSTO, a.SUSPENDIDO');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_Stock', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_Proveedores: maestro de proveedores (MA_CUENTAS con TipoVista 'PR'). Sin telefono, mail ni CBU a proposito.
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_Proveedores', N'V') IS NOT NULL DROP VIEW dir.vw_Proveedores;
    EXEC(N'CREATE VIEW dir.vw_Proveedores
AS
SELECT
    LTRIM(RTRIM(p.CODIGO))                            AS ProveedorCodigo,
    LTRIM(RTRIM(p.RAZON_SOCIAL))                      AS RazonSocial,
    LTRIM(RTRIM(ISNULL(p.NUMERO_DOCUMENTO, N'''')))     AS Documento,
    LTRIM(RTRIM(ISNULL(p.IVA, N'''')))                  AS CondicionIva,
    LTRIM(RTRIM(ISNULL(p.LOCALIDAD, N'''')))            AS Localidad,
    LTRIM(RTRIM(ISNULL(p.PROVINCIA, N'''')))            AS Provincia,
    CASE WHEN ISNULL(p.Dada_De_Baja, 0) = 0 THEN 0 ELSE 1 END AS DadoDeBaja,
    CASE WHEN ISNULL(p.BLOQUEO, 0) = 0 THEN 0 ELSE 1 END      AS Bloqueado
FROM dbo.Vt_Proveedores p');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_Proveedores', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_Compras: UNA FILA POR COMPROBANTE DE COMPRA, con signo (notas de credito en negativo).
--   Fuente FISCAL = libro de IVA compras: MV_ASIENTOS con LIVA_TIPO='COMPRAS' (una fila por comprobante, verificado en toda la historia).
--     Incluye los comprobantes cargados directo en contabilidad, sin cabecera de compra (liquidaciones de tarjeta, gastos bancarios...):
--     en 2026 son 280 de 1998. Los importes LIVA_* ya vienen con signo y cuadran al centavo con Libro_ComprasConFP.
--   Fuente PROFORMA = cabecera C_MV_Cpte de los tipos de IA_MCP_TC_PROFORMA_COMPRAS (no van al libro), no anulados, con signo de V_TA_Cpte.
--   NetoSinIva = gravado + no gravado + exento. Iva = todas las alicuotas. Percepciones = percepciones IVA + IIBB + ganancias.
--   OtrosImpuestos = Total - Neto - Iva - Percepciones (impuestos internos y redondeos; normalmente 0).
--   NetoArticulos = parte del neto con lineas de articulos (de la cabecera; 0 si no tiene cabecera).
--   Id = C_MV_Cpte.ID (NULL si no tiene cabecera de compra), para unir con dir.vw_ComprasDetalle.
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_Compras', N'V') IS NOT NULL DROP VIEW dir.vw_Compras;
    EXEC(N'CREATE VIEW dir.vw_Compras
AS
SELECT
    x.Id, x.Tc, x.TipoComprobante, x.Comprobante, x.Fecha, YEAR(x.Fecha) AS Anio, MONTH(x.Fecha) AS Mes,
    CONVERT(char(7), x.Fecha, 120) AS AnioMes, x.Vencimiento, x.UnidadNegocio, x.ProveedorCodigo, x.ProveedorNombre,
    x.Documento, x.EsNotaCredito, x.EsProforma, x.TieneCabecera,
    CAST(x.Neto AS decimal(18,2))                                         AS NetoSinIva,
    CAST(x.Iva AS decimal(18,2))                                          AS Iva,
    CAST(x.Percepciones AS decimal(18,2))                                 AS Percepciones,
    CAST(x.Total - x.Neto - x.Iva - x.Percepciones AS decimal(18,2))      AS OtrosImpuestos,
    CAST(x.Total AS decimal(18,2))                                        AS Total,
    CAST(x.NetoArticulos AS decimal(18,2))                                AS NetoArticulos
FROM (
    -- Comprobantes del libro de IVA compras
    SELECT
        c.ID                                                     AS Id,
        LTRIM(RTRIM(a.TC))                                       AS Tc,
        ISNULL(LTRIM(RTRIM(t.Descripcion)), LTRIM(RTRIM(a.TC)))  AS TipoComprobante,
        LTRIM(RTRIM(a.SUCURSAL)) + N''-'' + LTRIM(RTRIM(a.NUMERO)) + LTRIM(RTRIM(a.LETRA)) AS Comprobante,
        a.FECHA                                                  AS Fecha,
        ISNULL(a.VENCIMIENTO, a.FECHA)                           AS Vencimiento,
        LTRIM(RTRIM(ISNULL(a.UNEGOCIO, N'''')))                    AS UnidadNegocio,
        LTRIM(RTRIM(a.CUENTA))                                   AS ProveedorCodigo,
        COALESCE(NULLIF(LTRIM(RTRIM(a.CABNOMBRE)), N''''), LTRIM(RTRIM(m.DESCRIPCION)), N'''') AS ProveedorNombre,
        LTRIM(RTRIM(ISNULL(a.CABCUIT, N'''')))                     AS Documento,
        CASE WHEN a.[DEBE-HABER] = N''D'' THEN 1 ELSE 0 END        AS EsNotaCredito,
        0                                                        AS EsProforma,
        CASE WHEN c.ID IS NULL THEN 0 ELSE 1 END                 AS TieneCabecera,
        ISNULL(a.LIVA_ImpNetoGrav, 0) + ISNULL(a.LIVA_ImpNetoNGrav, 0) + ISNULL(a.LIVA_EXENTO, 0) AS Neto,
        ISNULL(a.LIVA_ImpIVA, 0) + ISNULL(a.LIVA_ImpIVARec, 0) + ISNULL(a.LIVA_ImpIva2, 0)
          + ISNULL(a.LIVA_ImpIVA3, 0) + ISNULL(a.LIVA_ImpIVA4, 0)                                 AS Iva,
        ISNULL(a.LIVA_Ret_Perc, 0) + ISNULL(a.LIVA_Ret_IBtos, 0) + ISNULL(a.LIVA_Ret_Ganancias, 0) AS Percepciones,
        ISNULL(a.LIVA_TOTAL, 0)                                  AS Total,
        CASE WHEN a.[DEBE-HABER] = N''D'' THEN -1 ELSE 1 END * ISNULL(c.ImporteInsumos, 0) AS NetoArticulos
    FROM dbo.MV_ASIENTOS a
    LEFT JOIN dbo.C_MV_Cpte c
           ON c.TC = a.TC AND c.CUENTA = a.CUENTA AND c.SUCURSAL = a.SUCURSAL AND c.NUMERO = a.NUMERO AND c.LETRA = a.LETRA
          AND ISNULL(c.ANULADA, 0) = 0
    LEFT JOIN (SELECT CODIGO, MAX(DESCRIPCION) AS Descripcion FROM dbo.V_TA_Cpte GROUP BY CODIGO) t ON t.CODIGO = a.TC
    LEFT JOIN dbo.MA_CUENTAS m ON m.CODIGO = a.CUENTA
    WHERE a.LIVA_TIPO = N''COMPRAS''

    UNION ALL

    -- Proformas de compra (no fiscales): desde la cabecera
    SELECT
        c.ID,
        LTRIM(RTRIM(c.TC)),
        LTRIM(RTRIM(t.Descripcion)),
        LTRIM(RTRIM(c.SUCURSAL)) + N''-'' + LTRIM(RTRIM(c.NUMERO)) + LTRIM(RTRIM(c.LETRA)),
        c.FECHA,
        ISNULL(c.VENCIMIENTO, c.FECHA),
        LTRIM(RTRIM(ISNULL(c.UNEGOCIO, N''''))),
        LTRIM(RTRIM(c.CUENTA)),
        COALESCE(NULLIF(LTRIM(RTRIM(c.NOMBRE)), N''''), LTRIM(RTRIM(m.DESCRIPCION)), N''''),
        LTRIM(RTRIM(ISNULL(c.DOCUMENTONUMERO, N''''))),
        CASE WHEN t.DebeHaber = N''D'' THEN 1 ELSE 0 END,
        1,
        1,
        CASE WHEN t.DebeHaber = N''D'' THEN -1 ELSE 1 END * ISNULL(c.IMPORTE_S_IVA, 0),
        CASE WHEN t.DebeHaber = N''D'' THEN -1 ELSE 1 END * (ISNULL(c.ImporteIva, 0) + ISNULL(c.ImporteIva2, 0) + ISNULL(c.ImporteIvaRec, 0)),
        0,
        CASE WHEN t.DebeHaber = N''D'' THEN -1 ELSE 1 END * ISNULL(c.IMPORTE, 0),
        CASE WHEN t.DebeHaber = N''D'' THEN -1 ELSE 1 END * ISNULL(c.ImporteInsumos, 0)
    FROM dbo.C_MV_Cpte c
    INNER JOIN (SELECT CODIGO, MAX(DEBEHABER) AS DebeHaber, MAX(DESCRIPCION) AS Descripcion
                FROM dbo.V_TA_Cpte
                WHERE UPPER(SISTEMA) = N''COMPRAS'' AND DEBEHABER IN (N''D'', N''H'')
                GROUP BY CODIGO) t
            ON t.CODIGO = c.TC
    INNER JOIN (SELECT TOP 1 N'','' + REPLACE(ISNULL(VALOR, N''''), N'' '', N'''') + N'','' AS Lista
                FROM dbo.TA_CONFIGURACION WHERE CLAVE = N''IA_MCP_TC_PROFORMA_COMPRAS'') cfg
            ON CHARINDEX(N'','' + LTRIM(RTRIM(c.TC)) + N'','', cfg.Lista) > 0
    LEFT JOIN dbo.MA_CUENTAS m ON m.CODIGO = c.CUENTA
    WHERE ISNULL(c.ANULADA, 0) = 0
) x');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_Compras', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_ComprasDetalle: UNA FILA POR LINEA DE ARTICULO de las compras de dir.vw_Compras (mismo filtro, mismo signo).
--   CostoUnitario = precio neto unitario de compra (sin IVA, con descuento de linea). Cantidad y NetoSinIva con signo.
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_ComprasDetalle', N'V') IS NOT NULL DROP VIEW dir.vw_ComprasDetalle;
    EXEC(N'CREATE VIEW dir.vw_ComprasDetalle
AS
SELECT
    v.Id, v.Tc, v.Comprobante, v.Fecha, v.AnioMes, v.UnidadNegocio, v.ProveedorCodigo, v.ProveedorNombre,
    v.EsNotaCredito, v.EsProforma,
    LTRIM(RTRIM(i.IDARTICULO))                                   AS ArticuloCodigo,
    LTRIM(RTRIM(i.DESCRIPCION))                                  AS ArticuloDescripcion,
    ISNULL(LTRIM(RTRIM(r.Descripcion)), N''Sin rubro'')            AS RubroNombre,
    LTRIM(RTRIM(ISNULL(i.IDUNIDAD, N'''')))                        AS Unidad,
    CAST(CASE WHEN v.EsNotaCredito = 1 THEN -1 ELSE 1 END * ISNULL(i.CANTIDAD, 0) AS decimal(18,4))       AS Cantidad,
    CAST(ISNULL(i.IMPORTE_S_IVA, 0) * (1 - ISNULL(i.PorcDto, 0) / 100.0) AS decimal(18,4)) AS CostoUnitario,
    CAST(CASE WHEN v.EsNotaCredito = 1 THEN -1 ELSE 1 END * ISNULL(i.CANTIDAD, 0) * ISNULL(i.IMPORTE_S_IVA, 0) * (1 - ISNULL(i.PorcDto, 0) / 100.0) AS decimal(18,2)) AS NetoSinIva
FROM dbo.C_MV_Cpte c
INNER JOIN dir.vw_Compras v      ON v.Id = c.ID
INNER JOIN dbo.C_MV_CpteInsumos i ON i.TC = c.TC AND i.IDCOMPROBANTE = c.IDCOMPROBANTE AND i.CUENTA = c.CUENTA
LEFT JOIN dbo.V_MA_ARTICULOS a   ON a.IDARTICULO = i.IDARTICULO
LEFT JOIN dbo.V_TA_Rubros r      ON r.IdRubro = a.IDRUBRO');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_ComprasDetalle', ERROR_MESSAGE());
END CATCH
GO

-- dir.vw_CuentaCorrienteProveedores: saldos abiertos con proveedores, por comprobante.
--   Saldo > 0 = le debemos (facturas impagas, fuente CO_CPTES_SALDOS, la misma que usa AlfaCore).
--   Saldo < 0 = credito a nuestro favor sin aplicar (pagos a cuenta, notas de credito; fuente CO_CPTES_CREDITOS_PENDIENTES).
--   Deuda neta = SUM(Saldo). DiasVencido > 0 = vencido hace esos dias (solo tiene sentido en las facturas).
BEGIN TRY
    IF OBJECT_ID(N'dir.vw_CuentaCorrienteProveedores', N'V') IS NOT NULL DROP VIEW dir.vw_CuentaCorrienteProveedores;
    EXEC(N'CREATE VIEW dir.vw_CuentaCorrienteProveedores
AS
SELECT
    y.ProveedorCodigo, ISNULL(LTRIM(RTRIM(m.DESCRIPCION)), y.Nombre) AS ProveedorNombre,
    y.Tc, y.Comprobante, y.Fecha, y.Vencimiento, y.Saldo, y.EsCredito,
    CASE WHEN y.EsCredito = 1 THEN NULL ELSE DATEDIFF(day, y.Vencimiento, GETDATE()) END AS DiasVencido,
    CASE WHEN y.EsCredito = 1 THEN N''Credito a favor''
         WHEN DATEDIFF(day, y.Vencimiento, GETDATE()) <= 0  THEN N''A vencer''
         WHEN DATEDIFF(day, y.Vencimiento, GETDATE()) <= 30 THEN N''Vencido 1-30''
         WHEN DATEDIFF(day, y.Vencimiento, GETDATE()) <= 60 THEN N''Vencido 31-60''
         WHEN DATEDIFF(day, y.Vencimiento, GETDATE()) <= 90 THEN N''Vencido 61-90''
         ELSE N''Vencido mas de 90'' END AS Tramo
FROM (
    SELECT s.CUENTA AS CuentaRaw, LTRIM(RTRIM(s.CUENTA)) AS ProveedorCodigo, LTRIM(RTRIM(ISNULL(s.CABNOMBRE, N''''))) AS Nombre,
           LTRIM(RTRIM(s.TC)) AS Tc,
           LTRIM(RTRIM(s.SUCURSAL)) + N''-'' + LTRIM(RTRIM(s.NUMERO)) + LTRIM(RTRIM(s.LETRA)) AS Comprobante,
           s.FECHA AS Fecha, ISNULL(s.VENCIMIENTO, s.FECHA) AS Vencimiento,
           CAST(s.SALDO AS decimal(18,2)) AS Saldo, 0 AS EsCredito
    FROM dbo.CO_CPTES_SALDOS s
    WHERE s.TipoVista = N''PR'' AND s.SALDO <> 0
    UNION ALL
    SELECT k.CUENTA, LTRIM(RTRIM(k.CUENTA)), LTRIM(RTRIM(ISNULL(k.CABNOMBRE, N''''))),
           LTRIM(RTRIM(k.TC)),
           LTRIM(RTRIM(k.SUCURSAL)) + N''-'' + LTRIM(RTRIM(k.NUMERO)) + LTRIM(RTRIM(k.LETRA)),
           k.FECHA, ISNULL(k.VENCIMIENTO, k.FECHA),
           CAST(-k.SALDO AS decimal(18,2)), 1
    FROM dbo.CO_CPTES_CREDITOS_PENDIENTES k
    WHERE k.TipoVista = N''PR'' AND k.SALDO <> 0
) y
LEFT JOIN dbo.MA_CUENTAS m ON m.CODIGO = y.CuentaRaw');
END TRY
BEGIN CATCH
    INSERT INTO #mcp_errores (Vista, Error) VALUES (N'dir.vw_CuentaCorrienteProveedores', ERROR_MESSAGE());
END CATCH
GO

-- Resultado: vacio si todas las vistas se crearon; si no, que vista y por que (lo muestra habilitar-base).
DECLARE @e nvarchar(max);
SELECT @e = ISNULL(@e + NCHAR(10), N'') + Vista + N': ' + ISNULL(Error, N'') FROM #mcp_errores;
IF EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE = N'IA_MCP_ESQUEMA_ERRORES')
    UPDATE dbo.TA_CONFIGURACION SET VALOR = N'', ValorAux = @e WHERE CLAVE = N'IA_MCP_ESQUEMA_ERRORES';
ELSE
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, ValorAux, FechaHora_Grabacion)
    VALUES (N'IA_MCP', N'IA_MCP_ESQUEMA_ERRORES', N'', N'Vistas del esquema dir que no se pudieron crear en esta base (vacio = todo bien). Lo escribe la actualizacion del MCP.', @e, GETDATE());
DROP TABLE #mcp_errores;
GO
