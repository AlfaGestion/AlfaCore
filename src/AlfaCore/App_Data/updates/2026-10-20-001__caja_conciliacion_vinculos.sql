SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'dbo.BK_ExtractoMovimiento',N'U') IS NULL
    THROW 50001,'Instalá primero la actualización de conciliación bancaria.',1;
GO
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.BK_Conciliacion',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BK_Conciliacion(
        Id bigint IDENTITY PRIMARY KEY, Operacion uniqueidentifier NOT NULL UNIQUE, Huella varchar(64) NOT NULL,
        Cuenta nvarchar(15) NOT NULL, Moneda nvarchar(4) NOT NULL, Importe decimal(19,4) NOT NULL,
        Usuario nvarchar(100) NOT NULL, FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        Activa bit NOT NULL DEFAULT 1, Motivo nvarchar(300) NULL,
        UsuarioAnulacion nvarchar(100) NULL, FechaAnulacionUtc datetime2 NULL);
    CREATE TABLE dbo.BK_ConciliacionBanco(
        Id bigint IDENTITY PRIMARY KEY, ConciliacionId bigint NOT NULL REFERENCES dbo.BK_Conciliacion(Id),
        MovimientoId bigint NOT NULL REFERENCES dbo.BK_ExtractoMovimiento(MovimientoId),
        Fecha date NOT NULL, Importe decimal(19,4) NOT NULL, Detalle nvarchar(500) NOT NULL,
        Huella varchar(64) NOT NULL, Activa bit NOT NULL DEFAULT 1);
    CREATE UNIQUE INDEX UX_BK_ConciliacionBanco_Activa ON dbo.BK_ConciliacionBanco(MovimientoId) WHERE Activa=1;
    CREATE TABLE dbo.BK_ConciliacionAlfa(
        Id bigint IDENTITY PRIMARY KEY, ConciliacionId bigint NOT NULL REFERENCES dbo.BK_Conciliacion(Id),
        Cuenta nvarchar(15) NOT NULL, Periodo nvarchar(6) NOT NULL, Mes tinyint NOT NULL,
        Asiento int NOT NULL, TipoRegistro nvarchar(4) NOT NULL, Secuencia int NOT NULL,
        Fecha datetime NOT NULL, Importe decimal(19,4) NOT NULL, Detalle nvarchar(200) NOT NULL,
        Comprobante nvarchar(30) NOT NULL, Unidad nvarchar(4) NOT NULL,
        Huella varchar(64) NOT NULL, Activa bit NOT NULL DEFAULT 1);
    CREATE UNIQUE INDEX UX_BK_ConciliacionAlfa_Activa ON dbo.BK_ConciliacionAlfa
        (Cuenta,Periodo,Mes,Asiento,TipoRegistro,Secuencia) WHERE Activa=1;
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.BK_ConciliacionBanco') AND name='IX_BK_ConciliacionBanco_Historial')
    CREATE INDEX IX_BK_ConciliacionBanco_Historial ON dbo.BK_ConciliacionBanco(MovimientoId,ConciliacionId);
COMMIT;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-DIAS-BANCO')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-DIAS-BANCO','3',N'Días de margen para sugerencias banco/Alfa');
GO
