/*
    2026-11-24 · Tickets: "Atender primero" (bandejas, plazos y seguimiento).
    - TICK_TICKETS: FechaSeguimiento ("recordarme el...") y SeguimientoDesde (cuando se programó; si el cliente
      escribe despues, el ticket vuelve a la cola aunque la fecha no haya llegado).
    - TICK_ESTADOS_HIST: cada cambio de estado (de/a), para frenar el plazo mientras se espera al cliente y,
      mas adelante, medir el tiempo en cada etapa.
    - TICK_PLAZOS: horas habiles de primera respuesta y de resolucion por prioridad efectiva (0 a 3).
    - TA_CONFIGURACION (grupo TICKETS): pesos del orden "Atender primero" y umbrales. Solo se insertan si faltan.
    - Menu web: Tickets > Configuracion (D010185-WEB-TICKETS-CONFIG).
    No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NULL RETURN;

IF COL_LENGTH(N'dbo.TICK_TICKETS', N'FechaSeguimiento') IS NULL
    ALTER TABLE dbo.TICK_TICKETS ADD FechaSeguimiento datetime NULL;
IF COL_LENGTH(N'dbo.TICK_TICKETS', N'SeguimientoDesde') IS NULL
    ALTER TABLE dbo.TICK_TICKETS ADD SeguimientoDesde datetime NULL;
GO

IF OBJECT_ID(N'dbo.TICK_ESTADOS_HIST', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_ESTADOS_HIST
    (
        IdHist bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TICK_ESTADOS_HIST PRIMARY KEY,
        IdTicket bigint NOT NULL,
        EstadoAnterior nvarchar(30) NULL,
        EstadoNuevo nvarchar(30) NOT NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_TICK_ESTADOS_HIST_FechaHora DEFAULT (GETDATE()),
        Usuario nvarchar(80) NULL
    );
    CREATE NONCLUSTERED INDEX IX_TICK_ESTADOS_HIST_Ticket ON dbo.TICK_ESTADOS_HIST (IdTicket, FechaHora);
END;
GO

IF OBJECT_ID(N'dbo.TICK_PLAZOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_PLAZOS
    (
        Prioridad int NOT NULL CONSTRAINT PK_TICK_PLAZOS PRIMARY KEY,
        HorasRespuesta int NOT NULL,
        HorasResolucion int NOT NULL,
        Activo bit NOT NULL CONSTRAINT DF_TICK_PLAZOS_Activo DEFAULT (1),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT CK_TICK_PLAZOS_Prioridad CHECK (Prioridad BETWEEN 0 AND 3)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_PLAZOS WHERE Prioridad = 3) INSERT INTO dbo.TICK_PLAZOS (Prioridad, HorasRespuesta, HorasResolucion) VALUES (3, 2, 8);
IF NOT EXISTS (SELECT 1 FROM dbo.TICK_PLAZOS WHERE Prioridad = 2) INSERT INTO dbo.TICK_PLAZOS (Prioridad, HorasRespuesta, HorasResolucion) VALUES (2, 4, 24);
IF NOT EXISTS (SELECT 1 FROM dbo.TICK_PLAZOS WHERE Prioridad = 1) INSERT INTO dbo.TICK_PLAZOS (Prioridad, HorasRespuesta, HorasResolucion) VALUES (1, 8, 40);
IF NOT EXISTS (SELECT 1 FROM dbo.TICK_PLAZOS WHERE Prioridad = 0) INSERT INTO dbo.TICK_PLAZOS (Prioridad, HorasRespuesta, HorasResolucion) VALUES (0, 16, 80);
GO

-- Pesos y umbrales del orden "Atender primero" (valores por defecto; se editan en Tickets > Configuracion).
IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
BEGIN
    DECLARE @k TABLE (Clave nvarchar(100), Valor nvarchar(150));
    INSERT INTO @k (Clave, Valor) VALUES
        (N'TICKETS-PESO-VENCIDO', N'100'),
        (N'TICKETS-PESO-POR-VENCER', N'50'),
        (N'TICKETS-PESO-PRIORIDAD', N'15'),
        (N'TICKETS-PESO-CLIENTE-PRIORITARIO', N'20'),
        (N'TICKETS-PESO-VARIOS-CASOS', N'10'),
        (N'TICKETS-PESO-HORA-ESPERA', N'1'),
        (N'TICKETS-PESO-HORA-ESPERA-TOPE', N'40'),
        (N'TICKETS-PESO-SEGUIMIENTO-VENCIDO', N'40'),
        (N'TICKETS-PLAZO-POR-VENCER-PORC', N'75'),
        (N'TICKETS-CLIENTE-VARIOS-CASOS', N'3'),
        (N'TICKETS-ESTADOS-ESPERA-CLIENTE', N'EN_ESPERA');

    INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, GRUPO, FechaHora_Grabacion, FechaHora_Modificacion)
    SELECT k.Clave, k.Valor, N'TICKETS', GETDATE(), GETDATE()
    FROM @k k
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE UPPER(LTRIM(RTRIM(c.CLAVE))) = UPPER(k.Clave));
END;
GO

-- Menu web: Tickets > Configuracion
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;
GO

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NOT NULL
EXEC sys.sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D010185-WEB-TICKETS-CONFIG'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, NombreWeb)
    VALUES (N''ALFA'', N''D010185-WEB-TICKETS-CONFIG'', N''/tickets/configuracion'', N''TicketsConfiguracion'', N''bi-sliders'', 1, 9, 0,
            N''Configuración de tickets'');';

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NOT NULL
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N''/tickets/configuracion'', Componente = N''TicketsConfiguracion'', Icono = N''bi-sliders'',
    NombreWeb = N''Configuración de tickets'', HabilitadoWeb = 1
WHERE Clave = N''D010185-WEB-TICKETS-CONFIG'';
UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D010185-WEB-TICKETS''
WHERE Clave = N''D010185-WEB-TICKETS-CONFIG'' AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave = N''D010185-WEB-TICKETS'');';
GO

-- Permisos: quienes tienen restricciones web y ya ven Tickets reciben tambien su configuracion.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(w.Usuario)), LTRIM(RTRIM(w.Sistema)), N'D010185-WEB-TICKETS-CONFIG', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB w
    WHERE UPPER(LTRIM(RTRIM(w.Clave))) = N'D010185-WEB-TICKETS'
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D010185-WEB-TICKETS-CONFIG');
