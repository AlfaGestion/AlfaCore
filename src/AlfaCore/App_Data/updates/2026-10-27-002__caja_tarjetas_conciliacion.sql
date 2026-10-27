SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.TJ_Importacion','U') IS NULL
CREATE TABLE dbo.TJ_Importacion(
 Id bigint IDENTITY PRIMARY KEY,Nombre nvarchar(255) NOT NULL,Original varbinary(max) NOT NULL,Hash char(64) NOT NULL,
 Banco nvarchar(50) NOT NULL,Comercio nvarchar(50) NOT NULL,Tarjeta nvarchar(50) NOT NULL,Cuenta nvarchar(15) NOT NULL,
 Sucursal nvarchar(10) NOT NULL,Desde date NOT NULL,Hasta date NOT NULL,Bruto decimal(19,2) NOT NULL,Neto decimal(19,2) NOT NULL,
 DetalleCompleto bit NOT NULL,Avisos nvarchar(max) NOT NULL,Usuario nvarchar(50) NOT NULL,FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT UQ_TJ_Importacion UNIQUE(Hash,Cuenta));
IF OBJECT_ID('dbo.TJ_Vinculo','U') IS NULL
CREATE TABLE dbo.TJ_Vinculo(Id bigint IDENTITY PRIMARY KEY,ImportacionId bigint NOT NULL REFERENCES dbo.TJ_Importacion(Id),
 Activo bit NOT NULL DEFAULT 1,ImporteResumen decimal(19,2) NOT NULL,ImporteAlfa decimal(19,2) NOT NULL,
 Motivo nvarchar(500) NOT NULL,Usuario nvarchar(50) NOT NULL,FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UsuarioAnulacion nvarchar(50) NULL,FechaAnulacion datetime2 NULL,MotivoAnulacion nvarchar(500) NULL);
IF OBJECT_ID('dbo.TJ_Detalle','U') IS NULL
BEGIN
 CREATE TABLE dbo.TJ_Detalle(Id bigint IDENTITY PRIMARY KEY,ImportacionId bigint NOT NULL REFERENCES dbo.TJ_Importacion(Id),
 Fecha date NOT NULL,FechaPago date NULL,Liquidacion nvarchar(30) NOT NULL,Lote nvarchar(30) NOT NULL,Cupon nvarchar(30) NOT NULL,
 Terminal nvarchar(30) NOT NULL,Nivel nvarchar(10) NOT NULL,Cantidad int NOT NULL,Importe decimal(19,2) NOT NULL,
 Huella char(64) NOT NULL,CONSTRAINT UQ_TJ_Detalle UNIQUE(Huella));
 CREATE INDEX IX_TJ_Detalle_Importacion ON dbo.TJ_Detalle(ImportacionId,Fecha);
END;
IF OBJECT_ID('dbo.TJ_VinculoDetalle','U') IS NULL
BEGIN
 CREATE TABLE dbo.TJ_VinculoDetalle(VinculoId bigint NOT NULL REFERENCES dbo.TJ_Vinculo(Id),DetalleId bigint NOT NULL REFERENCES dbo.TJ_Detalle(Id),Activo bit NOT NULL,PRIMARY KEY(VinculoId,DetalleId));
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.TJ_VinculoDetalle') AND name='UX_TJ_Detalle_Activo')
 CREATE UNIQUE INDEX UX_TJ_Detalle_Activo ON dbo.TJ_VinculoDetalle(DetalleId) WHERE Activo=1;
IF OBJECT_ID('dbo.TJ_VinculoAlfa','U') IS NULL
BEGIN
 CREATE TABLE dbo.TJ_VinculoAlfa(VinculoId bigint NOT NULL REFERENCES dbo.TJ_Vinculo(Id),Cuenta nvarchar(15) NOT NULL,
 Clave varchar(200) NOT NULL,Huella char(64) NOT NULL,Importe decimal(19,2) NOT NULL,Activo bit NOT NULL,PRIMARY KEY(VinculoId,Clave));
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.TJ_VinculoAlfa') AND name='UX_TJ_Alfa_Activo')
 CREATE UNIQUE INDEX UX_TJ_Alfa_Activo ON dbo.TJ_VinculoAlfa(Cuenta,Clave) WHERE Activo=1;
IF OBJECT_ID('dbo.TA_CONFIGURACION','U') IS NOT NULL
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='TARJETAS-PYTHON')
 INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION) VALUES('CAJA','TARJETAS-PYTHON','python',N'Python del lector de liquidaciones');
 IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='TARJETAS-TOLERANCIA')
 INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION) VALUES('CAJA','TARJETAS-TOLERANCIA','1.00',N'Tolerancia de conciliación de tarjetas');
 IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='TARJETAS-ASOCIACIONES')
 INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION) VALUES('CAJA','TARJETAS-ASOCIACIONES','',N'[]',N'Comercio, tarjeta, cuenta y sucursal');
END;
COMMIT;
