-- Registro operativo de pruebas ARCA, separado de la grabación comercial legacy.
-- No almacena certificados, claves privadas ni tickets WSAA.
IF OBJECT_ID(N'dbo.ALFACORE_VENTAS_HOMO_PRUEBA',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_VENTAS_HOMO_PRUEBA (
        Operacion uniqueidentifier NOT NULL CONSTRAINT PK_ALFACORE_VENTAS_HOMO_PRUEBA PRIMARY KEY,
        OrigenId int NOT NULL, Usuario nvarchar(255) NOT NULL, UnidadNegocio nvarchar(4) NOT NULL,
        Ambiente nvarchar(20) NOT NULL CONSTRAINT CK_ALFACORE_VENTAS_HOMO_AMBIENTE CHECK(Ambiente=N'HOMOLOGACION'),
        Cuit nvarchar(11) NOT NULL, PuntoVenta int NOT NULL, TipoComprobante int NOT NULL,
        Numero bigint NULL, Estado nvarchar(20) NOT NULL, Importe decimal(18,2) NOT NULL,
        Solicitud nvarchar(max) NULL, Respuesta nvarchar(max) NULL,
        Cae nvarchar(20) NULL, Vencimiento datetime2 NULL,
        FechaCreacion datetime2 NOT NULL CONSTRAINT DF_ALFACORE_VENTAS_HOMO_FECHA DEFAULT SYSUTCDATETIME(),
        FechaActualizacion datetime2 NOT NULL CONSTRAINT DF_ALFACORE_VENTAS_HOMO_ACTUALIZACION DEFAULT SYSUTCDATETIME()
    );
    -- Compat 80: un índice filtrado hace fallar los INSERT/UPDATE de SqlClient (1934, ARITHABORT);
    -- ahí no se crea y la unicidad del número queda a cargo del servicio.
    IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) >= 90
        CREATE UNIQUE INDEX UX_ALFACORE_VENTAS_HOMO_NUMERO
            ON dbo.ALFACORE_VENTAS_HOMO_PRUEBA(Cuit,PuntoVenta,TipoComprobante,Numero) WHERE Numero IS NOT NULL;
END;
