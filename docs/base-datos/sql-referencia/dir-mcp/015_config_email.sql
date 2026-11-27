-- MCP / script 15: claves del ingreso por mail (IA_MCP_OAUTH_EMAIL_*). Opcional: si faltan, valen los mismos valores por defecto.
-- Va en la base POR DEFECTO del MCP (la de ALFACORE_MCP_CONNECTION, donde ya estan las IA_MCP_OAUTH_* del script 012):
-- la pantalla de ingreso se muestra antes de saber de que base es el usuario.
-- Idempotente: inserta solo las que faltan; nunca pisa ni borra.
--   sqlcmd -S <servidor> -d <base por defecto> -f i:65001 -i 015_config_email.sql
SET NOCOUNT ON;
GO
DECLARE @k TABLE (Clave nvarchar(100) PRIMARY KEY, Valor nvarchar(300), Descripcion nvarchar(500));
INSERT INTO @k (Clave, Valor, Descripcion) VALUES
 (N'IA_MCP_OAUTH_EMAIL_HABILITADO',     N'SI', N'SI = la pantalla de conexion pide el mail y manda un codigo de 6 digitos (clientes). NO = solo usuario y clave. Requiere el SMTP del servidor (Mcp__Email__*).'),
 (N'IA_MCP_OAUTH_EMAIL_CODIGO_MIN',     N'10', N'Minutos de validez del codigo enviado por mail.'),
 (N'IA_MCP_OAUTH_EMAIL_MAX_ENVIOS',     N'3',  N'Maximo de codigos por mail cada 15 minutos (evita usar el formulario para llenar casillas).'),
 (N'IA_MCP_OAUTH_EMAIL_AVISO_CONEXION', N'SI', N'SI = cada conexion nueva manda un mail de aviso al usuario (si no fue el, avisa a ALFA NET).');

INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
SELECT N'IA_MCP', k.Clave, k.Valor, k.Descripcion, GETDATE()
FROM @k k
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE c.CLAVE = k.Clave);

SELECT CLAVE, VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE LIKE N'IA[_]MCP[_]OAUTH[_]EMAIL[_]%' ORDER BY CLAVE;
GO
