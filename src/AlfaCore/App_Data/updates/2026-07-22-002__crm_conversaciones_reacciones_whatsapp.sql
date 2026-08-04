IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'MessageType') IS NULL
    RETURN;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.CONV_MENSAJES')
      AND name = N'CK_CONV_MENSAJES_MessageType'
)
BEGIN
    ALTER TABLE dbo.CONV_MENSAJES
        DROP CONSTRAINT CK_CONV_MENSAJES_MessageType;
END;

ALTER TABLE dbo.CONV_MENSAJES WITH CHECK
    ADD CONSTRAINT CK_CONV_MENSAJES_MessageType
    CHECK
    (
        MessageType IN
        (
            N'TEXT',
            N'IMAGE',
            N'DOCUMENT',
            N'AUDIO',
            N'VIDEO',
            N'STICKER',
            N'LOCATION',
            N'CONTACT',
            N'REACTION',
            N'SYSTEM',
            N'UNKNOWN'
        )
    );

ALTER TABLE dbo.CONV_MENSAJES
    CHECK CONSTRAINT CK_CONV_MENSAJES_MessageType;
