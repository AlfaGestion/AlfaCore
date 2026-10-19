/*
    CONV_MENSAJES.AutorAutomatico: qué automatización mandó el mensaje (BOT, AUTOMATIZACION,
    AUTOCIERRE_AVISO, AUTOCIERRE, BIENVENIDA, REGLA, SLA, PROGRAMADO).

    Los mensajes automáticos se grababan con UsuarioAutor/SistemaAutor vacíos (esas columnas tienen FK
    a TA_USUARIOS y un CHECK de "los dos o ninguno"), así que nada podía reconocerlos: el tope de
    respuestas del asistente, la contención repetida, el aviso previo al cierre por inactividad, el
    mensaje fijo de fuera de horario y los recordatorios de SLA no funcionaban. La aplicación también
    agrega la columna sola si falta (EnsureMensajeOrigenColumnAsync). Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'AutorAutomatico') IS NULL
    ALTER TABLE dbo.CONV_MENSAJES ADD AutorAutomatico nvarchar(30) NULL;
