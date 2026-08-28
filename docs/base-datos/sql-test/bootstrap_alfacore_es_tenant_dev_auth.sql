/*
  BOOTSTRAP EXCLUSIVO DE DEVELOPMENT LOCAL — identidad mínima Base 84.
  No pertenece a App_Data/updates ni debe ejecutarse en una base operativa.
*/
:setvar ExpectedDatabase "ALFACORE_ES_TENANT_DEV"
:setvar EsLocalLogin "eslocal@alfacore.dev"
:setvar EsLocalTenantPasswordEncoded ""

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() <> N'$(ExpectedDatabase)'
    THROW 51200, 'SEGURIDAD: este bootstrap solo puede ejecutarse en ALFACORE_ES_TENANT_DEV.', 1;

IF CAST(SERVERPROPERTY('IsLocalDB') AS int) <> 1
    THROW 51201, 'SEGURIDAD: el bootstrap del tenant ES Local requiere SQL Server LocalDB.', 1;
GO

IF OBJECT_ID(N'dbo.TA_USUARIOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TA_USUARIOS
    (
        NOMBRE nvarchar(50) NOT NULL,
        SISTEMA nvarchar(50) NOT NULL,
        PASSWORD nvarchar(255) NOT NULL CONSTRAINT DF_ES_LOCAL_TA_USUARIOS_PASSWORD DEFAULT (N''),
        email_de nvarchar(150) NULL,
        EsGrupo bit NOT NULL CONSTRAINT DF_ES_LOCAL_TA_USUARIOS_EsGrupo DEFAULT (0),
        Activo bit NOT NULL CONSTRAINT DF_ES_LOCAL_TA_USUARIOS_Activo DEFAULT (1),
        Administrador bit NOT NULL CONSTRAINT DF_ES_LOCAL_TA_USUARIOS_Administrador DEFAULT (0),
        CONSTRAINT PK_ES_LOCAL_TA_USUARIOS PRIMARY KEY (NOMBRE, SISTEMA)
    );
END;
GO

IF COL_LENGTH(N'dbo.TA_USUARIOS', N'NOMBRE') <> 100
   OR COL_LENGTH(N'dbo.TA_USUARIOS', N'SISTEMA') <> 100
BEGIN
    DECLARE @PkUsuarios sysname =
    (
        SELECT TOP (1) kc.name
        FROM sys.key_constraints kc
        WHERE kc.parent_object_id = OBJECT_ID(N'dbo.TA_USUARIOS')
          AND kc.[type] = N'PK'
    );
    IF @PkUsuarios IS NOT NULL
    BEGIN
        DECLARE @DropPkUsuariosSql nvarchar(max) =
            N'ALTER TABLE dbo.TA_USUARIOS DROP CONSTRAINT ' + QUOTENAME(@PkUsuarios) + N';';
        EXEC sys.sp_executesql @DropPkUsuariosSql;
    END;

    ALTER TABLE dbo.TA_USUARIOS ALTER COLUMN NOMBRE nvarchar(50) NOT NULL;
    ALTER TABLE dbo.TA_USUARIOS ALTER COLUMN SISTEMA nvarchar(50) NOT NULL;
    ALTER TABLE dbo.TA_USUARIOS ADD CONSTRAINT PK_ES_LOCAL_TA_USUARIOS PRIMARY KEY (NOMBRE, SISTEMA);
END;
GO

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TA_CONFIGURACION
    (
        CLAVE nvarchar(100) NOT NULL,
        VALOR nvarchar(255) NULL,
        ValorAux nvarchar(max) NULL,
        GRUPO nvarchar(100) NULL,
        CONSTRAINT PK_ES_LOCAL_TA_CONFIGURACION PRIMARY KEY (CLAVE)
    );
END;
GO

IF OBJECT_ID(N'dbo.TA_CLASIFICACIONES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TA_CLASIFICACIONES
    (
        Codigo nvarchar(20) NOT NULL,
        Descripcion nvarchar(100) NULL,
        CONSTRAINT PK_ES_LOCAL_TA_CLASIFICACIONES PRIMARY KEY (Codigo)
    );
END;

IF OBJECT_ID(N'dbo.V_TA_Tecnicos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.V_TA_Tecnicos
    (
        IdTecnico nvarchar(4) NOT NULL,
        Nombre nvarchar(100) NULL,
        Cargo nvarchar(100) NULL,
        UsuarioAsociado nvarchar(50) NULL,
        SistemaAsociado nvarchar(50) NULL,
        Baja bit NOT NULL CONSTRAINT DF_ES_LOCAL_V_TA_Tecnicos_Baja DEFAULT (0),
        CONSTRAINT PK_ES_LOCAL_V_TA_Tecnicos PRIMARY KEY (IdTecnico)
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_USUARIOS
    WHERE NOMBRE = N'Administrador ES Local'
      AND SISTEMA = N'CN000PR'
)
BEGIN
    INSERT dbo.TA_USUARIOS
        (NOMBRE, SISTEMA, PASSWORD, email_de, EsGrupo, Activo, Administrador)
    VALUES
        (N'Administrador ES Local', N'CN000PR', N'$(EsLocalTenantPasswordEncoded)', N'$(EsLocalLogin)', 0, 1, 1);
END
ELSE
BEGIN
    UPDATE dbo.TA_USUARIOS
    SET PASSWORD = N'$(EsLocalTenantPasswordEncoded)',
        email_de = N'$(EsLocalLogin)',
        EsGrupo = 0,
        Activo = 1,
        Administrador = 1
    WHERE NOMBRE = N'Administrador ES Local'
      AND SISTEMA = N'CN000PR';
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_USUARIOS
    WHERE NOMBRE = N'Administrador ES Local'
      AND SISTEMA = N'CN000PR'
      AND email_de = N'$(EsLocalLogin)'
      AND Activo = 1
      AND Administrador = 1
)
    THROW 51202, 'No se pudo verificar el usuario interno ES Local.', 1;
GO

/* Esquema oficial mínimo del módulo requerido por Conversaciones y WhatsApp API. */
:r ..\..\..\src\AlfaCore\App_Data\updates\2026-05-17-999__conversaciones_modelo_base.sql
:r ..\..\..\src\AlfaCore\App_Data\updates\2026-08-21-002__conversaciones_whatsapp_multinumero_reemitido.sql
:r ..\..\..\src\AlfaCore\App_Data\updates\2026-08-18-010__conversaciones_whatsapp_web_por_numero.sql
:r ..\..\..\src\AlfaCore\App_Data\updates\2026-08-13-001__conversaciones_reglas.sql
:r ..\..\..\src\AlfaCore\App_Data\updates\2026-08-13-002__conversaciones_reglas_condiciones.sql
:r ..\..\..\src\AlfaCore\App_Data\updates\2026-08-13-003__conversaciones_asistente.sql
