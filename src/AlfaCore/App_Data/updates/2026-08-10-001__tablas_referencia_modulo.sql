-- Registro de tablas de referencia que administra el modulo Archivos > Tablas: cada fila describe
-- una tabla fisica chica (con forma Codigo/Descripcion/Color opcional) que el editor generico
-- puede administrar sin necesitar una pantalla propia. No es un modulo licenciable -- no se
-- registra como MenuKeyRaiz de ningun modulo, asi que queda disponible para todos los clientes
-- sin costo (ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md).
-- Idempotente -- se puede reintentar sin romper datos.

IF OBJECT_ID(N'dbo.ALFACORE_TABLAS_REFERENCIA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_TABLAS_REFERENCIA
    (
        Clave               nvarchar(50)  NOT NULL,
        NombreVisible       nvarchar(100) NOT NULL,
        TablaFisica         nvarchar(128) NOT NULL,
        ColumnaId           nvarchar(50)  NOT NULL CONSTRAINT DF_ALFACORE_TABLAS_REFERENCIA_ColumnaId DEFAULT ('Id'),
        ColumnaCodigo       nvarchar(50)  NOT NULL CONSTRAINT DF_ALFACORE_TABLAS_REFERENCIA_ColumnaCodigo DEFAULT ('Codigo'),
        ColumnaDescripcion  nvarchar(50)  NOT NULL CONSTRAINT DF_ALFACORE_TABLAS_REFERENCIA_ColumnaDescripcion DEFAULT ('Descripcion'),
        ColumnaColor        nvarchar(50)  NULL,
        Activo              bit           NOT NULL CONSTRAINT DF_ALFACORE_TABLAS_REFERENCIA_Activo DEFAULT (1),
        Orden               int           NOT NULL CONSTRAINT DF_ALFACORE_TABLAS_REFERENCIA_Orden DEFAULT (0),
        ModificadoUtc       datetime      NOT NULL CONSTRAINT DF_ALFACORE_TABLAS_REFERENCIA_ModificadoUtc DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_ALFACORE_TABLAS_REFERENCIA PRIMARY KEY CLUSTERED (Clave ASC)
    );
END;
GO

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_TABLAS_REFERENCIA'))
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TABLAS_REFERENCIA WHERE Clave = N'CLASIFICACIONES')
BEGIN
    INSERT INTO dbo.ALFACORE_TABLAS_REFERENCIA (Clave, NombreVisible, TablaFisica, ColumnaId, ColumnaCodigo, ColumnaDescripcion, ColumnaColor, Orden)
    VALUES (N'CLASIFICACIONES', N'Clasificaciones de clientes', N'TA_CLASIFICACIONES', N'Id', N'Codigo', N'Descripcion', N'Color', 1);
END;
GO
