SET NOCOUNT ON;

-- 1. Guardia: si no existe ALFACORE_MENU_WEB, salir sin tocar nada.
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- 2. Columna NombreWeb en bases antiguas.
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

-- Clave nueva para el módulo web de partes de horas.
DECLARE @Clave nvarchar(50) = N'D010186';

-- Si la clave no existe en TA_MENU, agregarla dentro del script.
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Clave = @Clave)
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
        N'ALFA',
        N'D010185',
        @Clave,
        N'Partes de horas',
        N'bi-clock-history',
        N'PartesHoras',
        1,
        N'10186',
        N'Registro y control de horas invertidas por cliente, ticket, tarea y tecnico.'
    );
END;

-- 3. INSERT en ALFACORE_MENU_WEB si la clave no existe.
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Clave = @Clave)
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
        N'ALFA',
        @Clave,
        N'/partes-horas',
        N'PartesHoras',
        N'bi-clock-history',
        1,
        10186,
        1,
        N'Registro y control de horas invertidas por cliente, ticket, tarea y tecnico.',
        N'Partes de horas'
    );
END;

-- 4. UPDATE en ALFACORE_MENU_WEB.
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N'/partes-horas',
    Componente = N'PartesHoras',
    Icono = N'bi-clock-history',
    HabilitadoWeb = 1,
    OrdenWeb = COALESCE(NULLIF(OrdenWeb, 0), 10186),
    Observacion = N'Registro y control de horas invertidas por cliente, ticket, tarea y tecnico.',
    NombreWeb = N'Partes de horas'
WHERE Clave = @Clave
   OR UPPER(LTRIM(RTRIM(ISNULL(NombreWeb, N'')))) = N'PARTES DE HORAS';
GO

-- 5. Descripción en TA_MENU solo si existe la columna y está vacía.
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = N'Registro y control de horas invertidas por cliente, ticket, tarea y tecnico.'
    WHERE Clave = N'D010186'
      AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), N'') = N'';
END;
GO

-- 6. Permisos en TA_TAREAS para usuarios con filas explícitas.
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D010186'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D010186'
      );
END;
GO
