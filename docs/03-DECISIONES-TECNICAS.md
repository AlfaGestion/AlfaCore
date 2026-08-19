# Decisiones Técnicas

## 2026-08-18 - Convivencia de proveedores para WhatsApp en Conversaciones

- La configuración de WhatsApp quedó separada en dos conceptos: `Meta Cloud API` y `WhatsApp Web`.
- Se agregaron claves `CONV_WHATSAPP_*` nuevas en `TA_CONFIGURACION` para persistir:
  `ProviderMode`, `DefaultProvider`, datos de sesión Web, estado de vinculación y número esperado.
- La operación real actual de AlfaCore sigue usando `Meta Cloud API` para envío y webhook.
- La sesión `WhatsApp Web` quedó documentada y preparada a nivel configuración para permitir una futura integración por QR o por número sin mezclar tokens, `phone_number_id` ni webhook oficial.
- La UI de `Conversaciones > Configuración` ahora expone explícitamente el modo:
  `Solo Meta`, `Solo Web` o `Meta + Web`, junto con el proveedor predeterminado.
- AlfaCore genera además un QR y un código de texto temporal para preparar el emparejamiento visual de la futura sesión Web; esos artefactos se guardan en `TA_CONFIGURACION` y hoy funcionan como scaffolding operativo, no como login nativo completo contra WhatsApp Web.

## 2026-08-03 - Configuración de AlfaKnowledge en TA_CONFIGURACION

- Se agregó la configuración de conexión de AlfaKnowledge al módulo `Conversaciones > Configuración`.
- Los parámetros `BaseUrl`, `ApiKey` y `TimeoutSeconds` se persisten en `dbo.TA_CONFIGURACION` con las claves `CONV_ALFAKNOWLEDGE_BASE_URL`, `CONV_ALFAKNOWLEDGE_API_KEY` y `CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS`.
- AlfaKnowledge quedó configurado en modo estricto por base: no toma `BaseUrl`, `ApiKey` ni `TimeoutSeconds` desde `appsettings.json`, `.env` ni variables de entorno.
- El servicio de sugerencias de Conversaciones solo funciona si la base activa tiene sus propias claves cargadas en `TA_CONFIGURACION`.
