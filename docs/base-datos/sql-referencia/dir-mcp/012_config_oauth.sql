-- MCP etapa 5 / script 12: claves IA_MCP_OAUTH_* en TA_CONFIGURACION de la base de DATOS (la misma de 004).
-- Idempotente: inserta solo las que faltan, nunca pisa ni borra. Los redirects permitidos son la lista blanca de aplicaciones
-- que pueden conectarse: una URI mal puesta aca es la forma de "abrirle la puerta" a una app ajena, asi que revisar con cuidado.
SET NOCOUNT ON;
GO
DECLARE @k TABLE (Clave nvarchar(100) PRIMARY KEY, Valor nvarchar(300), Descripcion nvarchar(500), Aux nvarchar(max));
INSERT INTO @k (Clave, Valor, Descripcion, Aux) VALUES
 (N'IA_MCP_OAUTH_ACCESS_TOKEN_MIN', N'60',  N'Vida del access token en minutos (5-1440). Un usuario desactivado deja de poder renovar al vencer.', NULL),
 (N'IA_MCP_OAUTH_REFRESH_DIAS',     N'30',  N'Vida del refresh token en dias (1-365). Pasado ese tiempo sin renovar hay que volver a iniciar sesion.', NULL),
 (N'IA_MCP_OAUTH_CODIGO_SEG',       N'60',  N'Segundos de validez del codigo de autorizacion (10-300).', NULL),
 (N'IA_MCP_OAUTH_MAX_INTENTOS',     N'5',   N'Intentos de login fallidos antes de bloquear el usuario (3-20).', NULL),
 (N'IA_MCP_OAUTH_BLOQUEO_MIN',      N'15',  N'Minutos de bloqueo del usuario tras superar los intentos (1-1440).', NULL),
 (N'IA_MCP_OAUTH_MAX_CLIENTES',     N'500', N'Maximo de clientes OAuth que se pueden registrar dinamicamente.', NULL),
 (N'IA_MCP_OAUTH_REQUIERE_TOTP',    N'SI',  N'SI = el login exige segundo factor (codigo de la app autenticadora). Un usuario sin segundo factor configurado NO puede ingresar. NO solo para pruebas.', NULL),
 (N'IA_MCP_OAUTH_REDIRECTS',        N'',    N'Lista blanca de redirect URIs que un cliente puede registrar (una por linea o separadas por coma). http solo en loopback; puerto comodin solo en loopback (http://localhost:*/callback).',
  N'https://claude.ai/api/mcp/auth_callback
https://claude.com/api/mcp/auth_callback
https://chatgpt.com/connector_platform_oauth_redirect
http://localhost:*/callback
http://127.0.0.1:*/callback');

INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, ValorAux, FechaHora_Grabacion)
SELECT N'IA_MCP', k.Clave, k.Valor, k.Descripcion, k.Aux, GETDATE()
FROM @k k
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE c.CLAVE = k.Clave);

SELECT CLAVE, VALOR, CAST(ValorAux AS nvarchar(max)) AS ValorAux FROM dbo.TA_CONFIGURACION WHERE CLAVE LIKE N'IA[_]MCP[_]OAUTH[_]%' ORDER BY CLAVE;
GO
