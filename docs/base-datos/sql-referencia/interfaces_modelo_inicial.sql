/*
Etapa 1 - modelo inicial para Interfaces / Recepcion documental

Objetivo:
- registrar ingresos documentales independientes del circuito operativo final
- permitir uno o varios archivos por comprobante recibido
- conservar auditoria funcional de alta, cambios de estado y anulacion
- leer el destino de recepcion desde TA_CONFIGURACION con claves propias de Interfaces

Notas:
- el modulo nace desacoplado de compras/contabilidad para reducir riesgo inicial
- los estados y tipos se resuelven con tablas propias del dominio
- las configuraciones parametrizables se apoyan en TA_CONFIGURACION
- los usuarios y PCs se guardan como texto para no acoplar la recepcion a una FK rigida
*/

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.INT_ESTADO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.INT_ESTADO
    (
        IdEstado int IDENTITY(1,1) NOT NULL,
        Codigo nvarchar(30) NOT NULL,
        Descripcion nvarchar(100) NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_INT_ESTADO_Orden DEFAULT (0),
        Activo bit NOT NULL CONSTRAINT DF_INT_ESTADO_Activo DEFAULT (1),
        PermiteEdicion bit NOT NULL CONSTRAINT DF_INT_ESTADO_PermiteEdicion DEFAULT (0),
        EsInicial bit NOT NULL CONSTRAINT DF_INT_ESTADO_EsInicial DEFAULT (0),
        EsFinal bit NOT NULL CONSTRAINT DF_INT_ESTADO_EsFinal DEFAULT (0),
        Color nvarchar(20) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_INT_ESTADO_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_INT_ESTADO PRIMARY KEY CLUSTERED (IdEstado),
        CONSTRAINT UQ_INT_ESTADO_Codigo UNIQUE (Codigo)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.INT_ESTADO WHERE Codigo = N'A_PROCESAR')
    INSERT INTO dbo.INT_ESTADO (Codigo, Descripcion, Orden, PermiteEdicion, EsInicial, EsFinal, Color)
    VALUES (N'A_PROCESAR', N'A procesar', 10, 1, 1, 0, N'warning');
IF NOT EXISTS (SELECT 1 FROM dbo.INT_ESTADO WHERE Codigo = N'EN_PROCESO')
    INSERT INTO dbo.INT_ESTADO (Codigo, Descripcion, Orden, PermiteEdicion, EsInicial, EsFinal, Color)
    VALUES (N'EN_PROCESO', N'En proceso', 20, 0, 0, 0, N'info');
IF NOT EXISTS (SELECT 1 FROM dbo.INT_ESTADO WHERE Codigo = N'PROCESADO')
    INSERT INTO dbo.INT_ESTADO (Codigo, Descripcion, Orden, PermiteEdicion, EsInicial, EsFinal, Color)
    VALUES (N'PROCESADO', N'Procesado', 30, 0, 0, 0, N'primary');
IF NOT EXISTS (SELECT 1 FROM dbo.INT_ESTADO WHERE Codigo = N'APROBADO')
    INSERT INTO dbo.INT_ESTADO (Codigo, Descripcion, Orden, PermiteEdicion, EsInicial, EsFinal, Color)
    VALUES (N'APROBADO', N'Aprobado', 40, 0, 0, 1, N'success');
IF NOT EXISTS (SELECT 1 FROM dbo.INT_ESTADO WHERE Codigo = N'RECHAZADO')
    INSERT INTO dbo.INT_ESTADO (Codigo, Descripcion, Orden, PermiteEdicion, EsInicial, EsFinal, Color)
    VALUES (N'RECHAZADO', N'Rechazado', 50, 0, 0, 1, N'danger');
IF NOT EXISTS (SELECT 1 FROM dbo.INT_ESTADO WHERE Codigo = N'ANULADO')
    INSERT INTO dbo.INT_ESTADO (Codigo, Descripcion, Orden, PermiteEdicion, EsInicial, EsFinal, Color)
    VALUES (N'ANULADO', N'Anulado', 60, 0, 0, 1, N'secondary');
