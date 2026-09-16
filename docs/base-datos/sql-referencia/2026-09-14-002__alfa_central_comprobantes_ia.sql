-- Ejecutar contra ALFA_CENTRAL. No ejecutar en las bases de clientes.
-- No activa clientes ni selecciona bases. Reejecutar conserva las selecciones existentes.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.bases', N'U') IS NULL OR OBJECT_ID(N'dbo.Modulos', N'U') IS NULL
    OR OBJECT_ID(N'dbo.ClienteModulos', N'U') IS NULL
    THROW 50000, N'La base destino no contiene el catálogo central de clientes y módulos.', 1;

BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.bases', N'CompraIaWorkerHabilitado') IS NULL
    ALTER TABLE dbo.bases ADD CompraIaWorkerHabilitado bit NOT NULL
        CONSTRAINT DF_bases_CompraIaWorkerHabilitado DEFAULT (0) WITH VALUES;

IF NOT EXISTS (SELECT 1 FROM dbo.Modulos WITH (UPDLOCK, HOLDLOCK)
               WHERE UPPER(LTRIM(RTRIM(Codigo))) = N'COMPROBANTES_IA')
    INSERT INTO dbo.Modulos (Codigo, Nombre, Descripcion, MenuKeyRaiz, Precio, Activo)
    VALUES (N'COMPROBANTES_IA', N'Carga de comprobantes con IA',
        N'Requiere activación explícita por cliente y selección de bases en Administrar, incluso para clientes legacy.',
        N'', 0, 1);
COMMIT TRANSACTION;
