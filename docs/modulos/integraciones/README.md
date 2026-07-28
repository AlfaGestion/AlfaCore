# Integraciones de AlfaCore

Este directorio agrupa fichas técnicas e informativas de integraciones externas usadas o preparadas en AlfaCore.

El objetivo es que cada documento sirva para:

- soporte y mantenimiento del equipo;
- onboarding de nuevas implementaciones;
- base de conocimiento para una IA interna;
- material explicativo reutilizable con clientes, sin exponer secretos.

## Documentos

- [WhatsApp Cloud API](./whatsapp_cloud_api.md)
- [Instagram Messaging API](./instagram_messaging_meta.md)
- [Facebook Messenger Platform](./facebook_messenger_meta.md)
- [Mercado Libre](./mercado_libre.md)
- [OpenAI para InformesIA](./openai_informes_ia.md)
- [Google reCAPTCHA](./google_recaptcha.md)
- [SMTP y correo transaccional](./smtp_correo_transaccional.md)
- [Web Push y PWA](./web_push_pwa.md)
- [ARCA / AFIP](./arca_afip.md)

## Convenciones

- Las credenciales no se documentan con valores reales.
- Si la configuración vive en `TA_CONFIGURACION`, se documenta la clave.
- Si la configuración vive en `appsettings` o `.env`, se documenta la sección.
- Los problemas frecuentes incluyen lo aprendido durante la integración de AlfaCore, no solo errores genéricos de la API.
- Las fuentes oficiales se listan al final de cada ficha para validar cambios de proveedores.
