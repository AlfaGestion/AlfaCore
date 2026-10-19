-- Operaciones comerciales de homologación: snapshot durable antes del CAE.
-- No cambia configuración, documentos previos, triggers ni procedimientos legacy.
IF OBJECT_ID(N'dbo.ALFACORE_VENTAS_CARGA_OPERACION',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_VENTAS_CARGA_OPERACION
    (
        Operacion uniqueidentifier NOT NULL CONSTRAINT PK_ALFACORE_VENTAS_CARGA_OPERACION PRIMARY KEY,
        OrigenId int NOT NULL,
        UnidadNegocio nvarchar(4) NOT NULL,
        Usuario nvarchar(50) NOT NULL,
        Ambiente varchar(20) NOT NULL CONSTRAINT CK_ALFACORE_VENTAS_CARGA_AMBIENTE CHECK(Ambiente='HOMOLOGACION'),
        Estado varchar(30) NOT NULL,
        PlanJson nvarchar(max) NOT NULL,
        FacturaId int NULL,
        Comprobante nvarchar(13) NULL,
        NumeroAsiento int NULL,
        Cae nvarchar(20) NULL,
        Vencimiento datetime NULL,
        Mensaje nvarchar(1000) NULL,
        FechaCreacion datetime2 NOT NULL CONSTRAINT DF_ALFACORE_VENTAS_CARGA_FECHA DEFAULT SYSUTCDATETIME(),
        FechaActualizacion datetime2 NOT NULL CONSTRAINT DF_ALFACORE_VENTAS_CARGA_ACT DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ALFACORE_VENTAS_CARGA_ORIGEN ON dbo.ALFACORE_VENTAS_CARGA_OPERACION(OrigenId,FechaCreacion);
END;

-- Un rechazo no consume número en ARCA. Conservarlo en el historial sin bloquear
-- el número para una nueva operación corregida; aprobadas/inciertas siguen siendo únicas.
IF OBJECT_ID(N'dbo.ALFACORE_VENTAS_HOMO_PRUEBA',N'U') IS NOT NULL
   AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ALFACORE_VENTAS_HOMO_PRUEBA')
              AND name=N'UX_ALFACORE_VENTAS_HOMO_NUMERO' AND filter_definition NOT LIKE '%RECHAZADA%')
BEGIN
    DROP INDEX UX_ALFACORE_VENTAS_HOMO_NUMERO ON dbo.ALFACORE_VENTAS_HOMO_PRUEBA;
    CREATE UNIQUE INDEX UX_ALFACORE_VENTAS_HOMO_NUMERO
        ON dbo.ALFACORE_VENTAS_HOMO_PRUEBA(Cuit,PuntoVenta,TipoComprobante,Numero)
        WHERE Numero IS NOT NULL AND Estado<>N'RECHAZADA';
END;
