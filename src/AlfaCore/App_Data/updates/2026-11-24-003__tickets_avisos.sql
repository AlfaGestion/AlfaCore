/*
    2026-11-24 · Tickets: avisos automaticos y prioridad sugerida por IA.
    - TICK_AVISOS: cada aviso enviado (plazo por vencer/vencido, seguimiento vencido, cliente prioritario esperando,
      resumen diario, prioridad sugerida por IA). La clave unica evita mandar el mismo aviso dos veces.
    - TA_CONFIGURACION (grupo TICKETS): que avisos se mandan, a quien, desde cuantas horas y a que hora va el
      resumen diario. Solo se insertan si faltan.
    No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NULL RETURN;

IF OBJECT_ID(N'dbo.TICK_AVISOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_AVISOS
    (
        IdAviso bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TICK_AVISOS PRIMARY KEY,
        IdTicket bigint NOT NULL,
        Tipo nvarchar(30) NOT NULL,
        Clave nvarchar(120) NOT NULL,
        Destino nvarchar(200) NULL,
        Detalle nvarchar(400) NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_TICK_AVISOS_FechaHora DEFAULT (GETDATE())
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_TICK_AVISOS_Clave ON dbo.TICK_AVISOS (IdTicket, Tipo, Clave);
END;
GO

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
BEGIN
    DECLARE @k TABLE (Clave nvarchar(100), Valor nvarchar(150));
    INSERT INTO @k (Clave, Valor) VALUES
        (N'TICKETS-AVISOS-ACTIVO', N'S'),
        (N'TICKETS-AVISO-POR-VENCER', N'S'),
        (N'TICKETS-AVISO-VENCIDO', N'S'),
        (N'TICKETS-AVISO-SEGUIMIENTO', N'S'),
        (N'TICKETS-AVISO-COORDINADORES', N''),
        (N'TICKETS-AVISO-PRIORITARIO-HORAS', N'4'),
        (N'TICKETS-RESUMEN-DIARIO-HORA', N'9'),
        (N'TICKETS-IA-PRIORIDAD-BOT', N'S');

    INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, GRUPO, FechaHora_Grabacion, FechaHora_Modificacion)
    SELECT k.Clave, k.Valor, N'TICKETS', GETDATE(), GETDATE()
    FROM @k k
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE UPPER(LTRIM(RTRIM(c.CLAVE))) = UPPER(k.Clave));
END;
GO
