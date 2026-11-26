/*
    Historial de la ayuda con IA (2026-11-26).
    - AYUDA_IA_MENSAJES: cada pregunta y respuesta de la ayuda con IA, por usuario y conversación, para
      retomarla después. Se borran solas las más viejas que AYUDA_IA_HISTORIAL_DIAS.
    - TA_CONFIGURACION (grupo AYUDA): AYUDA_IA_HISTORIAL_DIAS = 60. Solo se inserta si falta.
    No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.AYUDA_IA_MENSAJES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AYUDA_IA_MENSAJES
    (
        IdMensaje bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_AYUDA_IA_MENSAJES PRIMARY KEY,
        IdConversacion uniqueidentifier NOT NULL,
        Usuario nvarchar(100) NOT NULL,
        Tema nvarchar(60) NOT NULL CONSTRAINT DF_AYUDA_IA_MENSAJES_Tema DEFAULT (N''),
        Pregunta nvarchar(2000) NOT NULL,
        Respuesta nvarchar(max) NOT NULL,
        FuentesJson nvarchar(max) NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_AYUDA_IA_MENSAJES_FechaHora DEFAULT (GETDATE())
    );
END;
GO

IF OBJECT_ID(N'dbo.AYUDA_IA_MENSAJES', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AYUDA_IA_MENSAJES') AND name = N'IX_AYUDA_IA_MENSAJES_Usuario')
    CREATE NONCLUSTERED INDEX IX_AYUDA_IA_MENSAJES_Usuario ON dbo.AYUDA_IA_MENSAJES (Usuario, FechaHora) INCLUDE (IdConversacion);
GO

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'AYUDA_IA_HISTORIAL_DIAS')
    INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, GRUPO, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES (N'AYUDA_IA_HISTORIAL_DIAS', N'60', N'AYUDA', GETDATE(), GETDATE());
GO
