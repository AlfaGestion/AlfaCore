IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
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
    NombreWeb
)
SELECT
    m.Menu,
    N'D015002',
    N'/seguridad/autorizacion-tareas',
    N'AutorizacionTareas',
    N'bi-shield-lock-fill',
    1,
    15002,
    0,
    N'Permisos reales compartidos entre Desktop y AlfaCore sobre el árbol oficial del menú.',
    N'Autorización de tareas'
FROM dbo.TA_MENU m
WHERE m.Clave = N'D015002'
  AND ISNULL(m.Habilitado, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.ALFACORE_MENU_WEB w
      WHERE w.Menu = m.Menu
        AND w.Clave = N'D015002'
  );
GO

UPDATE w
SET w.RutaWeb = N'/seguridad/autorizacion-tareas',
    w.Componente = N'AutorizacionTareas',
    w.Icono = N'bi-shield-lock-fill',
    w.HabilitadoWeb = 1,
    w.OrdenWeb = 15002,
    w.EsFavoritoDefault = 0,
    w.Observacion = N'Permisos reales compartidos entre Desktop y AlfaCore sobre el árbol oficial del menú.',
    w.NombreWeb = N'Autorización de tareas'
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D015002';
GO

IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = N'Seguridad y administración de usuarios del sistema.'
    WHERE Clave = N'D0150'
      AND ISNULL(CAST(Descripcion AS nvarchar(max)), N'') = N'';

    UPDATE dbo.TA_MENU
    SET Descripcion = N'Administración de permisos reales compartidos entre Desktop y AlfaCore.'
    WHERE Clave = N'D015002'
      AND ISNULL(CAST(Descripcion AS nvarchar(max)), N'') = N'';
END;
GO
