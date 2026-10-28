/*
    Calendario: eventos repetidos (series) estilo Google Calendar.
    Cada repetición se graba como un evento real; IdSerie une los eventos de la serie (es el
    IdEvento del primero) y ReglaRepeticion guarda la regla en JSON (frecuencia, intervalo,
    días, fin). Los eventos ya cargados quedan como eventos sueltos (IdSerie NULL).
    Idempotente.
*/

IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.CAL_EVENTOS', N'IdSerie') IS NULL
    ALTER TABLE dbo.CAL_EVENTOS ADD IdSerie bigint NULL;

IF COL_LENGTH(N'dbo.CAL_EVENTOS', N'ReglaRepeticion') IS NULL
    ALTER TABLE dbo.CAL_EVENTOS ADD ReglaRepeticion nvarchar(600) NULL;
GO

IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.CAL_EVENTOS', N'IdSerie') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CAL_EVENTOS_IdSerie' AND object_id = OBJECT_ID(N'dbo.CAL_EVENTOS'))
BEGIN
    -- Compat 80: un índice filtrado hace fallar los INSERT/UPDATE de SqlClient (1934, ARITHABORT).
    IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) >= 90
        CREATE NONCLUSTERED INDEX IX_CAL_EVENTOS_IdSerie
            ON dbo.CAL_EVENTOS (IdSerie, FechaInicio)
            WHERE IdSerie IS NOT NULL;
    ELSE
        CREATE NONCLUSTERED INDEX IX_CAL_EVENTOS_IdSerie
            ON dbo.CAL_EVENTOS (IdSerie, FechaInicio);
END;
GO
