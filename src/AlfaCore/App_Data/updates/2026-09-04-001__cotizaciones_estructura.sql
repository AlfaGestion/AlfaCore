/*
    Cotizaciones - módulo general (no exclusivo de CRM).
    Documento comercial propio (no fiscal): no factura, no cobra, no mueve stock.
    Versionado real: COT_COTIZACION es el documento raíz (identidad/numeración/estado/
    vínculo opcional a una Oportunidad de CRM), COT_VERSION es un snapshot completo e
    inmutable de cada versión enviada (datos comerciales, texto de propuesta, totales).
    COT_SECCION/COT_DET cuelgan de una versión puntual, nunca del documento raíz.
    Sin FKs físicas (mismo criterio que CRM_COTIZACION/POS_): varias tablas referenciadas
    (Vt_Clientes, V_TA_Tareas) son vistas u objetos legacy; integridad validada en la app.
    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.COT_COTIZACION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COT_COTIZACION
    (
        IdCotizacion bigint IDENTITY(1,1) NOT NULL,
        Numero int NOT NULL,
        TC nvarchar(4) NOT NULL CONSTRAINT DF_COT_COTIZACION_TC DEFAULT (N'COT'),
        IdOportunidad bigint NULL,
        CodigoCliente nvarchar(30) NULL,
        Estado nvarchar(20) NOT NULL CONSTRAINT DF_COT_COTIZACION_Estado DEFAULT (N'BORRADOR'),
        IdVersionActual bigint NULL,
        UsuarioAlta nvarchar(80) NULL,
        Baja bit NOT NULL CONSTRAINT DF_COT_COTIZACION_Baja DEFAULT (0),
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_COT_COTIZACION_FHA DEFAULT (GETDATE()),
        FechaHoraModificacion datetime NULL,
        CONSTRAINT PK_COT_COTIZACION PRIMARY KEY CLUSTERED (IdCotizacion),
        CONSTRAINT UQ_COT_COTIZACION_Numero UNIQUE NONCLUSTERED (Numero)
    );
END;
GO

IF OBJECT_ID(N'dbo.COT_VERSION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COT_VERSION
    (
        IdVersion bigint IDENTITY(1,1) NOT NULL,
        IdCotizacion bigint NOT NULL,
        NumeroVersion int NOT NULL,
        Fecha date NOT NULL CONSTRAINT DF_COT_VERSION_Fecha DEFAULT (CAST(GETDATE() AS date)),
        FechaVencimiento date NULL,
        EmpresaProspecto nvarchar(200) NULL,
        ContactoNombre nvarchar(150) NULL,
        ContactoEmail nvarchar(150) NULL,
        ContactoTelefono nvarchar(40) NULL,
        DocumentoFiscal nvarchar(30) NULL,
        CodigoMoneda nvarchar(8) NULL,
        Observaciones nvarchar(max) NULL,
        CuerpoPropuesta nvarchar(max) NULL,
        DescuentoGeneralPorcentaje decimal(9,4) NOT NULL CONSTRAINT DF_COT_VERSION_DtoGral DEFAULT (0),
        Subtotal decimal(18,2) NOT NULL CONSTRAINT DF_COT_VERSION_Subtotal DEFAULT (0),
        TotalDescuento decimal(18,2) NOT NULL CONSTRAINT DF_COT_VERSION_TotalDto DEFAULT (0),
        Total decimal(18,2) NOT NULL CONSTRAINT DF_COT_VERSION_Total DEFAULT (0),
        EstadoVersion nvarchar(20) NOT NULL CONSTRAINT DF_COT_VERSION_Estado DEFAULT (N'BORRADOR'),
        PublicToken nvarchar(64) NULL,
        UsuarioAlta nvarchar(80) NULL,
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_COT_VERSION_FHA DEFAULT (GETDATE()),
        FechaHoraModificacion datetime NULL,
        FechaHoraEnvio datetime NULL,
        CONSTRAINT PK_COT_VERSION PRIMARY KEY CLUSTERED (IdVersion),
        CONSTRAINT CK_COT_VERSION_Total CHECK (Total >= 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.COT_SECCION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COT_SECCION
    (
        IdSeccion bigint IDENTITY(1,1) NOT NULL,
        IdVersion bigint NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_COT_SECCION_Orden DEFAULT (0),
        Titulo nvarchar(150) NOT NULL,
        Descripcion nvarchar(500) NULL,
        MostrarSubtotal bit NOT NULL CONSTRAINT DF_COT_SECCION_MostrarSubtotal DEFAULT (1),
        Activo bit NOT NULL CONSTRAINT DF_COT_SECCION_Activo DEFAULT (1),
        CONSTRAINT PK_COT_SECCION PRIMARY KEY CLUSTERED (IdSeccion)
    );
END;
GO

IF OBJECT_ID(N'dbo.COT_DET', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COT_DET
    (
        IdDetalle bigint IDENTITY(1,1) NOT NULL,
        IdVersion bigint NOT NULL,
        IdSeccion bigint NULL,
        Orden int NOT NULL CONSTRAINT DF_COT_DET_Orden DEFAULT (0),
        Tipo nvarchar(20) NOT NULL CONSTRAINT DF_COT_DET_Tipo DEFAULT (N'LIBRE'),
        CodigoRef nvarchar(50) NULL,
        Descripcion nvarchar(300) NOT NULL,
        Cantidad decimal(18,4) NOT NULL CONSTRAINT DF_COT_DET_Cantidad DEFAULT (1),
        PrecioBase decimal(18,4) NOT NULL CONSTRAINT DF_COT_DET_PrecioBase DEFAULT (0),
        PorcentajeDescuento decimal(9,4) NOT NULL CONSTRAINT DF_COT_DET_PctDto DEFAULT (0),
        PrecioUnitario decimal(18,4) NOT NULL CONSTRAINT DF_COT_DET_PrecioUnitario DEFAULT (0),
        TasaIva decimal(9,4) NOT NULL CONSTRAINT DF_COT_DET_TasaIva DEFAULT (0),
        Subtotal decimal(18,2) NOT NULL CONSTRAINT DF_COT_DET_Subtotal DEFAULT (0),
        ImpactaTotal bit NOT NULL CONSTRAINT DF_COT_DET_ImpactaTotal DEFAULT (1),
        OrigenPrecio nvarchar(20) NULL,
        CONSTRAINT PK_COT_DET PRIMARY KEY CLUSTERED (IdDetalle)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COT_COTIZACION') AND name = N'IX_COT_COTIZACION_Oportunidad')
    CREATE NONCLUSTERED INDEX IX_COT_COTIZACION_Oportunidad ON dbo.COT_COTIZACION (IdOportunidad, Baja);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COT_COTIZACION') AND name = N'IX_COT_COTIZACION_Cliente')
    CREATE NONCLUSTERED INDEX IX_COT_COTIZACION_Cliente ON dbo.COT_COTIZACION (CodigoCliente, Baja);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COT_COTIZACION') AND name = N'IX_COT_COTIZACION_Estado')
    CREATE NONCLUSTERED INDEX IX_COT_COTIZACION_Estado ON dbo.COT_COTIZACION (Estado, Baja, FechaHoraAlta DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COT_VERSION') AND name = N'IX_COT_VERSION_Cotizacion')
    CREATE NONCLUSTERED INDEX IX_COT_VERSION_Cotizacion ON dbo.COT_VERSION (IdCotizacion, NumeroVersion);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COT_VERSION') AND name = N'IX_COT_VERSION_PublicToken')
    CREATE NONCLUSTERED INDEX IX_COT_VERSION_PublicToken ON dbo.COT_VERSION (PublicToken) WHERE PublicToken IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COT_SECCION') AND name = N'IX_COT_SECCION_Version')
    CREATE NONCLUSTERED INDEX IX_COT_SECCION_Version ON dbo.COT_SECCION (IdVersion, Orden);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COT_DET') AND name = N'IX_COT_DET_Version')
    CREATE NONCLUSTERED INDEX IX_COT_DET_Version ON dbo.COT_DET (IdVersion, IdSeccion, Orden);
GO

/*
    Config nueva en TA_CONFIGURACION (clave/valor, respeta la regla del proyecto: nunca
    borrado masivo, siempre por CLAVE, JSON grande va a ValorAux). Se insertan solo si no
    existen -- no se pisa nada si un cliente ya las cargó a mano.
*/
IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COTIZACIONES_PERMITE_DESCUENTO_LINEA')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COTIZACIONES', N'COTIZACIONES_PERMITE_DESCUENTO_LINEA', N'0', N'Habilita el descuento por línea en el módulo Cotizaciones (además del descuento general, siempre disponible).', GETDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COTIZACIONES_ALFA_PRECIO_BASE')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COTIZACIONES', N'COTIZACIONES_ALFA_PRECIO_BASE', N'0', N'Precio base del configurador "Alfa Gestión" dentro de Cotizaciones.', GETDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COTIZACIONES_ALFA_PRECIO_USUARIO')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COTIZACIONES', N'COTIZACIONES_ALFA_PRECIO_USUARIO', N'0', N'Valor por usuario adicional del configurador "Alfa Gestión" dentro de Cotizaciones.', GETDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COTIZACIONES_ALFA_MODULOS')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, ValorAux, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COTIZACIONES', N'COTIZACIONES_ALFA_MODULOS', N'', N'[]', N'Catálogo de módulos de Alfa Gestión disponibles en el configurador (JSON: [{"codigo","nombre"}]). Vacío por defecto -- un cliente que nunca lo carga no ve nada para configurar acá.', GETDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COTIZACIONES_ALFA_PACKS')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, ValorAux, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COTIZACIONES', N'COTIZACIONES_ALFA_PACKS', N'', N'[]', N'Reglas de recomendación de pack de horas de implementación (JSON: [{"maxUsuarios","maxModulos","idTarea"}]), cada idTarea referencia V_TA_Tareas.IdTarea.', GETDATE());
END;
GO
