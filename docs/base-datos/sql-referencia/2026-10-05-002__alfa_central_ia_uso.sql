-- Ejecutar contra ALFA_CENTRAL. No ejecutar en las bases de clientes.
-- Medición del consumo de OpenAI por base/cliente (2026-10-05). Idempotente: reejecutar no borra
-- consumos ni precios cargados.
--
-- IA_PRECIO_MODELO: precio vigente por modelo (USD por millón de tokens, por búsqueda y por GB-día).
--   El costo de cada consumo se calcula al grabarlo con el precio vigente del modelo, eligiendo la
--   coincidencia por prefijo más larga (ej. "gpt-4o-mini" cubre "gpt-4o-mini-2024-07-18").
--   Si se usa otro modelo (variable OPENAI_MODEL), cargar su precio acá: mientras falte, esos consumos
--   quedan con CostoUsd NULL y se ven como "sin precio" en V_IA_USO_DIARIO.
-- IA_USO: un registro por llamada a OpenAI (o por búsqueda/almacenamiento de archivos).
-- V_IA_USO_DIARIO: totales por día, cliente, base, función y modelo.
-- IA_CONFIG: USD_POR_CREDITO = costo de OpenAI que equivale a 1 crédito IA (por defecto 0,001).
-- Módulo IA_CREDITOS: se le asigna a cada cliente un plan de tipo CREDITOS (CantidadIncluida = créditos
--   incluidos por mes, Precio = abono fijo, PrecioExcedente = precio por crédito adicional). El cargo
--   mensual se genera desde Administrar → Consumo IA.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.bases', N'U') IS NULL
    THROW 50000, N'La base destino no contiene el catálogo central de bases (dbo.bases).', 1;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.IA_PRECIO_MODELO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IA_PRECIO_MODELO
    (
        IdPrecio int IDENTITY(1,1) NOT NULL,
        Modelo nvarchar(80) NOT NULL,
        VigenteDesde date NOT NULL,
        UsdPorMillonEntrada decimal(18, 6) NOT NULL CONSTRAINT DF_IA_PRECIO_MODELO_Entrada DEFAULT (0),
        UsdPorMillonEntradaCacheada decimal(18, 6) NOT NULL CONSTRAINT DF_IA_PRECIO_MODELO_Cacheada DEFAULT (0),
        UsdPorMillonSalida decimal(18, 6) NOT NULL CONSTRAINT DF_IA_PRECIO_MODELO_Salida DEFAULT (0),
        UsdPorBusqueda decimal(18, 6) NOT NULL CONSTRAINT DF_IA_PRECIO_MODELO_Busqueda DEFAULT (0),
        UsdPorGbDia decimal(18, 6) NOT NULL CONSTRAINT DF_IA_PRECIO_MODELO_GbDia DEFAULT (0),
        Observacion nvarchar(250) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_IA_PRECIO_MODELO_FHG DEFAULT (GETDATE()),
        CONSTRAINT PK_IA_PRECIO_MODELO PRIMARY KEY CLUSTERED (IdPrecio),
        CONSTRAINT UQ_IA_PRECIO_MODELO UNIQUE (Modelo, VigenteDesde)
    );
END;

