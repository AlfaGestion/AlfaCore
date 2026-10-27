-- Ejecutar contra ALFA_CENTRAL. No ejecutar en las bases de clientes.
-- Licencias del legacy: ALFA_CENTRAL pasa a ser la unica fuente de la licencia principal, los datos FTP
-- y los puntos de venta (PDV) de cada cliente. Hasta ahora el legacy (VB6) los leia por SQL directo de
-- ESTADISTICAS.dbo.clientes / V_TA_TPV en 10.8.0.31; desde ahora los va a pedir a AlfaCore por HTTP.
--
-- 1) dbo.clientes: agrega FTP_Server, FTP_Usuario, FTP_Clave y FechaActivacion (si faltan).
-- 2) dbo.ClientesTPV: copia de la estructura de ESTADISTICAS.dbo.V_TA_TPV (PK IdCliente + CODIGO).
-- 3) Migra desde ESTADISTICAS (mismo servidor):
--    - clientes que existen en las dos: la licencia de ESTADISTICAS reemplaza a la de ALFA_CENTRAL
--      (es la que tienen guardada los legacy instalados) y se completan FTP y FechaActivacion.
--    - clientes que solo estan en ESTADISTICAS: se crean (EsClienteLegacy = 1, sin idweb ni usuario web).
--    - PDV: se copian los que falten.
-- Requiere que el usuario que lo ejecuta pueda leer ESTADISTICAS. Reejecutar no duplica nada; vuelve a
-- copiar licencia/FTP desde ESTADISTICAS mientras esa base siga existiendo.
-- No modifica ESTADISTICAS.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.clientes', N'U') IS NULL OR OBJECT_ID(N'dbo.bases', N'U') IS NULL
    THROW 50000, N'La base destino no contiene el catalogo central de clientes y bases.', 1;
IF OBJECT_ID(N'ESTADISTICAS.dbo.clientes', N'U') IS NULL OR OBJECT_ID(N'ESTADISTICAS.dbo.V_TA_TPV', N'U') IS NULL
    THROW 50001, N'No se puede leer ESTADISTICAS.dbo.clientes / V_TA_TPV desde esta conexion.', 1;

BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.clientes', N'FTP_Server') IS NULL
    ALTER TABLE dbo.clientes ADD FTP_Server nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.clientes', N'FTP_Usuario') IS NULL
    ALTER TABLE dbo.clientes ADD FTP_Usuario nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.clientes', N'FTP_Clave') IS NULL
    ALTER TABLE dbo.clientes ADD FTP_Clave nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.clientes', N'FechaActivacion') IS NULL
    ALTER TABLE dbo.clientes ADD FechaActivacion datetime NULL;

