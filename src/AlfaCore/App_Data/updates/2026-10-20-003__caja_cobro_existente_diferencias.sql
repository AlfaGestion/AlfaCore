IF COL_LENGTH('dbo.BK_CobroExistente','ImporteBanco') IS NULL
    ALTER TABLE dbo.BK_CobroExistente ADD ImporteBanco decimal(19,4) NULL;
IF COL_LENGTH('dbo.BK_CobroExistente','Observacion') IS NULL
    ALTER TABLE dbo.BK_CobroExistente ADD Observacion nvarchar(300) NULL;
