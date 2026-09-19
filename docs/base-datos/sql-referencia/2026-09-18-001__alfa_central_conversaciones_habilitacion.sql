-- Ejecutar contra ALFA_CENTRAL. No ejecutar en las bases de clientes.
-- Mismo criterio que 2026-09-14-002__alfa_central_comprobantes_ia.sql, para Conversaciones: a partir
-- de ahora requiere activación explícita por cliente (ClienteModulos) y selección de bases
-- (ConversacionesWorkerHabilitado), incluso para clientes legacy. No activa clientes ni selecciona
-- bases. Reejecutar conserva las selecciones existentes.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.bases', N'U') IS NULL OR OBJECT_ID(N'dbo.Modulos', N'U') IS NULL
    OR OBJECT_ID(N'dbo.ClienteModulos', N'U') IS NULL
    THROW 50000, N'La base destino no contiene el catálogo central de clientes y módulos.', 1;

BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.bases', N'ConversacionesWorkerHabilitado') IS NULL
    ALTER TABLE dbo.bases ADD ConversacionesWorkerHabilitado bit NOT NULL
        CONSTRAINT DF_bases_ConversacionesWorkerHabilitado DEFAULT (0) WITH VALUES;

UPDATE dbo.Modulos
SET Descripcion = N'Requiere activación explícita por cliente y selección de bases en Administrar, incluso para clientes legacy.'
WHERE UPPER(LTRIM(RTRIM(Codigo))) = N'CONVERSACIONES';
COMMIT TRANSACTION;
