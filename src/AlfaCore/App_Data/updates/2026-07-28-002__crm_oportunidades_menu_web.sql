-- ============================================================
-- CRM oportunidades - alta en menú web y permisos iniciales
-- Clave TA_MENU: D010185-WEB-CRM
-- Se agrega como opción hija del módulo CRM.
-- ============================================================

SET NOCOUNT ON;

-- 1. Guardia: ALFACORE_MENU_WEB debe existir
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- 2. Columna NombreWeb
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

-- 3. INSERT en ALFACORE_MENU_WEB
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
AND EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Clave = N'D010185')
AND NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Clave = N'D010185-WEB-CRM')
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
    SELECT TOP (1)
        Menu,
        N'D010185',
        N'D010185-WEB-CRM',
        N'CRM oportunidades',
        N'bi-kanban',
        N'Crm',
        1,
        N'101851',
        N'Pipeline comercial de leads y oportunidades con etapas configurables.'
    FROM dbo.TA_MENU
    WHERE Clave = N'D010185';
END;
GO

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
    NombreWeb,
    PadreClave
)
SELECT
    COALESCE(m.Menu, N'ALFA'),
    N'D010185-WEB-CRM',
    N'/crm',
    N'Crm',
    N'bi-kanban',
    1,
    4,
    0,
    N'Pipeline comercial de leads y oportunidades con etapas configurables.',
    N'CRM oportunidades',
    N'D010185'
FROM (SELECT TOP (1) Menu FROM dbo.ALFACORE_MENU_WEB WHERE Clave = N'D010185') w
FULL OUTER JOIN (SELECT TOP (1) Menu FROM dbo.TA_MENU WHERE Clave = N'D010185-WEB-CRM') m ON 1 = 1
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB x
    WHERE x.Menu = COALESCE(m.Menu, N'ALFA')
      AND x.Clave = N'D010185-WEB-CRM'
);
GO

-- 4. UPDATE en ALFACORE_MENU_WEB
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N'/crm',
    Componente = N'Crm',
    Icono = N'bi-kanban',
    HabilitadoWeb = 1,
    OrdenWeb = 4,
    EsFavoritoDefault = 0,
    Observacion = N'Pipeline comercial de leads y oportunidades con etapas configurables.',
    NombreWeb = N'CRM oportunidades',
    PadreClave = N'D010185'
WHERE Clave = N'D010185-WEB-CRM';
GO

-- 5. Descripción en TA_MENU
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = N'Pipeline comercial de leads y oportunidades con etapas configurables.'
    WHERE Clave = N'D010185-WEB-CRM'
      AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), N'') = N'';
END;
GO

-- 6. Permisos en TA_TAREAS
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D010185-WEB-CRM'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D010185-WEB-CRM'
      );
END;
GO
