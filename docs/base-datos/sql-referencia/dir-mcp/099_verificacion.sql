-- MCP de consulta en lenguaje natural - verificacion posterior (solo SELECT).
-- Correr despues de la actualizacion 2026-11-27-040__ia_mcp_esquema_dir.sql y del 005. Parte A: calidad de datos. Parte B: prueba de permisos del login (EXECUTE AS).
-- sqlcmd -S <servidor> -d <base> -f i:65001 -i 099_verificacion.sql -v MCP_LOGIN="mcp_dir_ro"
SET NOCOUNT ON;
GO
-- A1. Objetos creados.
SELECT s.name AS Esquema, o.name AS Objeto, o.type_desc AS Tipo
FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE s.name = N'dir' OR o.name = N'IA_LOG_CONSULTAS' ORDER BY s.name, o.name;
GO
-- A2. Porcentaje de lineas sin costo por mes (ultimos 13 meses). Si es alto, el margen de esos meses no es confiable.
SELECT AnioMes, COUNT(*) AS Lineas, SUM(SinCosto) AS LineasSinCosto,
       CAST(100.0 * SUM(SinCosto) / COUNT(*) AS decimal(5,2)) AS PctSinCosto
FROM dir.vw_Ventas
WHERE Fecha >= DATEADD(month, -13, GETDATE())
GROUP BY AnioMes ORDER BY AnioMes;
GO
-- A3. Cuadre de la vista contra el libro oficial (debe dar diferencia 0 por tipo). Fechas en formato yyyymmdd
--     (el literal 'yyyy-mm-dd' se interpreta como a-d-m en bases con idioma espanol).
SELECT v.Tc, v.NetoVista, l.NetoLibro, v.NetoVista - l.NetoLibro AS Diferencia
FROM (SELECT Tc, SUM(NetoSinIva) AS NetoVista FROM dir.vw_Ventas
      WHERE Fecha >= '20260101' AND Fecha < '20260201' GROUP BY Tc) v
LEFT JOIN (SELECT LTRIM(RTRIM(TC)) AS Tc, SUM(IMPORTE_SINIVA) AS NetoLibro FROM dbo.Libro_VentasConFP
           WHERE FECHA >= '20260101' AND FECHA < '20260201' GROUP BY LTRIM(RTRIM(TC))) l ON l.Tc = v.Tc
ORDER BY v.Tc;
GO
-- (La parte A ya corrio.) Para la parte B hace falta el login de 005 y la variable MCP_LOGIN:
-- :setvar MCP_LOGIN "mcp_dir_ro"   (SSMS en Modo SQLCMD: quitar el '-- ' inicial)
-- GUARDA: estas variables solo las reemplaza sqlcmd (-v) o SSMS con "Consulta > Modo SQLCMD" activado.
-- Si no, quedan como texto literal y el script abortaria a medias: aca se corta antes de tocar nada.
IF LEFT(N'$(MCP_LOGIN)', 2) = N'$' + N'('
BEGIN
    RAISERROR(N'ABORTADO la parte B (permisos del login): las variables SQLCMD no fueron reemplazadas. En SSMS activar Consulta > Modo SQLCMD y completar los :setvar de arriba; o correr con sqlcmd -v.', 16, 1);
    SET NOEXEC ON;
END
GO
-- B. Prueba de permisos del login dedicado. Esperado: dir.* = 1 ; tablas base = 0 ; log = INSERT 1 / SELECT 0.
EXECUTE AS USER = N'$(MCP_LOGIN)';
SELECT HAS_PERMS_BY_NAME(N'dir.vw_Ventas',        N'OBJECT', N'SELECT') AS PuedeLeer_dir_vw_Ventas,
       HAS_PERMS_BY_NAME(N'dbo.V_MV_Cpte',        N'OBJECT', N'SELECT') AS PuedeLeer_tabla_base_V_MV_Cpte,
       HAS_PERMS_BY_NAME(N'dbo.TA_CONFIGURACION', N'OBJECT', N'SELECT') AS PuedeLeer_TA_CONFIGURACION,
       HAS_PERMS_BY_NAME(N'dbo.IA_LOG_CONSULTAS', N'OBJECT', N'INSERT') AS PuedeInsertar_log,
       HAS_PERMS_BY_NAME(N'dbo.IA_LOG_CONSULTAS', N'OBJECT', N'SELECT') AS PuedeLeer_log,
       HAS_PERMS_BY_NAME(N'dir.vw_Ventas',        N'OBJECT', N'UPDATE') AS PuedeEscribir_dir;
BEGIN TRY
    SELECT TOP 1 Tc, Fecha, NetoSinIva FROM dir.vw_Ventas;   -- debe funcionar (ownership chaining)
    SELECT TOP 1 * FROM dbo.V_MV_Cpte;                       -- debe FALLAR (sin permiso)
    SELECT 'ERROR: el login pudo leer una tabla base' AS Resultado;
END TRY
BEGIN CATCH
    SELECT 'OK: acceso directo a la tabla base denegado' AS Resultado, ERROR_MESSAGE() AS Detalle;
END CATCH
REVERT;
GO
SET NOEXEC OFF;
GO
