IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_MENU_WEB
    (
        Menu nvarchar(50) NOT NULL,
        Clave nvarchar(50) NOT NULL,
        RutaWeb nvarchar(250) NULL,
        Componente nvarchar(150) NULL,
        Icono nvarchar(50) NULL,
        HabilitadoWeb bit NOT NULL CONSTRAINT DF_ALFACORE_MENU_WEB_HabilitadoWeb_WebOnly DEFAULT (1),
        OrdenWeb int NULL,
        EsFavoritoDefault bit NOT NULL CONSTRAINT DF_ALFACORE_MENU_WEB_Favorito_WebOnly DEFAULT (0),
        Observacion nvarchar(250) NULL,
        NombreWeb nvarchar(150) NULL,
        CONSTRAINT PK_ALFACORE_MENU_WEB_WebOnly PRIMARY KEY (Menu, Clave)
    );
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD PadreClave nvarchar(50) NULL;
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'DescripcionWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD DescripcionWeb nvarchar(500) NULL;
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_MENU_WEB')
      AND name = N'RutaWeb'
      AND is_nullable = 0
)
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ALTER COLUMN RutaWeb nvarchar(250) NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_MENU_WEB')
      AND name = N'IX_ALFACORE_MENU_WEB_PadreClave'
)
BEGIN
    CREATE INDEX IX_ALFACORE_MENU_WEB_PadreClave
        ON dbo.ALFACORE_MENU_WEB (Menu, PadreClave, OrdenWeb, Clave);
END;
GO

IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
BEGIN
    UPDATE w
       SET w.NombreWeb = COALESCE(NULLIF(LTRIM(RTRIM(w.NombreWeb)), N''), NULLIF(LTRIM(RTRIM(m.Nombre)), N''), w.Clave),
           w.DescripcionWeb = COALESCE(NULLIF(LTRIM(RTRIM(w.DescripcionWeb)), N''), NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(500), m.Descripcion))), N''), NULLIF(LTRIM(RTRIM(w.Observacion)), N'')),
           w.PadreClave = COALESCE(NULLIF(LTRIM(RTRIM(w.PadreClave)), N''), NULLIF(LTRIM(RTRIM(m.Titulo)), N''))
    FROM dbo.ALFACORE_MENU_WEB w
    INNER JOIN dbo.TA_MENU m
        ON m.Menu = w.Menu
       AND m.Clave = w.Clave;

    ;WITH Ancestros AS
    (
        SELECT
            m.Menu,
            m.Clave,
            NULLIF(LTRIM(RTRIM(m.Titulo)), N'') AS PadreClave,
            NULLIF(LTRIM(RTRIM(m.Nombre)), N'') AS NombreWeb,
            NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(500), m.Descripcion))), N'') AS DescripcionWeb
        FROM dbo.TA_MENU m
        INNER JOIN dbo.ALFACORE_MENU_WEB w
            ON w.Menu = m.Menu
           AND w.Clave = m.Clave

        UNION ALL

        SELECT
            p.Menu,
            p.Clave,
            NULLIF(LTRIM(RTRIM(p.Titulo)), N'') AS PadreClave,
            NULLIF(LTRIM(RTRIM(p.Nombre)), N'') AS NombreWeb,
            NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(500), p.Descripcion))), N'') AS DescripcionWeb
        FROM dbo.TA_MENU p
        INNER JOIN Ancestros a
            ON a.Menu = p.Menu
           AND a.PadreClave = p.Clave
    )
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
        NombreWeb,
        DescripcionWeb,
        PadreClave
    )
    SELECT DISTINCT
        a.Menu,
        a.Clave,
        NULL,
        NULL,
        NULL,
        1,
        NULL,
        0,
        N'Nodo agrupador migrado desde TA_MENU para el shell web.',
        COALESCE(a.NombreWeb, a.Clave),
        a.DescripcionWeb,
        a.PadreClave
    FROM Ancestros a
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.ALFACORE_MENU_WEB x
        WHERE x.Menu = a.Menu
          AND x.Clave = a.Clave
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB
    WHERE Menu = N'ALFA'
      AND Clave = N'D010185WEB'
)
BEGIN
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
        NombreWeb,
        DescripcionWeb,
        PadreClave
    )
    VALUES
    (
        N'ALFA',
        N'D010185WEB',
        NULL,
        NULL,
        N'bi-collection',
        1,
        18500,
        0,
        N'Sección web del módulo CRM.',
        N'CRM',
        N'Sección web del módulo CRM.',
        N'D010185'
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB
    WHERE Menu = N'ALFA'
      AND Clave = N'D010185-WEB-CONVERSACIONES'
)
BEGIN
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
        NombreWeb,
        DescripcionWeb,
        PadreClave
    )
    VALUES
    (
        N'ALFA',
        N'D010185-WEB-CONVERSACIONES',
        N'/conversaciones',
        N'Conversaciones',
        N'bi-chat-left-text-fill',
        1,
        18501,
        0,
        N'Inbox operativo de conversaciones.',
        N'Conversaciones',
        N'Inbox operativo de conversaciones, seguimiento y atención comercial.',
        N'D010185WEB'
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB
    WHERE Menu = N'ALFA'
      AND Clave = N'D010185-WEB-TICKETS'
)
BEGIN
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
        NombreWeb,
        DescripcionWeb,
        PadreClave
    )
    VALUES
    (
        N'ALFA',
        N'D010185-WEB-TICKETS',
        N'/tickets',
        N'Tickets',
        N'bi-life-preserver',
        1,
        18502,
        0,
        N'Mesa de ayuda y seguimiento de incidencias.',
        N'Tickets',
        N'Mesa de ayuda, soporte y seguimiento de incidencias.',
        N'D010185WEB'
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_TAREAS_WEB
    (
        Usuario nvarchar(50) NOT NULL,
        Sistema nvarchar(50) NOT NULL,
        Clave nvarchar(50) NOT NULL,
        FechaHoraGrabacion datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_WEB_Fecha DEFAULT (GETDATE()),
        UsuarioGrabacion nvarchar(50) NULL,
        CONSTRAINT PK_ALFACORE_TAREAS_WEB PRIMARY KEY (Usuario, Sistema, Clave)
    );
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_TAREAS_WEB', N'FechaHoraGrabacion') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_TAREAS_WEB
        ADD FechaHoraGrabacion datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_WEB_Fecha_Alter DEFAULT (GETDATE());
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_TAREAS_WEB', N'UsuarioGrabacion') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_TAREAS_WEB
        ADD UsuarioGrabacion nvarchar(50) NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB')
      AND name = N'IX_ALFACORE_TAREAS_WEB_SISTEMA_USUARIO'
)
BEGIN
    CREATE INDEX IX_ALFACORE_TAREAS_WEB_SISTEMA_USUARIO
        ON dbo.ALFACORE_TAREAS_WEB (Sistema, Usuario, Clave);
