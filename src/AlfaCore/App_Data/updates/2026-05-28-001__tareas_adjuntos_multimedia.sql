SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_ADJUNTOS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_TAREAS_ADJUNTOS
        (
            IdAdjunto bigint IDENTITY(1,1) NOT NULL,
            IdTarea bigint NOT NULL,
            NombreArchivo nvarchar(255) NOT NULL,
            MimeType nvarchar(100) NOT NULL,
            TamanoBytes bigint NOT NULL,
            Contenido varbinary(max) NOT NULL,
            UsuarioAlta nvarchar(50) NULL,
            FechaHoraAlta datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_ADJUNTOS_Alta DEFAULT (GETDATE()),
            UsuarioModificacion nvarchar(50) NULL,
            FechaHoraModificacion datetime NULL,
            Activa bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_ADJUNTOS_Activa DEFAULT (1),
            CONSTRAINT PK_ALFACORE_TAREAS_ADJUNTOS PRIMARY KEY CLUSTERED (IdAdjunto ASC),
            CONSTRAINT FK_ALFACORE_TAREAS_ADJUNTOS_TAREA
                FOREIGN KEY (IdTarea) REFERENCES dbo.ALFACORE_TAREAS (IdTarea),
            CONSTRAINT CK_ALFACORE_TAREAS_ADJUNTOS_MIME
                CHECK (MimeType LIKE N'image/%' OR MimeType LIKE N'audio/%' OR MimeType LIKE N'video/%'),
            CONSTRAINT CK_ALFACORE_TAREAS_ADJUNTOS_TAMANO
                CHECK (TamanoBytes > 0 AND TamanoBytes <= 52428800)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_ALFACORE_TAREAS_ADJUNTOS_TAREA'
          AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_ADJUNTOS')
    )
    BEGIN
        CREATE INDEX IX_ALFACORE_TAREAS_ADJUNTOS_TAREA
            ON dbo.ALFACORE_TAREAS_ADJUNTOS (IdTarea, Activa, FechaHoraAlta);
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
