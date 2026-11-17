/*
    Conversaciones - Respuestas rápidas.
    Textos guardados que se insertan en el chat escribiendo "::" y el atajo (por ejemplo "::info").
    Se cargan en Configuración > Respuestas rápidas y son de toda la base (las ven todos los usuarios).
    Reemplaza las "respuestas predefinidas" que usaban los clientes que venían de Odoo. Idempotente.
*/
IF OBJECT_ID(N'dbo.CONV_RESPUESTAS_RAPIDAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_RESPUESTAS_RAPIDAS
    (
        IdRespuesta int IDENTITY(1,1) NOT NULL,
        Atajo nvarchar(40) NOT NULL,
        Texto nvarchar(max) NOT NULL,
        Activa bit NOT NULL CONSTRAINT DF_CONV_RESPUESTAS_RAPIDAS_Activa DEFAULT (1),
        Usuario nvarchar(60) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_RESPUESTAS_RAPIDAS_FHG DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_RESPUESTAS_RAPIDAS PRIMARY KEY CLUSTERED (IdRespuesta)
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_CONV_RESPUESTAS_RAPIDAS_Atajo'
      AND object_id = OBJECT_ID(N'dbo.CONV_RESPUESTAS_RAPIDAS'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_RESPUESTAS_RAPIDAS_Atajo
        ON dbo.CONV_RESPUESTAS_RAPIDAS (Atajo);
END;
GO
