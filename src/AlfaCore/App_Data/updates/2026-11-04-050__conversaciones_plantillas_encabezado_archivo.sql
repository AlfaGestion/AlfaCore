/*
    Conversaciones - Plantillas de WhatsApp con archivo en el encabezado (PDF o imagen).
    Guarda con cada plantilla su archivo de encabezado (la oferta en PDF o la imagen, como hacía Odoo):
      - se usa como ejemplo al pedir la aprobación en Meta (header_handle);
      - se manda por defecto al enviar la plantilla desde Conversaciones (se puede reemplazar al enviar).
    En SQL para que quede con el backup de la base. Idempotente.
*/
IF OBJECT_ID(N'dbo.CONV_PLANTILLAS', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.CONV_PLANTILLAS_ENCABEZADO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_PLANTILLAS_ENCABEZADO
    (
        IdPlantilla bigint NOT NULL,
        NombreArchivo nvarchar(255) NOT NULL,
        MimeType nvarchar(100) NOT NULL,
        Contenido varbinary(max) NOT NULL,
        TamanoBytes bigint NOT NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_ENCABEZADO_FHG DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_PLANTILLAS_ENCABEZADO PRIMARY KEY CLUSTERED (IdPlantilla),
        CONSTRAINT FK_CONV_PLANTILLAS_ENCABEZADO_PLANTILLA FOREIGN KEY (IdPlantilla) REFERENCES dbo.CONV_PLANTILLAS (IdPlantilla)
    );
END;
GO
