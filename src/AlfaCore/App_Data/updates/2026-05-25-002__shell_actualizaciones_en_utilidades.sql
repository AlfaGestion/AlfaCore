SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
    RETURN;

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
    NombreWeb
)
SELECT
    m.Menu,
    N'D989003',
    N'/actualizaciones',
    N'Actualizaciones',
    N'bi-cloud-arrow-up',
    1,
    9890030,
    0,
    N'Actualizaciones de base de datos y control del motor de versiones.',
    N'Actualizaciones'
FROM dbo.TA_MENU m
WHERE m.Clave = N'D989003'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.ALFACORE_MENU_WEB w
      WHERE w.Menu = m.Menu
        AND w.Clave = m.Clave
  );

UPDATE w
SET
    RutaWeb = N'/actualizaciones',
    Componente = N'Actualizaciones',
    Icono = N'bi-cloud-arrow-up',
    HabilitadoWeb = 1,
    OrdenWeb = COALESCE(NULLIF(w.OrdenWeb, 0), 9890030),
    Observacion = COALESCE(NULLIF(w.Observacion, N''), N'Actualizaciones de base de datos y control del motor de versiones.'),
    NombreWeb = COALESCE(NULLIF(w.NombreWeb, N''), N'Actualizaciones')
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D989003';

IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = N'Herramientas avanzadas de administración y mantenimiento del sistema.'
    WHERE Clave = N'D9890'
      AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), N'') = N'';

    UPDATE dbo.TA_MENU
    SET Descripcion = N'Actualizaciones de base, control de scripts SQL e historial de versiones aplicadas.'
    WHERE Clave = N'D989003'
      AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), N'') = N'';
END;
