SET NOCOUNT ON;

-- Guarda el importe base (por viaje, antes de cantidad y adicionales) que el
-- usuario carga en "Importe cliente" / "Importe fletero". Hasta ahora solo se
-- persistian TOTAL_IMPORTE / TOTAL_FLETE (totales ya multiplicados por
-- cantidad y con adicionales aplicados), por lo que al editar un viaje la
-- pantalla volvia a cargar el total como si fuera el importe base y los
-- adicionales se aplicaban de nuevo sobre un valor ya inflado.
IF OBJECT_ID(N'dbo.MV_VIAJES_CARGA', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.MV_VIAJES_CARGA', 'IMPORTE_CLIENTE') IS NULL
        ALTER TABLE dbo.MV_VIAJES_CARGA ADD IMPORTE_CLIENTE money NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_IMPORTE_CLIENTE DEFAULT(0) WITH VALUES;

    IF COL_LENGTH('dbo.MV_VIAJES_CARGA', 'IMPORTE_FLETERO') IS NULL
        ALTER TABLE dbo.MV_VIAJES_CARGA ADD IMPORTE_FLETERO money NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_IMPORTE_FLETERO DEFAULT(0) WITH VALUES;
END;
