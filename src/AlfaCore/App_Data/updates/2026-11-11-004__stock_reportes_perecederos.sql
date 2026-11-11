/*
    2026-11-11 · Reportes de stock web (/stock/reportes) y productos perecederos.
    1. V_MA_ARTICULOS: asegura Perecedero (bit) y Largo (float), que el legacy usa para los días de vencimiento
       (Ma_art.frm guarda TxtDiasCaducidad en Largo). En las bases que ya las tienen no cambia nada.
    2. TA_CONFIGURACION UsaPerecederos (grupo STOCK): muestra Perecedero y los días en el maestro web y pide el
       vencimiento en los ingresos de stock. Se crea en SI si la base ya tiene artículos perecederos; si no, en NO.
       Si ya existe no se toca.
    3. Menú web D80-RPTSTOCK «Reportes de stock» bajo Stock (D80): saldos, movimientos del período, ficha y perecederos
       (port de V_Cons_Ficha_Stock).
    4. Permisos: a los usuarios con permisos web explícitos se les da D80-RPTSTOCK si en el legacy tenían la consulta o
       los reportes de stock (D8079 Consulta de Stock, D8A80 Reportes de Stock, D8081 Stock comprometido).
    No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

-- 1. Columnas de artículos usadas por perecederos
IF OBJECT_ID(N'dbo.V_MA_ARTICULOS', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'Perecedero') IS NULL
    ALTER TABLE dbo.V_MA_ARTICULOS ADD Perecedero bit NULL CONSTRAINT DF_v_ma_articulos_Perecedero DEFAULT (0);
IF OBJECT_ID(N'dbo.V_MA_ARTICULOS', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'Largo') IS NULL
    ALTER TABLE dbo.V_MA_ARTICULOS ADD Largo float NULL;
GO

-- 2. Clave UsaPerecederos
IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'USAPERECEDEROS')
    EXEC sys.sp_executesql N'
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion)
    SELECT N''STOCK'', N''UsaPerecederos'',
        CASE WHEN EXISTS (SELECT 1 FROM dbo.V_MA_ARTICULOS WHERE ISNULL(Perecedero, 0) = 1 AND ISNULL(SUSPENDIDO, 0) = 0) THEN N''SI'' ELSE N''NO'' END,
        GETDATE();';
GO

-- 3. Menú web
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;
GO
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

EXEC sys.sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D80'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault)
    VALUES (N''ALFA'', N''D80'', N''D'', N''Stock'', N''/shell/D80'', N''ShellWorkspacePage'', N''bi-box-seam'', 1, 80, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D80-RPTSTOCK'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault)
    VALUES (N''ALFA'', N''D80-RPTSTOCK'', N''D80'', N''Reportes de stock'', N''/stock/reportes'', N''StockReportes'', N''bi-clipboard-data'', 1, 20, 0);
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N''D80'', NombreWeb = N''Reportes de stock'', RutaWeb = N''/stock/reportes'', Componente = N''StockReportes'',
    Icono = N''bi-clipboard-data'', HabilitadoWeb = 1, OrdenWeb = 20
WHERE Menu = N''ALFA'' AND Clave = N''D80-RPTSTOCK'';';

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'DescripcionWeb') IS NOT NULL
    EXEC sys.sp_executesql N'
    UPDATE dbo.ALFACORE_MENU_WEB
    SET DescripcionWeb = N''Saldos a una fecha en unidades y bultos, movimientos del período, ficha de stock y productos perecederos.''
    WHERE Menu = N''ALFA'' AND Clave = N''D80-RPTSTOCK'';';

-- 4. Permisos
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(w.Usuario)), LTRIM(RTRIM(w.Sistema)), N'D80-RPTSTOCK', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB w
    WHERE ISNULL(w.Clave, N'') <> N''
      AND EXISTS (SELECT 1 FROM dbo.TA_TAREAS t
                  WHERE UPPER(LTRIM(RTRIM(t.USUARIO))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                    AND UPPER(LTRIM(RTRIM(t.SISTEMA))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                    AND UPPER(LTRIM(RTRIM(t.TAREA))) IN (N'D8079', N'D8A80', N'D8081'))
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D80-RPTSTOCK');
