namespace AlfaCore.Services;

/// <summary>
/// Esquema de medición y cobro de IA en ALFA_CENTRAL (2026-10-05). Texto idéntico a
/// docs/base-datos/sql-referencia/2026-10-05-002__alfa_central_ia_uso.sql (lo verifica un test).
/// <see cref="IaUsoFlushService"/> lo aplica al arrancar si faltan las tablas, así no depende de que
/// alguien lo corra a mano. Es idempotente: no borra consumos, precios ni topes cargados.
/// </summary>
public static class CentralIaSchema
{
    internal const string Script = """
-- Ejecutar contra ALFA_CENTRAL. No ejecutar en las bases de clientes.
-- AlfaCore lo aplica solo al arrancar (CentralIaSchema, mismo texto) si las tablas no existen y el
-- usuario de la conexión central tiene permisos; también se puede correr a mano.
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
-- IA_TOPE_CREDITOS: tope mensual de créditos por cliente. Al llegar al tope el asistente deja de
--   responder y pasa las conversaciones a una persona; desde AvisoPorcentaje se avisa en la campana.
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

IF OBJECT_ID(N'dbo.IA_TOPE_CREDITOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IA_TOPE_CREDITOS
    (
        IdCliente nvarchar(40) NOT NULL,
        TopeCreditos int NOT NULL,
        AvisoPorcentaje int NOT NULL CONSTRAINT DF_IA_TOPE_CREDITOS_Aviso DEFAULT (80),
        Activo bit NOT NULL CONSTRAINT DF_IA_TOPE_CREDITOS_Activo DEFAULT (1),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_IA_TOPE_CREDITOS_Fecha DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(80) NULL,
        CONSTRAINT PK_IA_TOPE_CREDITOS PRIMARY KEY CLUSTERED (IdCliente),
        CONSTRAINT CK_IA_TOPE_CREDITOS_Valores CHECK (TopeCreditos >= 0 AND AvisoPorcentaje BETWEEN 1 AND 100)
    );
END;

-- 2026-10-06: configuración de los planes de créditos (editable desde Administrar → Consumo IA).
IF NOT EXISTS (SELECT 1 FROM dbo.IA_CONFIG WHERE Clave = N'PLAN_DEFAULT_CODIGO')
    INSERT INTO dbo.IA_CONFIG (Clave, Valor, Observacion)
    VALUES (N'PLAN_DEFAULT_CODIGO', N'IA_INICIAL', N'Plan de créditos que aplica a los clientes sin plan asignado. Vacío = ninguno.');

IF NOT EXISTS (SELECT 1 FROM dbo.IA_CONFIG WHERE Clave = N'TOPE_FACTOR_EXCEDENTES')
    INSERT INTO dbo.IA_CONFIG (Clave, Valor, Observacion)
    VALUES (N'TOPE_FACTOR_EXCEDENTES', N'2', N'Tope automático de los planes con excedentes = créditos incluidos × este factor. 0 = sin tope automático.');

IF NOT EXISTS (SELECT 1 FROM dbo.IA_CONFIG WHERE Clave = N'AVISO_PORCENTAJE')
    INSERT INTO dbo.IA_CONFIG (Clave, Valor, Observacion)
    VALUES (N'AVISO_PORCENTAJE', N'80', N'Porcentaje del tope automático desde el que se avisa en la campana.');

-- IA_SOLICITUD_PLAN: pedidos de cambio de plan que hace el cliente desde Asistente IA → General.
-- Se aprueban o rechazan en Administrar → Consumo IA.
IF OBJECT_ID(N'dbo.IA_SOLICITUD_PLAN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IA_SOLICITUD_PLAN
    (
        Id int IDENTITY(1,1) NOT NULL,
        IdCliente nvarchar(40) NOT NULL,
        IdPlan int NOT NULL,
        Estado nvarchar(20) NOT NULL CONSTRAINT DF_IA_SOLICITUD_PLAN_Estado DEFAULT (N'PENDIENTE'),
        SolicitadoPor nvarchar(120) NULL,
        SolicitadoUtc datetime NOT NULL CONSTRAINT DF_IA_SOLICITUD_PLAN_Solicitado DEFAULT (GETUTCDATE()),
        DecididoPor nvarchar(80) NULL,
        DecididoUtc datetime NULL,
        CONSTRAINT PK_IA_SOLICITUD_PLAN PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_IA_SOLICITUD_PLAN_Estado CHECK (Estado IN (N'PENDIENTE', N'APROBADA', N'RECHAZADA', N'CANCELADA'))
    );
    CREATE NONCLUSTERED INDEX IX_IA_SOLICITUD_PLAN_Cliente ON dbo.IA_SOLICITUD_PLAN (IdCliente, Estado);
END;

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

-- 2026-10-06: planes de créditos aprobados (Inicial incluido con el sistema, Estándar y Pro). Solo se
-- crean si no existen; después se editan en Administrar → Módulos → Planes. En los planes CREDITOS,
-- PrecioExcedente es el precio por cada 1.000 créditos adicionales. Si falla (por ejemplo, una base
-- central sin la tabla Planes) no frena el resto del esquema.
BEGIN TRY
    DECLARE @IdModuloIa int;
    IF OBJECT_ID(N'dbo.Planes', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Modulos', N'U') IS NOT NULL
        SELECT TOP (1) @IdModuloIa = Id FROM dbo.Modulos WHERE UPPER(LTRIM(RTRIM(Codigo))) = N'IA_CREDITOS';

    IF @IdModuloIa IS NOT NULL
        INSERT INTO dbo.Planes (IdModulo, Codigo, Nombre, Descripcion, TipoFacturacion, Precio, Moneda,
                                CantidadIncluida, PermiteExcedentes, PrecioExcedente, Activo, VisibleCatalogo)
        SELECT @IdModuloIa, v.Codigo, v.Nombre, v.Descripcion, N'CREDITOS', v.Precio, N'USD',
               v.Incluidos, v.Excedentes, v.PrecioExcedente, 1, 1
        FROM (VALUES
            (N'IA_INICIAL', N'IA Inicial', N'Viene con el sistema. Al llegar a los créditos incluidos, el asistente pasa las conversaciones al equipo.', 0, 3000, 0, NULL),
            (N'IA_ESTANDAR', N'IA Estándar', N'Para la mayoría de los clientes, con archivos. El excedente se factura a fin de mes.', 35, 20000, 1, 3),
            (N'IA_PRO', N'IA Pro', N'Uso intensivo o varias bases. El excedente se factura a fin de mes.', 140, 80000, 1, 3)
        ) AS v (Codigo, Nombre, Descripcion, Precio, Incluidos, Excedentes, PrecioExcedente)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Planes p WHERE p.IdModulo = @IdModuloIa AND p.Codigo = v.Codigo);
END TRY
BEGIN CATCH
    PRINT N'No se pudieron crear los planes de créditos IA: ' + ERROR_MESSAGE();
END CATCH;
GO
""";

    /// <summary>Objetos que tienen que existir para considerar aplicado el esquema.</summary>
    internal const string VerificacionSql = """
        SELECT CASE WHEN OBJECT_ID(N'dbo.IA_USO', N'U') IS NOT NULL
                     AND OBJECT_ID(N'dbo.IA_PRECIO_MODELO', N'U') IS NOT NULL
                     AND OBJECT_ID(N'dbo.IA_CONFIG', N'U') IS NOT NULL
                     AND OBJECT_ID(N'dbo.IA_TOPE_CREDITOS', N'U') IS NOT NULL
                     AND OBJECT_ID(N'dbo.IA_SOLICITUD_PLAN', N'U') IS NOT NULL
                     AND OBJECT_ID(N'dbo.V_IA_USO_DIARIO', N'V') IS NOT NULL
                    THEN 1 ELSE 0 END;
        """;

    /// <summary>Lotes separados por GO, en orden.</summary>
    internal static IReadOnlyList<string> Lotes()
        => System.Text.RegularExpressions.Regex
            .Split(Script, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
}
