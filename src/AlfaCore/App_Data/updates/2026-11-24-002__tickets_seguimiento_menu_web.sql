/*
    2026-11-24 · Tickets > Seguimiento (/tickets/seguimiento): panel de entradas/cierres, atraso, cumplimiento de
    plazos, tiempo por etapa, carga por tecnico y clientes con mas casos.
    Menu web D010185-WEB-TICKETS-SEGUIMIENTO, hijo de Tickets. Permisos: quienes con restricciones web ya ven
    Tickets. No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;
GO

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NOT NULL
EXEC sys.sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D010185-WEB-TICKETS-SEGUIMIENTO'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, NombreWeb)
    VALUES (N''ALFA'', N''D010185-WEB-TICKETS-SEGUIMIENTO'', N''/tickets/seguimiento'', N''TicketsSeguimiento'', N''bi-speedometer2'', 1, 2, 0,
            N''Seguimiento de tickets'');';

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NOT NULL
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N''/tickets/seguimiento'', Componente = N''TicketsSeguimiento'', Icono = N''bi-speedometer2'',
    NombreWeb = N''Seguimiento de tickets'', HabilitadoWeb = 1
WHERE Clave = N''D010185-WEB-TICKETS-SEGUIMIENTO'';
UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D010185-WEB-TICKETS''
WHERE Clave = N''D010185-WEB-TICKETS-SEGUIMIENTO'' AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave = N''D010185-WEB-TICKETS'');';
GO

IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(w.Usuario)), LTRIM(RTRIM(w.Sistema)), N'D010185-WEB-TICKETS-SEGUIMIENTO', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB w
    WHERE UPPER(LTRIM(RTRIM(w.Clave))) = N'D010185-WEB-TICKETS'
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D010185-WEB-TICKETS-SEGUIMIENTO');
