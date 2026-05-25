SET NOCOUNT ON;

IF COL_LENGTH('dbo.TA_MENU', 'Descripcion') IS NULL
    RETURN;

UPDATE dbo.TA_MENU
SET Descripcion = 'Maestros generales y accesos base del sistema.'
WHERE Clave = 'D01'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de compras, proveedores, artículos e informes relacionados.'
WHERE Clave = 'D50'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de ventas, facturación, clientes y cobranza pendiente.'
WHERE Clave = 'D60'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de caja y bancos, saldos, ingresos, egresos y pendientes financieros.'
WHERE Clave = 'D75'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de stock, valorización, análisis de existencias y artículos críticos.'
WHERE Clave = 'D80'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de contabilidad, visión gerencial del debe, haber y actividad contable.'
WHERE Clave = 'D95'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Utilidades, herramientas auxiliares y accesos complementarios del sistema.'
WHERE Clave = 'D98'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Alta, edición y consulta del maestro comercial de clientes.'
WHERE Clave = 'D010105'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Alta, edición y consulta del maestro comercial de proveedores.'
WHERE Clave = 'D010110'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Agenda web de contactos, personas y datos de comunicación.'
WHERE Clave = 'D010170'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Gestión web de usuarios, permisos y accesos del sistema.'
WHERE Clave = 'D015001'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Auditoría técnica, operativa y de usuarios sobre procesos del sistema.'
WHERE Clave = 'D015003'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Recepción documental, clasificación y procesamiento automático de comprobantes.'
WHERE Clave = 'D0160'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Configuración de destinos, reglas y lectura automática del módulo Interfaces.'
WHERE Clave = 'D016001'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de compras y acceso a comprobantes comerciales del circuito.'
WHERE Clave = 'D5001'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de ventas y acceso a comprobantes comerciales del circuito.'
WHERE Clave = 'D6000'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de caja y bancos con consultas operativas y financieras.'
WHERE Clave = 'D7510'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de stock con acceso a existencias, fichas y análisis.'
WHERE Clave = 'D8079'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Dashboard de contabilidad con posiciones, consultas y análisis contables.'
WHERE Clave = 'D9502'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Calendario operativo, guardias y planificación visual.'
WHERE Clave = 'D9808'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';

UPDATE dbo.TA_MENU
SET Descripcion = 'Diseñador y ejecución de consultas web guardadas del sistema.'
WHERE Clave = 'D9820'
  AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), '') = '';
