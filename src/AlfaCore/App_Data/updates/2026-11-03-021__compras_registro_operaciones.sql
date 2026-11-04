-- Idempotencia y trazabilidad de altas de compras desde AlfaCore. No modifica movimientos legacy.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_COMPRA_OPERACION',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_COMPRA_OPERACION(
        Operacion uniqueidentifier NOT NULL CONSTRAINT PK_ALFACORE_COMPRA_OPERACION PRIMARY KEY,
        SolicitudHash varchar(64) NOT NULL, Usuario nvarchar(100) NOT NULL, DocumentoId bigint NULL,
        CompraId int NOT NULL, Comprobante nvarchar(13) NOT NULL, Periodo nvarchar(6) NOT NULL,
        Mes int NOT NULL, Asiento int NOT NULL, TipoRegistro nvarchar(4) NOT NULL, Total money NOT NULL,
        SolicitudJson nvarchar(max) NOT NULL, FechaHora datetime NOT NULL CONSTRAINT DF_ALFACORE_COMPRA_FECHA DEFAULT GETDATE()
    );
    CREATE UNIQUE INDEX UX_ALFACORE_COMPRA_ORIGEN ON dbo.ALFACORE_COMPRA_OPERACION(DocumentoId) WHERE DocumentoId IS NOT NULL;
    CREATE UNIQUE INDEX UX_ALFACORE_COMPRA_ID ON dbo.ALFACORE_COMPRA_OPERACION(CompraId);
END;
