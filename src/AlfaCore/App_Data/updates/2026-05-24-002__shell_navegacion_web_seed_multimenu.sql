IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
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
    SELECT N'D015001',  N'/usuarios',                 N'Usuarios',               N'bi-shield-lock-fill',     15001, 0, N'Gestión web de usuarios' UNION ALL
    SELECT N'D015003',  N'/auditoria',                N'Auditoria',              N'bi-shield-exclamation',   15003, 0, N'Centro web de auditoría' UNION ALL
    SELECT N'D0160',    N'/interfaces',               N'Interfaces',             N'bi-folder2-open',         16000, 1, N'Recepción documental web' UNION ALL
    SELECT N'D016001',  N'/interfaces/configuracion', N'InterfacesConfiguracion',N'bi-sliders',             16001, 0, N'Configuración web de Interfaces' UNION ALL
    SELECT N'D5001',    N'/compras/comprobantes',     N'Comprobantes',           N'bi-receipt',              50010, 0, N'Comprobantes dashboard compras' UNION ALL
    SELECT N'D6000',    N'/ventas/comprobantes',      N'VentasComprobantes',     N'bi-receipt',              60000, 0, N'Comprobantes dashboard ventas' UNION ALL
    SELECT N'D7510',    N'/caja-bancos',              N'CajaBancos',             N'bi-bank',                 75100, 0, N'Dashboard web de caja y bancos' UNION ALL
    SELECT N'D8079',    N'/stock',                    N'Stock',                  N'bi-box-seam',             80790, 0, N'Dashboard web de stock' UNION ALL
    SELECT N'D9502',    N'/contabilidad',             N'Contabilidad',           N'bi-journal-bookmark-fill',95020, 0, N'Dashboard web de contabilidad' UNION ALL
    SELECT N'D9808',    N'/calendario',               N'Calendario',             N'bi-calendar3',            98080, 0, N'Calendario web' UNION ALL
    SELECT N'D9820',    N'/consultas',                N'Consultas',              N'bi-table',                98200, 1, N'Diseñador de consultas web'
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
