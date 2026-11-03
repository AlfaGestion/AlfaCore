namespace AlfaCore.Services;

/// <summary>
/// Esquema del portal del estudio contable en ALFA_CENTRAL (2026-11-03). Texto idéntico a
/// docs/base-datos/sql-referencia/2026-11-03-001__alfa_central_estudios.sql (lo verifica un test).
/// <see cref="EstudioContableCentral"/> lo aplica la primera vez que se usa. Idempotente.
/// </summary>
public static class CentralEstudiosSchema
{
    internal const string Script = """
-- Ejecutar contra ALFA_CENTRAL. No ejecutar en las bases de clientes.
-- AlfaCore lo aplica solo la primera vez que se usa el portal del estudio (CentralEstudiosSchema, mismo
-- texto); también se puede correr a mano. Idempotente.
-- Portal del estudio contable (2026-11-03): el dueño de una base invita a un estudio; el estudio entra con
-- su propia cuenta central (cliente de tipo 'E') y en cada base actúa con el usuario de TA_USUARIOS que le
-- creó el dueño, con los permisos que el dueño le dio. El dueño puede revocar el acceso cuando quiera.
--
-- ESTUDIO_ACCESOS: una fila por base y estudio.
--   Estado PENDIENTE (invitado, sin aceptar), ACTIVO o REVOCADO.
--   TokenHash: SHA-256 del enlace de invitación (el enlace se muestra una sola vez al dueño).
--   UsuarioBase: TA_USUARIOS.NOMBRE creado en la base para el estudio.
--   Perfil: CONSULTA o CARGA (los permisos se pueden ajustar después en Autorización de tareas).
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.bases', N'U') IS NULL
    THROW 50000, N'La base destino no contiene el catálogo central de bases (dbo.bases).', 1;

IF OBJECT_ID(N'dbo.ESTUDIO_ACCESOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ESTUDIO_ACCESOS
    (
        IdAcceso          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ESTUDIO_ACCESOS PRIMARY KEY,
        IdBase            int            NOT NULL,
        EmailInvitado     nvarchar(150)  NOT NULL,
        UsuarioBase       nvarchar(50)   NOT NULL,
        Perfil            varchar(20)    NOT NULL CONSTRAINT DF_ESTUDIO_ACCESOS_Perfil DEFAULT ('CONSULTA'),
        Estado            varchar(15)    NOT NULL CONSTRAINT DF_ESTUDIO_ACCESOS_Estado DEFAULT ('PENDIENTE'),
        TokenHash         char(64)       NULL,
        TokenVence        datetime       NULL,
        IdClienteEstudio  nvarchar(9)    NULL,
        LoginEstudio      nvarchar(100)  NULL,
        InvitadoPor       nvarchar(100)  NULL,
        FechaInvitacion   datetime       NOT NULL CONSTRAINT DF_ESTUDIO_ACCESOS_FechaInvitacion DEFAULT (GETDATE()),
        FechaAceptacion   datetime       NULL,
        RevocadoPor       nvarchar(100)  NULL,
        FechaRevocacion   datetime       NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ESTUDIO_ACCESOS') AND name = N'IX_ESTUDIO_ACCESOS_Estudio')
    CREATE INDEX IX_ESTUDIO_ACCESOS_Estudio ON dbo.ESTUDIO_ACCESOS (IdClienteEstudio, Estado) INCLUDE (IdBase, UsuarioBase, Perfil);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ESTUDIO_ACCESOS') AND name = N'IX_ESTUDIO_ACCESOS_Base')
    CREATE INDEX IX_ESTUDIO_ACCESOS_Base ON dbo.ESTUDIO_ACCESOS (IdBase, Estado);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ESTUDIO_ACCESOS') AND name = N'UX_ESTUDIO_ACCESOS_Token')
    CREATE UNIQUE INDEX UX_ESTUDIO_ACCESOS_Token ON dbo.ESTUDIO_ACCESOS (TokenHash) WHERE TokenHash IS NOT NULL;
""";

    internal const string VerificacionSql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ESTUDIO_ACCESOS', N'U') IS NULL THEN 0 ELSE 1 END;";
}
