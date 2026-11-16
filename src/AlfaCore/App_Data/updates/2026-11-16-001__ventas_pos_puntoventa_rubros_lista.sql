SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET NOCOUNT ON;

-- ============================================================
-- Modulo Ventas / POS - configuracion comercial por punto de venta
--  * POS_PUNTOVENTA.IDLISTA: lista de precios del punto de venta
--    (vacia = la del cliente / configuracion general, normalmente
--    el maestro de articulos).
--  * POS_PUNTOVENTA.CLASEPRECIO: clase de precio 1..8 (NULL = la de
--    la configuracion general).
--  * POS_PUNTOVENTA_RUBRO: rubros que vende el punto de venta (sin
--    filas = todos). Limita el catalogo del Mostrador y la carta QR.
-- Idempotente: puede ejecutarse varias veces sin romper datos.
-- ============================================================

IF COL_LENGTH(N'dbo.POS_PUNTOVENTA', N'IDLISTA') IS NULL
    ALTER TABLE dbo.POS_PUNTOVENTA ADD IDLISTA nvarchar(10) NULL;
GO

IF COL_LENGTH(N'dbo.POS_PUNTOVENTA', N'CLASEPRECIO') IS NULL
    ALTER TABLE dbo.POS_PUNTOVENTA ADD CLASEPRECIO int NULL;
GO

IF OBJECT_ID(N'dbo.POS_PUNTOVENTA_RUBRO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_PUNTOVENTA_RUBRO(
        IDPUNTOVENTA int          NOT NULL,
        IDRUBRO      nvarchar(10) NOT NULL,
        CONSTRAINT PK_POS_PUNTOVENTA_RUBRO PRIMARY KEY CLUSTERED (IDPUNTOVENTA ASC, IDRUBRO ASC)
    );
END;
GO
