# Integración: Mercado Libre

## Alcance en AlfaCore

Mercado Libre está integrado como canal `MERCADOLIBRE` en Conversaciones para atender preguntas de publicaciones. AlfaCore soporta OAuth, webhook de notificaciones, procesamiento del topic `questions`, consulta de detalle de preguntas, enriquecimiento con datos de publicación/comprador y respuesta mediante API.

Código relacionado:

- `src/AlfaCore/Services/ConversacionesService.cs`
- `src/AlfaCore/Services/ConversacionesConfigService.cs`
- `src/AlfaCore/Models/ConversacionesConfiguracionModels.cs`
- `src/AlfaCore/App_Data/updates/2026-07-23-003__crm_conversaciones_mercadolibre_canal.sql`

Documento histórico relacionado: `docs/modulos/conversaciones_mercadolibre.md`.

## Configuración

La configuración vive en `TA_CONFIGURACION`, grupo `CONVERSACIONES`.

| Clave | Uso |
|---|---|
| `CONV_MELI_CLIENT_ID` | App ID / Client ID |
| `CONV_MELI_CLIENT_SECRET` | Client Secret |
| `CONV_MELI_ACCESS_TOKEN` | Access token OAuth vigente |
| `CONV_MELI_REFRESH_TOKEN` | Refresh token |
| `CONV_MELI_SELLER_ID` | ID de la cuenta vendedora |
| `CONV_MELI_SITE_ID` | Sitio, por defecto `MLA` |
| `CONV_MELI_PUBLIC_BASE_URL` | Base pública HTTPS de AlfaCore |
| `CONV_MELI_WEBHOOK_PATH` | `/api/conversaciones/mercadolibre/webhook` |
| `CONV_MELI_OAUTH_CALLBACK_PATH` | `/api/conversaciones/mercadolibre/oauth/callback` |
| `CONV_MELI_API_BASE_URL` | `https://api.mercadolibre.com` |

## Flujo técnico

1. Se crea una aplicación en Mercado Libre Developers.
2. AlfaCore arma la Redirect URI OAuth.
3. El vendedor autoriza la app.
4. Mercado Libre redirige a `/api/conversaciones/mercadolibre/oauth/callback`.
5. AlfaCore intercambia el `code` por tokens y guarda `access_token`, `refresh_token` y `seller_id`.
6. Mercado Libre envía notificaciones al webhook.
7. Para `questions`, AlfaCore consulta el recurso de la pregunta.
8. La conversación se identifica por el ID de pregunta.
9. La respuesta sale por el endpoint de respuestas de preguntas.

## Mapeo interno

- `Canal = MERCADOLIBRE`
- `IdentificadorExternoConversacion = id de pregunta`
- `IdentificadorExternoContacto = id comprador`
- `UsuarioExterno = item_id`

## Problemas frecuentes

- La Redirect URI debe coincidir exactamente con la registrada en la app de Mercado Libre.
- Si el `access_token` vence y no se renueva, el webhook puede llegar pero no se puede consultar el detalle real de la pregunta.
- Mercado Libre puede enviar notificaciones con un recurso y no con todo el contenido; AlfaCore debe consultar el detalle antes de mostrarlo como conversación completa.
- Las preguntas respondidas y no respondidas pueden aparecer por endpoints/filtros distintos; AlfaCore contempla más de una búsqueda para robustez.
- Hay que separar pregunta, comprador y publicación: el texto operativo está en la pregunta, pero el contexto comercial está en el item.

## Lecciones aplicadas en AlfaCore

- Guardar `ApiBaseUrl` permite aislar cambios de endpoint o ambientes sin tocar código.
- El webhook no debe depender de tener token perfecto para registrar la notificación; conviene guardar y degradar con diagnóstico.
- La conversación por pregunta evita mezclar consultas de distintos productos o publicaciones en un hilo único por comprador.
- Conviene enriquecer con item y buyer de forma tolerante: si falla un dato secundario, la pregunta igual debe ingresar.

## Fuentes oficiales

- [Mercado Libre Developers: documentación general](https://developers.mercadolibre.com.ar/es_ar/api-docs-es)
- [Mercado Libre Developers: autenticación y autorización](https://developers.mercadolibre.com.ar/es_ar/autenticacion-y-autorizacion)
- [Mercado Libre Developers: preguntas y respuestas](https://developers.mercadolibre.com.ar/es_ar/preguntas-y-respuestas)
- [Mercado Libre Developers: notificaciones](https://developers.mercadolibre.com.ar/es_ar/notificaciones)
