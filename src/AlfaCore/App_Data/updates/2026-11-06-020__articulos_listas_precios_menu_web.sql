/*
    2026-11-06 · Listas de precios de ventas (/articulos/listas-precios): alta, modificación y baja de listas, carga de precios,
    agregado de artículos en bloque, aumento por porcentaje y redondeo. Usa la clave legacy D010504 «Listas de Precios (Ventas)»
    (Shell:V_MA_PRECIOS @PARAMETROADICIONAL=V) como opción del menú web y permiso, bajo Maestros (D0101) junto a Artículos.
    Permisos: a los usuarios con permisos web explícitos se les da D010504 solo si ya la tenían en el legacy (TA_TAREAS).
    Los usuarios sin filas en ALFACORE_TAREAS_WEB ya ven todo el menú web.
    No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

-- 1. Guardia
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

-- 2. Columnas nuevas en bases antiguas
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;

-- 3. Alta de la opción (solo si no existe)
EXEC sys.sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D010504'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, NombreWeb)
    VALUES (N''ALFA'', N''D010504'', N''/articulos/listas-precios'', N''ArticulosListasPrecios'', N''bi-tags'', 1, 10116, 0,
            N''Listas de precios'');';

-- 4. Ruta, icono, nombre y padre si la fila ya existía (en el web D0105 es «Tablas»: va bajo Maestros, al lado de Artículos)
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N''/articulos/listas-precios'', Componente = N''ArticulosListasPrecios'', Icono = N''bi-tags'',
    NombreWeb = N''Listas de precios'', HabilitadoWeb = 1, OrdenWeb = 10116
WHERE Clave = N''D010504'';
UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D0101''
WHERE Clave = N''D010504'' AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave = N''D0101'');';

-- 5. Permisos: solo usuarios con restricciones web que ya tenían D010504 en el legacy
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(w.Usuario)), LTRIM(RTRIM(w.Sistema)), N'D010504', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB w
    WHERE ISNULL(w.Clave, N'') <> N''
      AND EXISTS (SELECT 1 FROM dbo.TA_TAREAS t
                  WHERE UPPER(LTRIM(RTRIM(t.USUARIO))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                    AND UPPER(LTRIM(RTRIM(t.SISTEMA))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                    AND UPPER(LTRIM(RTRIM(t.TAREA))) = N'D010504')
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D010504');
