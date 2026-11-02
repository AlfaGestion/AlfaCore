SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.TJ_Importacion',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TJ_Analisis',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TJ_Analisis
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ImportacionId bigint NOT NULL,
        Usuario nvarchar(256) NOT NULL,
        FechaUtc datetime2 NOT NULL CONSTRAINT DF_TJ_Analisis_Fecha DEFAULT SYSUTCDATETIME(),
        Contenido nvarchar(max) NOT NULL,
        CONSTRAINT FK_TJ_Analisis_Importacion FOREIGN KEY(ImportacionId) REFERENCES dbo.TJ_Importacion(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_TJ_Analisis_Recuperar ON dbo.TJ_Analisis(ImportacionId,Usuario,Id DESC);
END;
