-- Ejecutar contra ALFANET2007 (base central de ALFA NET). No ejecutar en las bases de clientes.
-- AlfaCore lo aplica solo (MonitorClientesSchema, mismo texto) si las tablas no existen.
--
-- MON_CONTROL_TIPO: qué se controla y con qué límites por defecto (en minutos desde el último OK).
--   Medida EDAD = el estado sale de cuánto hace del último OK; RESULTADO = sale del último resultado.
-- MON_CONTROL_CLIENTE: límites propios de un cliente (NULL = usa el del tipo) o control desactivado.
-- MON_ORIGEN: cada sistema que reporta (cliente + servidor SQL + base), con la hora del último reporte.
-- MON_ESTADO: último dato de cada control de cada origen (una fila por tipo + destino, ej. un PDV).
-- MON_SEGUIMIENTO: quién tomó una alerta, hasta cuándo está silenciada y una nota (fase 2).
-- MON_CONFIG: configuración de los avisos al chat de soporte (clave / valor).
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MON_CONTROL_TIPO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MON_CONTROL_TIPO
    (
        Codigo nvarchar(30) NOT NULL,
        Nombre nvarchar(80) NOT NULL,
        Descripcion nvarchar(300) NULL,
        Medida nvarchar(10) NOT NULL CONSTRAINT DF_MON_CONTROL_TIPO_Medida DEFAULT (N'EDAD'),
        AvisoMinutos int NULL,
        CriticoMinutos int NULL,
        Orden int NOT NULL CONSTRAINT DF_MON_CONTROL_TIPO_Orden DEFAULT (100),
        Activo bit NOT NULL CONSTRAINT DF_MON_CONTROL_TIPO_Activo DEFAULT (1),
        CONSTRAINT PK_MON_CONTROL_TIPO PRIMARY KEY CLUSTERED (Codigo),
        CONSTRAINT CK_MON_CONTROL_TIPO_Medida CHECK (Medida IN (N'EDAD', N'RESULTADO'))
    );
END;

INSERT INTO dbo.MON_CONTROL_TIPO (Codigo, Nombre, Descripcion, Medida, AvisoMinutos, CriticoMinutos, Orden)
SELECT v.Codigo, v.Nombre, v.Descripcion, v.Medida, v.AvisoMinutos, v.CriticoMinutos, v.Orden
FROM (VALUES
    (N'REPORTE', N'Reporte del sistema', N'Hace cuánto que el sistema del cliente no manda datos al monitor (servidor apagado, Timer cerrado, sin internet).', N'EDAD', 60, 180, 10),
    (N'ENVIO_PDV', N'Envío a punto de venta', N'Último envío correcto de artículos y precios a cada punto de venta (NETCPV).', N'EDAD', 120, 180, 20),
    (N'RECEPCION_PDV', N'Recepción de punto de venta', N'Última recepción correcta de movimientos de cada punto de venta (NETCPV).', N'EDAD', 60, 120, 30),
    (N'BACKUP', N'Backup diario', N'Último backup correcto de la base.', N'EDAD', 1560, 2160, 40),
    (N'TAREA', N'Tarea automática', N'Resultado de la última ejecución de cada tarea automática del Timer (ERROR = crítico, ADVERTENCIA = aviso).', N'RESULTADO', NULL, NULL, 50)
) AS v (Codigo, Nombre, Descripcion, Medida, AvisoMinutos, CriticoMinutos, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MON_CONTROL_TIPO t WHERE t.Codigo = v.Codigo);

