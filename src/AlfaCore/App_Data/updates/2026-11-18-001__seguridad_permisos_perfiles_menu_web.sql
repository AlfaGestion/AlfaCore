-- Permisos web más claros (Seguridad > Autorización de tareas y Seguridad > Perfiles).
--  * ALFACORE_PERMISOS_WEB_EXPLICITOS: permisos sensibles que NUNCA se dan por "sin permisos cargados = ve todo"
--    y que tampoco cuentan como restricción del menú (por eso van en una tabla aparte y no en ALFACORE_TAREAS_WEB).
--    Primero: "Configurar Conversaciones" (D010185-WEB-CONV-CONFIG).
--  * Perfiles: ALFACORE_PERFILES_WEB (+ _CLAVES, + _USUARIOS). Un usuario tiene a lo sumo un perfil; sus permisos
--    se copian a las tablas de permisos marcados con IdPerfil, así todo lo que ya lee esas tablas sigue igual.
--  * Conversaciones deja de ser un único permiso: Plantillas, Configurar y Ver todos los números.
--    "Ver todos los números" sigue guardándose en CONV_ADMINISTRADORES (la usa la bandeja).
--  * Menú/permisos web propios (ALFACORE_MENU_WEB / ALFACORE_TAREAS_WEB); no escribe TA_MENU ni TA_TAREAS.
-- Idempotente. Compatible con compatibility level 80/100 (sin THROW, OPENJSON ni STRING_SPLIT).
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL OR OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.ALFACORE_PERFILES_WEB', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_PERFILES_WEB
    (
        IdPerfil               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ALFACORE_PERFILES_WEB PRIMARY KEY,
        Sistema                nvarchar(50)  NOT NULL,
        Nombre                 nvarchar(80)  NOT NULL,
        Descripcion            nvarchar(300) NULL,
        FechaHoraGrabacion     datetime      NOT NULL CONSTRAINT DF_ALFACORE_PERFILES_WEB_Fecha DEFAULT (GETDATE()),
        UsuarioGrabacion       nvarchar(50)  NULL,
        FechaHoraModificacion  datetime      NULL,
        CONSTRAINT UQ_ALFACORE_PERFILES_WEB_Nombre UNIQUE (Sistema, Nombre)
    );
END
GO

