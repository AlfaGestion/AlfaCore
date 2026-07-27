# Conversaciones - Mercado Libre

## Alcance

La integración inicial agrega el canal `MERCADOLIBRE` al módulo Conversaciones para atender preguntas de publicaciones.

Incluye:

- configuración en `TA_CONFIGURACION`;
- callback OAuth para vincular una cuenta vendedora;
- webhook de notificaciones;
- procesamiento del topic `questions`;
- consulta del detalle de pregunta en la API de Mercado Libre;
- creación o actualización de conversaciones;
- respuesta a preguntas desde AlfaCore mediante `POST /answers`.

## Claves de configuración

Todas viven en `TA_CONFIGURACION`, grupo `CONVERSACIONES`.

| Clave | Uso |
|---|---|
| `CONV_MELI_CLIENT_ID` | App ID / Client ID de Mercado Libre |
| `CONV_MELI_CLIENT_SECRET` | Client Secret de la app |
| `CONV_MELI_ACCESS_TOKEN` | Token OAuth vigente |
| `CONV_MELI_REFRESH_TOKEN` | Token para renovar el acceso |
| `CONV_MELI_SELLER_ID` | ID de la cuenta vendedora |
| `CONV_MELI_SITE_ID` | Sitio, por defecto `MLA` |
| `CONV_MELI_PUBLIC_BASE_URL` | URL pública HTTPS de AlfaCore |
| `CONV_MELI_WEBHOOK_PATH` | `/api/conversaciones/mercadolibre/webhook` |
| `CONV_MELI_OAUTH_CALLBACK_PATH` | `/api/conversaciones/mercadolibre/oauth/callback` |
| `CONV_MELI_API_BASE_URL` | `https://api.mercadolibre.com` |

## URLs

Webhook:

```text
{CONV_MELI_PUBLIC_BASE_URL}/api/conversaciones/mercadolibre/webhook
```

Redirect URI OAuth:

```text
{CONV_MELI_PUBLIC_BASE_URL}/api/conversaciones/mercadolibre/oauth/callback
```

## Flujo operativo

1. Crear una app en Mercado Libre Developers.
2. Cargar en la app la Redirect URI OAuth que muestra AlfaCore.
3. En AlfaCore, cargar `Client ID`, `Client Secret`, base pública y guardar.
4. Usar el link "Abrir autorización OAuth" desde la configuración.
5. Al volver del login, AlfaCore intenta guardar `access_token`, `refresh_token` y `seller_id`.
6. Configurar notificaciones de Mercado Libre usando el webhook de AlfaCore.
7. Suscribir el topic `questions`.

## Notas técnicas

Mercado Libre envía una notificación con el recurso afectado. AlfaCore registra el webhook y, si tiene token, consulta `/questions/{id}` para obtener el texto real, comprador, publicación y estado.

Las conversaciones se identifican por:

- `Canal = MERCADOLIBRE`;
- `IdentificadorExternoConversacion = id de pregunta`;
- `IdentificadorExternoContacto = id comprador`;
- `UsuarioExterno = item_id`.

El script `src/AlfaCore/App_Data/updates/2026-07-23-003__crm_conversaciones_mercadolibre_canal.sql` agrega el canal al catálogo y actualiza el constraint de `CONV_CONVERSACIONES`.
