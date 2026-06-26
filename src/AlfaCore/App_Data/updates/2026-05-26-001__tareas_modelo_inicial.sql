SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_LISTAS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_TAREAS_LISTAS
        (
            IdLista int IDENTITY(1,1) NOT NULL,
            Nombre nvarchar(120) NOT NULL,
            EsDefault bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_LISTAS_EsDefault DEFAULT (0),
            UsuarioAlta nvarchar(50) NULL,
            FechaHora_Alta datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_LISTAS_Alta DEFAULT (GETDATE()),
            FechaHora_Modificacion datetime NULL,
            Activa bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_LISTAS_Activa DEFAULT (1),
            CONSTRAINT PK_ALFACORE_TAREAS_LISTAS PRIMARY KEY CLUSTERED (IdLista ASC)
        );
    END;

    IF OBJECT_ID(N'dbo.ALFACORE_TAREAS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_TAREAS
        (
            IdTarea bigint IDENTITY(1,1) NOT NULL,
            IdLista int NOT NULL,
            Titulo nvarchar(200) NOT NULL,
            Descripcion nvarchar(max) NULL,
            FechaVencimiento date NULL,
            UsuarioAsignado nvarchar(50) NOT NULL,
            Estado nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_Estado DEFAULT (N'PENDIENTE'),
            UsuarioAlta nvarchar(50) NULL,
            UsuarioCreador nvarchar(50) NULL,
            FechaHoraAlta datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_Alta DEFAULT (GETDATE()),
            FechaHoraModificacion datetime NULL,
            FechaHoraCompletada datetime NULL,
            Activa bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_Activa DEFAULT (1),
            CONSTRAINT PK_ALFACORE_TAREAS PRIMARY KEY CLUSTERED (IdTarea ASC),
            CONSTRAINT CK_ALFACORE_TAREAS_Estado CHECK (Estado IN (N'PENDIENTE', N'EN_CURSO', N'COMPLETADA'))
        );
    END;

    IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
        (
            IdNota bigint IDENTITY(1,1) NOT NULL,
            Texto nvarchar(500) NOT NULL,
            Fecha date NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_NOTAS_Fecha DEFAULT (CONVERT(date, GETDATE())),
            Usuario nvarchar(50) NOT NULL,
            Completada bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_NOTAS_Completada DEFAULT (0),
            FechaHoraAlta datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_NOTAS_Alta DEFAULT (GETDATE()),
            FechaHoraCompletada datetime NULL,
            Activa bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_NOTAS_Activa DEFAULT (1),
            CONSTRAINT PK_ALFACORE_TAREAS_NOTAS_RAPIDAS PRIMARY KEY CLUSTERED (IdNota ASC)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_ALFACORE_TAREAS_LISTAS'
          AND parent_object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS')
    )
    BEGIN
        ALTER TABLE dbo.ALFACORE_TAREAS WITH CHECK
        ADD CONSTRAINT FK_ALFACORE_TAREAS_LISTAS
            FOREIGN KEY (IdLista) REFERENCES dbo.ALFACORE_TAREAS_LISTAS (IdLista);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_TAREAS_ESTADO' AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS'))
        CREATE INDEX IX_ALFACORE_TAREAS_ESTADO ON dbo.ALFACORE_TAREAS (Estado, Activa, FechaVencimiento);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_TAREAS_ASIGNADO' AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS'))
        CREATE INDEX IX_ALFACORE_TAREAS_ASIGNADO ON dbo.ALFACORE_TAREAS (UsuarioAsignado, Estado, Activa);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_TAREAS_NOTAS_USUARIO' AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS'))
        CREATE INDEX IX_ALFACORE_TAREAS_NOTAS_USUARIO ON dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS (Usuario, Activa, Completada, Fecha);

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.ALFACORE_TAREAS_LISTAS
        WHERE EsDefault = 1
          AND ISNULL(Activa, 1) = 1
    )
    BEGIN
        INSERT INTO dbo.ALFACORE_TAREAS_LISTAS (Nombre, EsDefault, UsuarioAlta, FechaHora_Alta, Activa)
        VALUES (N'Mis tareas', 1, SYSTEM_USER, GETDATE(), 1);
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