END;
GO

IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB
    (
        Usuario,
        Sistema,
        Clave,
        FechaHoraGrabacion,
        UsuarioGrabacion
    )
    SELECT DISTINCT
        t.USUARIO,
        t.SISTEMA,
        t.TAREA,
        GETDATE(),
        N'MIGRACION-TA_TAREAS'
    FROM dbo.TA_TAREAS t
    INNER JOIN dbo.ALFACORE_MENU_WEB w
        ON UPPER(LTRIM(RTRIM(w.Clave))) = UPPER(LTRIM(RTRIM(t.TAREA)))
    WHERE ISNULL(LTRIM(RTRIM(t.TAREA)), N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))   = UPPER(LTRIM(RTRIM(t.TAREA)))
      );

    INSERT INTO dbo.ALFACORE_TAREAS_WEB
    (
        Usuario,
        Sistema,
        Clave,
        FechaHoraGrabacion,
        UsuarioGrabacion
    )
    SELECT DISTINCT
        t.USUARIO,
        t.SISTEMA,
        N'D010185WEB',
        GETDATE(),
        N'MIGRACION-CRM'
    FROM dbo.TA_TAREAS t
    WHERE UPPER(LTRIM(RTRIM(t.TAREA))) = N'D010185'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'D010185WEB'
      );

    INSERT INTO dbo.ALFACORE_TAREAS_WEB
    (
        Usuario,
        Sistema,
        Clave,
        FechaHoraGrabacion,
        UsuarioGrabacion
    )
    SELECT DISTINCT
        t.USUARIO,
        t.SISTEMA,
        v.Clave,
        GETDATE(),
        N'MIGRACION-CRM'
    FROM dbo.TA_TAREAS t
    CROSS JOIN
    (
        SELECT N'D010185-WEB-CONVERSACIONES' AS Clave
        UNION ALL
        SELECT N'D010185-WEB-TICKETS'
    ) v
    WHERE UPPER(LTRIM(RTRIM(t.TAREA))) = N'D010185'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))   = UPPER(LTRIM(RTRIM(v.Clave)))
      );
END;
GO
