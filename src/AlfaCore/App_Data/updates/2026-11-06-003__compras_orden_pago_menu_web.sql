/*
    2026-11-06 · Orden de Pago a proveedores (/compras/pagos/orden-de-pago): OPG (o PGFP con el interruptor Proforma) con comprobantes,
    créditos y medios de pago. Usa la clave legacy D509012 «Orden de pago y Liquidacion» (Pagos.exe OPGConMedios) como opción del menú
    web y permiso, renombrada «Orden de Pago». Crea el journal ALFACORE_PAGO_OPERACION (idempotencia de la grabación).
    Permisos: a los usuarios con permisos web explícitos se les da D509012 solo si ya la tenían en el legacy (TA_TAREAS, leída desde
    la copia de LegacyTablesUpdateGuard). No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

-- 1. Journal de operaciones de pago (una fila por orden grabada; la clave es el identificador que genera la pantalla)
IF OBJECT_ID(N'dbo.ALFACORE_PAGO_OPERACION', N'U') IS NULL
    CREATE TABLE dbo.ALFACORE_PAGO_OPERACION (
        Operacion     uniqueidentifier NOT NULL CONSTRAINT PK_ALFACORE_PAGO_OPERACION PRIMARY KEY,
        ComprobanteId int              NOT NULL,
        Tc            nvarchar(4)      NOT NULL,
        Comprobante   nvarchar(13)     NOT NULL,
        NumeroAsiento int              NOT NULL,
        Importe       decimal(18,2)    NOT NULL,
        Anticipo      decimal(18,2)    NOT NULL CONSTRAINT DF_ALFACORE_PAGO_OPERACION_Anticipo DEFAULT (0),
        SolicitudHash nvarchar(64)     NOT NULL,
        Unidad        nvarchar(4)      NOT NULL,
        Usuario       nvarchar(50)     NOT NULL,
        FechaHora     datetime         NOT NULL CONSTRAINT DF_ALFACORE_PAGO_OPERACION_Fecha DEFAULT (GETDATE())
    );

-- 2. Guardia del menú web
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

-- 3. Columnas nuevas en bases antiguas
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;

-- 4. Alta de la opción (solo si no existe)
EXEC sys.sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D509012'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, NombreWeb)
    VALUES (N''ALFA'', N''D509012'', N''/compras/pagos/orden-de-pago'', N''OrdenPago'', N''bi-cash-coin'', 1, 50404, 0, N''Orden de Pago'');';

-- 5. Ruta, icono, nombre y padre si la fila ya existía
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N''/compras/pagos/orden-de-pago'', Componente = N''OrdenPago'', Icono = N''bi-cash-coin'',
    NombreWeb = N''Orden de Pago'', HabilitadoWeb = 1
WHERE Clave = N''D509012'';
UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D5090''
WHERE Clave = N''D509012'' AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave = N''D5090'');';

-- 6. Permisos: solo usuarios con restricciones web que ya tenían D509012 en el legacy
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(w.Usuario)), LTRIM(RTRIM(w.Sistema)), N'D509012', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB w
    WHERE ISNULL(w.Clave, N'') <> N''
      AND EXISTS (SELECT 1 FROM dbo.TA_TAREAS t
                  WHERE UPPER(LTRIM(RTRIM(t.USUARIO))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                    AND UPPER(LTRIM(RTRIM(t.SISTEMA))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                    AND UPPER(LTRIM(RTRIM(t.TAREA))) = N'D509012')
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D509012');
