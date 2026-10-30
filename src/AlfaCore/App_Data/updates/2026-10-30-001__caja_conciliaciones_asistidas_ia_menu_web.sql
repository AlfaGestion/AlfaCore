-- Caja y Bancos: nombres de las conciliaciones con IA en el menú web.
--   D75-CONCILIA-IA: "Conciliación bancaria inteligente" -> "Conciliación bancaria asistida con IA"
--   D75-TARJETAS-IA: "Conciliación de tarjetas"          -> "Conciliación de tarjetas asistida con IA"
-- Solo ALFACORE_MENU_WEB (no toca TA_MENU / TA_TAREAS del legacy). Idempotente.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB SET NombreWeb=N''Conciliación bancaria asistida con IA''
 WHERE Clave=N''D75-CONCILIA-IA'' AND ISNULL(NombreWeb,N'''')<>N''Conciliación bancaria asistida con IA'';
UPDATE dbo.ALFACORE_MENU_WEB SET NombreWeb=N''Conciliación de tarjetas asistida con IA''
 WHERE Clave=N''D75-TARJETAS-IA'' AND ISNULL(NombreWeb,N'''')<>N''Conciliación de tarjetas asistida con IA'';';