IF OBJECT_ID(N'dbo.ClientesTPV', N'U') IS NULL
    CREATE TABLE dbo.ClientesTPV (
        [IdCliente] nvarchar(15) NOT NULL,
        [CODIGO] nvarchar(4) NOT NULL,
        [SUCURSAL] nvarchar(4) NULL,
        [DESCRIPCION] nvarchar(100) NULL,
        [SERVER] nvarchar(100) NULL,
        [DBNAME] nvarchar(100) NULL,
        [USUARIO] nvarchar(100) NULL,
        [PASSWORD] nvarchar(50) NULL,
        [CONECTADO] bit NULL,
        [ULTIMA_DETECCION] datetime NULL,
        [PEND_TABLAS] bit NULL,
        [PEND_CLIENTES] bit NULL,
        [PEND_ARTICULOS] bit NULL,
        [PEND_PRECIOS] bit NULL,
        [PEND_PROVEEDORES] bit NULL,
        [TIT_CLIENTE] nvarchar(15) NULL,
        [PUERTO] nvarchar(6) NULL,
        [Internet] bit NULL,
        [OL_ExceptoArticulos] bit NULL,
        [OL_ExceptoPrecios] bit NULL,
        [OL_ExceptoCuentas] bit NULL,
        [IdLista] nvarchar(4) NULL,
        [MvRemoto] bit NULL,
        [SUCURSAL1] nvarchar(4) NULL,
        [SUCURSAL2] nvarchar(4) NULL,
        [UNEGOCIO] nvarchar(4) NULL,
        [OL_ExceptoTablas] bit NULL,
        [DEPOSITO1] nvarchar(4) NULL,
        [DEPOSITO2] nvarchar(4) NULL,
        [DEPOSITO3] nvarchar(4) NULL,
        [DEPOSITO4] nvarchar(4) NULL,
        [Franquicia] bit NULL,
        [Baja] bit NULL,
        [FONDOFIJO] money NULL,
        [CierreMail] nvarchar(max) NULL,
        [caja1] nvarchar(4) NULL,
        [caja2] nvarchar(4) NULL,
        [caja3] nvarchar(4) NULL,
        [cajatrans] nvarchar(4) NULL,
        [IdListaRMTRF] nvarchar(4) NULL,
        [PORFTP] bit NULL,
        [Intervalo] float NULL,
        [ProxSincro] datetime NULL,
        [IntervaloM] float NULL,
        [ProxSincroM] datetime NULL,
        [EnviaPrecios] bit NULL,
        [EnviaCuentas] bit NULL,
        [EnviaTablas] bit NULL,
        [Licencia] nvarchar(250) NULL,
        [ControlaSincronizacion] bit NULL,
        [CodCliente] nvarchar(13) NULL,
        [BaseServidor] nvarchar(50) NULL,
        [ServerRemoto] nvarchar(50) NULL,
        CONSTRAINT PK_ClientesTPV PRIMARY KEY (IdCliente, CODIGO)
    );
COMMIT TRANSACTION;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Origen normalizado (TRIM y collation de esta base para comparar).
IF OBJECT_ID(N'tempdb..#Origen') IS NOT NULL DROP TABLE #Origen;
SELECT LTRIM(RTRIM(e.IdCliente)) COLLATE DATABASE_DEFAULT AS IdCliente,
       LEFT(LTRIM(RTRIM(ISNULL(e.Nombre, N''))), 150) COLLATE DATABASE_DEFAULT AS Nombre,
       NULLIF(LTRIM(RTRIM(e.LicenciaPrincipal)), N'') COLLATE DATABASE_DEFAULT AS Licencia,
       NULLIF(LTRIM(RTRIM(e.FTP_Server)), N'') COLLATE DATABASE_DEFAULT AS FTP_Server,
       NULLIF(LTRIM(RTRIM(e.FTP_Usuario)), N'') COLLATE DATABASE_DEFAULT AS FTP_Usuario,
       NULLIF(LTRIM(RTRIM(e.FTP_Clave)), N'') COLLATE DATABASE_DEFAULT AS FTP_Clave,
       e.FechaActivacion,
       e.FechaAlta
INTO #Origen
FROM ESTADISTICAS.dbo.clientes e
WHERE LTRIM(RTRIM(ISNULL(e.IdCliente, N''))) <> N'';

