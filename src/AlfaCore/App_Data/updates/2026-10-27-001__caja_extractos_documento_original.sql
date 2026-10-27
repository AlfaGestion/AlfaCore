-- El testigo se guarda en la misma base y transacción que la importación.
IF OBJECT_ID(N'dbo.BK_ImportacionExtracto', N'U') IS NULL RETURN;
IF COL_LENGTH('dbo.BK_ImportacionExtracto','ArchivoOriginal') IS NULL
    ALTER TABLE dbo.BK_ImportacionExtracto ADD ArchivoOriginal varbinary(max) NULL;
-- Los históricos quedan NULL: no es posible reconstruir el original desde sus movimientos.
