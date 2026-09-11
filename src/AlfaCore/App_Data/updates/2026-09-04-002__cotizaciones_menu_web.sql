/*
    Cotizaciones - alta en el menú web.
    Módulo 100% nuevo, sin equivalente en TA_MENU (nunca existió en la app de escritorio) --
    se sigue el patrón autosuficiente de ALFACORE_MENU_WEB (PadreClave propio, sin depender
    de TA_MENU), el mismo que ya se usa para nodos web-nativos como
    D010185-WEB-CONVERSACIONES/D010185-WEB-TICKETS (ver 2026-07-01-001).
    Nodo raíz propio (no cuelga de ningún módulo existente): PadreClave = 'D'.
    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB
    WHERE Menu = N'ALFA'
      AND Clave = N'DCOTIZACIONES'
)
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
        NombreWeb,
        DescripcionWeb,
        PadreClave
    )
    VALUES
    (
        N'ALFA',
        N'DCOTIZACIONES',
        N'/cotizaciones',
        N'Cotizaciones',
        N'bi-file-earmark-text',
        1,
        19000,
        0,
        N'Cotizaciones comerciales: artículos, servicios, packs de horas y configurador de Alfa Gestión, con versionado y seguimiento por estado.',
        N'Cotizaciones',
        N'Cotizaciones comerciales: artículos, servicios, packs de horas y configurador de Alfa Gestión, con versionado y seguimiento por estado.',
        N'D'
    );
END;
GO

-- Permisos: solo para usuarios que ya tienen filas explícitas en ALFACORE_TAREAS_WEB
-- (usuarios con restricciones activas); usuarios sin ninguna fila tienen acceso irrestricto.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT t.Usuario, t.Sistema, N'DCOTIZACIONES', GETDATE(), N'MIGRACION-COTIZACIONES'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE ISNULL(LTRIM(RTRIM(t.Clave)), N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'DCOTIZACIONES'
      );
END;
GO
