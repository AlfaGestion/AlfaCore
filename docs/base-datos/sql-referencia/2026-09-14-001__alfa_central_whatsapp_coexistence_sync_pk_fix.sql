/*
  ES-2 fix - Corrige la PK de dbo.WhatsAppEmbeddedCoexistenceSync.
  Destino exclusivo: ALFA_CENTRAL.
  Script idempotente. NO es ejecutado automáticamente por AlfaCore.

  Compatible explícitamente con SQL Server 2016 SP1 / compatibility_level 100 (confirmado en
  producción, 2026-09-14), corregido en dos rondas sobre errores REALES de ejecución en preflight
  (ningún intento llegó a ejecutar DDL):
    1) SIN STRING_AGG ni WITHIN GROUP (exige compatibility_level >= 130; falló con "'STRING_AGG' no es
       un nombre de función integrada reconocido"). La detección de PK se hizo con COUNT/EXISTS sobre
       sys.key_constraints/sys.index_columns/sys.columns, comparando cada columna por su key_ordinal
       exacto en vez de concatenar nombres.
    2) SIN llamar a QUOTENAME() directamente dentro de EXEC(expresión) (falló con "Msg 102, Sintaxis
       incorrecta cerca de 'QUOTENAME'" -- la forma EXEC(expresión) no acepta una función dentro de la
       concatenación). El SQL dinámico ahora se arma primero en una variable con SET y se ejecuta con
       EXEC sp_executesql.

  Problema original: 2026-09-11-001 creó la tabla con PK (IdBase, PhoneNumberId, SyncType) -- one-shot
  PARA SIEMPRE por número. Meta permite un history sync nuevo después de un offboard real + un nuevo
  consentimiento en un onboarding posterior sobre el mismo número, así que el one-shot debe ser POR
  INTENTO DE ONBOARDING: (IdBase, IdOnboarding, PhoneNumberId, SyncType).

  Reglas:
    tabla inexistente                    -> no-op
    PK ya es la nueva (4 cols exactas)   -> no-op
    sin PK                               -> THROW (esquema inesperado)
    PK vieja (3 cols exactas) + filas>0  -> THROW (hace falta migración de datos real, fuera de alcance)
    PK vieja (3 cols exactas) + vacía    -> DROP PK vieja + ADD PK nueva + DROP IX_WAECS_IdBase si existe
    cualquier otra PK                    -> THROW (esquema inesperado, este script no adivina)

  Idempotente: puede ejecutarse más de una vez sin efecto adicional una vez corregida.
*/
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync', N'U') IS NULL
BEGIN
    PRINT 'dbo.WhatsAppEmbeddedCoexistenceSync no existe todavía -- nada que corregir (la próxima vez que se cree, ya será con 2026-09-11-001 corregido).';
END;
GO

