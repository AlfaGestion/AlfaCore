IF OBJECT_ID(N'dbo.IA_Compras_CAB', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.IA_Compras_CAB', 'SolicitarCancelacion') IS NULL
    BEGIN
        ALTER TABLE dbo.IA_Compras_CAB
        ADD SolicitarCancelacion bit NOT NULL
            CONSTRAINT DF_IA_Compras_CAB_SolicitarCancelacion DEFAULT (0);
    END;

    IF COL_LENGTH('dbo.IA_Compras_CAB', 'FechaHora_Inicio') IS NULL
    BEGIN
        ALTER TABLE dbo.IA_Compras_CAB
        ADD FechaHora_Inicio datetime NULL;
    END;

    IF COL_LENGTH('dbo.IA_Compras_CAB', 'FechaHora_Fin') IS NULL
    BEGIN
        ALTER TABLE dbo.IA_Compras_CAB
        ADD FechaHora_Fin datetime NULL;
    END;

    IF COL_LENGTH('dbo.IA_Compras_CAB', 'Intentos') IS NULL
    BEGIN
        ALTER TABLE dbo.IA_Compras_CAB
        ADD Intentos int NOT NULL
            CONSTRAINT DF_IA_Compras_CAB_Intentos DEFAULT (0);
    END;
END;
GO

IF OBJECT_ID(N'dbo.CK_IA_Compras_CAB_Estado', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.IA_Compras_CAB DROP CONSTRAINT CK_IA_Compras_CAB_Estado;
END;
GO

IF OBJECT_ID(N'dbo.IA_Compras_CAB', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.IA_Compras_CAB WITH CHECK ADD CONSTRAINT CK_IA_Compras_CAB_Estado CHECK
    (
        [Estado] IN
        (
            'PENDIENTE',
            'PENDIENTE_LECTURA',
            'PROCESANDO_LECTURA',
            'PROCESADO',
            'APROBADO',
            'RECHAZADO',
            'ERROR_LECTURA',
            'SIN_PROVEEDOR',
            'CANCELADO',
            'ELIMINADO'
        )
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'INTERFACESIACOMPRASWORKERHABILITADO')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES ('INTERFACES', 'InterfacesIaComprasWorkerHabilitado', 'SI', 'Habilita el worker en segundo plano para lectura de compras.', GETDATE(), GETDATE());
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'INTERFACESIACOMPRASWORKERMAXPARALELO')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES ('INTERFACES', 'InterfacesIaComprasWorkerMaxParalelo', '1', 'Cantidad maxima de procesos simultaneos del worker de compras.', GETDATE(), GETDATE());
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'INTERFACESIACOMPRASWORKERINTERVALOSEGUNDOS')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES ('INTERFACES', 'InterfacesIaComprasWorkerIntervaloSegundos', '10', 'Intervalo en segundos entre barridos del worker de compras.', GETDATE(), GETDATE());
END;
GO
