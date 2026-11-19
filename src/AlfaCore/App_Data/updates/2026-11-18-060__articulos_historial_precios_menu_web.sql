/*
    2026-11-18 · Historial de precios (/articulos/historial-precios): control de cambios de costos y precios sobre V_MV_PreciosHis.
    Usa la clave legacy D010507 «Reporte de Cambios de Precios (Historico)» como opción del menú web y permiso, bajo Maestros (D0101).
    Permisos: a los usuarios con permisos web explícitos se les da D010507 solo si ya la tenían en el legacy (TA_TAREAS).
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
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D010507'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, NombreWeb)
    VALUES (N''ALFA'', N''D010507'', N''/articulos/historial-precios'', N''ArticulosHistorialPrecios'', N''bi-graph-up-arrow'', 1, 10117, 0,
            N''Historial de precios'');';

-- 4. Ruta, icono, nombre y padre si la fila ya existía
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N''/articulos/historial-precios'', Componente = N''ArticulosHistorialPrecios'', Icono = N''bi-graph-up-arrow'',
    NombreWeb = N''Historial de precios'', HabilitadoWeb = 1, OrdenWeb = 10117
WHERE Clave = N''D010507'';
UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D0101''
WHERE Clave = N''D010507'' AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave = N''D0101'');';

-- 5. Permisos: solo usuarios con restricciones web que ya tenían D010507 en el legacy
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(w.Usuario)), LTRIM(RTRIM(w.Sistema)), N'D010507', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB w
    WHERE ISNULL(w.Clave, N'') <> N''
      AND EXISTS (SELECT 1 FROM dbo.TA_TAREAS t
                  WHERE UPPER(LTRIM(RTRIM(t.USUARIO))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                    AND UPPER(LTRIM(RTRIM(t.SISTEMA))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                    AND UPPER(LTRIM(RTRIM(t.TAREA))) = N'D010507')
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D010507');
