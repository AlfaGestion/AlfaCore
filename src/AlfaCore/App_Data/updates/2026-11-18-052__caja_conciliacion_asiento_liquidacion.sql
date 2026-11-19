SET XACT_ABORT ON;
-- Asiento de liquidación de procesadores de cobro (NAVE): un asiento por día de acreditación y extracto.
-- Trazabilidad e idempotencia. Aditiva; sin THROW/OPENJSON/STRING_SPLIT (bases con compatibilidad 80/100).
IF OBJECT_ID(N'dbo.BK_Conciliacion',N'U') IS NULL
    RAISERROR('Instalá primero los vínculos de conciliación.',16,1);
GO
IF OBJECT_ID(N'dbo.BK_AsientoLiquidacion',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BK_AsientoLiquidacion(
        Operacion uniqueidentifier NOT NULL PRIMARY KEY,
        ImportacionId bigint NOT NULL REFERENCES dbo.BK_ImportacionExtracto(ImportacionId),
        DetalleId bigint NOT NULL REFERENCES dbo.BK_DetalleProcesador(DetalleId),
        Fecha date NOT NULL,
        SolicitudHash varchar(64) NOT NULL,
        CuentaBanco nvarchar(15) NOT NULL,
        Periodo nvarchar(6) NOT NULL,Mes tinyint NOT NULL,NumeroAsiento int NOT NULL,
        TipoRegistro nvarchar(4) NOT NULL,Comprobante nvarchar(13) NOT NULL,Tc nvarchar(8) NOT NULL,Unidad nvarchar(4) NOT NULL,
        ConciliacionId bigint NOT NULL REFERENCES dbo.BK_Conciliacion(Id),
        Bruto decimal(19,4) NOT NULL,Comision decimal(19,4) NOT NULL,IvaComision decimal(19,4) NOT NULL,Neto decimal(19,4) NOT NULL,
        Usuario nvarchar(100) NOT NULL,FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
    -- Un día de un extracto se liquida una sola vez.
    CREATE UNIQUE INDEX UX_BK_AsientoLiquidacion_Dia ON dbo.BK_AsientoLiquidacion(ImportacionId,Fecha);
END;
GO
IF OBJECT_ID(N'dbo.BK_AsientoLiquidacionMovimiento',N'U') IS NULL
BEGIN
    -- Un crédito del banco solo puede pertenecer a un asiento de liquidación, aunque luego se desconcilie.
    CREATE TABLE dbo.BK_AsientoLiquidacionMovimiento(
        Operacion uniqueidentifier NOT NULL REFERENCES dbo.BK_AsientoLiquidacion(Operacion),
        MovimientoId bigint NOT NULL UNIQUE REFERENCES dbo.BK_ExtractoMovimiento(MovimientoId),
        Importe decimal(19,4) NOT NULL,
        PRIMARY KEY(Operacion,MovimientoId));
END;
GO
-- Comprobante contable del asiento (CC por defecto; si no existe en la tabla de comprobantes con letra X se usa CC).
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-NAVE-COMPROBANTE')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-NAVE-COMPROBANTE',N'CC',N'Comprobante contable del asiento de liquidación de NAVE (CC si no existe o no es contable con letra X)');
GO
