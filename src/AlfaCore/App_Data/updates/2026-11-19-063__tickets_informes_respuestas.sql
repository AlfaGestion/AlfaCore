/*
  Respuestas del cliente a los informes de tickets publicados: "Conforme" o "Tengo una consulta",
  desde la pagina publica /{idweb}/informe/{token}. Cada respuesta tambien queda en TICK_ACTIVIDAD.
  Idempotente.
*/
IF OBJECT_ID(N'dbo.TICK_INFORMES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.TICK_INFORME_RESPUESTAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_INFORME_RESPUESTAS (
        IdRespuesta int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TICK_INFORME_RESPUESTAS PRIMARY KEY,
        IdInforme int NOT NULL,
        Tipo varchar(12) NOT NULL,
        Nombre nvarchar(120) NULL,
        Texto nvarchar(2000) NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_TICK_INFORME_RESPUESTAS_Fecha DEFAULT (GETDATE())
    );
    CREATE INDEX IX_TICK_INFORME_RESPUESTAS_Informe ON dbo.TICK_INFORME_RESPUESTAS (IdInforme, FechaHora);
END
