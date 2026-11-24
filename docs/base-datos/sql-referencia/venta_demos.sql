-- Venta online (docs/modulos/venta-online.md), fase 1: demos por familia de rubros.
-- Cada fila dice a qué base y con qué usuario central entra /demo/{rubro}.
-- La clave del usuario demo NO va acá: se lee de configuración VentaDemo:Claves:{Familia}.
-- Ejecutar manualmente contra ALFA_CENTRAL — no se aplica solo. Idempotente.

IF OBJECT_ID(N'dbo.VentaDemos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VentaDemos
    (
        Familia         nvarchar(30)  NOT NULL,   -- BARES | COMERCIO | DISTRIBUIDORAS | SERVICIOS
        IdBase          int           NOT NULL,   -- dbo.bases de la base demo
        CentralLogin    nvarchar(100) NOT NULL,   -- usuario central con el que se entra a la demo
        RutaInicial     nvarchar(200) NOT NULL CONSTRAINT DF_VentaDemos_RutaInicial DEFAULT (N''),  -- ej. ventas/punto-venta (sin / inicial)
        NegocioEjemplo  nvarchar(100) NOT NULL CONSTRAINT DF_VentaDemos_Negocio DEFAULT (N''),      -- texto de la barra DEMO
        BackupDorado    nvarchar(400) NULL,       -- fase 2: .bak para el reset nocturno
        Activo          bit           NOT NULL CONSTRAINT DF_VentaDemos_Activo DEFAULT (1),
        ModificadoUtc   datetime      NOT NULL CONSTRAINT DF_VentaDemos_ModificadoUtc DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_VentaDemos PRIMARY KEY CLUSTERED (Familia ASC)
    );
END;
GO

-- Ejemplo (completar IdBase y login reales antes de ejecutar):
-- IF NOT EXISTS (SELECT 1 FROM dbo.VentaDemos WHERE Familia = N'BARES')
--     INSERT INTO dbo.VentaDemos (Familia, IdBase, CentralLogin, RutaInicial, NegocioEjemplo)
--     VALUES (N'BARES', <IDBASE>, N'demo.bares', N'ventas/punto-venta', N'Café La Esquina');
