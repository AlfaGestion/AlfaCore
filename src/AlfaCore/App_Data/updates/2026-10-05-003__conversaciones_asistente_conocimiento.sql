/*
  Asistente IA de Conversaciones: información del negocio por bloques temáticos y archivos de
  conocimiento (2026-10-05).

  - CONV_ASISTENTE_BLOQUES: texto por tema (horarios, envíos, medios de pago, preguntas frecuentes...).
    Los bloques activos se suman a la "Información general" (CONV_ASISTENTE.Informacion) al responder.
  - CONV_ASISTENTE_ARCHIVOS: ficha de cada archivo subido. El contenido vive en un vector store de
    OpenAI propio de la base (id en TA_CONFIGURACION, clave CONV_ASISTENTE_VECTOR_STORE_ID); acá solo se
    guarda nombre, tamaño, estado e ids de OpenAI.
  Idempotente.
*/
IF OBJECT_ID(N'dbo.CONV_ASISTENTE_BLOQUES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ASISTENTE_BLOQUES
    (
        IdBloque int IDENTITY(1,1) NOT NULL,
        Categoria nvarchar(40) NOT NULL CONSTRAINT DF_CONV_ASISTENTE_BLOQUES_Categoria DEFAULT (N'GENERAL'),
        Titulo nvarchar(150) NOT NULL,
        Contenido nvarchar(max) NOT NULL CONSTRAINT DF_CONV_ASISTENTE_BLOQUES_Contenido DEFAULT (N''),
        Activo bit NOT NULL CONSTRAINT DF_CONV_ASISTENTE_BLOQUES_Activo DEFAULT (1),
        Orden int NOT NULL CONSTRAINT DF_CONV_ASISTENTE_BLOQUES_Orden DEFAULT (100),
        FechaAlta datetime NOT NULL CONSTRAINT DF_CONV_ASISTENTE_BLOQUES_FechaAlta DEFAULT (GETDATE()),
        UsuarioAlta nvarchar(80) NULL,
        FechaModificacion datetime NULL,
        UsuarioModificacion nvarchar(80) NULL,
        CONSTRAINT PK_CONV_ASISTENTE_BLOQUES PRIMARY KEY CLUSTERED (IdBloque)
    );
END;

IF OBJECT_ID(N'dbo.CONV_ASISTENTE_ARCHIVOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ASISTENTE_ARCHIVOS
    (
        IdArchivo int IDENTITY(1,1) NOT NULL,
        NombreArchivo nvarchar(260) NOT NULL,
        TipoContenido nvarchar(120) NULL,
        Tamano bigint NOT NULL CONSTRAINT DF_CONV_ASISTENTE_ARCHIVOS_Tamano DEFAULT (0),
        OpenAiFileId nvarchar(80) NULL,
        VectorStoreId nvarchar(80) NULL,
        Estado nvarchar(20) NOT NULL CONSTRAINT DF_CONV_ASISTENTE_ARCHIVOS_Estado DEFAULT (N'PROCESANDO'),
        UltimoError nvarchar(500) NULL,
        Baja bit NOT NULL CONSTRAINT DF_CONV_ASISTENTE_ARCHIVOS_Baja DEFAULT (0),
        FechaAlta datetime NOT NULL CONSTRAINT DF_CONV_ASISTENTE_ARCHIVOS_FechaAlta DEFAULT (GETDATE()),
        UsuarioAlta nvarchar(80) NULL,
        FechaModificacion datetime NULL,
        CONSTRAINT PK_CONV_ASISTENTE_ARCHIVOS PRIMARY KEY CLUSTERED (IdArchivo),
        CONSTRAINT CK_CONV_ASISTENTE_ARCHIVOS_Estado CHECK (Estado IN (N'PROCESANDO', N'LISTO', N'ERROR'))
    );
END;
