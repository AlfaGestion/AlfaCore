# Decisiones Técnicas

## 2026-08-03 - Configuración de AlfaKnowledge en TA_CONFIGURACION

- Se agregó la configuración de conexión de AlfaKnowledge al módulo `Conversaciones > Configuración`.
- Los parámetros `BaseUrl`, `ApiKey` y `TimeoutSeconds` se persisten en `dbo.TA_CONFIGURACION` con las claves `CONV_ALFAKNOWLEDGE_BASE_URL`, `CONV_ALFAKNOWLEDGE_API_KEY` y `CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS`.
- AlfaKnowledge quedó configurado en modo estricto por base: no toma `BaseUrl`, `ApiKey` ni `TimeoutSeconds` desde `appsettings.json`, `.env` ni variables de entorno.
- El servicio de sugerencias de Conversaciones solo funciona si la base activa tiene sus propias claves cargadas en `TA_CONFIGURACION`.
