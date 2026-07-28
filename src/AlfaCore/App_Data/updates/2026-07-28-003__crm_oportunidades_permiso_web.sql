-- ============================================================
-- CRM oportunidades - permiso web para usuarios con CRM
-- Clave web: D010185-WEB-CRM
-- Corrige bases donde el menú ya fue creado antes de heredar
-- permisos en ALFACORE_TAREAS_WEB.
-- ============================================================

SET NOCOUNT ON;

-- Guardia: la tabla de permisos web debe existir.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- Asegura que el menú web exista si el script previo no llegó a insertarlo.
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    BEGIN
        ALTER TABLE dbo.ALFACORE_MENU_WEB
            ADD NombreWeb nvarchar(150) NULL;
    END;

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
        COALESCE((SELECT TOP (1) Menu FROM dbo.ALFACORE_MENU_WEB WHERE Clave = N'D010185'), N'ALFA'),
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
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.ALFACORE_MENU_WEB x
        WHERE x.Clave = N'D010185-WEB-CRM'
    );

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
END;
GO

-- Hereda el permiso para usuarios con permisos web explícitos dentro de CRM.
INSERT INTO dbo.ALFACORE_TAREAS_WEB
(
    Usuario,
    Sistema,
    Clave,
    FechaHoraGrabacion,
    UsuarioGrabacion
)
SELECT DISTINCT
    tw.Usuario,
    tw.Sistema,
    N'D010185-WEB-CRM',
    GETDATE(),
    N'MIGRACION-CRM'
FROM dbo.ALFACORE_TAREAS_WEB tw
WHERE UPPER(LTRIM(RTRIM(tw.Clave))) IN
(
    N'D010185',
    N'D010185WEB',
    N'D010185-WEB-CONVERSACIONES',
    N'D010185-WEB-TICKETS',
    N'D010186'
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_TAREAS_WEB x
    WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(tw.Usuario)))
      AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(tw.Sistema)))
      AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'D010185-WEB-CRM'
);
GO

-- También hereda desde usuarios legacy con permiso explícito al módulo CRM.
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB
    (
        Usuario,
        Sistema,
        Clave,
        FechaHoraGrabacion,
        UsuarioGrabacion
    )
    SELECT DISTINCT
        t.USUARIO,
        t.SISTEMA,
        N'D010185-WEB-CRM',
        GETDATE(),
        N'MIGRACION-CRM'
    FROM dbo.TA_TAREAS t
    WHERE UPPER(LTRIM(RTRIM(t.TAREA))) IN (N'D010185', N'D010185-WEB-CRM')
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'D010185-WEB-CRM'
      );
END;
GO
