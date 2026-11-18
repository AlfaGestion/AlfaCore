-- Menú web con la misma estructura que Alfa en Archivos y Utilidades (2026-11-18).
--  * Archivos > Tablas (D0103): Tablas de referencia (D010301) y Chequeras (D010350), como en Alfa.
--    Antes "Tablas de referencia" ocupaba D0105, que en Alfa es Precios, y Chequeras colgaba suelta de Archivos.
--  * Archivos > Precios (D0105): Listas de precios (D010504), como en Alfa (antes en Maestros).
--  * Utilidades: Paquete para el estudio (D9832, en Alfa "Utilidades Estudios Contables") y Estudio contable
--    (D016040) pasan de Archivos > Interfaces a Utilidades.
--  * Reporte de Ventas (D60RPTWEB) sin padre: queda en Ventas.
-- Permisos: cada usuario y cada perfil conserva lo que veía (quien tenía "Tablas" pasa a tener las claves nuevas).
-- Solo menú/permisos web (ALFACORE_MENU_WEB / ALFACORE_TAREAS_WEB / ALFACORE_PERFILES_WEB_CLAVES).
-- Idempotente. Compatible con compatibility level 80/100.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
GO
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL RETURN;

-- 1) Permisos, calculados con la ubicación vieja (D0105 = Tablas de referencia mientras tenga esa ruta).
--    Quien tenía la clave Origen recibe también la clave Nueva (mismo usuario o perfil).
IF OBJECT_ID(N'tempdb..#map') IS NOT NULL DROP TABLE #map;
CREATE TABLE #map (Origen nvarchar(50) NOT NULL, Nueva nvarchar(50) NOT NULL);

IF EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB
           WHERE Menu = N'ALFA' AND Clave = N'D0105' AND ISNULL(RutaWeb, N'') = N'/archivos/tablas')
    INSERT INTO #map VALUES (N'D0105', N'D0103'), (N'D0105', N'D010301');
INSERT INTO #map VALUES (N'D010350', N'D0103'), (N'D010504', N'D0105'),
                        (N'D9832', N'D98'), (N'D016040', N'D98');

IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    DECLARE @conPerfil bit = CASE WHEN COL_LENGTH(N'dbo.ALFACORE_TAREAS_WEB', N'IdPerfil') IS NULL THEN 0 ELSE 1 END;
    DECLARE @sql nvarchar(max) = N'
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion'
        + CASE WHEN @conPerfil = 1 THEN N', IdPerfil' ELSE N'' END + N')
    SELECT DISTINCT LTRIM(RTRIM(t.Usuario)), LTRIM(RTRIM(t.Sistema)), m.Nueva, GETDATE(), N''SYSTEM'''
        + CASE WHEN @conPerfil = 1 THEN N', t.IdPerfil' ELSE N'' END + N'
    FROM dbo.ALFACORE_TAREAS_WEB t
    JOIN #map m ON UPPER(LTRIM(RTRIM(t.Clave))) = m.Origen
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave)))   = m.Nueva);';
    EXEC sys.sp_executesql @sql;
END

IF OBJECT_ID(N'dbo.ALFACORE_PERFILES_WEB_CLAVES', N'U') IS NOT NULL
    EXEC sys.sp_executesql N'
    INSERT INTO dbo.ALFACORE_PERFILES_WEB_CLAVES (IdPerfil, Clave)
    SELECT DISTINCT c.IdPerfil, m.Nueva
    FROM dbo.ALFACORE_PERFILES_WEB_CLAVES c
    JOIN #map m ON UPPER(LTRIM(RTRIM(c.Clave))) = m.Origen
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_PERFILES_WEB_CLAVES x
                      WHERE x.IdPerfil = c.IdPerfil AND UPPER(LTRIM(RTRIM(x.Clave))) = m.Nueva);';

-- D0105 pasa a ser el grupo Precios: quien lo tenía solo por "Tablas" y no tiene Listas de precios, ya no lo necesita.
IF EXISTS (SELECT 1 FROM #map WHERE Origen = N'D0105')
BEGIN
    IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
        DELETE t FROM dbo.ALFACORE_TAREAS_WEB t
        WHERE UPPER(LTRIM(RTRIM(t.Clave))) = N'D0105'
          AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
                            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
                            AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'D010504');
    IF OBJECT_ID(N'dbo.ALFACORE_PERFILES_WEB_CLAVES', N'U') IS NOT NULL
        EXEC sys.sp_executesql N'
        DELETE c FROM dbo.ALFACORE_PERFILES_WEB_CLAVES c
        WHERE UPPER(LTRIM(RTRIM(c.Clave))) = N''D0105''
          AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_PERFILES_WEB_CLAVES x
                          WHERE x.IdPerfil = c.IdPerfil AND UPPER(LTRIM(RTRIM(x.Clave))) = N''D010504'');';
END
DROP TABLE #map;
GO

-- 2) Menú.
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL RETURN;
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D01') RETURN;

EXEC sys.sp_executesql N'
DECLARE @m TABLE (Clave nvarchar(50), Padre nvarchar(50), Nombre nvarchar(150), Ruta nvarchar(200), Componente nvarchar(100), Icono nvarchar(60), Orden int);
INSERT INTO @m VALUES
 (N''D0103'',   N''D01'',   N''Tablas'',               NULL,                NULL,                   N''bi-table'', 10300),
 (N''D010301'', N''D0103'', N''Tablas de referencia'', N''/archivos/tablas'', N''TablasReferencia'',   N''bi-table'', 10301),
 (N''D0105'',   N''D01'',   N''Precios'',              NULL,                NULL,                   N''bi-tags'',  10500);

INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault)
SELECT N''ALFA'', m.Clave, m.Padre, m.Nombre, m.Ruta, m.Componente, m.Icono, 1, m.Orden, 0
FROM @m m
WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu = N''ALFA'' AND w.Clave = m.Clave);

UPDATE w SET w.PadreClave = m.Padre, w.NombreWeb = m.Nombre, w.RutaWeb = m.Ruta, w.Componente = m.Componente,
             w.Icono = m.Icono, w.HabilitadoWeb = 1, w.OrdenWeb = m.Orden
FROM dbo.ALFACORE_MENU_WEB w JOIN @m m ON m.Clave = w.Clave
WHERE w.Menu = N''ALFA'';

-- Se mueven sin tocar nombre ni ruta.
UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D0103'', OrdenWeb = 10350 WHERE Menu = N''ALFA'' AND Clave = N''D010350'';
UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D0105'', OrdenWeb = 10504 WHERE Menu = N''ALFA'' AND Clave = N''D010504'';
IF EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D98'')
BEGIN
    UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D98'', OrdenWeb = 98320 WHERE Menu = N''ALFA'' AND Clave = N''D9832'';
    UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D98'', OrdenWeb = 98321 WHERE Menu = N''ALFA'' AND Clave = N''D016040'';
END
IF EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D60'')
    UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D60''
    WHERE Menu = N''ALFA'' AND Clave = N''D60RPTWEB'' AND ISNULL(LTRIM(RTRIM(PadreClave)), N'''') = N'''';';
GO
