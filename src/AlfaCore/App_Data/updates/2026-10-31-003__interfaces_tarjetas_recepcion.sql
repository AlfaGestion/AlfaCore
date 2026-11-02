SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.INT_TIPO_DOCUMENTO',N'U') IS NULL OR OBJECT_ID(N'dbo.TJ_Analisis',N'U') IS NULL RETURN;
IF NOT EXISTS(SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo=N'LIQUIDACION_TARJETAS')
    INSERT dbo.INT_TIPO_DOCUMENTO(Codigo,Descripcion,Orden,Activo) VALUES(N'LIQUIDACION_TARJETAS',N'Liquidación de tarjetas',25,1);
IF OBJECT_ID(N'dbo.TJ_Recepcion',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TJ_Recepcion(
        Id bigint IDENTITY PRIMARY KEY,
        DocumentoId bigint NOT NULL UNIQUE REFERENCES dbo.INT_COMPROBANTE_RECIBIDO(IdComprobanteRecibido),
        Usuario nvarchar(50) NOT NULL,Sistema nvarchar(50) NOT NULL,
        Nombre nvarchar(255) NOT NULL,Original varbinary(max) NOT NULL,Hash char(64) NOT NULL,
        Cuenta nvarchar(50) NOT NULL DEFAULT N'',Sucursal nvarchar(10) NOT NULL DEFAULT N'',
        Estado nvarchar(20) NOT NULL DEFAULT N'PENDIENTE',Mensaje nvarchar(1000) NOT NULL DEFAULT N'En cola de lectura.',
        Intentos int NOT NULL DEFAULT 0,ProximoUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),ActualizadoUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        Lease uniqueidentifier NULL,HastaUtc datetime2 NULL,ImportacionId bigint NULL REFERENCES dbo.TJ_Importacion(Id),
        AnalisisId bigint NULL REFERENCES dbo.TJ_Analisis(Id),Candidatas nvarchar(max) NULL,
        CONSTRAINT CK_TJ_Recepcion_Estado CHECK(Estado IN(N'PENDIENTE',N'LEYENDO',N'CUENTA',N'ANALIZANDO',N'LISTO',N'ERROR'))
    );
    CREATE INDEX IX_TJ_Recepcion_Cola ON dbo.TJ_Recepcion(Estado,ProximoUtc,Id);
END;
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE=N'TARJETAS-DOCUMENTAL-HABILITADO')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION) VALUES(N'CAJA',N'TARJETAS-DOCUMENTAL-HABILITADO',N'SI',N'Procesar tarjetas desde Recepción documental');
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE=N'TARJETAS-DOCUMENTAL-MINUTOS')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION) VALUES(N'CAJA',N'TARJETAS-DOCUMENTAL-MINUTOS',N'20',N'Tiempo máximo por análisis documental (1 a 120)');
