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
    N'D010185',
    N'/shell/D010185',
    N'ShellWorkspacePage',
    N'bi-headset',
    1,
    18,
    0,
    N'CRM web con conversaciones y tickets.',
    N'CRM'
FROM dbo.TA_MENU m
WHERE m.Clave = N'D010185'
  AND ISNULL(m.Habilitado, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.ALFACORE_MENU_WEB w
      WHERE w.Menu = m.Menu
        AND w.Clave = N'D010185'
  );
GO

UPDATE w
SET w.RutaWeb = N'/shell/D010185',
    w.Componente = N'ShellWorkspacePage',
    w.Icono = N'bi-headset',
    w.HabilitadoWeb = 1,
    w.OrdenWeb = 18,
    w.EsFavoritoDefault = 0,
    w.Observacion = N'CRM web con conversaciones y tickets.',
    w.NombreWeb = N'CRM'
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D010185';
GO

IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = N'CRM, conversaciones, tickets y seguimiento comercial.'
    WHERE Clave = N'D010185'
      AND ISNULL(CAST(Descripcion AS nvarchar(max)), N'') = N'';
END;
GO
