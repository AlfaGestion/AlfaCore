/*
Conversaciones - indices de rendimiento

Objetivo:
- reducir CPU en el inbox y auditoria del modulo Conversaciones
- soportar polling frecuente sin escaneos completos sobre CONV_MENSAJES
- mantener el script idempotente y seguro para bases donde el modulo no exista
*/

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CONV_CONVERSACIONES_Inbox_Orden'
      AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_CONVERSACIONES_Inbox_Orden
        ON dbo.CONV_CONVERSACIONES (Canal, CodigoEstado, FechaHoraUltimoMensaje DESC, IdConversacion DESC)
        INCLUDE (IdTecnico, Archivada, Bloqueada, ClienteCodigo, IdContacto, TelefonoWhatsApp, NombreVisible, ResumenUltimoMensaje, FechaHoraCierre);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CONV_CONVERSACIONES_Tecnico_Orden'
      AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_CONVERSACIONES_Tecnico_Orden
        ON dbo.CONV_CONVERSACIONES (IdTecnico, FechaHoraUltimoMensaje DESC, IdConversacion DESC)
        INCLUDE (Canal, CodigoEstado, Archivada, ClienteCodigo, IdContacto, TelefonoWhatsApp, NombreVisible, ResumenUltimoMensaje);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CONV_MENSAJES_Conversacion_Direction_FechaDesc'
      AND object_id = OBJECT_ID(N'dbo.CONV_MENSAJES')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_MENSAJES_Conversacion_Direction_FechaDesc
        ON dbo.CONV_MENSAJES (IdConversacion, Direction, FechaHora DESC, IdMensaje DESC)
        INCLUDE (FechaHora_Grabacion, MessageType, EstadoEnvio, IdTecnicoAutor);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CONV_MENSAJES_Conversacion_FechaDesc'
      AND object_id = OBJECT_ID(N'dbo.CONV_MENSAJES')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_MENSAJES_Conversacion_FechaDesc
        ON dbo.CONV_MENSAJES (IdConversacion, FechaHora DESC, IdMensaje DESC)
        INCLUDE (Direction, MessageType, FechaHora_Grabacion, EstadoEnvio, IdTecnicoAutor);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CONV_MENSAJES_Fecha_Auditoria'
      AND object_id = OBJECT_ID(N'dbo.CONV_MENSAJES')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_MENSAJES_Fecha_Auditoria
        ON dbo.CONV_MENSAJES (FechaHora DESC, IdMensaje DESC)
        INCLUDE (IdConversacion, Direction, MessageType, IdTecnicoAutor);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CONV_MENSAJES_Autor_Direction_Fecha'
      AND object_id = OBJECT_ID(N'dbo.CONV_MENSAJES')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_MENSAJES_Autor_Direction_Fecha
        ON dbo.CONV_MENSAJES (IdTecnicoAutor, Direction, FechaHora DESC)
        INCLUDE (IdConversacion, MessageType);
END;
GO

IF OBJECT_ID(N'dbo.CONV_ADJUNTOS', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_CONV_ADJUNTOS_IdMensaje'
         AND object_id = OBJECT_ID(N'dbo.CONV_ADJUNTOS')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_ADJUNTOS_IdMensaje
        ON dbo.CONV_ADJUNTOS (IdMensaje)
        INCLUDE (IdAdjunto, TipoArchivo, NombreArchivo, MimeType, RutaLocal, UrlArchivo);
END;
GO

IF OBJECT_ID(N'dbo.CONV_ASIGNACIONES', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_CONV_ASIGNACIONES_Fecha_Tecnico'
         AND object_id = OBJECT_ID(N'dbo.CONV_ASIGNACIONES')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_ASIGNACIONES_Fecha_Tecnico
        ON dbo.CONV_ASIGNACIONES (FechaHora, IdTecnico, IdConversacion);
END;
GO

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES_LECTURA_USUARIO', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_CONV_CONV_LECT_USU_Usuario_Conversacion'
         AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES_LECTURA_USUARIO')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_CONV_LECT_USU_Usuario_Conversacion
        ON dbo.CONV_CONVERSACIONES_LECTURA_USUARIO (Usuario, IdConversacion)
        INCLUDE (Sistema, IdUltimoMensajeLeido, FechaHora_Modificacion);
END;
GO
