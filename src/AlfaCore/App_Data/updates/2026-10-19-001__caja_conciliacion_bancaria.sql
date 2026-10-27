SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.BK_ImportacionExtracto',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_ImportacionExtracto
(
    ImportacionId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OrigenCodigo VARCHAR(30) NOT NULL,
    ArchivoNombre NVARCHAR(260) NOT NULL,
    ArchivoRuta NVARCHAR(500) NOT NULL,
    HashArchivo VARCHAR(128) NULL,
    FechaImportacionUtc DATETIME2(0) NOT NULL,
    UsuarioImportacion NVARCHAR(100) NOT NULL,
    CantidadMovimientos INT NOT NULL CONSTRAINT DF_BK_ImportacionExtracto_Cantidad DEFAULT (0),
    ImporteTotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_BK_ImportacionExtracto_Importe DEFAULT (0),
    EstadoProceso VARCHAR(30) NOT NULL,
    Observaciones NVARCHAR(500) NULL
);
END;
GO

IF OBJECT_ID(N'dbo.BK_ExtractoMovimiento',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_ExtractoMovimiento
(
    MovimientoId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ImportacionId BIGINT NOT NULL,
    OrigenCodigo VARCHAR(30) NOT NULL,
    FechaMovimiento DATE NOT NULL,
    FechaValor DATE NULL,
    ReferenciaExterna NVARCHAR(120) NULL,
    DescripcionOriginal NVARCHAR(500) NOT NULL,
    Importe DECIMAL(18,2) NOT NULL,
    Saldo DECIMAL(18,2) NULL,
    MonedaCodigo VARCHAR(10) NOT NULL CONSTRAINT DF_BK_ExtractoMovimiento_Moneda DEFAULT ('ARS'),
    ContraparteNombre NVARCHAR(200) NULL,
    ContraparteCuit VARCHAR(20) NULL,
    JsonOrigen NVARCHAR(MAX) NULL,
    EstadoRevision VARCHAR(30) NOT NULL CONSTRAINT DF_BK_ExtractoMovimiento_Revision DEFAULT ('Pendiente'),
    FechaCreacionUtc DATETIME2(0) NOT NULL CONSTRAINT DF_BK_ExtractoMovimiento_Creacion DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_BK_ExtractoMovimiento_Importacion FOREIGN KEY (ImportacionId) REFERENCES dbo.BK_ImportacionExtracto(ImportacionId)
);
END;
GO

IF OBJECT_ID(N'dbo.BK_MovimientoClasificado',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_MovimientoClasificado
(
    ClasificacionId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    MovimientoId BIGINT NOT NULL,
    TipoMovimiento VARCHAR(30) NOT NULL,
    Confianza DECIMAL(5,2) NOT NULL,
    ReglaCodigo VARCHAR(50) NULL,
    ClienteProbableCodigo VARCHAR(30) NULL,
    ClienteProbableNombre NVARCHAR(200) NULL,
    Motivo NVARCHAR(1000) NULL,
    FechaClasificacionUtc DATETIME2(0) NOT NULL CONSTRAINT DF_BK_MovimientoClasificado_Fecha DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_BK_MovimientoClasificado_Movimiento FOREIGN KEY (MovimientoId) REFERENCES dbo.BK_ExtractoMovimiento(MovimientoId)
);
END;
GO

IF OBJECT_ID(N'dbo.BK_MovimientoMatch',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_MovimientoMatch
(
    MatchId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    MovimientoId BIGINT NOT NULL,
    TipoMatch VARCHAR(30) NOT NULL,
    ClienteCodigo VARCHAR(30) NULL,
    ClienteNombre NVARCHAR(200) NULL,
    Confianza DECIMAL(5,2) NOT NULL,
    ResumenPropuesta NVARCHAR(1000) NOT NULL,
    EvidenciaJson NVARCHAR(MAX) NULL,
    Observaciones NVARCHAR(1000) NULL,
    EstadoPropuesta VARCHAR(30) NOT NULL CONSTRAINT DF_BK_MovimientoMatch_Estado DEFAULT ('PENDING'),
    FechaGeneracionUtc DATETIME2(0) NOT NULL CONSTRAINT DF_BK_MovimientoMatch_Fecha DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_BK_MovimientoMatch_Movimiento FOREIGN KEY (MovimientoId) REFERENCES dbo.BK_ExtractoMovimiento(MovimientoId)
);
END;
GO

IF OBJECT_ID(N'dbo.BK_MovimientoAplicacion',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_MovimientoAplicacion
(
    AplicacionId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    MatchId BIGINT NOT NULL,
    TipoComprobante VARCHAR(10) NULL,
    Letra VARCHAR(5) NULL,
    CentroEmisor INT NULL,
    NumeroComprobante BIGINT NULL,
    Cuota INT NULL,
    ImporteAplicado DECIMAL(18,2) NOT NULL,
    ImportePendienteComprobante DECIMAL(18,2) NULL,
    EsParcial BIT NOT NULL CONSTRAINT DF_BK_MovimientoAplicacion_Parcial DEFAULT (0),
    OrigenVista VARCHAR(80) NOT NULL CONSTRAINT DF_BK_MovimientoAplicacion_Vista DEFAULT ('VE_CPTES_SALDOS_VENTAS'),
    CONSTRAINT FK_BK_MovimientoAplicacion_Match FOREIGN KEY (MatchId) REFERENCES dbo.BK_MovimientoMatch(MatchId)
);
END;
GO

IF OBJECT_ID(N'dbo.BK_ReglaInterpretacion',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_ReglaInterpretacion
(
    ReglaId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ReglaCodigo VARCHAR(50) NOT NULL,
    Descripcion NVARCHAR(250) NOT NULL,
    OrigenCodigo VARCHAR(30) NULL,
    PatronTexto NVARCHAR(200) NULL,
    ImporteSigno SMALLINT NULL,
    CategoriaObjetivo VARCHAR(30) NOT NULL,
    ClienteFijoCodigo VARCHAR(30) NULL,
    Prioridad INT NOT NULL CONSTRAINT DF_BK_ReglaInterpretacion_Prioridad DEFAULT (100),
    Activa BIT NOT NULL CONSTRAINT DF_BK_ReglaInterpretacion_Activa DEFAULT (1)
);
END;
GO

IF OBJECT_ID(N'dbo.BK_AliasClienteBanco',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_AliasClienteBanco
(
    AliasId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ClienteCodigo VARCHAR(30) NOT NULL,
    AliasTexto NVARCHAR(200) NOT NULL,
    Cuit VARCHAR(20) NULL,
    CbuCvu VARCHAR(32) NULL,
    Activo BIT NOT NULL CONSTRAINT DF_BK_AliasClienteBanco_Activo DEFAULT (1),
    FechaAltaUtc DATETIME2(0) NOT NULL CONSTRAINT DF_BK_AliasClienteBanco_Fecha DEFAULT (SYSUTCDATETIME())
);
END;
GO

IF OBJECT_ID(N'dbo.BK_GeneracionAlfaLog',N'U') IS NULL
BEGIN
CREATE TABLE dbo.BK_GeneracionAlfaLog
(
    LogId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    MovimientoId BIGINT NULL,
    MatchId BIGINT NULL,
    Accion VARCHAR(50) NOT NULL,
    Resultado VARCHAR(30) NOT NULL,
    Mensaje NVARCHAR(2000) NULL,
    FechaUtc DATETIME2(0) NOT NULL CONSTRAINT DF_BK_GeneracionAlfaLog_Fecha DEFAULT (SYSUTCDATETIME()),
    Usuario NVARCHAR(100) NULL
);
END;
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BK_ExtractoMovimiento') AND name=N'IX_BK_ExtractoMovimiento_ImportacionId')
CREATE INDEX IX_BK_ExtractoMovimiento_ImportacionId ON dbo.BK_ExtractoMovimiento(ImportacionId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BK_ExtractoMovimiento') AND name=N'IX_BK_ExtractoMovimiento_FechaImporte')
CREATE INDEX IX_BK_ExtractoMovimiento_FechaImporte ON dbo.BK_ExtractoMovimiento(FechaMovimiento, Importe);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BK_MovimientoClasificado') AND name=N'IX_BK_MovimientoClasificado_MovimientoId')
CREATE INDEX IX_BK_MovimientoClasificado_MovimientoId ON dbo.BK_MovimientoClasificado(MovimientoId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BK_MovimientoMatch') AND name=N'IX_BK_MovimientoMatch_MovimientoId')
CREATE INDEX IX_BK_MovimientoMatch_MovimientoId ON dbo.BK_MovimientoMatch(MovimientoId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BK_AliasClienteBanco') AND name=N'IX_BK_AliasClienteBanco_AliasTexto')
CREATE INDEX IX_BK_AliasClienteBanco_AliasTexto ON dbo.BK_AliasClienteBanco(AliasTexto);
GO

IF COL_LENGTH('dbo.BK_ImportacionExtracto','CuentaBanco') IS NULL
    ALTER TABLE dbo.BK_ImportacionExtracto ADD CuentaBanco nvarchar(15) NULL;
GO
IF COL_LENGTH('dbo.BK_ExtractoMovimiento','HuellaWeb') IS NULL
    ALTER TABLE dbo.BK_ExtractoMovimiento ADD HuellaWeb varchar(64) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.BK_ExtractoMovimiento') AND name='IX_BK_HuellaWeb')
    CREATE INDEX IX_BK_HuellaWeb ON dbo.BK_ExtractoMovimiento(HuellaWeb);
GO
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='MP_PAGO_RECIBIDO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('MP_PAGO_RECIBIDO',    'MP: Pago recibido de cliente',       'MercadoPago', 'pago recibido',      1,  'Cobranza',            10);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='MP_TRANSFERENCIA_IN')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('MP_TRANSFERENCIA_IN', 'MP: Transferencia recibida (CVU)',   'MercadoPago', 'transferencia',      1,  'Cobranza',            11);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='MP_RETIRO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('MP_RETIRO',           'MP: Retiro/transferencia a cuenta',  'MercadoPago', 'retiro',             -1, 'TransferenciaInterna',12);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='MP_COMISION')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('MP_COMISION',         'MP: Comisión de Mercado Pago',       'MercadoPago', 'comision',           -1, 'Comision',            13);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='MP_COMISION2')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('MP_COMISION2',        'MP: Tarifa por uso',                 'MercadoPago', 'tarifa',             -1, 'Comision',            14);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='MP_IMPUESTO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('MP_IMPUESTO',         'MP: Impuesto / percepcion',          'MercadoPago', 'percepcion',         -1, 'Impuesto',            15);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='MP_IIBB')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('MP_IIBB',             'MP: Ingresos Brutos',                'MercadoPago', 'ingresos brutos',    -1, 'Impuesto',            16);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='MP_DEVOLUCION')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('MP_DEVOLUCION',       'MP: Devolución / contracargo',       'MercadoPago', 'devolucion',          1, 'Ajuste',              17);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_CTA_DNI_BENEF')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_CTA_DNI_BENEF', 'Provincia: Beneficio / reintegro Cuenta DNI', 'Provincia', 'benef pei-cuenta dni', 1, 'ReintegroPromocion', 18);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_CTA_DNI')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_CTA_DNI',       'Provincia: Movimiento Cuenta DNI',             'Provincia', 'cuenta dni',           1, 'ReintegroPromocion', 19);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_INTERESES')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_INTERESES',     'Provincia: Acreditación de intereses',         'Provincia', 'acredit.intereses',    1, 'IngresoFinanciero',  19);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_INTERESES2')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_INTERESES2',    'Provincia: Intereses acreditados',             'Provincia', 'intereses periodo',    1, 'IngresoFinanciero',  19);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_TRANSF_IN')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_TRANSF_IN',     'Provincia: Transferencia recibida',   'Provincia', 'transferencia acred', 1,  'Cobranza',            20);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_TRANSF_IN2')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_TRANSF_IN2',    'Provincia: Crédito por transferencia','Provincia', 'credito transf',      1,  'Cobranza',            21);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_TRANSF_OUT')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_TRANSF_OUT',    'Provincia: Transferencia emitida',    'Provincia', 'transferencia debi',  -1, 'TransferenciaInterna',22);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_CHEQUE_IN')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_CHEQUE_IN',     'Provincia: Acreditación cheque',      'Provincia', 'cheque acred',        1,  'Cobranza',            23);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_CHEQUE_OUT')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_CHEQUE_OUT',    'Provincia: Débito cheque propio',     'Provincia', 'cheque debi',         -1, 'Gasto',               24);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_COM_MANT')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_COM_MANT',      'Provincia: Comisión mantenimiento',   'Provincia', 'mantenimiento',       -1, 'Comision',            25);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_COM_GRAL')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_COM_GRAL',      'Provincia: Comisión bancaria',        'Provincia', 'comision',            -1, 'Comision',            26);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_IVA_COM')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_IVA_COM',       'Provincia: IVA sobre comisiones',     'Provincia', 'iva comision',        -1, 'Impuesto',            27);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_IIBB')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_IIBB',          'Provincia: Ingresos Brutos',          'Provincia', 'ing brutos',          -1, 'Impuesto',            28);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_SELLADO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_SELLADO',       'Provincia: Sellado / impuesto',       'Provincia', 'sellado',             -1, 'Impuesto',            29);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_DEBITO_AUTO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_DEBITO_AUTO',   'Provincia: Débito automático',        'Provincia', 'debito automatico',   -1, 'Gasto',               30);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='PROV_TRANSF_INT')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('PROV_TRANSF_INT',    'Provincia: Transferencia entre propias','Provincia','transf propia',       -1, 'TransferenciaInterna',31);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_INTERESES')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_INTERESES',      'Santander: Acreditación de intereses', 'Santander', 'intereses',           1, 'IngresoFinanciero',  39);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_TRANSF_IN')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_TRANSF_IN',     'Santander: Transferencia recibida',   'Santander', 'transferencia recib', 1,  'Cobranza',            40);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_TRANSF_IN2')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_TRANSF_IN2',    'Santander: Acreditación CBU',         'Santander', 'acreditacion',        1,  'Cobranza',            41);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_TRANSF_OUT')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_TRANSF_OUT',    'Santander: Transferencia emitida',    'Santander', 'transferencia emitid',-1, 'TransferenciaInterna',42);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_COM_MANT')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_COM_MANT',      'Santander: Comisión mantenimiento',   'Santander', 'mantenimiento',       -1, 'Comision',            43);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_COM_GRAL')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_COM_GRAL',      'Santander: Comisión bancaria',        'Santander', 'comision',            -1, 'Comision',            44);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_IVA_COM')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_IVA_COM',       'Santander: IVA sobre comisiones',     'Santander', 'iva',                 -1, 'Impuesto',            45);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_IIBB')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_IIBB',          'Santander: Ingresos Brutos',          'Santander', 'ing brutos',          -1, 'Impuesto',            46);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_CHEQUE_IN')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_CHEQUE_IN',     'Santander: Acreditación cheque',      'Santander', 'cheque',              1,  'Cobranza',            47);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SAN_DEBITO_AUTO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SAN_DEBITO_AUTO',   'Santander: Débito automático',        'Santander', 'debito automatico',   -1, 'Gasto',               48);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_INTERESES')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_INTERESES',      'Genérico: Acreditación de intereses',             NULL, 'intereses',    1, 'IngresoFinanciero', 57);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_REINTEGRO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_REINTEGRO',     'Genérico: Reintegro / devolución promocional', NULL, 'reintegro',   1, 'ReintegroPromocion', 58);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_PROMO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_PROMO',         'Genérico: Promoción bancaria',                  NULL, 'promo',       1, 'ReintegroPromocion', 59);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_COMISION')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_COMISION',      'Genérico: Comisión',                  NULL, 'comision',      NULL, 'Comision',            60);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_IMPUESTO_IVA')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_IMPUESTO_IVA',  'Genérico: IVA',                       NULL, 'iva',           -1,   'Impuesto',            61);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_IMPUESTO_IIBB')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_IMPUESTO_IIBB', 'Genérico: IIBB',                      NULL, 'ingresos brutos',NULL,'Impuesto',            62);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_IMPUESTO_SELL')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_IMPUESTO_SELL', 'Genérico: Sellado',                   NULL, 'sellado',       -1,   'Impuesto',            63);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_SUELDO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_SUELDO',        'Genérico: Pago de sueldos',           NULL, 'sueldo',        -1,   'Gasto',               64);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_HONORARIOS')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_HONORARIOS',    'Genérico: Honorarios',                NULL, 'honorarios',    -1,   'Gasto',               65);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_TRANSF_INT')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_TRANSF_INT',    'Genérico: Transferencia interna propia',NULL,'transf propia',-1,   'TransferenciaInterna',66);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_AJUSTE')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_AJUSTE',        'Genérico: Ajuste / nota de crédito',  NULL, 'ajuste',        NULL, 'Ajuste',              67);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='GEN_DEVOLUCION')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('GEN_DEVOLUCION',    'Genérico: Devolución',                NULL, 'devolucion',    NULL, 'Ajuste',              68);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SIGN_CREDITO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SIGN_CREDITO', 'Fallback: Crédito sin clasificar → Cobranza', NULL, NULL,  1, 'Cobranza', 90);
IF NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion WHERE ReglaCodigo='SIGN_DEBITO')
INSERT INTO dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad) VALUES ('SIGN_DEBITO',  'Fallback: Débito sin clasificar → Gasto',     NULL, NULL, -1, 'Gasto',    91);

-- Tolerancia heredada del motor inicial, configurable sin modificar el algoritmo.
IF OBJECT_ID('dbo.TA_CONFIGURACION','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-TOLERANCIA')
INSERT INTO dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
VALUES('CAJA','CONCILIACION-TOLERANCIA','1.00',N'Tolerancia de propuestas de conciliación');
