SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO', N'U') IS NULL
    THROW 50000, 'No existe dbo.INT_COMPROBANTE_RECIBIDO en la base activa.', 1;

IF OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO', N'U') IS NULL
    THROW 50000, 'No existe dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO en la base activa.', 1;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.IA_Compras_CAB', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.IA_Compras_CAB
        (
            ID int IDENTITY(1,1) NOT NULL,
            IdComprobanteRecibido bigint NULL,
            IdAdjuntoFuente bigint NULL,
            Estado nvarchar(20) NOT NULL CONSTRAINT DF_IA_Compras_CAB_Estado DEFAULT (N'PENDIENTE'),
            FechaHora_Proceso datetime NOT NULL CONSTRAINT DF_IA_Compras_CAB_FhProceso DEFAULT (GETDATE()),
            FechaHora_Modificacion datetime NULL,
            Usuario_Proceso nvarchar(50) NULL,
            Observaciones_Rev nvarchar(1000) NULL,
            Archivo_RutaOriginal nvarchar(500) NOT NULL,
            Archivo_NombreOriginal nvarchar(260) NOT NULL,
            Archivo_NombreRenombrado nvarchar(260) NULL,
            Proveedor_Nombre nvarchar(150) NULL,
            Proveedor_CUIT nvarchar(13) NULL,
            Proveedor_Domicilio nvarchar(150) NULL,
            Proveedor_CondIVA nvarchar(30) NULL,
            Cuenta_Contable nvarchar(15) NULL,
            Match_Metodo nvarchar(50) NULL,
            TipoComprobante nvarchar(50) NULL,
            Letra nvarchar(1) NULL,
            PuntoVenta nvarchar(4) NULL,
            Numero nvarchar(8) NULL,
            Fecha datetime NULL,
            Vencimiento datetime NULL,
            CAE nvarchar(14) NULL,
            VtoCAE datetime NULL,
            Moneda nvarchar(10) NULL,
            NetoGravado money NULL,
            NetoNoGravado money NULL,
            Exento money NULL,
            IVA_21 money NULL,
            IVA_105 money NULL,
            IVA_27 money NULL,
            Percepcion_IVA money NULL,
            Percepcion_IIBB money NULL,
            Percepcion_Ganancias money NULL,
            ImpuestosInternos money NULL,
            OtrosImpuestos money NULL,
            Total money NULL,
            Lector_Observaciones nvarchar(1000) NULL,
            Lector_Error nvarchar(1000) NULL,
            JsonResultado nvarchar(max) NULL,
            CONSTRAINT PK_IA_Compras_CAB PRIMARY KEY CLUSTERED (ID ASC),
            CONSTRAINT CK_IA_Compras_CAB_Estado CHECK
            (
                Estado IN (N'PENDIENTE', N'PROCESADO', N'APROBADO', N'RECHAZADO', N'ERROR_LECTURA', N'SIN_PROVEEDOR', N'ELIMINADO')
            )
        );
    END;

    IF OBJECT_ID(N'dbo.IA_Compras_DET', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.IA_Compras_DET
        (
            ID int IDENTITY(1,1) NOT NULL,
            ID_CAB int NOT NULL,
            NroRenglon int NOT NULL,
            Cantidad nvarchar(20) NULL,
            Codigo_Articulo nvarchar(50) NULL,
            Descripcion nvarchar(200) NULL,
            UD nvarchar(10) NULL,
            Importe_Lista money NULL,
            Dto1 float NULL,
            Dto2 float NULL,
            Importe_Neto money NULL,
            IVA float NULL,
            ImpuestosInternos money NULL,
            Total money NULL,
            AuxNroLote nvarchar(50) NULL,
            AuxNroSerie nvarchar(50) NULL,
            BlPq nvarchar(20) NULL,
            Moneda nvarchar(10) NULL,
            TotImpInt money NULL,
            CONSTRAINT PK_IA_Compras_DET PRIMARY KEY CLUSTERED (ID ASC)
        );
    END;

    IF COL_LENGTH('dbo.IA_Compras_CAB', 'IdComprobanteRecibido') IS NULL
        ALTER TABLE dbo.IA_Compras_CAB ADD IdComprobanteRecibido bigint NULL;

    IF COL_LENGTH('dbo.IA_Compras_CAB', 'IdAdjuntoFuente') IS NULL
        ALTER TABLE dbo.IA_Compras_CAB ADD IdAdjuntoFuente bigint NULL;

    IF COL_LENGTH('dbo.IA_Compras_CAB', 'JsonResultado') IS NULL
        ALTER TABLE dbo.IA_Compras_CAB ADD JsonResultado nvarchar(max) NULL;

    ALTER TABLE dbo.IA_Compras_CAB ALTER COLUMN Observaciones_Rev nvarchar(1000) NULL;
    ALTER TABLE dbo.IA_Compras_CAB ALTER COLUMN Proveedor_Nombre nvarchar(150) NULL;
    ALTER TABLE dbo.IA_Compras_CAB ALTER COLUMN Proveedor_Domicilio nvarchar(150) NULL;
    ALTER TABLE dbo.IA_Compras_CAB ALTER COLUMN Proveedor_CondIVA nvarchar(30) NULL;
    ALTER TABLE dbo.IA_Compras_CAB ALTER COLUMN Match_Metodo nvarchar(50) NULL;
    ALTER TABLE dbo.IA_Compras_CAB ALTER COLUMN Moneda nvarchar(10) NULL;
    ALTER TABLE dbo.IA_Compras_CAB ALTER COLUMN Lector_Observaciones nvarchar(1000) NULL;
    ALTER TABLE dbo.IA_Compras_CAB ALTER COLUMN Lector_Error nvarchar(1000) NULL;

    IF COL_LENGTH('dbo.IA_Compras_DET', 'BlPq') IS NULL
        ALTER TABLE dbo.IA_Compras_DET ADD BlPq nvarchar(20) NULL;

    IF COL_LENGTH('dbo.IA_Compras_DET', 'Moneda') IS NULL
        ALTER TABLE dbo.IA_Compras_DET ADD Moneda nvarchar(10) NULL;

    IF COL_LENGTH('dbo.IA_Compras_DET', 'TotImpInt') IS NULL
        ALTER TABLE dbo.IA_Compras_DET ADD TotImpInt money NULL;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_IA_Compras_DET_CAB'
          AND parent_object_id = OBJECT_ID(N'dbo.IA_Compras_DET')
    )
    BEGIN
        ALTER TABLE dbo.IA_Compras_DET WITH CHECK
        ADD CONSTRAINT FK_IA_Compras_DET_CAB
            FOREIGN KEY (ID_CAB) REFERENCES dbo.IA_Compras_CAB (ID)
            ON DELETE CASCADE;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_IA_Compras_CAB_INT_COMPROBANTE'
          AND parent_object_id = OBJECT_ID(N'dbo.IA_Compras_CAB')
    )
    BEGIN
        ALTER TABLE dbo.IA_Compras_CAB WITH CHECK
        ADD CONSTRAINT FK_IA_Compras_CAB_INT_COMPROBANTE
            FOREIGN KEY (IdComprobanteRecibido) REFERENCES dbo.INT_COMPROBANTE_RECIBIDO (IdComprobanteRecibido);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_IA_Compras_CAB_INT_ADJUNTO'
          AND parent_object_id = OBJECT_ID(N'dbo.IA_Compras_CAB')
    )
    BEGIN
        ALTER TABLE dbo.IA_Compras_CAB WITH CHECK
        ADD CONSTRAINT FK_IA_Compras_CAB_INT_ADJUNTO
            FOREIGN KEY (IdAdjuntoFuente) REFERENCES dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO (IdAdjunto);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = N'CK_IA_Compras_CAB_Estado'
          AND parent_object_id = OBJECT_ID(N'dbo.IA_Compras_CAB')
    )
    BEGIN
        ALTER TABLE dbo.IA_Compras_CAB WITH CHECK
        ADD CONSTRAINT CK_IA_Compras_CAB_Estado CHECK
        (
            Estado IN (N'PENDIENTE', N'PROCESADO', N'APROBADO', N'RECHAZADO', N'ERROR_LECTURA', N'SIN_PROVEEDOR', N'ELIMINADO')
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_IA_Compras_CAB_IdComprobanteRecibido'
          AND object_id = OBJECT_ID(N'dbo.IA_Compras_CAB')
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_IA_Compras_CAB_IdComprobanteRecibido
            ON dbo.IA_Compras_CAB (IdComprobanteRecibido, FechaHora_Proceso DESC, ID DESC);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_IA_Compras_DET_ID_CAB'
          AND object_id = OBJECT_ID(N'dbo.IA_Compras_DET')
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_IA_Compras_DET_ID_CAB
            ON dbo.IA_Compras_DET (ID_CAB, NroRenglon);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESIACOMPRASHABILITADO')
    BEGIN
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'INTERFACES', N'InterfacesIaComprasHabilitado', N'SI', N'Activa lector IA compras', GETDATE());
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESIACOMPRASPYTHONEXE')
    BEGIN
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'INTERFACES', N'InterfacesIaComprasPythonExe', N'C:\dev\IA_ProcesarDocumentos\.venv\Scripts\python.exe', N'Python lector compras', GETDATE());
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESIACOMPRASSCRIPTPATH')
    BEGIN
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'INTERFACES', N'InterfacesIaComprasScriptPath', N'C:\dev\IA_ProcesarDocumentos\lector_facturas_to_json_v5.py', N'Script lector compras', GETDATE());
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'INTERFACESIACOMPRASWORKDIR')
    BEGIN
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'INTERFACES', N'InterfacesIaComprasWorkDir', N'C:\dev\IA_ProcesarDocumentos', N'Carpeta lector compras', GETDATE());
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRAN;

    THROW;
END CATCH;
