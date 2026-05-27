SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_TAREAS_COMPARTIDOS
        (
            IdCompartido bigint IDENTITY(1,1) NOT NULL,
            TipoObjeto nvarchar(20) NOT NULL,
            IdObjeto bigint NOT NULL,
            UsuarioDestino nvarchar(50) NULL,
            EsPublico bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_COMPARTIDOS_EsPublico DEFAULT (0),
            UsuarioAlta nvarchar(50) NULL,
            FechaHoraAlta datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_COMPARTIDOS_Alta DEFAULT (GETDATE()),
            Activa bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_COMPARTIDOS_Activa DEFAULT (1),
            CONSTRAINT PK_ALFACORE_TAREAS_COMPARTIDOS PRIMARY KEY CLUSTERED (IdCompartido ASC),
            CONSTRAINT CK_ALFACORE_TAREAS_COMPARTIDOS_Tipo CHECK (TipoObjeto IN (N'LISTA', N'NOTA', N'TAREA')),
            CONSTRAINT CK_ALFACORE_TAREAS_COMPARTIDOS_Destino CHECK
            (
                (EsPublico = 1 AND UsuarioDestino IS NULL)
                OR
                (EsPublico = 0 AND UsuarioDestino IS NOT NULL)
            )
        );
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = N'CK_ALFACORE_TAREAS_COMPARTIDOS_Tipo'
          AND parent_object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS')
    )
    BEGIN
        ALTER TABLE dbo.ALFACORE_TAREAS_COMPARTIDOS
            DROP CONSTRAINT CK_ALFACORE_TAREAS_COMPARTIDOS_Tipo;
    END;

    ALTER TABLE dbo.ALFACORE_TAREAS_COMPARTIDOS WITH CHECK
        ADD CONSTRAINT CK_ALFACORE_TAREAS_COMPARTIDOS_Tipo
            CHECK (TipoObjeto IN (N'LISTA', N'NOTA', N'TAREA'));

    UPDATE l
    SET UsuarioAlta = owner.UsuarioAlta,
        FechaHora_Modificacion = GETDATE()
    FROM dbo.ALFACORE_TAREAS_LISTAS l
    CROSS APPLY
    (
        SELECT TOP (1)
            NULLIF(LTRIM(RTRIM(ISNULL(t.UsuarioAlta, N''))), N'') AS UsuarioAlta
        FROM dbo.ALFACORE_TAREAS t
        WHERE t.IdLista = l.IdLista
          AND ISNULL(t.Activa, 1) = 1
          AND NULLIF(LTRIM(RTRIM(ISNULL(t.UsuarioAlta, N''))), N'') IS NOT NULL
        ORDER BY t.FechaHoraAlta
    ) owner
    WHERE NULLIF(LTRIM(RTRIM(ISNULL(l.UsuarioAlta, N''))), N'') IS NULL
       OR UPPER(LTRIM(RTRIM(ISNULL(l.UsuarioAlta, N'')))) IN (N'SYSTEM_USER', N'DBO');

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'UX_ALFACORE_TAREAS_COMPARTIDOS_PUBLICO'
          AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS')
    )
    BEGIN
        CREATE UNIQUE INDEX UX_ALFACORE_TAREAS_COMPARTIDOS_PUBLICO
            ON dbo.ALFACORE_TAREAS_COMPARTIDOS (TipoObjeto, IdObjeto, EsPublico)
            WHERE EsPublico = 1 AND Activa = 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'UX_ALFACORE_TAREAS_COMPARTIDOS_USUARIO'
          AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS')
    )
    BEGIN
        CREATE UNIQUE INDEX UX_ALFACORE_TAREAS_COMPARTIDOS_USUARIO
            ON dbo.ALFACORE_TAREAS_COMPARTIDOS (TipoObjeto, IdObjeto, UsuarioDestino)
            WHERE EsPublico = 0 AND Activa = 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_ALFACORE_TAREAS_COMPARTIDOS_VISIBILIDAD'
          AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS')
    )
    BEGIN
        CREATE INDEX IX_ALFACORE_TAREAS_COMPARTIDOS_VISIBILIDAD
            ON dbo.ALFACORE_TAREAS_COMPARTIDOS (TipoObjeto, Activa, EsPublico, UsuarioDestino, IdObjeto);
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
