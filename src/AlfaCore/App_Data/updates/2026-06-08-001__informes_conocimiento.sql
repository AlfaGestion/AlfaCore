SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ALFACORE_INFORMES_ARTICULOS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_INFORMES_ARTICULOS
        (
            IdArticulo bigint IDENTITY(1,1) NOT NULL,
            IdPadre bigint NULL,
            Categoria nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_INF_ART_Categoria DEFAULT (N'WORKSPACE'),
            Titulo nvarchar(180) NOT NULL,
            Emoji nvarchar(60) NULL,
            Contenido nvarchar(max) NULL,
            Resumen nvarchar(300) NULL,
            PortadaTipo nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_INF_ART_PortadaTipo DEFAULT (N'NONE'),
            PortadaValor nvarchar(500) NULL,
            EsCompartido bit NOT NULL CONSTRAINT DF_ALFACORE_INF_ART_Compartido DEFAULT (0),
            TokenPublico uniqueidentifier NULL,
            Bloqueado bit NOT NULL CONSTRAINT DF_ALFACORE_INF_ART_Bloqueado DEFAULT (0),
            Orden int NOT NULL CONSTRAINT DF_ALFACORE_INF_ART_Orden DEFAULT (10),
            UsuarioPropietario nvarchar(50) NULL,
            UsuarioAlta nvarchar(50) NULL,
            FechaHora_Alta datetime NOT NULL CONSTRAINT DF_ALFACORE_INF_ART_Alta DEFAULT (GETDATE()),
            UsuarioModificacion nvarchar(50) NULL,
            FechaHora_Modificacion datetime NULL,
            Archivado bit NOT NULL CONSTRAINT DF_ALFACORE_INF_ART_Archivado DEFAULT (0),
            FechaHora_Archivado datetime NULL,
            CONSTRAINT PK_ALFACORE_INFORMES_ARTICULOS PRIMARY KEY CLUSTERED (IdArticulo),
            CONSTRAINT CK_ALFACORE_INF_ART_Categoria CHECK (Categoria IN (N'WORKSPACE', N'PRIVATE')),
            CONSTRAINT CK_ALFACORE_INF_ART_PortadaTipo CHECK (PortadaTipo IN (N'NONE', N'GRADIENT', N'URL')),
            CONSTRAINT FK_ALFACORE_INF_ART_PADRE FOREIGN KEY (IdPadre)
                REFERENCES dbo.ALFACORE_INFORMES_ARTICULOS (IdArticulo)
        );
    END;

    IF OBJECT_ID(N'dbo.ALFACORE_INFORMES_FAVORITOS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_INFORMES_FAVORITOS
        (
            IdArticulo bigint NOT NULL,
            Usuario nvarchar(50) NOT NULL,
            FechaHora_Alta datetime NOT NULL CONSTRAINT DF_ALFACORE_INF_FAV_Alta DEFAULT (GETDATE()),
            CONSTRAINT PK_ALFACORE_INFORMES_FAVORITOS PRIMARY KEY CLUSTERED (IdArticulo, Usuario),
            CONSTRAINT FK_ALFACORE_INF_FAV_ART FOREIGN KEY (IdArticulo)
                REFERENCES dbo.ALFACORE_INFORMES_ARTICULOS (IdArticulo)
        );
    END;

    IF OBJECT_ID(N'dbo.ALFACORE_INFORMES_COMENTARIOS', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_INFORMES_COMENTARIOS
        (
            IdComentario bigint IDENTITY(1,1) NOT NULL,
            IdArticulo bigint NOT NULL,
            Texto nvarchar(1000) NOT NULL,
            UsuarioAlta nvarchar(50) NULL,
            FechaHora_Alta datetime NOT NULL CONSTRAINT DF_ALFACORE_INF_COM_Alta DEFAULT (GETDATE()),
            Resuelto bit NOT NULL CONSTRAINT DF_ALFACORE_INF_COM_Resuelto DEFAULT (0),
            UsuarioModificacion nvarchar(50) NULL,
            FechaHora_Modificacion datetime NULL,
            CONSTRAINT PK_ALFACORE_INFORMES_COMENTARIOS PRIMARY KEY CLUSTERED (IdComentario),
            CONSTRAINT FK_ALFACORE_INF_COM_ART FOREIGN KEY (IdArticulo)
                REFERENCES dbo.ALFACORE_INFORMES_ARTICULOS (IdArticulo)
        );
    END;

    IF OBJECT_ID(N'dbo.ALFACORE_INFORMES_VERSIONES', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_INFORMES_VERSIONES
        (
            IdVersion bigint IDENTITY(1,1) NOT NULL,
            IdArticulo bigint NOT NULL,
            Titulo nvarchar(180) NOT NULL,
            Emoji nvarchar(60) NULL,
            Contenido nvarchar(max) NULL,
            PortadaTipo nvarchar(20) NULL,
            PortadaValor nvarchar(500) NULL,
            Usuario nvarchar(50) NULL,
            FechaHora datetime NOT NULL CONSTRAINT DF_ALFACORE_INF_VER_Fecha DEFAULT (GETDATE()),
            CONSTRAINT PK_ALFACORE_INFORMES_VERSIONES PRIMARY KEY CLUSTERED (IdVersion),
            CONSTRAINT FK_ALFACORE_INF_VER_ART FOREIGN KEY (IdArticulo)
                REFERENCES dbo.ALFACORE_INFORMES_ARTICULOS (IdArticulo)
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_INF_ART_ARBOL' AND object_id = OBJECT_ID(N'dbo.ALFACORE_INFORMES_ARTICULOS'))
        CREATE INDEX IX_ALFACORE_INF_ART_ARBOL ON dbo.ALFACORE_INFORMES_ARTICULOS (Categoria, Archivado, IdPadre, Orden) INCLUDE (Titulo, Emoji, FechaHora_Modificacion);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_INF_ART_BUSQUEDA' AND object_id = OBJECT_ID(N'dbo.ALFACORE_INFORMES_ARTICULOS'))
        CREATE INDEX IX_ALFACORE_INF_ART_BUSQUEDA ON dbo.ALFACORE_INFORMES_ARTICULOS (Archivado, Categoria, Titulo) INCLUDE (Resumen);

    IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_INFORMES_ARTICULOS)
    BEGIN
        INSERT INTO dbo.ALFACORE_INFORMES_ARTICULOS
            (Categoria, Titulo, Emoji, Contenido, Resumen, PortadaTipo, PortadaValor, UsuarioAlta, FechaHora_Alta, FechaHora_Modificacion, Orden)
        VALUES
            (N'WORKSPACE', N'ALFA NET', N'bi-heart-fill',
             N'<h2>Base de conocimiento interna</h2><p>Use este espacio para documentar procedimientos, implementaciones, preguntas frecuentes y decisiones operativas.</p><ul class="ticket-editor-todo"><li><input type="checkbox"> Crear guias por modulo</li><li><input type="checkbox"> Marcar articulos importantes como favoritos</li><li><input type="checkbox"> Compartir documentos de trabajo cuando corresponda</li></ul>',
             N'Base de conocimiento interna para procedimientos, implementaciones y documentacion operativa.',
             N'GRADIENT', N'alfa', SYSTEM_USER, GETDATE(), GETDATE(), 10);
    END;

    UPDATE dbo.ALFACORE_INFORMES_ARTICULOS
       SET Contenido = N'<h2>Base de conocimiento interna</h2><p>Use este espacio para documentar procedimientos, implementaciones, preguntas frecuentes y decisiones operativas.</p><ul class="ticket-editor-todo"><li><input type="checkbox"> Crear guias por modulo</li><li><input type="checkbox"> Marcar articulos importantes como favoritos</li><li><input type="checkbox"> Compartir documentos de trabajo cuando corresponda</li></ul>',
           Resumen = N'Base de conocimiento interna para procedimientos, implementaciones y documentacion operativa.',
           FechaHora_Modificacion = GETDATE()
     WHERE Titulo = N'ALFA NET'
       AND ISNULL(Contenido, N'') LIKE N'## Base de conocimiento interna%';

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    BEGIN
        ALTER TABLE dbo.ALFACORE_MENU_WEB
            ADD NombreWeb nvarchar(150) NULL;
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = N'ALFA' AND Clave = N'D010190')
    BEGIN
        INSERT INTO dbo.TA_MENU
            (Menu, Titulo, Clave, Nombre, Imagen, Proceso, Habilitado, ORDEN, DESCRIPCION)
        VALUES
            (N'ALFA', N'D', N'D010190', N'Informes', NULL, N'Informes', 1, N'190',
             N'Base de conocimiento, documentacion interna y articulos compartidos de AlfaCore.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D010190')
    BEGIN
        INSERT INTO dbo.ALFACORE_MENU_WEB
            (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion, NombreWeb)
        VALUES
            (N'ALFA', N'D010190', N'/informes', N'Informes', N'bi-journal-richtext', 1, 190, 1,
             N'Base de conocimiento estilo Knowledge para documentacion interna, favoritos, plantillas y articulos compartidos.',
             N'Informes');
    END;

    UPDATE dbo.ALFACORE_MENU_WEB
       SET RutaWeb = N'/informes',
           Componente = N'Informes',
           Icono = N'bi-journal-richtext',
           HabilitadoWeb = 1,
           OrdenWeb = 190,
           EsFavoritoDefault = 1,
           Observacion = N'Base de conocimiento estilo Knowledge para documentacion interna, favoritos, plantillas y articulos compartidos.',
           NombreWeb = N'Informes'
     WHERE Menu = N'ALFA'
       AND Clave = N'D010190';

    IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
    BEGIN
        INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
        SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D010190'
        FROM dbo.TA_TAREAS t
        WHERE ISNULL(t.TAREA, N'') <> N''
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.TA_TAREAS x
              WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
                AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
                AND UPPER(LTRIM(RTRIM(x.TAREA))) = N'D010190'
          );
    END;
END;
GO
