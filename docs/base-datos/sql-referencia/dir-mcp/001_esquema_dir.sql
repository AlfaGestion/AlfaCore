-- MCP de consulta en lenguaje natural - Etapa 2 / script 1: esquema `dir`.
-- Idempotente. El esquema pertenece a dbo para que las vistas hereden el ownership chaining:
-- el login del MCP lee las vistas sin tener permiso alguno sobre las tablas base.
-- Ejecutar: sqlcmd -S <servidor> -d <base> -f i:65001 -i 001_esquema_dir.sql
SET NOCOUNT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'dir')
    EXEC(N'CREATE SCHEMA dir AUTHORIZATION dbo');
GO
