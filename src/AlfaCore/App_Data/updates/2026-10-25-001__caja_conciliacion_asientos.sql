SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.BK_Conciliacion',N'U') IS NULL
    BEGIN RAISERROR(N'Instalá primero los vínculos de conciliación.', 16, 1); RETURN; END;
IF OBJECT_ID(N'dbo.BK_AsientoOperacion',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BK_AsientoOperacion(
        Operacion uniqueidentifier NOT NULL PRIMARY KEY,
        MovimientoId bigint NOT NULL UNIQUE REFERENCES dbo.BK_ExtractoMovimiento(MovimientoId),
        SolicitudHash varchar(64) NOT NULL,SolicitudJson nvarchar(max) NOT NULL,CuentaBanco nvarchar(15) NOT NULL,
        Periodo nvarchar(6) NOT NULL,Mes tinyint NOT NULL,NumeroAsiento int NOT NULL,
        TipoRegistro nvarchar(4) NOT NULL,Comprobante nvarchar(13) NOT NULL,Unidad nvarchar(4) NOT NULL,
        ConciliacionId bigint NOT NULL REFERENCES dbo.BK_Conciliacion(Id),
        Usuario nvarchar(100) NOT NULL,FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
END;
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-ASIENTOS')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-ASIENTOS','JSON',N'{"TipoRegistro":"","Reglas":[]}',N'Cuentas y conceptos para asientos bancarios');
