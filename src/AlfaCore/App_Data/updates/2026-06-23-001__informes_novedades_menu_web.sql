-- ============================================================
-- Informes / Novedades internas - menu web, permisos y tablas
-- Clave nueva TA_MENU / ALFACORE_MENU_WEB: D010191
-- ============================================================

SET NOCOUNT ON;

-- 1. Guardia: si no existe ALFACORE_MENU_WEB, salir sin tocar nada.
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- 2. Columna NombreWeb para bases antiguas.
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

-- 3. INSERT en ALFACORE_MENU_WEB. Si la clave legacy nueva no existe, se crea primero con guardia.
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Clave = N'D010191')
    BEGIN
        INSERT INTO dbo.TA_MENU
            (Menu, Titulo, Clave, Nombre, Imagen, Proceso, Habilitado, ORDEN, DESCRIPCION)
        SELECT TOP (1)
            Menu,
            N'D010190',
            N'D010191',
            N'Novedades',
            N'bi-megaphone',
            N'Novedades',
            1,
            N'191',
            N'Novedades internas, boletines de patch y anuncios programados de AlfaCore.'
        FROM dbo.TA_MENU
        WHERE Clave = N'D010190';
    END;

    INSERT INTO dbo.ALFACORE_MENU_WEB
    (
        Menu,
        Clave,
        RutaWeb,
        Componente,
        Icono,
        HabilitadoWeb,
        OrdenWeb,
        EsFavoritoDefault,
        Observacion,
        NombreWeb
    )
    SELECT
        m.Menu,
        N'D010191',
        N'/informes/novedades',
        N'Novedades',
        N'bi-megaphone',
        1,
        191,
        0,
        N'Novedades internas, boletines de patch, anuncios programados y seguimiento de lectura por usuario.',
        N'Novedades'
    FROM dbo.TA_MENU m
    WHERE m.Clave = N'D010191'
      AND ISNULL(m.Habilitado, 1) = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_MENU_WEB w
          WHERE w.Menu = m.Menu
            AND w.Clave = N'D010191'
      );
END;
GO

-- 4. UPDATE en ALFACORE_MENU_WEB.
UPDATE dbo.ALFACORE_MENU_WEB
   SET RutaWeb = N'/informes/novedades',
       Componente = N'Novedades',
       Icono = N'bi-megaphone',
       HabilitadoWeb = 1,
       OrdenWeb = COALESCE(NULLIF(OrdenWeb, 0), 191),
       EsFavoritoDefault = COALESCE(EsFavoritoDefault, 0),
       Observacion = COALESCE(NULLIF(Observacion, N''), N'Novedades internas, boletines de patch, anuncios programados y seguimiento de lectura por usuario.'),
       NombreWeb = COALESCE(NULLIF(NombreWeb, N''), N'Novedades')
 WHERE Clave = N'D010191';
GO

-- 5. Descripcion en TA_MENU: solo si la columna existe y el campo esta vacio.
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
       SET Descripcion = N'Novedades internas, boletines de patch y anuncios programados de AlfaCore.'
     WHERE Clave = N'D010191'
       AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), N'') = N'';
END;
GO

-- 6. Permisos en TA_TAREAS.
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D010191'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D010191'
      );
END;
GO

-- Tablas propias del modulo Novedades.
BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ALFACORE_NOVEDADES', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_NOVEDADES
        (
            IdNovedad bigint IDENTITY(1,1) NOT NULL,
            Tipo nvarchar(30) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Tipo DEFAULT (N'MANUAL'),
            Estado nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Estado DEFAULT (N'BORRADOR'),
            Prioridad nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Prioridad DEFAULT (N'INFO'),
            Version nvarchar(80) NULL,
            Titulo nvarchar(180) NOT NULL,
            Bajada nvarchar(300) NULL,
            Resumen nvarchar(500) NULL,
            Contenido nvarchar(max) NULL,
            Icono nvarchar(60) NULL,
            Acento nvarchar(20) NULL,
            PortadaTipo nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_PortadaTipo DEFAULT (N'NONE'),
            PortadaValor nvarchar(500) NULL,
            Footer nvarchar(300) NULL,
            FechaProgramada datetime NULL,
            FechaPublicacion datetime NULL,
            VigenteHasta datetime NULL,
            MostrarPopup bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_MostrarPopup DEFAULT (1),
            LecturaObligatoria bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_LecturaObligatoria DEFAULT (0),
            RepetirHastaLeer bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_Repetir DEFAULT (1),
            Segmento nvarchar(30) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Segmento DEFAULT (N'TODOS'),
            UsuarioAlta nvarchar(50) NULL,
            FechaHora_Alta datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_Alta DEFAULT (GETDATE()),
            UsuarioModificacion nvarchar(50) NULL,
            FechaHora_Modificacion datetime NULL,
            Archivado bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_Archivado DEFAULT (0),
            CONSTRAINT PK_ALFACORE_NOVEDADES PRIMARY KEY CLUSTERED (IdNovedad),
            CONSTRAINT CK_ALFACORE_NOV_Tipo CHECK (Tipo IN (N'MANUAL', N'BOLETIN_PATCH')),
            CONSTRAINT CK_ALFACORE_NOV_Estado CHECK (Estado IN (N'BORRADOR', N'PROGRAMADO', N'PUBLICADO', N'ARCHIVADO')),
            CONSTRAINT CK_ALFACORE_NOV_Prioridad CHECK (Prioridad IN (N'INFO', N'IMPORTANTE', N'CRITICO')),
            CONSTRAINT CK_ALFACORE_NOV_PortadaTipo CHECK (PortadaTipo IN (N'NONE', N'GRADIENT', N'URL'))
        );
    END;

    IF OBJECT_ID(N'dbo.ALFACORE_NOVEDADES_LECTURAS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_NOVEDADES_LECTURAS
        (
            IdNovedad bigint NOT NULL,
            Usuario nvarchar(50) NOT NULL,
            FechaHoraPrimeraVista datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Primera DEFAULT (GETDATE()),
            FechaHoraUltimaVista datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Ultima DEFAULT (GETDATE()),
            FechaHoraLeido datetime NULL,
            VecesVisto int NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Veces DEFAULT (0),
            CONSTRAINT PK_ALFACORE_NOVEDADES_LECTURAS PRIMARY KEY CLUSTERED (IdNovedad, Usuario),
            CONSTRAINT FK_ALFACORE_NOV_LEC_NOVEDAD FOREIGN KEY (IdNovedad)
                REFERENCES dbo.ALFACORE_NOVEDADES (IdNovedad)
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_NOV_ESTADO_FECHA' AND object_id = OBJECT_ID(N'dbo.ALFACORE_NOVEDADES'))
        CREATE INDEX IX_ALFACORE_NOV_ESTADO_FECHA ON dbo.ALFACORE_NOVEDADES (Estado, Archivado, MostrarPopup, FechaProgramada, VigenteHasta) INCLUDE (Prioridad, Titulo, Version);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_NOV_LECT_USUARIO' AND object_id = OBJECT_ID(N'dbo.ALFACORE_NOVEDADES_LECTURAS'))
        CREATE INDEX IX_ALFACORE_NOV_LECT_USUARIO ON dbo.ALFACORE_NOVEDADES_LECTURAS (Usuario, FechaHoraLeido) INCLUDE (IdNovedad, VecesVisto);

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
GO
