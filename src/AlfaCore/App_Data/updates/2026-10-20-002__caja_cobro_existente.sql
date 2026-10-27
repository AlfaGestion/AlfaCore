IF OBJECT_ID(N'dbo.BK_CobroExistente',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BK_CobroExistente(
        MovimientoId bigint NOT NULL PRIMARY KEY REFERENCES dbo.BK_ExtractoMovimiento(MovimientoId),
        CuentaCliente nvarchar(15) NOT NULL,Periodo nvarchar(6) NOT NULL,Mes tinyint NOT NULL,
        Asiento int NOT NULL,TipoRegistro nvarchar(4) NOT NULL,Secuencia int NOT NULL,
        Tc nvarchar(4) NOT NULL,Comprobante nvarchar(13) NOT NULL,Importe decimal(19,4) NOT NULL,
        Usuario nvarchar(100) NOT NULL,FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_BK_CobroExistente_Recibo UNIQUE(CuentaCliente,Periodo,Mes,Asiento,TipoRegistro,Secuencia));
END;
