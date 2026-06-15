SET NOCOUNT ON;

-- 1. Guardia: si no existe ALFACORE_MENU_WEB, salir sin tocar nada
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- 2. Columna NombreWeb en bases antiguas
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

DECLARE @MenuBase nvarchar(50) = N'ALFA';
DECLARE @ClaveRoot nvarchar(50) = N'D010180';
DECLARE @ClaveReportes nvarchar(50) = N'D010182';

-- 3. INSERT en ALFACORE_MENU_WEB
IF EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = @ClaveRoot)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = @ClaveReportes)
    BEGIN
        INSERT INTO dbo.ALFACORE_MENU_WEB
        (
            Menu,
            Clave,
            RutaWeb,
            Componente,
            Icono,
            HabilitadoWeb,
            OrdenWeb,
            EsFavoritoDefault,
            Observacion,
            NombreWeb
        )
        VALUES
        (
            @MenuBase,
            @ClaveReportes,
            N'/carga-viajes/reportes',
            N'CargaViajes',
            N'bi-bar-chart-line',
            1,
            18002,
            0,
            N'Reportes de liquidacion de choferes y fleteros.',
            N'Reportes'
        );
    END;
END;

-- 4. UPDATE en ALFACORE_MENU_WEB
UPDATE w
SET
    w.RutaWeb = CASE
        WHEN w.Clave = @ClaveReportes THEN N'/carga-viajes/reportes'
        ELSE w.RutaWeb
    END,
    w.Componente = CASE
        WHEN w.Clave = @ClaveReportes THEN N'CargaViajes'
        ELSE w.Componente
    END,
    w.Icono = CASE
        WHEN w.Clave = @ClaveReportes THEN N'bi-bar-chart-line'
        ELSE w.Icono
    END,
    w.HabilitadoWeb = 1,
    w.OrdenWeb = CASE
        WHEN w.Clave = @ClaveReportes THEN COALESCE(NULLIF(w.OrdenWeb, 0), 18002)
        ELSE w.OrdenWeb
    END,
    w.EsFavoritoDefault = CASE
        WHEN w.Clave = @ClaveReportes THEN COALESCE(w.EsFavoritoDefault, 0)
        ELSE w.EsFavoritoDefault
    END,
    w.Observacion = CASE
        WHEN w.Clave = @ClaveReportes THEN COALESCE(NULLIF(w.Observacion, N''), N'Reportes de liquidacion de choferes y fleteros.')
        ELSE w.Observacion
    END,
    w.NombreWeb = CASE
        WHEN w.Clave = @ClaveReportes THEN COALESCE(NULLIF(w.NombreWeb, N''), N'Reportes')
        ELSE w.NombreWeb
    END
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Menu = @MenuBase
  AND w.Clave = @ClaveReportes;

-- 5. Descripcion en TA_MENU
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.TA_MENU
        WHERE Menu = @MenuBase
          AND Clave = @ClaveReportes
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM dbo.TA_MENU
            WHERE Menu = @MenuBase
              AND Clave = @ClaveRoot
        )
        BEGIN
            INSERT INTO dbo.TA_MENU
            (
                Menu,
                Titulo,
                Clave,
                Nombre,
                Imagen,
                Proceso,
                Habilitado,
                ORDEN,
                DESCRIPCION
            )
            VALUES
            (
                @MenuBase,
                @ClaveRoot,
                @ClaveReportes,
                N'Reportes',
                N'bi-bar-chart-line',
                N'CargaViajes',
                1,
                N'18002',
                N'Reportes de liquidacion de choferes y fleteros.'
            );
        END;
    END;

    IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
    BEGIN
        UPDATE dbo.TA_MENU
        SET Descripcion = COALESCE(NULLIF(CAST(Descripcion AS nvarchar(max)), N''), N'Reportes de liquidacion de choferes y fleteros.')
        WHERE Menu = @MenuBase
          AND Clave = @ClaveReportes;
    END;
END;

-- 6. Permisos en TA_TAREAS para usuarios con restricciones activas
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, @ClaveReportes
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA))) = @ClaveReportes
      );
END;
GO
