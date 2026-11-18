-- Configuración de Conversaciones más ordenada (2026-11-18):
--  * Seguridad > Accesos a Conversaciones: quién ve todos los números y qué usuarios atienden cada número
--    (antes en Conversaciones > Configuración > Operación y accesos). Mismas tablas: CONV_ADMINISTRADORES y
--    CONV_WHATSAPP_NUMERO_USUARIOS; no se mueve ningún dato.
--  * Permiso "Respuestas rápidas" dentro de Conversaciones: editar las respuestas rápidas sin poder configurar
--    el resto. Se da a quien ya entraba a Conversaciones (usuarios con permisos web cargados).
--  * Menú/permisos web propios (ALFACORE_MENU_WEB / ALFACORE_TAREAS_WEB); no escribe TA_MENU ni TA_TAREAS.
-- Idempotente. Compatible con compatibility level 80/100.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'DescripcionWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD DescripcionWeb nvarchar(300) NULL;
GO
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

EXEC sys.sp_executesql N'
DECLARE @m TABLE (Clave nvarchar(50), Padre nvarchar(50), Nombre nvarchar(150), Ruta nvarchar(200), Componente nvarchar(100), Icono nvarchar(60), Orden int, Descripcion nvarchar(300));
INSERT INTO @m VALUES
 (N''D015-WEB-ACCESOS-CONV'', N''D0150'', N''Accesos a Conversaciones'', N''/seguridad/accesos-conversaciones'', N''AccesosConversaciones'', N''bi-person-gear'', 15005,
  N''Quién ve todos los números de WhatsApp y qué usuarios atienden cada número.''),
 (N''D010185-WEB-CONV-RESPUESTAS'', N''D010185-WEB-CONVERSACIONES'', N''Respuestas rápidas'', NULL, NULL, N''bi-lightning'', 2,
  N''Crear y editar las respuestas rápidas del chat (se usan escribiendo ::), sin poder cambiar el resto de la configuración.'');

INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, DescripcionWeb)
SELECT N''ALFA'', m.Clave, m.Padre, m.Nombre, m.Ruta, m.Componente, m.Icono, 1, m.Orden, 0, m.Descripcion
FROM @m m
WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu = N''ALFA'' AND w.Clave = m.Clave);

UPDATE w
SET w.PadreClave = m.Padre, w.NombreWeb = m.Nombre, w.RutaWeb = m.Ruta, w.Componente = m.Componente,
    w.Icono = m.Icono, w.HabilitadoWeb = 1, w.OrdenWeb = m.Orden, w.DescripcionWeb = m.Descripcion
FROM dbo.ALFACORE_MENU_WEB w
JOIN @m m ON m.Clave = w.Clave
WHERE w.Menu = N''ALFA'';';
GO

-- Permisos iniciales (solo usuarios con permisos web propios; los que no tienen ninguno ya ven todo):
--  * Respuestas rápidas: quien ya entraba a Conversaciones.
--  * Accesos a Conversaciones: quien ya entraba a Autorización de tareas.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NULL RETURN;
DECLARE @soloPropias nvarchar(60) = CASE WHEN COL_LENGTH(N'dbo.ALFACORE_TAREAS_WEB', N'IdPerfil') IS NULL THEN N'' ELSE N' AND t.IdPerfil IS NULL' END;
DECLARE @sql nvarchar(max) = N'
INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
SELECT DISTINCT LTRIM(RTRIM(t.Usuario)), LTRIM(RTRIM(t.Sistema)), p.Nueva, GETDATE(), N''SYSTEM''
FROM dbo.ALFACORE_TAREAS_WEB t
JOIN (SELECT N''D010185-WEB-CONVERSACIONES'' AS Origen, N''D010185-WEB-CONV-RESPUESTAS'' AS Nueva
      UNION ALL SELECT N''D015002'', N''D015-WEB-ACCESOS-CONV'') p
  ON UPPER(LTRIM(RTRIM(t.Clave))) = p.Origen
WHERE NOT EXISTS (
      SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
        AND UPPER(LTRIM(RTRIM(x.Clave)))   = p.Nueva)' + @soloPropias + N';';
EXEC sys.sp_executesql @sql;
GO
