SET NOCOUNT ON;
GO

-- ============================================================
-- Conversaciones — Reglas: condiciones adicionales
--
-- Horario: SIEMPRE (default) | DENTRO | FUERA. Limita la regla a los
--   días/horario configurados en Automatizaciones (o su complemento).
--   Si el horario no está configurado, la condición se ignora.
-- SoloPrimerContacto: la regla solo dispara en el primer mensaje
--   entrante de la conversación (útil para un menú de bienvenida).
-- ============================================================

IF OBJECT_ID(N'dbo.CONV_REGLAS', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.CONV_REGLAS', N'Horario') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_REGLAS
        ADD Horario nvarchar(20) NOT NULL CONSTRAINT DF_CONV_REGLAS_Horario DEFAULT (N'SIEMPRE');
END;
GO

IF COL_LENGTH(N'dbo.CONV_REGLAS', N'SoloPrimerContacto') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_REGLAS
        ADD SoloPrimerContacto bit NOT NULL CONSTRAINT DF_CONV_REGLAS_SoloPrimerContacto DEFAULT (0);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CONV_REGLAS_Horario'
)
BEGIN
    ALTER TABLE dbo.CONV_REGLAS
        ADD CONSTRAINT CK_CONV_REGLAS_Horario CHECK (Horario IN (N'SIEMPRE', N'DENTRO', N'FUERA'));
END;
GO
