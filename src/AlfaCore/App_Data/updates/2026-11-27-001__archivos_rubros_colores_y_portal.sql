-- Registra Rubros en el editor oficial de Tablas de referencia.
-- Los datos siguen viviendo en V_TA_RUBROS, incluida la columna COLOR legacy.
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_TABLAS_REFERENCIA', N'U') IS NULL RETURN;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TABLAS_REFERENCIA WHERE Clave = N'RUBROS')
BEGIN
    INSERT INTO dbo.ALFACORE_TABLAS_REFERENCIA
        (Clave, NombreVisible, TablaFisica, ColumnaId, ColumnaCodigo, ColumnaDescripcion, ColumnaColor, Orden)
    VALUES
        (N'RUBROS', N'Rubros de artículos', N'V_TA_RUBROS', N'ID', N'IdRubro', N'Descripcion', N'COLOR', 2);
END;
GO

UPDATE dbo.ALFACORE_TABLAS_REFERENCIA
SET NombreVisible = N'Rubros de artículos',
    TablaFisica = N'V_TA_RUBROS',
    ColumnaId = N'ID',
    ColumnaCodigo = N'IdRubro',
    ColumnaDescripcion = N'Descripcion',
    ColumnaColor = N'COLOR',
    Activo = 1,
    Orden = 2,
    ModificadoUtc = GETUTCDATE()
WHERE Clave = N'RUBROS';
GO