-- Precios de referencia de OpenAI (verificar en https://openai.com/api/pricing antes de facturar).
IF NOT EXISTS (SELECT 1 FROM dbo.IA_PRECIO_MODELO WHERE Modelo = N'gpt-4o-mini')
    INSERT INTO dbo.IA_PRECIO_MODELO (Modelo, VigenteDesde, UsdPorMillonEntrada, UsdPorMillonEntradaCacheada, UsdPorMillonSalida, Observacion)
    VALUES (N'gpt-4o-mini', '2024-07-18', 0.15, 0.075, 0.60, N'Precio de lista OpenAI. Modelo por defecto si no hay OPENAI_MODEL.');

IF NOT EXISTS (SELECT 1 FROM dbo.IA_PRECIO_MODELO WHERE Modelo = N'file_search')
    INSERT INTO dbo.IA_PRECIO_MODELO (Modelo, VigenteDesde, UsdPorBusqueda, Observacion)
    VALUES (N'file_search', '2025-01-01', 0.0025, N'USD 2,50 cada 1.000 búsquedas en archivos.');

IF NOT EXISTS (SELECT 1 FROM dbo.IA_PRECIO_MODELO WHERE Modelo = N'vector_store')
    INSERT INTO dbo.IA_PRECIO_MODELO (Modelo, VigenteDesde, UsdPorGbDia, Observacion)
    VALUES (N'vector_store', '2025-01-01', 0.10, N'USD 0,10 por GB por día. El primer GB por proyecto de OpenAI es gratis (no se descuenta por base).');

IF OBJECT_ID(N'dbo.IA_USO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IA_USO
    (
        IdUso bigint IDENTITY(1,1) NOT NULL,
        FechaHoraUtc datetime2(0) NOT NULL,
        IdBase int NULL,
        IdCliente nvarchar(40) NULL,
        BaseDatos nvarchar(128) NULL,
        Funcion nvarchar(40) NOT NULL,
        Modelo nvarchar(80) NOT NULL,
        TokensEntrada int NOT NULL CONSTRAINT DF_IA_USO_Entrada DEFAULT (0),
        TokensEntradaCacheados int NOT NULL CONSTRAINT DF_IA_USO_Cacheados DEFAULT (0),
        TokensSalida int NOT NULL CONSTRAINT DF_IA_USO_Salida DEFAULT (0),
        Busquedas int NOT NULL CONSTRAINT DF_IA_USO_Busquedas DEFAULT (0),
        BytesAlmacenados bigint NOT NULL CONSTRAINT DF_IA_USO_Bytes DEFAULT (0),
        CostoUsd decimal(18, 8) NULL,
        Referencia nvarchar(120) NULL,
        ServidorApp nvarchar(128) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_IA_USO_FHG DEFAULT (GETDATE()),
        CONSTRAINT PK_IA_USO PRIMARY KEY CLUSTERED (IdUso)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.IA_USO') AND name = N'IX_IA_USO_Cliente_Fecha')
    CREATE NONCLUSTERED INDEX IX_IA_USO_Cliente_Fecha ON dbo.IA_USO (IdCliente, FechaHoraUtc)
        INCLUDE (IdBase, Funcion, Modelo, TokensEntrada, TokensEntradaCacheados, TokensSalida, Busquedas, CostoUsd);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.IA_USO') AND name = N'IX_IA_USO_Base_Fecha')
    CREATE NONCLUSTERED INDEX IX_IA_USO_Base_Fecha ON dbo.IA_USO (IdBase, FechaHoraUtc)
        INCLUDE (Funcion, Modelo, TokensEntrada, TokensEntradaCacheados, TokensSalida, Busquedas, CostoUsd);

IF OBJECT_ID(N'dbo.IA_CONFIG', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IA_CONFIG
    (
        Clave nvarchar(60) NOT NULL,
        Valor nvarchar(200) NOT NULL,
        Observacion nvarchar(250) NULL,
        CONSTRAINT PK_IA_CONFIG PRIMARY KEY CLUSTERED (Clave)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.IA_CONFIG WHERE Clave = N'USD_POR_CREDITO')
    INSERT INTO dbo.IA_CONFIG (Clave, Valor, Observacion)
    VALUES (N'USD_POR_CREDITO', N'0.001', N'Costo de OpenAI (USD) que equivale a 1 crédito IA. El margen va en el precio del plan.');

IF OBJECT_ID(N'dbo.Modulos', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.Modulos WITH (UPDLOCK, HOLDLOCK) WHERE UPPER(LTRIM(RTRIM(Codigo))) = N'IA_CREDITOS')
    INSERT INTO dbo.Modulos (Codigo, Nombre, Descripcion, MenuKeyRaiz, Precio, Activo)
    VALUES (N'IA_CREDITOS', N'Créditos de IA',
        N'Consumo de IA (asistente, análisis, informes, archivos). Asignar un plan de tipo CREDITOS: créditos incluidos por mes y precio por crédito excedente.',
        N'', 0, 1);

COMMIT TRANSACTION;
GO

CREATE OR ALTER VIEW dbo.V_IA_USO_DIARIO
AS
SELECT
    CAST(u.FechaHoraUtc AS date) AS Dia,
    u.IdCliente,
    u.IdBase,
    u.Funcion,
    u.Modelo,
    COUNT_BIG(*) AS Llamadas,
    SUM(CAST(u.TokensEntrada AS bigint)) AS TokensEntrada,
    SUM(CAST(u.TokensEntradaCacheados AS bigint)) AS TokensEntradaCacheados,
    SUM(CAST(u.TokensSalida AS bigint)) AS TokensSalida,
    SUM(CAST(u.Busquedas AS bigint)) AS Busquedas,
    SUM(u.CostoUsd) AS CostoUsd,
    SUM(CASE WHEN u.CostoUsd IS NULL THEN 1 ELSE 0 END) AS LlamadasSinPrecio
FROM dbo.IA_USO u
GROUP BY CAST(u.FechaHoraUtc AS date), u.IdCliente, u.IdBase, u.Funcion, u.Modelo;
GO
