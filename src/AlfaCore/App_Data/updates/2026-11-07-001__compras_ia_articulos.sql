/*
    2026-11-07 · Compras con IA: emparejamiento de renglones con artículos, alta desde la factura y aprendizaje por proveedor.
    - Crea IA_ProveedorArticuloUdsXBl (unidades por bulto confirmadas por proveedor y artículo) si la base no la tiene; es la misma tabla
      que usa V_mv_Cpra del legacy, así que no se toca si ya existe.
    - Agrega las claves de configuración del alta de artículos desde una factura (rubro, tipo y unidad por defecto; vacías = el sistema
      propone según el artículo parecido o los artículos anteriores del proveedor y, si no hay, el primer valor de cada maestro).
    - Agrega COMPRAS-ARCA-TOLERANCIA: diferencia máxima (en pesos) aceptada entre una carga de compras y lo informado por ARCA al completar una
      importación de Control ARCA con el comprobante físico.
    Idempotente. Compatibility level 80/100: sin THROW, OPENJSON ni STRING_SPLIT.
*/
SET NOCOUNT ON;
GO
IF OBJECT_ID(N'dbo.IA_ProveedorArticuloUdsXBl', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IA_ProveedorArticuloUdsXBl
    (
        CuentaProveedor nvarchar(20) NOT NULL,
        IdArticulo nvarchar(20) NOT NULL,
        UdsXBl float NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_IA_ProveedorArticuloUdsXBl_Fecha DEFAULT (GETDATE()),
        Usuario nvarchar(50) NULL,
        CONSTRAINT PK_IA_ProveedorArticuloUdsXBl PRIMARY KEY (CuentaProveedor, IdArticulo)
    );
END
GO
IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COMPRAS-ALTA-RUBRO')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COMPRAS', N'COMPRAS-ALTA-RUBRO', N'', N'Rubro por defecto de los artículos que se dan de alta desde una factura leída por IA. Vacío: se propone según el artículo parecido o el proveedor.', GETDATE());
    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COMPRAS-ALTA-TIPO')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COMPRAS', N'COMPRAS-ALTA-TIPO', N'', N'Tipo de artículo por defecto en el alta desde una factura leída por IA. Vacío: se propone según el artículo parecido o el proveedor.', GETDATE());
    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COMPRAS-ALTA-UNIDAD')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COMPRAS', N'COMPRAS-ALTA-UNIDAD', N'', N'Unidad por defecto en el alta de artículos desde una factura leída por IA. Vacío: se propone según el artículo parecido o el proveedor.', GETDATE());
    IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'COMPRAS-ARCA-TOLERANCIA')
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
        VALUES (N'COMPRAS', N'COMPRAS-ARCA-TOLERANCIA', N'1.00', N'Diferencia máxima en pesos entre el total, el IVA y el neto gravado de una carga de compras y lo importado de ARCA, para poder completar la importación con el comprobante físico.', GETDATE());
END
GO