IF OBJECT_ID(N'dbo.ALFACORE_PERFILES_WEB_CLAVES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_PERFILES_WEB_CLAVES
    (
        IdPerfil  int          NOT NULL,
        Clave     nvarchar(50) NOT NULL,
        CONSTRAINT PK_ALFACORE_PERFILES_WEB_CLAVES PRIMARY KEY (IdPerfil, Clave),
        CONSTRAINT FK_ALFACORE_PERFILES_WEB_CLAVES_PERFIL FOREIGN KEY (IdPerfil) REFERENCES dbo.ALFACORE_PERFILES_WEB (IdPerfil)
    );
END
GO

IF OBJECT_ID(N'dbo.ALFACORE_PERFILES_WEB_USUARIOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_PERFILES_WEB_USUARIOS
    (
        Usuario             nvarchar(50) NOT NULL,
        Sistema             nvarchar(50) NOT NULL,
        IdPerfil            int          NOT NULL,
        FechaHoraGrabacion  datetime     NOT NULL CONSTRAINT DF_ALFACORE_PERFILES_WEB_USUARIOS_Fecha DEFAULT (GETDATE()),
        UsuarioGrabacion    nvarchar(50) NULL,
        CONSTRAINT PK_ALFACORE_PERFILES_WEB_USUARIOS PRIMARY KEY (Usuario, Sistema),
        CONSTRAINT FK_ALFACORE_PERFILES_WEB_USUARIOS_PERFIL FOREIGN KEY (IdPerfil) REFERENCES dbo.ALFACORE_PERFILES_WEB (IdPerfil)
    );
END
GO

IF OBJECT_ID(N'dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS
    (
        Usuario             nvarchar(50) NOT NULL,
        Sistema             nvarchar(50) NOT NULL,
        Clave               nvarchar(50) NOT NULL,
        IdPerfil            int          NULL,   -- NULL = dado al usuario; con valor = viene de ese perfil
        FechaHoraGrabacion  datetime     NOT NULL CONSTRAINT DF_ALFACORE_PERMISOS_WEB_EXPLICITOS_Fecha DEFAULT (GETDATE()),
        UsuarioGrabacion    nvarchar(50) NULL,
        CONSTRAINT PK_ALFACORE_PERMISOS_WEB_EXPLICITOS PRIMARY KEY (Usuario, Sistema, Clave)
    );
END
GO

-- Permisos que vienen de un perfil: IdPerfil con valor (NULL = permiso propio del usuario).
IF COL_LENGTH(N'dbo.ALFACORE_TAREAS_WEB', N'IdPerfil') IS NULL
    ALTER TABLE dbo.ALFACORE_TAREAS_WEB ADD IdPerfil int NULL;
GO
IF OBJECT_ID(N'dbo.CONV_ADMINISTRADORES', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.CONV_ADMINISTRADORES', N'IdPerfil') IS NULL
    ALTER TABLE dbo.CONV_ADMINISTRADORES ADD IdPerfil int NULL;
GO

-- Menú web: Seguridad > Perfiles y las tres partes de Conversaciones (sin ruta: son permisos, no entradas del menú).
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'DescripcionWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD DescripcionWeb nvarchar(300) NULL;
GO
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

EXEC sys.sp_executesql N'
DECLARE @m TABLE (Clave nvarchar(50), Padre nvarchar(50), Nombre nvarchar(150), Ruta nvarchar(200), Componente nvarchar(100), Icono nvarchar(60), Orden int, Descripcion nvarchar(300));
INSERT INTO @m VALUES
 (N''D015-WEB-PERFILES'', N''D0150'', N''Perfiles de permisos'', N''/seguridad/perfiles'', N''PerfilesWeb'', N''bi-people-fill'', 15004,
  N''Grupos de permisos (por ejemplo Vendedor o Administración) que se asignan a los usuarios.''),
 (N''D010185-WEB-CONV-PLANTILLAS'', N''D010185-WEB-CONVERSACIONES'', N''Plantillas de WhatsApp'', NULL, NULL, N''bi-file-earmark-text'', 1,
  N''Crear, editar y mandar a aprobar plantillas de WhatsApp.''),
 (N''D010185-WEB-CONV-CONFIG'', N''D010185-WEB-CONVERSACIONES'', N''Configurar Conversaciones'', NULL, NULL, N''bi-gear'', 2,
  N''Canales, números, asistente IA y reglas. Se da solo si se tilda (o si el usuario es administrador del sistema).''),
 (N''D010185-WEB-CONV-TODOS'', N''D010185-WEB-CONVERSACIONES'', N''Ver todos los números'', NULL, NULL, N''bi-eye'', 3,
  N''Ver y responder las conversaciones de todos los números de WhatsApp. Se da solo si se tilda.'');

INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, DescripcionWeb)
SELECT N''ALFA'', m.Clave, m.Padre, m.Nombre, m.Ruta, m.Componente, m.Icono, 1, m.Orden, 0, m.Descripcion
FROM @m m
WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu = N''ALFA'' AND w.Clave = m.Clave);

UPDATE w
SET w.PadreClave = m.Padre, w.NombreWeb = m.Nombre, w.RutaWeb = m.Ruta, w.Componente = m.Componente,
    w.Icono = m.Icono, w.HabilitadoWeb = 1, w.OrdenWeb = m.Orden, w.DescripcionWeb = m.Descripcion
FROM dbo.ALFACORE_MENU_WEB w
JOIN @m m ON m.Clave = w.Clave
WHERE w.Menu = N''ALFA'';';
GO

-- Permisos iniciales (solo usuarios con permisos web explícitos; los que no tienen ninguno ya ven todo):
--  * Plantillas: quien ya entraba a Conversaciones.
--  * Perfiles: quien ya entraba a Autorización de tareas.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NULL RETURN;
EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
SELECT DISTINCT LTRIM(RTRIM(t.Usuario)), LTRIM(RTRIM(t.Sistema)), p.Nueva, GETDATE(), N''SYSTEM''
FROM dbo.ALFACORE_TAREAS_WEB t
JOIN (SELECT N''D010185-WEB-CONVERSACIONES'' AS Origen, N''D010185-WEB-CONV-PLANTILLAS'' AS Nueva
      UNION ALL SELECT N''D015002'', N''D015-WEB-PERFILES'') p
  ON UPPER(LTRIM(RTRIM(t.Clave))) = p.Origen
WHERE t.IdPerfil IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
        AND UPPER(LTRIM(RTRIM(x.Clave)))   = p.Nueva);';
GO

-- Hasta hoy, ser "administrador de conversaciones" también permitía configurar: se conserva dándoles
-- "Configurar Conversaciones" explícito. De ahora en más son dos permisos separados.
IF OBJECT_ID(N'dbo.CONV_ADMINISTRADORES', N'U') IS NULL OR OBJECT_ID(N'dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS', N'U') IS NULL RETURN;
EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS (Usuario, Sistema, Clave, IdPerfil, FechaHoraGrabacion, UsuarioGrabacion)
SELECT DISTINCT LTRIM(RTRIM(a.Usuario)), LTRIM(RTRIM(a.Sistema)), N''D010185-WEB-CONV-CONFIG'', NULL, GETDATE(), N''SYSTEM''
FROM dbo.CONV_ADMINISTRADORES a
WHERE ISNULL(LTRIM(RTRIM(a.Usuario)), N'''') <> N'''' AND ISNULL(LTRIM(RTRIM(a.Sistema)), N'''') <> N''''
  AND NOT EXISTS (
      SELECT 1 FROM dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS x
      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(a.Usuario)))
        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(a.Sistema)))
        AND UPPER(LTRIM(RTRIM(x.Clave)))   = N''D010185-WEB-CONV-CONFIG'');';
GO