-- Controles antes de tocar datos.
IF EXISTS (SELECT 1 FROM #Origen WHERE LEN(IdCliente) > 9 OR LEN(ISNULL(Licencia, N'')) > 20)
    THROW 50002, N'Hay codigos de cliente de mas de 9 caracteres o licencias de mas de 20 en ESTADISTICAS.', 1;
IF EXISTS (SELECT 1 FROM #Origen WHERE Licencia IS NOT NULL GROUP BY UPPER(Licencia) HAVING COUNT(*) > 1)
    THROW 50003, N'ESTADISTICAS tiene licencias repetidas entre clientes.', 1;
-- Una licencia de ESTADISTICAS no puede estar usada por OTRO cliente de ALFA_CENTRAL que no vaya a
-- cambiar la suya en esta misma migracion (indice unico UX_clientes_LicenciaPrincipal).
IF EXISTS (SELECT 1
           FROM #Origen o
           INNER JOIN dbo.clientes c
                   ON UPPER(LTRIM(RTRIM(c.LicenciaPrincipal))) = UPPER(o.Licencia)
                  AND UPPER(LTRIM(RTRIM(c.idcliente))) <> UPPER(o.IdCliente)
           WHERE NOT EXISTS (SELECT 1 FROM #Origen o2
                             WHERE UPPER(o2.IdCliente) = UPPER(LTRIM(RTRIM(c.idcliente)))
                               AND o2.Licencia IS NOT NULL))
    THROW 50004, N'Una licencia de ESTADISTICAS ya la usa otro cliente en ALFA_CENTRAL. Revisar antes de migrar.', 1;

BEGIN TRANSACTION;

-- Clientes que existen en las dos bases. Primero se libera la licencia de los que cambian, para que el
-- indice unico no choque si dos clientes intercambian valores.
UPDATE c SET LicenciaPrincipal = NULL
FROM dbo.clientes c
INNER JOIN #Origen o ON UPPER(o.IdCliente) = UPPER(LTRIM(RTRIM(c.idcliente)))
WHERE o.Licencia IS NOT NULL
  AND UPPER(LTRIM(RTRIM(ISNULL(c.LicenciaPrincipal, N'')))) <> UPPER(o.Licencia);

UPDATE c SET
    LicenciaPrincipal = ISNULL(o.Licencia, c.LicenciaPrincipal),
    FTP_Server = ISNULL(o.FTP_Server, c.FTP_Server),
    FTP_Usuario = ISNULL(o.FTP_Usuario, c.FTP_Usuario),
    FTP_Clave = ISNULL(o.FTP_Clave, c.FTP_Clave),
    FechaActivacion = ISNULL(c.FechaActivacion, o.FechaActivacion)
FROM dbo.clientes c
INNER JOIN #Origen o ON UPPER(o.IdCliente) = UPPER(LTRIM(RTRIM(c.idcliente)));

-- Clientes que solo estan en ESTADISTICAS.
INSERT INTO dbo.clientes (idcliente, nombre, LicenciaPrincipal, FTP_Server, FTP_Usuario, FTP_Clave,
                          FechaActivacion, created, superadmin, EsClienteLegacy)
SELECT o.IdCliente, NULLIF(o.Nombre, N''), ISNULL(o.Licencia, o.IdCliente), o.FTP_Server, o.FTP_Usuario,
       o.FTP_Clave, o.FechaActivacion, ISNULL(o.FechaAlta, GETDATE()), 0, 1
FROM #Origen o
WHERE NOT EXISTS (SELECT 1 FROM dbo.clientes c WHERE UPPER(LTRIM(RTRIM(c.idcliente))) = UPPER(o.IdCliente));

-- Puntos de venta.
INSERT INTO dbo.ClientesTPV
SELECT t.*
FROM ESTADISTICAS.dbo.V_TA_TPV t
WHERE NOT EXISTS (SELECT 1 FROM dbo.ClientesTPV x
                  WHERE x.IdCliente = LTRIM(RTRIM(t.IdCliente)) COLLATE DATABASE_DEFAULT
                    AND x.CODIGO = LTRIM(RTRIM(t.CODIGO)) COLLATE DATABASE_DEFAULT);

COMMIT TRANSACTION;

-- Resumen.
SELECT
    (SELECT COUNT(*) FROM #Origen) AS ClientesEstadisticas,
    (SELECT COUNT(*) FROM dbo.clientes) AS ClientesCentral,
    (SELECT COUNT(*) FROM #Origen o
       WHERE NOT EXISTS (SELECT 1 FROM dbo.clientes c
                         WHERE UPPER(LTRIM(RTRIM(c.idcliente))) = UPPER(o.IdCliente)
                           AND UPPER(LTRIM(RTRIM(ISNULL(c.LicenciaPrincipal, N'')))) = UPPER(ISNULL(o.Licencia, o.IdCliente)))) AS LicenciasSinCoincidir,
    (SELECT COUNT(*) FROM dbo.ClientesTPV) AS PuntosDeVenta;
DROP TABLE #Origen;
GO
