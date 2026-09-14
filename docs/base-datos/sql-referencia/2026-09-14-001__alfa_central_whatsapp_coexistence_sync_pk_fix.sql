/*
  ES-2 fix - Corrige la PK de dbo.WhatsAppEmbeddedCoexistenceSync.
  Destino exclusivo: ALFA_CENTRAL.
  Script idempotente. NO es ejecutado automáticamente por AlfaCore.

  Problema: 2026-09-11-001 creó la tabla con PK (IdBase, PhoneNumberId, SyncType) -- one-shot PARA
  SIEMPRE por número. Meta permite un history sync nuevo después de un offboard real + un nuevo
  consentimiento en un onboarding posterior sobre el mismo número, así que el one-shot debe ser POR
  INTENTO DE ONBOARDING: (IdBase, IdOnboarding, PhoneNumberId, SyncType).

  Seguro sólo mientras la tabla esté vacía (no hace falta migrar datos todavía). Si ya hay filas, este
  script se niega a tocar la PK y no hace ningún cambio -- en ese caso hace falta una migración de
  datos real, fuera del alcance de este script.

  Idempotente: si la tabla no existe, si la PK ya es la correcta, o si ya se corrigió antes, no hace
  nada (PRINT informativo, sin error). Puede ejecutarse más de una vez sin efecto adicional.

  Endurecido (2026-09-14, segunda revisión): la ausencia total de PK (@PkColumns IS NULL, p. ej. si
  alguien la borró manualmente) ahora es un esquema inesperado explícito (THROW) -- antes, por cómo
  evalúa NULL un IF/ELSE IF en T-SQL, ese caso caía silenciosamente en el bloque de migración con
  @PkName también NULL, produciendo un EXEC(NULL) de comportamiento indefinido. También se agregó
  TRY/CATCH con ROLLBACK explícito alrededor del DROP/ADD de la PK, además de mantener XACT_ABORT ON.
*/
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync', N'U') IS NULL
    PRINT 'dbo.WhatsAppEmbeddedCoexistenceSync no existe todavía -- nada que corregir (la próxima vez que se cree, ya será con 2026-09-11-001 corregido).';
ELSE
BEGIN
    DECLARE @PkColumns nvarchar(400);
    SELECT @PkColumns = STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
    FROM sys.key_constraints kc
    JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE kc.parent_object_id = OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync') AND kc.type = 'PK';

    -- Ausencia total de PK es esquema inesperado -- nunca debe caer en el bloque de migración de abajo
    -- (un IF/ELSE IF comparando contra NULL en T-SQL no dispara ninguna rama y cae al ELSE final, que
    -- es justamente el bloque que migra -- por eso este chequeo va primero y explícito).
    IF @PkColumns IS NULL
        THROW 51099, 'dbo.WhatsAppEmbeddedCoexistenceSync no tiene ninguna PK definida -- esquema inesperado. Revisar manualmente antes de continuar -- este script no adivina.', 1;
    ELSE IF @PkColumns = N'IdBase,IdOnboarding,PhoneNumberId,SyncType'
        PRINT 'La PK ya es (IdBase, IdOnboarding, PhoneNumberId, SyncType) -- nada que hacer.';
    ELSE IF @PkColumns <> N'IdBase,PhoneNumberId,SyncType'
        THROW 51100, 'La PK actual de dbo.WhatsAppEmbeddedCoexistenceSync no coincide ni con la esperada vieja ni con la nueva. Revisar manualmente antes de continuar -- este script no adivina.', 1;
    ELSE
    BEGIN
        DECLARE @RowCount int;
        SELECT @RowCount = COUNT(*) FROM dbo.WhatsAppEmbeddedCoexistenceSync;

        IF @RowCount > 0
            THROW 51101, 'dbo.WhatsAppEmbeddedCoexistenceSync ya tiene filas -- este script sólo corrige la PK sobre la tabla vacía. Hace falta una migración de datos real antes de tocar la PK.', 1;
        ELSE
        BEGIN
            DECLARE @PkName sysname;
            SELECT @PkName = kc.name
            FROM sys.key_constraints kc
            WHERE kc.parent_object_id = OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync') AND kc.type = 'PK';

            -- Defensivo, no debería poder pasar acá con @PkColumns validado arriba -- pero si @PkName
            -- igual saliera NULL por alguna razón, mejor un THROW explícito que un EXEC(NULL).
            IF @PkName IS NULL
                THROW 51102, 'No se pudo resolver el nombre de la restricción PK existente -- abortando sin tocar nada.', 1;

            BEGIN TRY
                BEGIN TRANSACTION;

                EXEC(N'ALTER TABLE dbo.WhatsAppEmbeddedCoexistenceSync DROP CONSTRAINT ' + QUOTENAME(@PkName) + N';');

                ALTER TABLE dbo.WhatsAppEmbeddedCoexistenceSync
                    WITH CHECK ADD CONSTRAINT PK_WhatsAppEmbeddedCoexistenceSync
                    PRIMARY KEY (IdBase, IdOnboarding, PhoneNumberId, SyncType);

                -- IdBase ya lidera la nueva PK/índice clustered: una búsqueda por sólo IdBase
                -- (GetForBaseAsync) hace seek sobre la PK sin necesitar este índice separado.
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync') AND name = N'IX_WAECS_IdBase')
                    DROP INDEX IX_WAECS_IdBase ON dbo.WhatsAppEmbeddedCoexistenceSync;

                COMMIT TRANSACTION;

                PRINT 'PK corregida a (IdBase, IdOnboarding, PhoneNumberId, SyncType).';
            END TRY
            BEGIN CATCH
                -- XACT_ABORT ON ya garantiza rollback automático ante error, pero se deja explícito
                -- (y sólo si la transacción sigue abierta) para que el mensaje de error sea claro y no
                -- quede ninguna ambigüedad sobre el estado final.
                IF XACT_STATE() <> 0
                    ROLLBACK TRANSACTION;
                THROW;
            END CATCH
        END
    END
END
GO
