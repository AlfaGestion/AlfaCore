IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_MENU_WEB (
        Menu nvarchar(50) NOT NULL,
        Clave nvarchar(50) NOT NULL,
        RutaWeb nvarchar(250) NOT NULL,
        Componente nvarchar(150) NULL,
        Icono nvarchar(50) NULL,
        HabilitadoWeb bit NOT NULL CONSTRAINT DF_ALFACORE_MENU_WEB_HabilitadoWeb DEFAULT (1),
        OrdenWeb int NULL,
        EsFavoritoDefault bit NOT NULL CONSTRAINT DF_ALFACORE_MENU_WEB_Favorito DEFAULT (0),
        Observacion nvarchar(250) NULL,
        CONSTRAINT PK_ALFACORE_MENU_WEB PRIMARY KEY (Menu, Clave)
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_FAVORITOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_FAVORITOS (
        Usuario nvarchar(50) NOT NULL,
        Sistema nvarchar(50) NOT NULL,
        Clave nvarchar(50) NOT NULL,
        Orden int NULL,
        CONSTRAINT PK_ALFACORE_FAVORITOS PRIMARY KEY (Usuario, Sistema, Clave)
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_RECIENTES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_RECIENTES (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ALFACORE_RECIENTES PRIMARY KEY,
        Usuario nvarchar(50) NULL,
        Sistema nvarchar(50) NULL,
        Clave nvarchar(50) NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_ALFACORE_RECIENTES_FechaHora DEFAULT (GETDATE())
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

DECLARE @MenuBase nvarchar(50) = N'ALFA';

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D', N'/', N'Launcher', N'bi-grid-3x3-gap-fill', 1, 0, 0, N'Raíz del shell web');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D01')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D01')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D01', N'/shell/D01', N'ShellWorkspacePage', N'bi-folder2-open', 1, 10, 0, N'Módulo raíz Archivos');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D50')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D50')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D50', N'/shell/D50', N'ShellWorkspacePage', N'bi-cart-fill', 1, 50, 0, N'Módulo raíz Compras');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D60')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D60')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D60', N'/shell/D60', N'ShellWorkspacePage', N'bi-graph-up-arrow', 1, 60, 0, N'Módulo raíz Ventas');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D75')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D75')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D75', N'/shell/D75', N'ShellWorkspacePage', N'bi-bank', 1, 75, 0, N'Módulo raíz Caja y Bancos');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D80')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D80')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D80', N'/shell/D80', N'ShellWorkspacePage', N'bi-box-seam', 1, 80, 0, N'Módulo raíz Stock');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D95')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D95')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D95', N'/shell/D95', N'ShellWorkspacePage', N'bi-journal-bookmark-fill', 1, 95, 0, N'Módulo raíz Gestión Contable');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D98')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D98')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D98', N'/shell/D98', N'ShellWorkspacePage', N'bi-tools', 1, 98, 0, N'Módulo raíz Utilidades');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D010105')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D010105')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D010105', N'/clientes', N'Clientes', N'bi-people-fill', 1, 10105, 1, N'ABM web de clientes');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D010110')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D010110')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D010110', N'/proveedores', N'ProveedoresMaestro', N'bi-truck', 1, 10110, 1, N'ABM web de proveedores');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D010170')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D010170')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D010170', N'/contactos', N'Contactos', N'bi-person-lines-fill', 1, 10170, 1, N'Agenda web de contactos');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D015001')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D015001')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D015001', N'/usuarios', N'Usuarios', N'bi-shield-lock-fill', 1, 15001, 0, N'Gestión web de usuarios');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D015003')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D015003')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D015003', N'/auditoria', N'Auditoria', N'bi-shield-exclamation', 1, 15003, 0, N'Centro web de auditoría');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D0160')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D0160')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D0160', N'/interfaces', N'Interfaces', N'bi-folder2-open', 1, 16000, 1, N'Recepción documental web');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D016001')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D016001')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D016001', N'/interfaces/configuracion', N'InterfacesConfiguracion', N'bi-sliders', 1, 16001, 0, N'Configuración web de Interfaces');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D5001')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D5001')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D5001', N'/compras/comprobantes', N'Comprobantes', N'bi-receipt', 1, 50010, 0, N'Comprobantes dashboard compras');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D6000')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D6000')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D6000', N'/ventas/comprobantes', N'VentasComprobantes', N'bi-receipt', 1, 60000, 0, N'Comprobantes dashboard ventas');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D7510')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D7510')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D7510', N'/caja-bancos', N'CajaBancos', N'bi-bank', 1, 75100, 0, N'Dashboard web de caja y bancos');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D8079')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D8079')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D8079', N'/stock', N'Stock', N'bi-box-seam', 1, 80790, 0, N'Dashboard web de stock');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D9502')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D9502')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D9502', N'/contabilidad', N'Contabilidad', N'bi-journal-bookmark-fill', 1, 95020, 0, N'Dashboard web de contabilidad');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D9808')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D9808')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D9808', N'/calendario', N'Calendario', N'bi-calendar3', 1, 98080, 0, N'Calendario web');
END;

IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = @MenuBase AND Clave = N'D9820')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = @MenuBase AND Clave = N'D9820')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion)
    VALUES (@MenuBase, N'D9820', N'/consultas', N'Consultas', N'bi-table', 1, 98200, 1, N'Diseñador de consultas web');
END;
GO
