-- MCP etapa 5 / script 11: login del ALMACEN OAuth (distinto del login de datos mcp_dir_ro).
-- Tiene permisos SOLO sobre las tablas MCP_*; no ve ventas, clientes ni ninguna tabla de AlfaCore.
-- Asi, si alguien compromete el login de datos no toca las credenciales, y viceversa.
-- NO contiene contrasena: se pasa por variable sqlcmd (en SSMS: Consulta > Modo SQLCMD y completar los :setvar).
--   sqlcmd -S <servidor> -f i:65001 -i 011_oauth_login_almacen.sql -v MCP_DB="<base_almacen>" MCP_AUTH_LOGIN="mcp_auth_rw" MCP_AUTH_PASSWORD="<contrasena fuerte>"
-- Ejecutar DESPUES de 010 y en la misma base donde se crearon las tablas MCP_*.
--
-- :setvar MCP_DB "<base_almacen>"
-- :setvar MCP_AUTH_LOGIN "mcp_auth_rw"
-- :setvar MCP_AUTH_PASSWORD "<contrasena fuerte>"
SET NOCOUNT ON;
GO
IF LEFT(N'$(MCP_AUTH_LOGIN)', 2) = N'$' + N'('
BEGIN
    RAISERROR(N'ABORTADO: las variables SQLCMD no fueron reemplazadas. En SSMS activar Consulta > Modo SQLCMD y completar los :setvar; o correr con sqlcmd -v.', 16, 1);
    SET NOEXEC ON;
END
GO
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(MCP_AUTH_LOGIN)')
    CREATE LOGIN [$(MCP_AUTH_LOGIN)] WITH PASSWORD = N'$(MCP_AUTH_PASSWORD)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF,
                                          DEFAULT_DATABASE = [$(MCP_DB)];
GO
USE [$(MCP_DB)];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(MCP_AUTH_LOGIN)')
    CREATE USER [$(MCP_AUTH_LOGIN)] FOR LOGIN [$(MCP_AUTH_LOGIN)];
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[MCP_USUARIO]       TO [$(MCP_AUTH_LOGIN)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[MCP_CLIENTE_OAUTH] TO [$(MCP_AUTH_LOGIN)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[MCP_CODIGO]        TO [$(MCP_AUTH_LOGIN)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[MCP_REFRESH_TOKEN] TO [$(MCP_AUTH_LOGIN)];
GRANT SELECT, INSERT, DELETE         ON OBJECT::[dbo].[MCP_EVENTO]        TO [$(MCP_AUTH_LOGIN)];
GO
-- Resumen de lo concedido (revisar a ojo: solo objetos MCP_*):
SELECT dp.name AS Usuario, p.permission_name AS Permiso, p.state_desc AS Estado, OBJECT_NAME(p.major_id) AS Sobre
FROM sys.database_permissions p
JOIN sys.database_principals dp ON dp.principal_id = p.grantee_principal_id
WHERE dp.name = N'$(MCP_AUTH_LOGIN)' ORDER BY OBJECT_NAME(p.major_id), p.permission_name;
GO
SET NOEXEC OFF;
GO
