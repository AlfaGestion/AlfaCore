/*
    2026-11-11 · Movimientos de stock web (/stock/movimientos): ingresos IR, vales de salida VS, transferencias entre
    depósitos TR, ajustes AJP y saldo inicial SI (port de V_MV_STOCK.exe). Graba en V_MV_STOCK con los comprobantes de
    V_TA_CPTE con SISTEMA='Stock'.
    1. Tabla ALFACORE_STOCK_MOV_OPERACION: una fila por grabación/anulación web, para no grabar dos veces la misma
       operación (doble clic o reconexión).
    2. Menú web: vuelve a habilitar Stock (D80, deshabilitado el 2026-07-02 al mover el tablero a Tableros) y agrega
       D80-MOVSTOCK «Movimientos de stock».
    3. Permisos: a los usuarios con permisos web explícitos se les da D80-MOVSTOCK si en el legacy tenían alguna opción
       de comprobantes de stock (D8A60 movimientos varios, D8A65 IR, D8A70 VS, D8065 ajustes, D8070 TR, D8075 modificación).
       Los usuarios sin filas en ALFACORE_TAREAS_WEB ya ven todo el menú web.
    No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

-- 1. Operaciones web de stock
IF OBJECT_ID(N'dbo.ALFACORE_STOCK_MOV_OPERACION', N'U') IS NULL
    CREATE TABLE dbo.ALFACORE_STOCK_MOV_OPERACION (
        Operacion uniqueidentifier NOT NULL CONSTRAINT PK_ALFACORE_STOCK_MOV_OPERACION PRIMARY KEY,
        TC nvarchar(4) NOT NULL,
        IdComprobante nvarchar(13) NOT NULL,
        Accion nchar(1) NOT NULL,
        Usuario nvarchar(50) NOT NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_ALFACORE_STOCK_MOV_OPERACION_FechaHora DEFAULT (GETDATE())
    );
IF OBJECT_ID(N'dbo.ALFACORE_STOCK_MOV_OPERACION', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_STOCK_MOV_OPERACION') AND name = N'IX_ALFACORE_STOCK_MOV_OPERACION_Cpte')
    CREATE INDEX IX_ALFACORE_STOCK_MOV_OPERACION_Cpte ON dbo.ALFACORE_STOCK_MOV_OPERACION (TC, IdComprobante);

-- 2. Menú web
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;

EXEC sys.sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D80'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault)
    VALUES (N''ALFA'', N''D80'', N''D'', N''Stock'', N''/shell/D80'', N''ShellWorkspacePage'', N''bi-box-seam'', 1, 80, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D80-MOVSTOCK'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault)
    VALUES (N''ALFA'', N''D80-MOVSTOCK'', N''D80'', N''Movimientos de stock'', N''/stock/movimientos'', N''StockMovimientos'', N''bi-arrow-left-right'', 1, 10, 0);';

EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N''D'', NombreWeb = N''Stock'', RutaWeb = N''/shell/D80'', Componente = N''ShellWorkspacePage'',
    Icono = N''bi-box-seam'', HabilitadoWeb = 1
WHERE Menu = N''ALFA'' AND Clave = N''D80'';
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N''D80'', NombreWeb = N''Movimientos de stock'', RutaWeb = N''/stock/movimientos'', Componente = N''StockMovimientos'',
    Icono = N''bi-arrow-left-right'', HabilitadoWeb = 1, OrdenWeb = 10
WHERE Menu = N''ALFA'' AND Clave = N''D80-MOVSTOCK'';';

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'DescripcionWeb') IS NOT NULL
    EXEC sys.sp_executesql N'
    UPDATE dbo.ALFACORE_MENU_WEB
    SET DescripcionWeb = N''Ingresos, vales de salida, transferencias entre depósitos, ajustes y saldo inicial de stock.''
    WHERE Menu = N''ALFA'' AND Clave = N''D80-MOVSTOCK'';';

-- 3. Permisos: solo usuarios con restricciones web que tenían comprobantes de stock en el legacy
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(w.Usuario)), LTRIM(RTRIM(w.Sistema)), N'D80-MOVSTOCK', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB w
    WHERE ISNULL(w.Clave, N'') <> N''
      AND EXISTS (SELECT 1 FROM dbo.TA_TAREAS t
                  WHERE UPPER(LTRIM(RTRIM(t.USUARIO))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                    AND UPPER(LTRIM(RTRIM(t.SISTEMA))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                    AND UPPER(LTRIM(RTRIM(t.TAREA))) IN (N'D8A60', N'D8A65', N'D8A70', N'D8065', N'D8070', N'D8075'))
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D80-MOVSTOCK');
