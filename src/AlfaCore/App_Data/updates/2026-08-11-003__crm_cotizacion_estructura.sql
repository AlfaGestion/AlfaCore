/*
    CRM - Fase 3: cotización / presupuesto.
    Documento comercial propio del CRM (no fiscal): no factura, no cobra, no mueve stock.
    Ligado a una oportunidad. Comprobante corto 'CTZ' y unidad de negocio para permitir
    unificar bases separadas por punto de venta y una futura migración a las tablas de Alfa.
    Sin FKs físicas (mismo criterio que el módulo POS_): integridad validada en la aplicación.
    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CRM_COTIZACION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_COTIZACION
    (
        IdCotizacion bigint IDENTITY(1,1) NOT NULL,
        Numero int NOT NULL,
        TC nvarchar(4) NOT NULL CONSTRAINT DF_CRM_COT_TC DEFAULT (N'CTZ'),
        UNEGOCIO nvarchar(4) NULL,
        IdOportunidad bigint NOT NULL,
        ClienteCodigo nvarchar(30) NULL,
        ClienteNombre nvarchar(200) NULL,
        EsConsumidorFinal bit NOT NULL CONSTRAINT DF_CRM_COT_ConsFinal DEFAULT (0),
        IdLista nvarchar(8) NULL,
        ClasePrecio smallint NOT NULL CONSTRAINT DF_CRM_COT_Clase DEFAULT (1),
        PreciosConIva bit NOT NULL CONSTRAINT DF_CRM_COT_PreciosConIva DEFAULT (0),
        IdTecnico nvarchar(20) NULL,
        Fecha date NOT NULL CONSTRAINT DF_CRM_COT_Fecha DEFAULT (CAST(GETDATE() AS date)),
        FechaVencimiento date NULL,
        Estado nvarchar(20) NOT NULL CONSTRAINT DF_CRM_COT_Estado DEFAULT (N'BORRADOR'),
        Observaciones nvarchar(max) NULL,
        TotalNeto decimal(18,2) NOT NULL CONSTRAINT DF_CRM_COT_TotalNeto DEFAULT (0),
        TotalIva decimal(18,2) NOT NULL CONSTRAINT DF_CRM_COT_TotalIva DEFAULT (0),
        Total decimal(18,2) NOT NULL CONSTRAINT DF_CRM_COT_Total DEFAULT (0),
        UsuarioAlta nvarchar(80) NULL,
        Baja bit NOT NULL CONSTRAINT DF_CRM_COT_Baja DEFAULT (0),
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_CRM_COT_FHA DEFAULT (GETDATE()),
        FechaHoraModificacion datetime NULL,
        CONSTRAINT PK_CRM_COTIZACION PRIMARY KEY CLUSTERED (IdCotizacion),
        CONSTRAINT UQ_CRM_COTIZACION_Numero UNIQUE NONCLUSTERED (Numero),
        CONSTRAINT CK_CRM_COT_Total CHECK (Total >= 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.CRM_COTIZACION_DET', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_COTIZACION_DET
    (
        IdDetalle bigint IDENTITY(1,1) NOT NULL,
        IdCotizacion bigint NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_CRM_COTDET_Orden DEFAULT (0),
        IdArticulo nvarchar(50) NULL,
        Descripcion nvarchar(200) NOT NULL,
        Cantidad decimal(18,4) NOT NULL CONSTRAINT DF_CRM_COTDET_Cantidad DEFAULT (1),
        PrecioUnitarioNeto decimal(18,4) NOT NULL CONSTRAINT DF_CRM_COTDET_PUNeto DEFAULT (0),
        TasaIva decimal(9,4) NOT NULL CONSTRAINT DF_CRM_COTDET_TasaIva DEFAULT (0),
        PrecioUnitarioConIva decimal(18,4) NOT NULL CONSTRAINT DF_CRM_COTDET_PUConIva DEFAULT (0),
        SubtotalNeto decimal(18,2) NOT NULL CONSTRAINT DF_CRM_COTDET_SubNeto DEFAULT (0),
        SubtotalIva decimal(18,2) NOT NULL CONSTRAINT DF_CRM_COTDET_SubIva DEFAULT (0),
        Subtotal decimal(18,2) NOT NULL CONSTRAINT DF_CRM_COTDET_Subtotal DEFAULT (0),
        CONSTRAINT PK_CRM_COTIZACION_DET PRIMARY KEY CLUSTERED (IdDetalle)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_COTIZACION') AND name = N'IX_CRM_COT_Oportunidad')
    CREATE NONCLUSTERED INDEX IX_CRM_COT_Oportunidad ON dbo.CRM_COTIZACION (IdOportunidad, Baja, FechaHoraAlta DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_COTIZACION') AND name = N'IX_CRM_COT_Cliente')
    CREATE NONCLUSTERED INDEX IX_CRM_COT_Cliente ON dbo.CRM_COTIZACION (ClienteCodigo, Baja, FechaHoraAlta DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_COTIZACION_DET') AND name = N'IX_CRM_COTDET_Cotizacion')
    CREATE NONCLUSTERED INDEX IX_CRM_COTDET_Cotizacion ON dbo.CRM_COTIZACION_DET (IdCotizacion, Orden);
GO