IF OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync', N'U') IS NOT NULL
BEGIN
    DECLARE @TableId int = OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync');

    -- Volcado único de (key_ordinal, nombre de columna) de la PK actual a una tabla variable, para no
    -- repetir el JOIN contra sys.key_constraints/sys.index_columns/sys.columns en cada comparación de
    -- abajo. Nada de STRING_AGG/WITHIN GROUP -- sólo COUNT/EXISTS sobre esta tabla variable.
    DECLARE @PkCols TABLE (KeyOrdinal int NOT NULL PRIMARY KEY, ColumnName sysname NOT NULL);
    INSERT INTO @PkCols (KeyOrdinal, ColumnName)
    SELECT ic.key_ordinal, c.name
    FROM sys.key_constraints kc
    JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE kc.parent_object_id = @TableId AND kc.type = 'PK';

    DECLARE @PkColumnCount int = (SELECT COUNT(*) FROM @PkCols);

    DECLARE @IsNewPk bit = CASE WHEN @PkColumnCount = 4
        AND EXISTS (SELECT 1 FROM @PkCols WHERE KeyOrdinal = 1 AND ColumnName = N'IdBase')
        AND EXISTS (SELECT 1 FROM @PkCols WHERE KeyOrdinal = 2 AND ColumnName = N'IdOnboarding')
        AND EXISTS (SELECT 1 FROM @PkCols WHERE KeyOrdinal = 3 AND ColumnName = N'PhoneNumberId')
        AND EXISTS (SELECT 1 FROM @PkCols WHERE KeyOrdinal = 4 AND ColumnName = N'SyncType')
        THEN 1 ELSE 0 END;

    DECLARE @IsOldPk bit = CASE WHEN @PkColumnCount = 3
        AND EXISTS (SELECT 1 FROM @PkCols WHERE KeyOrdinal = 1 AND ColumnName = N'IdBase')
        AND EXISTS (SELECT 1 FROM @PkCols WHERE KeyOrdinal = 2 AND ColumnName = N'PhoneNumberId')
        AND EXISTS (SELECT 1 FROM @PkCols WHERE KeyOrdinal = 3 AND ColumnName = N'SyncType')
        THEN 1 ELSE 0 END;

    IF @PkColumnCount = 0
        THROW 51099, 'dbo.WhatsAppEmbeddedCoexistenceSync no tiene ninguna PK definida -- esquema inesperado. Revisar manualmente antes de continuar -- este script no adivina.', 1;
    ELSE IF @IsNewPk = 1
        PRINT 'La PK ya es (IdBase, IdOnboarding, PhoneNumberId, SyncType) -- nada que hacer.';
    ELSE IF @IsOldPk = 0
        THROW 51100, 'La PK actual de dbo.WhatsAppEmbeddedCoexistenceSync no coincide ni con la esperada vieja (IdBase,PhoneNumberId,SyncType) ni con la nueva (IdBase,IdOnboarding,PhoneNumberId,SyncType). Revisar manualmente antes de continuar -- este script no adivina.', 1;
    ELSE
    BEGIN
        -- Acá @IsOldPk=1: PK vieja confirmada exactamente (3 columnas, orden y nombres exactos).
        DECLARE @RowCount int;
        SELECT @RowCount = COUNT(*) FROM dbo.WhatsAppEmbeddedCoexistenceSync;

        IF @RowCount > 0
            THROW 51101, 'dbo.WhatsAppEmbeddedCoexistenceSync ya tiene filas -- este script sólo corrige la PK sobre la tabla vacía. Hace falta una migración de datos real antes de tocar la PK.', 1;
        ELSE
        BEGIN
            DECLARE @PkName sysname;
            SELECT @PkName = kc.name
            FROM sys.key_constraints kc
            WHERE kc.parent_object_id = @TableId AND kc.type = 'PK';

            -- Defensivo, no debería poder pasar acá con @IsOldPk ya validado arriba -- pero si @PkName
            -- igual saliera NULL por alguna razón, mejor un THROW explícito que armar un
            -- sp_executesql con SQL dinámico NULL/corrupto.
            IF @PkName IS NULL
                THROW 51102, 'No se pudo resolver el nombre de la restricción PK existente -- abortando sin tocar nada.', 1;

            -- EXEC('...' + QUOTENAME(@x) + '...') falla en SQL Server 2016 SP1 / compat 100 con
            -- "Msg 102, Sintaxis incorrecta cerca de 'QUOTENAME'" -- la forma EXEC(expresión) no acepta
            -- una llamada a función dentro de la concatenación. Se materializa el SQL dinámico en una
            -- variable primero (SET, sin funciones dentro del propio EXEC) y se ejecuta con sp_executesql.
            DECLARE @DropPkSql nvarchar(1000);
            SET @DropPkSql = N'ALTER TABLE dbo.WhatsAppEmbeddedCoexistenceSync DROP CONSTRAINT ' + QUOTENAME(@PkName) + N';';

            BEGIN TRY
                BEGIN TRANSACTION;

                EXEC sp_executesql @DropPkSql;

                ALTER TABLE dbo.WhatsAppEmbeddedCoexistenceSync
                    WITH CHECK ADD CONSTRAINT PK_WhatsAppEmbeddedCoexistenceSync
                    PRIMARY KEY (IdBase, IdOnboarding, PhoneNumberId, SyncType);

                -- IdBase ya lidera la nueva PK/índice clustered: una búsqueda por sólo IdBase
                -- (GetForBaseAsync) hace seek sobre la PK sin necesitar este índice separado.
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @TableId AND name = N'IX_WAECS_IdBase')
                    DROP INDEX IX_WAECS_IdBase ON dbo.WhatsAppEmbeddedCoexistenceSync;

                COMMIT TRANSACTION;

                PRINT 'PK corregida a (IdBase, IdOnboarding, PhoneNumberId, SyncType).';
            END TRY
            BEGIN CATCH
                -- XACT_ABORT ON ya garantiza rollback automático ante error, pero se deja explícito
                -- (y sólo si sigue habiendo una transacción abierta) para que el mensaje de error sea
                -- claro y no quede ninguna ambigüedad sobre el estado final.
                IF @@TRANCOUNT > 0
                    ROLLBACK TRANSACTION;
                THROW;
            END CATCH
        END
    END
END;
GO
