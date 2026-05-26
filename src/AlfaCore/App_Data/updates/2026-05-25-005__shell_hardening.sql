IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_MENU_WEB (
        Menu nvarchar(50) NOT NULL,
        Clave nvarchar(50) NOT NULL,
        RutaWeb nvarchar(250) NOT NULL,
        Componente nvarchar(150) NULL,
        Icono nvarchar(50) NULL,
        HabilitadoWeb bit NOT NULL CONSTRAINT DF_ALFACORE_MENU_WEB_HabilitadoWeb_Hardening DEFAULT (1),
        OrdenWeb int NULL,
        EsFavoritoDefault bit NOT NULL CONSTRAINT DF_ALFACORE_MENU_WEB_Favorito_Hardening DEFAULT (0),
        Observacion nvarchar(250) NULL,
        CONSTRAINT PK_ALFACORE_MENU_WEB_Hardening PRIMARY KEY (Menu, Clave)
    );
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_FAVORITOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_FAVORITOS (
        Usuario nvarchar(50) NOT NULL,
        Sistema nvarchar(50) NOT NULL,
        Clave nvarchar(50) NOT NULL,
        Orden int NULL,
        CONSTRAINT PK_ALFACORE_FAVORITOS_Hardening PRIMARY KEY (Usuario, Sistema, Clave)
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_RECIENTES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_RECIENTES (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ALFACORE_RECIENTES_Hardening PRIMARY KEY,
        Usuario nvarchar(50) NULL,
        Sistema nvarchar(50) NULL,
        Clave nvarchar(50) NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_ALFACORE_RECIENTES_FechaHora_Hardening DEFAULT (GETDATE())
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_RECIENTES_USUARIO_SISTEMA_FECHA' AND object_id = OBJECT_ID(N'dbo.ALFACORE_RECIENTES'))
BEGIN
    CREATE INDEX IX_ALFACORE_RECIENTES_USUARIO_SISTEMA_FECHA
        ON dbo.ALFACORE_RECIENTES (Usuario, Sistema, FechaHora DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_FAVORITOS_SISTEMA_ORDEN' AND object_id = OBJECT_ID(N'dbo.ALFACORE_FAVORITOS'))
BEGIN
    CREATE INDEX IX_ALFACORE_FAVORITOS_SISTEMA_ORDEN
        ON dbo.ALFACORE_FAVORITOS (Sistema, Orden, Usuario);
END;
GO

;WITH Seed(Clave, RutaWeb, Componente, Icono, OrdenWeb, EsFavoritoDefault, Observacion) AS
(
    SELECT N'D',        N'/',                         N'Launcher',               N'bi-grid-3x3-gap-fill',    0,     0, N'Raíz del shell web' UNION ALL
    SELECT N'D01',      N'/shell/D01',                N'ShellWorkspacePage',     N'bi-folder2-open',         10,    0, N'Módulo raíz Archivos' UNION ALL
    SELECT N'D50',      N'/shell/D50',                N'ShellWorkspacePage',     N'bi-cart-fill',            50,    0, N'Módulo raíz Compras' UNION ALL
    SELECT N'D60',      N'/shell/D60',                N'ShellWorkspacePage',     N'bi-graph-up-arrow',       60,    0, N'Módulo raíz Ventas' UNION ALL
    SELECT N'D75',      N'/shell/D75',                N'ShellWorkspacePage',     N'bi-bank',                 75,    0, N'Módulo raíz Caja y Bancos' UNION ALL
    SELECT N'D80',      N'/shell/D80',                N'ShellWorkspacePage',     N'bi-box-seam',             80,    0, N'Módulo raíz Stock' UNION ALL
    SELECT N'D95',      N'/shell/D95',                N'ShellWorkspacePage',     N'bi-journal-bookmark-fill',95,    0, N'Módulo raíz Gestión Contable' UNION ALL
    SELECT N'D98',      N'/shell/D98',                N'ShellWorkspacePage',     N'bi-tools',                98,    0, N'Módulo raíz Utilidades' UNION ALL
    SELECT N'D010105',  N'/clientes',                 N'Clientes',               N'bi-people-fill',          10105, 1, N'ABM web de clientes' UNION ALL
    SELECT N'D010110',  N'/proveedores',              N'ProveedoresMaestro',     N'bi-truck',                10110, 1, N'ABM web de proveedores' UNION ALL
    SELECT N'D010170',  N'/contactos',                N'Contactos',              N'bi-person-lines-fill',    10170, 1, N'Agenda web de contactos' UNION ALL
    SELECT N'D010185',  N'/shell/D010185',            N'ShellWorkspacePage',     N'bi-headset',             10185, 0, N'Módulo raíz CRM' UNION ALL
    SELECT N'D015001',  N'/usuarios',                 N'Usuarios',               N'bi-shield-lock-fill',     15001, 0, N'Gestión web de usuarios' UNION ALL
    SELECT N'D015002',  N'/seguridad/autorizacion-tareas', N'AutorizacionTareas', N'bi-shield-lock-fill',   15002, 0, N'Permisos reales compartidos entre Desktop y AlfaCore sobre el árbol oficial del menú.' UNION ALL
    SELECT N'D015003',  N'/auditoria',                N'Auditoria',              N'bi-shield-exclamation',   15003, 0, N'Centro web de auditoría' UNION ALL
    SELECT N'D0160',    N'/interfaces',               N'Interfaces',             N'bi-folder2-open',         16000, 1, N'Recepción documental web' UNION ALL
    SELECT N'D016001',  N'/interfaces/configuracion', N'InterfacesConfiguracion',N'bi-sliders',             16001, 0, N'Configuración web de Interfaces' UNION ALL
    SELECT N'D5001',    N'/compras',                  N'Comprobantes',           N'bi-receipt',              50010, 0, N'Dashboard web de compras' UNION ALL
    SELECT N'D6000',    N'/ventas',                   N'VentasComprobantes',     N'bi-receipt',              60000, 0, N'Dashboard web de ventas' UNION ALL
    SELECT N'D7510',    N'/caja-bancos',              N'CajaBancos',             N'bi-bank',                 75100, 0, N'Dashboard web de caja y bancos' UNION ALL
    SELECT N'D8079',    N'/stock',                    N'Stock',                  N'bi-box-seam',             80790, 0, N'Dashboard web de stock' UNION ALL
    SELECT N'D9502',    N'/contabilidad',             N'Contabilidad',           N'bi-journal-bookmark-fill',95020, 0, N'Dashboard web de contabilidad' UNION ALL
    SELECT N'D9808',    N'/calendario',               N'Calendario',             N'bi-calendar3',            98080, 0, N'Calendario web' UNION ALL
    SELECT N'D9820',    N'/consultas',                N'Consultas',              N'bi-table',                98200, 1, N'Diseñador de consultas web' UNION ALL
    SELECT N'D989003',  N'/actualizaciones',          N'Actualizaciones',        N'bi-cloud-arrow-up',      9890030,0, N'Gestión de updates y administración técnica de base de datos'
)
INSERT INTO dbo.ALFACORE_MENU_WEB
(
    Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion
)
SELECT
    m.Menu,
    s.Clave,
    s.RutaWeb,
    s.Componente,
    s.Icono,
    1,
    s.OrdenWeb,
    s.EsFavoritoDefault,
    s.Observacion
FROM Seed s
INNER JOIN dbo.TA_MENU m
    ON m.Clave = s.Clave
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB w
    WHERE w.Menu = m.Menu
      AND w.Clave = s.Clave
);
GO
