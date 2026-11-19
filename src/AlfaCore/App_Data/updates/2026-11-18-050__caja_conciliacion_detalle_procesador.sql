-- Conciliación bancaria: detalle de operaciones de procesadores de cobro (NAVE, y los que se sumen).
-- Aditiva e idempotente. Sin FK a tablas legacy. Compatible con nivel de compatibilidad 80/100.
IF OBJECT_ID(N'dbo.BK_DetalleProcesador',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_DetalleProcesador
(
    DetalleId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Procesador VARCHAR(30) NOT NULL,
    ArchivoNombre NVARCHAR(260) NOT NULL,
    HashArchivo VARCHAR(128) NOT NULL,
    FechaDesde DATE NULL,
    FechaHasta DATE NULL,
    CantidadOperaciones INT NOT NULL CONSTRAINT DF_BK_DetalleProcesador_Cant DEFAULT (0),
    Bruto DECIMAL(18,2) NOT NULL CONSTRAINT DF_BK_DetalleProcesador_Bruto DEFAULT (0),
    Comision DECIMAL(18,2) NOT NULL CONSTRAINT DF_BK_DetalleProcesador_Com DEFAULT (0),
    IvaComision DECIMAL(18,2) NOT NULL CONSTRAINT DF_BK_DetalleProcesador_Iva DEFAULT (0),
    Neto DECIMAL(18,2) NOT NULL CONSTRAINT DF_BK_DetalleProcesador_Neto DEFAULT (0),
    UsuarioImportacion NVARCHAR(100) NOT NULL,
    FechaImportacionUtc DATETIME2(0) NOT NULL CONSTRAINT DF_BK_DetalleProcesador_Fecha DEFAULT (SYSUTCDATETIME())
);
END;
GO

IF OBJECT_ID(N'dbo.BK_DetalleProcesadorOperacion',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_DetalleProcesadorOperacion
(
    OperacionId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    DetalleId BIGINT NOT NULL,
    Codigo VARCHAR(60) NOT NULL,
    FechaOperacion DATETIME NULL,
    FechaAcreditacion DATE NULL,
    MedioPago NVARCHAR(80) NOT NULL CONSTRAINT DF_BK_DetalleProcesadorOp_Medio DEFAULT (''),
    Estado NVARCHAR(40) NOT NULL CONSTRAINT DF_BK_DetalleProcesadorOp_Estado DEFAULT (''),
    Bruto DECIMAL(18,2) NOT NULL,
    Comision DECIMAL(18,2) NOT NULL,
    IvaComision DECIMAL(18,2) NOT NULL,
    Neto DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_BK_DetalleProcesadorOp_Detalle FOREIGN KEY (DetalleId) REFERENCES dbo.BK_DetalleProcesador(DetalleId)
);
CREATE INDEX IX_BK_DetalleProcesadorOp_Detalle ON dbo.BK_DetalleProcesadorOperacion(DetalleId,Codigo);
END;
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_BK_DetalleProcesador_Hash' AND object_id=OBJECT_ID(N'dbo.BK_DetalleProcesador'))
    CREATE UNIQUE INDEX UX_BK_DetalleProcesador_Hash ON dbo.BK_DetalleProcesador(Procesador,HashArchivo);
GO

-- Cuántos caracteres del código de operación se comparan contra el extracto. Galicia corta el código de NAVE en 10.
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-NAVE-PREFIJO')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-NAVE-PREFIJO',N'10',N'Caracteres del código de operación de NAVE que se comparan con la referencia del extracto bancario (Galicia informa 10)');
-- Texto del extracto que identifica los créditos de NAVE.
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-NAVE-TEXTO')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-NAVE-TEXTO',N'NAVE',N'Texto de la descripción del extracto que identifica los créditos y devoluciones de NAVE');
GO
