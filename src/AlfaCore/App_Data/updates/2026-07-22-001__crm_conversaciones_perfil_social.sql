/*
Amplía el soporte de conversaciones sociales.

Objetivos:
- admitir los identificadores extensos que entrega Meta en mensajes de Instagram
- persistir datos públicos del perfil social para mostrarlos en el inbox
*/

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'MessageIdExterno') IS NULL
        ALTER TABLE dbo.CONV_MENSAJES ADD MessageIdExterno nvarchar(500) NULL;
    ELSE IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'MessageIdExterno') < 1000
        ALTER TABLE dbo.CONV_MENSAJES ALTER COLUMN MessageIdExterno nvarchar(500) NULL;

    IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'ReplyToMessageIdExterno') IS NULL
        ALTER TABLE dbo.CONV_MENSAJES ADD ReplyToMessageIdExterno nvarchar(500) NULL;
    ELSE IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'ReplyToMessageIdExterno') < 1000
        ALTER TABLE dbo.CONV_MENSAJES ALTER COLUMN ReplyToMessageIdExterno nvarchar(500) NULL;
END;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'UsuarioExterno') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD UsuarioExterno nvarchar(150) NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'FotoPerfilUrl') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD FotoPerfilUrl nvarchar(max) NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'SeguidoresExterno') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD SeguidoresExterno int NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'PerfilExternoVerificado') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD PerfilExternoVerificado bit NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'UsuarioSigueCuenta') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD UsuarioSigueCuenta bit NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'CuentaSigueUsuario') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD CuentaSigueUsuario bit NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'FechaHoraPerfilExterno') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD FechaHoraPerfilExterno datetime NULL;