IF OBJECT_ID(N'dbo.MON_CONTROL_CLIENTE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MON_CONTROL_CLIENTE
    (
        ClienteCodigo nvarchar(15) NOT NULL,
        TipoCodigo nvarchar(30) NOT NULL,
        AvisoMinutos int NULL,
        CriticoMinutos int NULL,
        Desactivado bit NOT NULL CONSTRAINT DF_MON_CONTROL_CLIENTE_Desactivado DEFAULT (0),
        Observacion nvarchar(250) NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_MON_CONTROL_CLIENTE_Fecha DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(80) NULL,
        CONSTRAINT PK_MON_CONTROL_CLIENTE PRIMARY KEY CLUSTERED (ClienteCodigo, TipoCodigo)
    );
END;

IF OBJECT_ID(N'dbo.MON_ORIGEN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MON_ORIGEN
    (
        IdOrigen int IDENTITY(1,1) NOT NULL,
        ClienteCodigo nvarchar(15) NOT NULL,
        LicenciaPrincipal nvarchar(40) NULL,
        DbServidor nvarchar(128) NOT NULL,
        DbNombre nvarchar(128) NOT NULL,
        HostCliente nvarchar(128) NULL,
        VersionSistema nvarchar(50) NULL,
        DesfaseSegundos int NOT NULL CONSTRAINT DF_MON_ORIGEN_Desfase DEFAULT (0),
        PrimerReporte datetime NOT NULL CONSTRAINT DF_MON_ORIGEN_Primer DEFAULT (GETDATE()),
        UltimoReporte datetime NOT NULL CONSTRAINT DF_MON_ORIGEN_Ultimo DEFAULT (GETDATE()),
        Desactivado bit NOT NULL CONSTRAINT DF_MON_ORIGEN_Desactivado DEFAULT (0),
        CONSTRAINT PK_MON_ORIGEN PRIMARY KEY CLUSTERED (IdOrigen),
        CONSTRAINT UQ_MON_ORIGEN UNIQUE (ClienteCodigo, DbServidor, DbNombre)
    );
END;

IF OBJECT_ID(N'dbo.MON_ESTADO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MON_ESTADO
    (
        IdOrigen int NOT NULL,
        TipoCodigo nvarchar(30) NOT NULL,
        Destino nvarchar(120) NOT NULL CONSTRAINT DF_MON_ESTADO_Destino DEFAULT (N''),
        UltimoOk datetime NULL,
        UltimoIntento datetime NULL,
        Resultado nvarchar(15) NULL,
        Mensaje nvarchar(500) NULL,
        FechaReporte datetime NOT NULL CONSTRAINT DF_MON_ESTADO_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_MON_ESTADO PRIMARY KEY CLUSTERED (IdOrigen, TipoCodigo, Destino),
        CONSTRAINT FK_MON_ESTADO_ORIGEN FOREIGN KEY (IdOrigen) REFERENCES dbo.MON_ORIGEN (IdOrigen) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.MON_SEGUIMIENTO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MON_SEGUIMIENTO
    (
        IdOrigen int NOT NULL,
        TipoCodigo nvarchar(30) NOT NULL,
        Destino nvarchar(120) NOT NULL CONSTRAINT DF_MON_SEGUIMIENTO_Destino DEFAULT (N''),
        TomadoPor nvarchar(80) NULL,
        TomadoFecha datetime NULL,
        SilenciadoHasta datetime NULL,
        SilenciadoPor nvarchar(80) NULL,
        Nota nvarchar(500) NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_MON_SEGUIMIENTO_Fecha DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(80) NULL,
        CONSTRAINT PK_MON_SEGUIMIENTO PRIMARY KEY CLUSTERED (IdOrigen, TipoCodigo, Destino),
        CONSTRAINT FK_MON_SEGUIMIENTO_ORIGEN FOREIGN KEY (IdOrigen) REFERENCES dbo.MON_ORIGEN (IdOrigen) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.MON_CONFIG', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MON_CONFIG
    (
        Clave nvarchar(50) NOT NULL,
        Valor nvarchar(1000) NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_MON_CONFIG_Fecha DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(80) NULL,
        CONSTRAINT PK_MON_CONFIG PRIMARY KEY CLUSTERED (Clave)
    );
END;

INSERT INTO dbo.MON_CONFIG (Clave, Valor)
SELECT v.Clave, v.Valor
FROM (VALUES
    (N'AVISO_ACTIVO', N'SI'),
    (N'AVISO_PRIORIDADES', N'1,2'),
    (N'AVISO_REPETIR_MINUTOS', N'120'),
    (N'AVISO_USUARIOS_PUSH', N''),
    (N'AVISO_ID_HILO', N''),
    (N'AVISO_ULTIMO_ENVIO', N''),
    (N'AVISO_ULTIMA_FIRMA', N''),
    (N'AVISO_FORZAR', N'0')
) AS v (Clave, Valor)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MON_CONFIG c WHERE c.Clave = v.Clave);

COMMIT TRANSACTION;
