/*
    Calendario: casilla "Mostrar en la barra superior" por evento.
    MostrarEnIndicador = 1 hace que el evento aparezca en el indicador de la barra superior
    (quién está a cargo ahora y quién sigue), sea del tipo que sea. Así el indicador no depende
    solo del tipo de evento.
    Idempotente.
*/

IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.CAL_EVENTOS', N'MostrarEnIndicador') IS NULL
    ALTER TABLE dbo.CAL_EVENTOS ADD MostrarEnIndicador bit NULL;
GO

IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.CAL_EVENTOS', N'MostrarEnIndicador') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CAL_EVENTOS_Indicador' AND object_id = OBJECT_ID(N'dbo.CAL_EVENTOS'))
    CREATE NONCLUSTERED INDEX IX_CAL_EVENTOS_Indicador
        ON dbo.CAL_EVENTOS (FechaInicio)
        INCLUDE (FechaFin, Estado, Baja)
        WHERE MostrarEnIndicador = 1;
GO
