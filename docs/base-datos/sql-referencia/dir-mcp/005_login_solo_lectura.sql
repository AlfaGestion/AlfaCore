-- MCP de consulta en lenguaje natural - Etapa 2 / script 5: login dedicado de SOLO LECTURA.
-- NO contiene contrasena: se pasa por variable sqlcmd, p. ej.:
--   sqlcmd -S <servidor> -f i:65001 -i 005_login_solo_lectura.sql ^
--          -v MCP_DB="ALFANET" MCP_LOGIN="mcp_dir_ro" MCP_PASSWORD="<elegir una contrasena fuerte>"
-- Requiere un login con permiso para crear logins (sysadmin / securityadmin). Ejecutar DESPUES de 001, 002 y 003.
--
-- Permisos que recibe (nada mas):
--   * SELECT sobre el esquema dir (vistas). Las vistas pertenecen a dbo, asi que el ownership chaining
--     les permite leer las tablas base SIN que el login tenga acceso directo a ellas.
--   * INSERT (sin SELECT) sobre dbo.IA_LOG_CONSULTAS, para la auditoria.
-- No pertenece a ningun rol fijo (ni db_datareader). No se hace ningun GRANT sobre dbo ni sobre ninguna tabla base.
--
-- DESDE SSMS: activar "Consulta > Modo SQLCMD", quitar el "-- " de las 3 lineas :setvar de abajo y completarlas.
-- (No guardar ni commitear el archivo con la contrasena escrita.)
-- :setvar MCP_DB "<nombre de la base, ej. MiBase"
-- :setvar MCP_LOGIN "mcp_dir_ro"
-- :setvar MCP_PASSWORD "<contrasena fuerte>"
SET NOCOUNT ON;
GO
-- GUARDA: estas variables solo las reemplaza sqlcmd (-v) o SSMS con "Consulta > Modo SQLCMD" activado.
-- Si no, quedan como texto literal y el script abortaria a medias: aca se corta antes de tocar nada.
IF LEFT(N'$(MCP_LOGIN)', 2) = N'$' + N'('
BEGIN
    RAISERROR(N'ABORTADO: las variables SQLCMD no fueron reemplazadas. En SSMS activar Consulta > Modo SQLCMD y completar los :setvar de arriba; o correr con sqlcmd -v.', 16, 1);
    SET NOEXEC ON;
END
GO
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(MCP_LOGIN)')
    CREATE LOGIN [$(MCP_LOGIN)] WITH PASSWORD = N'$(MCP_PASSWORD)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF,
                                     DEFAULT_DATABASE = [$(MCP_DB)];
GO
USE [$(MCP_DB)];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(MCP_LOGIN)')
    CREATE USER [$(MCP_LOGIN)] FOR LOGIN [$(MCP_LOGIN)] WITH DEFAULT_SCHEMA = [dir];
GO
GRANT SELECT ON SCHEMA::[dir] TO [$(MCP_LOGIN)];
GRANT INSERT ON OBJECT::[dbo].[IA_LOG_CONSULTAS] TO [$(MCP_LOGIN)];
GO
-- Resumen de lo concedido (revisar a ojo):
SELECT dp.name AS Usuario, p.class_desc AS Alcance, p.permission_name AS Permiso, p.state_desc AS Estado,
       CASE p.class WHEN 3 THEN SCHEMA_NAME(p.major_id) WHEN 1 THEN OBJECT_NAME(p.major_id) END AS Sobre
FROM sys.database_permissions p
JOIN sys.database_principals dp ON dp.principal_id = p.grantee_principal_id
WHERE dp.name = N'$(MCP_LOGIN)'
ORDER BY p.class, p.permission_name;
GO
SET NOEXEC OFF;
GO