GO

IF OBJECT_ID(N'dbo.INT_TIPO_DOCUMENTO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.INT_TIPO_DOCUMENTO
    (
        IdTipoDocumento int IDENTITY(1,1) NOT NULL,
        Codigo nvarchar(40) NOT NULL,
        Descripcion nvarchar(100) NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_INT_TIPO_DOCUMENTO_Orden DEFAULT (0),
        Activo bit NOT NULL CONSTRAINT DF_INT_TIPO_DOCUMENTO_Activo DEFAULT (1),
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_INT_TIPO_DOCUMENTO_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_INT_TIPO_DOCUMENTO PRIMARY KEY CLUSTERED (IdTipoDocumento),
        CONSTRAINT UQ_INT_TIPO_DOCUMENTO_Codigo UNIQUE (Codigo)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo = N'COMPROBANTE_COMPRA')
    INSERT INTO dbo.INT_TIPO_DOCUMENTO (Codigo, Descripcion, Orden)
    VALUES (N'COMPROBANTE_COMPRA', N'Comprobantes de compras', 10);
IF NOT EXISTS (SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo = N'LIQUIDACION_BANCARIA')
    INSERT INTO dbo.INT_TIPO_DOCUMENTO (Codigo, Descripcion, Orden)
    VALUES (N'LIQUIDACION_BANCARIA', N'Liquidaciones bancarias', 20);
IF NOT EXISTS (SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo = N'LIQUIDACION_TARJETA')
    INSERT INTO dbo.INT_TIPO_DOCUMENTO (Codigo, Descripcion, Orden)
    VALUES (N'LIQUIDACION_TARJETA', N'Liquidaciones de tarjetas', 30);
IF NOT EXISTS (SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo = N'LISTA_PRECIOS')
    INSERT INTO dbo.INT_TIPO_DOCUMENTO (Codigo, Descripcion, Orden)
    VALUES (N'LISTA_PRECIOS', N'Listas de precios', 40);
IF NOT EXISTS (SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo = N'MOVIMIENTO_STOCK')
    INSERT INTO dbo.INT_TIPO_DOCUMENTO (Codigo, Descripcion, Orden)
    VALUES (N'MOVIMIENTO_STOCK', N'Movimientos de stock', 50);
IF NOT EXISTS (SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo = N'GASTO')
    INSERT INTO dbo.INT_TIPO_DOCUMENTO (Codigo, Descripcion, Orden)
    VALUES (N'GASTO', N'Gastos', 60);
IF NOT EXISTS (SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo = N'OTROS')
    INSERT INTO dbo.INT_TIPO_DOCUMENTO (Codigo, Descripcion, Orden)
    VALUES (N'OTROS', N'Otros', 70);
GO

IF OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.INT_COMPROBANTE_RECIBIDO
    (
        IdComprobanteRecibido bigint IDENTITY(1,1) NOT NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        FechaHoraEstado datetime NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_FhEstado DEFAULT (GETDATE()),
        UsuarioAlta nvarchar(50) NOT NULL,
        PcAlta nvarchar(100) NOT NULL,
        UsuarioModificacion nvarchar(50) NULL,
        PcModificacion nvarchar(100) NULL,
        UsuarioAnulacion nvarchar(50) NULL,
        PcAnulacion nvarchar(100) NULL,
        FechaHoraAnulacion datetime NULL,
        IdEstado int NOT NULL,
        IdTipoDocumento int NOT NULL,
        Observacion nvarchar(1000) NULL,
        MotivoAnulacion nvarchar(500) NULL,
        CantidadAdjuntos int NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_CantidadAdjuntos DEFAULT (0),
        RutaBase nvarchar(500) NULL,
        ReferenciaExterna nvarchar(100) NULL,
        Eliminado bit NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_Eliminado DEFAULT (0),
        CONSTRAINT PK_INT_COMPROBANTE_RECIBIDO PRIMARY KEY CLUSTERED (IdComprobanteRecibido),
        CONSTRAINT FK_INT_COMPROBANTE_RECIBIDO_ESTADO FOREIGN KEY (IdEstado)
            REFERENCES dbo.INT_ESTADO (IdEstado),
        CONSTRAINT FK_INT_COMPROBANTE_RECIBIDO_TIPO FOREIGN KEY (IdTipoDocumento)
            REFERENCES dbo.INT_TIPO_DOCUMENTO (IdTipoDocumento),
        CONSTRAINT CK_INT_COMPROBANTE_RECIBIDO_Anulacion CHECK
        (
            (Eliminado = 0 AND FechaHoraAnulacion IS NULL)
            OR
            (Eliminado = 1 AND FechaHoraAnulacion IS NOT NULL)
        )
    );
END;
GO

IF OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO
    (
        IdAdjunto bigint IDENTITY(1,1) NOT NULL,
        IdComprobanteRecibido bigint NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_ADJ_Orden DEFAULT (1),
        NombreOriginal nvarchar(255) NOT NULL,
        NombreGuardado nvarchar(255) NOT NULL,
        RutaRelativa nvarchar(500) NOT NULL,
        Extension nvarchar(20) NULL,
        MimeType nvarchar(100) NULL,
        TamanoBytes bigint NOT NULL,
        HashArchivo nvarchar(128) NULL,
        EsPrincipal bit NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_ADJ_EsPrincipal DEFAULT (0),
        Eliminado bit NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_ADJ_Eliminado DEFAULT (0),
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_ADJ_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        UsuarioAlta nvarchar(50) NOT NULL,
        PcAlta nvarchar(100) NOT NULL,
        UsuarioModificacion nvarchar(50) NULL,
        PcModificacion nvarchar(100) NULL,
        CONSTRAINT PK_INT_COMPROBANTE_RECIBIDO_ADJUNTO PRIMARY KEY CLUSTERED (IdAdjunto),
        CONSTRAINT FK_INT_COMPROBANTE_RECIBIDO_ADJ_COMP FOREIGN KEY (IdComprobanteRecibido)
            REFERENCES dbo.INT_COMPROBANTE_RECIBIDO (IdComprobanteRecibido),
        CONSTRAINT CK_INT_COMPROBANTE_RECIBIDO_ADJ_Tamano CHECK (TamanoBytes > 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO_HIST', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.INT_COMPROBANTE_RECIBIDO_HIST
    (
        IdHistorial bigint IDENTITY(1,1) NOT NULL,
        IdComprobanteRecibido bigint NOT NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_INT_COMPROBANTE_RECIBIDO_HIST_FechaHora DEFAULT (GETDATE()),
        Usuario nvarchar(50) NOT NULL,
        Pc nvarchar(100) NOT NULL,
        Accion nvarchar(30) NOT NULL,
        IdEstadoAnterior int NULL,
        IdEstadoNuevo int NULL,
        Observacion nvarchar(1000) NULL,
        DataJson nvarchar(max) NULL,
        CONSTRAINT PK_INT_COMPROBANTE_RECIBIDO_HIST PRIMARY KEY CLUSTERED (IdHistorial),
        CONSTRAINT FK_INT_COMPROBANTE_RECIBIDO_HIST_COMP FOREIGN KEY (IdComprobanteRecibido)
            REFERENCES dbo.INT_COMPROBANTE_RECIBIDO (IdComprobanteRecibido),
        CONSTRAINT FK_INT_COMPROBANTE_RECIBIDO_HIST_EST_ANT FOREIGN KEY (IdEstadoAnterior)
            REFERENCES dbo.INT_ESTADO (IdEstado),
        CONSTRAINT FK_INT_COMPROBANTE_RECIBIDO_HIST_EST_NUE FOREIGN KEY (IdEstadoNuevo)
            REFERENCES dbo.INT_ESTADO (IdEstado),
        CONSTRAINT CK_INT_COMPROBANTE_RECIBIDO_HIST_Accion CHECK
        (
            Accion IN (N'ALTA', N'MODIFICACION', N'CAMBIO_ESTADO', N'ANULACION', N'ADJUNTO_ALTA', N'ADJUNTO_BAJA')
        )
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESRECEPCIONTIPO'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesRecepcionTipo',
        N'FTP',
        N'Tipo de destino de recepcion documental: FTP o CARPETA',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESRECEPCIONNOMBRE'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesRecepcionNombre',
        N'Recepción principal',
        N'Nombre descriptivo del destino de recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESRECEPCIONRUTA'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesRecepcionRuta',
        N'/',
        N'Ruta compartida o carpeta remota del destino de recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESFTPHOST'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesFtpHost',
        N'alfanet.ddns.net',
        N'Host FTP del destino de recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESFTPPUERTO'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesFtpPuerto',
        N'21',
        N'Puerto FTP del destino de recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESFTPUSUARIO'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesFtpUsuario',
        N'ftpalfa',
        N'Usuario FTP del destino de recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESFTPCLAVE'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesFtpClave',
        N'24681012',
        N'Clave FTP del destino de recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESFTPPASIVO'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesFtpPasivo',
        N'SI',
        N'Indica si el destino FTP usa modo pasivo',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESESTADOINICIAL'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesEstadoInicial',
        N'A_PROCESAR',
        N'Codigo de estado inicial para recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESTAMANOMAXIMOMB'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesTamanoMaximoMb',
        N'25',
        N'Tamano maximo por archivo para recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESEXTENSIONESPERMITIDAS'
)
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion
    )
    VALUES
    (
        N'INTERFACES',
        N'InterfacesExtensionesPermitidas',
        N'.pdf,.jpg,.jpeg,.png,.xls,.xlsx,.csv,.txt',
        N'Extensiones permitidas para recepcion documental',
        GETDATE()
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INT_COMPROBANTE_RECIBIDO_Estado_Fecha' AND object_id = OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO'))
    CREATE NONCLUSTERED INDEX IX_INT_COMPROBANTE_RECIBIDO_Estado_Fecha
        ON dbo.INT_COMPROBANTE_RECIBIDO (IdEstado, FechaHora_Grabacion DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INT_COMPROBANTE_RECIBIDO_Tipo_Fecha' AND object_id = OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO'))
    CREATE NONCLUSTERED INDEX IX_INT_COMPROBANTE_RECIBIDO_Tipo_Fecha
        ON dbo.INT_COMPROBANTE_RECIBIDO (IdTipoDocumento, FechaHora_Grabacion DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INT_COMPROBANTE_RECIBIDO_UsuarioAlta_Fecha' AND object_id = OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO'))
    CREATE NONCLUSTERED INDEX IX_INT_COMPROBANTE_RECIBIDO_UsuarioAlta_Fecha
        ON dbo.INT_COMPROBANTE_RECIBIDO (UsuarioAlta, FechaHora_Grabacion DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INT_COMPROBANTE_RECIBIDO_Eliminado_Fecha' AND object_id = OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO'))
    CREATE NONCLUSTERED INDEX IX_INT_COMPROBANTE_RECIBIDO_Eliminado_Fecha
        ON dbo.INT_COMPROBANTE_RECIBIDO (Eliminado, FechaHora_Grabacion DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INT_COMPROBANTE_RECIBIDO_ADJ_Comp_Orden' AND object_id = OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO'))
    CREATE NONCLUSTERED INDEX IX_INT_COMPROBANTE_RECIBIDO_ADJ_Comp_Orden
        ON dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO (IdComprobanteRecibido, Orden, IdAdjunto);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INT_COMPROBANTE_RECIBIDO_HIST_Comp_Fecha' AND object_id = OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO_HIST'))
    CREATE NONCLUSTERED INDEX IX_INT_COMPROBANTE_RECIBIDO_HIST_Comp_Fecha
        ON dbo.INT_COMPROBANTE_RECIBIDO_HIST (IdComprobanteRecibido, FechaHora DESC, IdHistorial DESC);
GO
