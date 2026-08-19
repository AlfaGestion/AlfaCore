-- ============================================================
-- Carritos de compra - estructura minima para carritos generales
-- y detalle de articulos.
--
-- Este script no toca NP ni el circuito de catalogos publicos.
-- Solo crea la persistencia minima para carritos generales.
-- ============================================================

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.ALFACORE_CARRITOS_WEB', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_CARRITOS_WEB
    (
        IdCarrito              int IDENTITY(1,1) NOT NULL,
        Nombre                 nvarchar(150) NOT NULL,
        Descripcion            nvarchar(500) NULL,
        Activo                 bit NOT NULL CONSTRAINT DF_ALFACORE_CARRITOS_WEB_Activo DEFAULT (1),
        OrigenArticulos        nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_CARRITOS_WEB_OrigenArticulos DEFAULT (N'maestro'),
        IdListaArticulos       nvarchar(4) NULL,
        OrigenPrecios          nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_CARRITOS_WEB_OrigenPrecios DEFAULT (N'segun-cliente'),
        IdListaPrecios         nvarchar(4) NULL,
        ClasePrecio            int NULL,
        FechaHora_Grabacion    datetime NOT NULL CONSTRAINT DF_ALFACORE_CARRITOS_WEB_FHGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NOT NULL CONSTRAINT DF_ALFACORE_CARRITOS_WEB_FHMod DEFAULT (GETDATE()),
        CONSTRAINT PK_ALFACORE_CARRITOS_WEB PRIMARY KEY CLUSTERED (IdCarrito ASC)
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_CARRITOS_WEB_DET', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_CARRITOS_WEB_DET
    (
        IdDetalle              int IDENTITY(1,1) NOT NULL,
        IdCarrito              int NOT NULL,
        Orden                  int NOT NULL,
        IdArticulo             nvarchar(25) NOT NULL,
        DescripcionArticulo    nvarchar(100) NULL,
        Presentacion           nvarchar(100) NULL,
        Marca                  nvarchar(100) NULL,
        Rubro                  nvarchar(100) NULL,
        FechaHora_Grabacion    datetime NOT NULL CONSTRAINT DF_ALFACORE_CARRITOS_WEB_DET_FHGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NOT NULL CONSTRAINT DF_ALFACORE_CARRITOS_WEB_DET_FHMod DEFAULT (GETDATE()),
        CONSTRAINT PK_ALFACORE_CARRITOS_WEB_DET PRIMARY KEY CLUSTERED (IdDetalle ASC)
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_CARRITOS_WEB', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.ALFACORE_CARRITOS_WEB_DET', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = N'FK_ALFACORE_CARRITOS_WEB_DET_CAB'
   )
BEGIN
    ALTER TABLE dbo.ALFACORE_CARRITOS_WEB_DET
        WITH CHECK ADD CONSTRAINT FK_ALFACORE_CARRITOS_WEB_DET_CAB
        FOREIGN KEY (IdCarrito) REFERENCES dbo.ALFACORE_CARRITOS_WEB (IdCarrito);
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_CARRITOS_WEB_DET', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'UX_ALFACORE_CARRITOS_WEB_DET_CARRITO_ARTICULO'
         AND object_id = OBJECT_ID(N'dbo.ALFACORE_CARRITOS_WEB_DET')
   )
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_ALFACORE_CARRITOS_WEB_DET_CARRITO_ARTICULO
        ON dbo.ALFACORE_CARRITOS_WEB_DET (IdCarrito ASC, IdArticulo ASC);
END;
GO
