SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.BK_Conciliacion',N'U') IS NULL
    THROW 50001,'Instalá primero los vínculos de conciliación.',1;
IF OBJECT_ID(N'dbo.BK_AsientoGastos',N'U') IS NULL
BEGIN
    -- Un asiento CC mensual de gastos e impuestos bancarios por extracto, conciliado contra todos sus movimientos.
    CREATE TABLE dbo.BK_AsientoGastos(
        Operacion uniqueidentifier NOT NULL PRIMARY KEY,
        ImportacionId bigint NOT NULL REFERENCES dbo.BK_ImportacionExtracto(ImportacionId),
        SolicitudHash varchar(64) NOT NULL,SolicitudJson nvarchar(max) NOT NULL,CuentaBanco nvarchar(15) NOT NULL,
        Periodo nvarchar(6) NOT NULL,Mes tinyint NOT NULL,NumeroAsiento int NOT NULL,
        TipoRegistro nvarchar(4) NOT NULL,Comprobante nvarchar(13) NOT NULL,Unidad nvarchar(4) NOT NULL,
        ConciliacionId bigint NOT NULL REFERENCES dbo.BK_Conciliacion(Id),Total decimal(19,4) NOT NULL,
        Usuario nvarchar(100) NOT NULL,FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
END;
IF OBJECT_ID(N'dbo.BK_AsientoGastosMovimiento',N'U') IS NULL
BEGIN
    -- Un movimiento solo puede pertenecer a un asiento de gastos, aunque luego se desconcilie.
    CREATE TABLE dbo.BK_AsientoGastosMovimiento(
        Operacion uniqueidentifier NOT NULL REFERENCES dbo.BK_AsientoGastos(Operacion),
        MovimientoId bigint NOT NULL UNIQUE REFERENCES dbo.BK_ExtractoMovimiento(MovimientoId),
        Concepto varchar(20) NOT NULL,Importe decimal(19,4) NOT NULL,
        PRIMARY KEY(Operacion,MovimientoId));
END;
-- Aditiva: no reemplaza una configuración existente. Las cuentas se toman del asiento modelo la primera vez que se genera el asiento.
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-GASTOS-MODELO')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-GASTOS-MODELO',N'GASTOS BANCARIOS',N'Asiento modelo de Alfa Gestión del que se toman las cuentas iniciales del asiento de gastos bancarios');
