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
DECLARE @ClaveChild nvarchar(50) = N'D010181';

-- 3. INSERT en ALFACORE_MENU_WEB
IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = @ClaveRoot)
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
            @ClaveRoot,
            N'/shell/D010180',
            N'ShellWorkspacePage',
            N'bi-truck',
            1,
            180,
            0,
            N'Modulo raiz de logistica para carga de viajes.',
            N'Logistica'
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = @ClaveChild)
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
            @ClaveChild,
            N'/carga-viajes',
            N'CargaViajes',
            N'bi-truck',
            1,
            18001,
            1,
            N'Pantalla web para alta, consulta y mantenimiento de viajes.',
            N'Carga de viaje'
        );
    END;
END;

-- 4. UPDATE en ALFACORE_MENU_WEB
UPDATE w
SET
    w.RutaWeb = CASE
        WHEN w.Clave = @ClaveRoot THEN N'/shell/D010180'
        WHEN w.Clave = @ClaveChild THEN N'/carga-viajes'
        ELSE w.RutaWeb
    END,
    w.Componente = CASE
        WHEN w.Clave = @ClaveRoot THEN N'ShellWorkspacePage'
        WHEN w.Clave = @ClaveChild THEN N'CargaViajes'
        ELSE w.Componente
    END,
    w.Icono = CASE
        WHEN w.Clave IN (@ClaveRoot, @ClaveChild) THEN N'bi-truck'
        ELSE w.Icono
    END,
    w.HabilitadoWeb = 1,
    w.OrdenWeb = CASE
        WHEN w.Clave = @ClaveRoot THEN COALESCE(NULLIF(w.OrdenWeb, 0), 180)
        WHEN w.Clave = @ClaveChild THEN COALESCE(NULLIF(w.OrdenWeb, 0), 18001)
        ELSE w.OrdenWeb
    END,
    w.EsFavoritoDefault = CASE
        WHEN w.Clave = @ClaveChild THEN COALESCE(w.EsFavoritoDefault, 1)
        ELSE COALESCE(w.EsFavoritoDefault, 0)
    END,
    w.Observacion = CASE
        WHEN w.Clave = @ClaveRoot THEN COALESCE(NULLIF(w.Observacion, N''), N'Modulo raiz de logistica para carga de viajes.')
        WHEN w.Clave = @ClaveChild THEN COALESCE(NULLIF(w.Observacion, N''), N'Pantalla web para alta, consulta y mantenimiento de viajes.')
        ELSE w.Observacion
    END,
    w.NombreWeb = CASE
        WHEN w.Clave = @ClaveRoot THEN COALESCE(NULLIF(w.NombreWeb, N''), N'Logistica')
        WHEN w.Clave = @ClaveChild THEN COALESCE(NULLIF(w.NombreWeb, N''), N'Carga de viaje')
        ELSE w.NombreWeb
    END
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Menu = @MenuBase
  AND w.Clave IN (@ClaveRoot, @ClaveChild);

-- 5. Descripcion en TA_MENU
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.TA_MENU
        WHERE Menu = @MenuBase
          AND Clave = @ClaveRoot
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM dbo.TA_MENU
            WHERE Menu = @MenuBase
              AND Clave = N'D'
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
                N'D',
                @ClaveRoot,
                N'Logistica',
                N'bi-truck',
                N'Logistica',
                1,
                N'180',
                N'Circuito web de logistica y carga de viajes.'
            );
        END;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.TA_MENU
        WHERE Menu = @MenuBase
          AND Clave = @ClaveChild
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
                @ClaveChild,
                N'Carga de viaje',
                N'bi-truck',
                N'CargaViajes',
                1,
                N'18001',
                N'Pantalla web de carga, consulta y mantenimiento de viajes.'
            );
        END;
    END;

    IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
    BEGIN
        UPDATE dbo.TA_MENU
        SET Descripcion = CASE
            WHEN Clave = @ClaveRoot THEN COALESCE(NULLIF(CAST(Descripcion AS nvarchar(max)), N''), N'Circuito web de logistica y carga de viajes.')
            WHEN Clave = @ClaveChild THEN COALESCE(NULLIF(CAST(Descripcion AS nvarchar(max)), N''), N'Pantalla web de carga, consulta y mantenimiento de viajes.')
            ELSE Descripcion
        END
        WHERE Menu = @MenuBase
          AND Clave IN (@ClaveRoot, @ClaveChild);
    END;
END;

-- 6. Permisos en TA_TAREAS para usuarios con restricciones activas
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, @ClaveChild
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA))) = @ClaveChild
      );
END;
GO
