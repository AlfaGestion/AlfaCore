# Conversaciones

## Descripción del módulo

`Conversaciones` centraliza la gestión de mensajes dentro de `AlfaCore`. Soporta dos canales:

- **WHATSAPP** — inbox operativo para atender clientes por WhatsApp Cloud API
- **INTERNO** — chat entre técnicos del equipo, sin dependencia de WhatsApp

Funcionalidades disponibles hoy:

- inbox con filtros por canal, estado y técnico asignado
- historial de mensajes con soporte de texto, imágenes, archivos, audio y notas internas
- asignación y reasignación de conversaciones a técnicos
- gestión de estados (Abierta, Pendiente, En gestión, Cerrada, Archivada)
- adjuntos: imágenes, documentos y mensajes de voz grabados en el navegador
- hilos internos entre técnicos (sin número de teléfono requerido)
- configuración del canal WhatsApp desde pantalla interna

Integra estructuras existentes del sistema:

- clientes desde `VT_CLIENTES`
- técnicos desde `V_TA_Tecnicos`
- contactos desde `MA_CONTACTOS`
- logging técnico en `AUX_ERR`

## Canales

### WHATSAPP

Conversaciones iniciadas por clientes o por el equipo vía WhatsApp Cloud API de Meta. Requieren número de teléfono. El envío usa el endpoint de Meta configurado en `TA_CONFIGURACION`.

### INTERNO

Hilos de chat entre técnicos. No requieren número de teléfono ni configuración de Meta. Los mensajes se guardan localmente con `EstadoEnvio = ENVIADO` sin pasar por la API de WhatsApp. Soportan todos los tipos de adjunto igual que el canal WhatsApp.

## Arquitectura implementada

### Frontend

- Blazor Server sobre la estructura existente de `AlfaCore`
- pantalla `/conversaciones` con layout de tres paneles:
  - lista de conversaciones con canal switcher (WhatsApp / Chat interno)
  - chat central con historial, compositor, adjuntos y grabación de voz
  - panel lateral con contexto, asignación y estado
- modal para crear hilos internos
- `IJSRuntime` para grabación de audio via `MediaRecorder`

### Backend

- `ConversacionesService` — servicio principal con acceso a SQL Server
- `ConversacionesConfigService` — gestión de parámetros del canal WhatsApp
- minimal API endpoints registrados en `Program.cs`
- archivos de adjuntos almacenados en `App_Data/uploads/conversaciones/{IdConversacion}/`
- script JS en `wwwroot/js/conversaciones.js` para grabación de audio

### Base de datos

Tablas del módulo (`CONV_*`):

- `CONV_ESTADOS` — catálogo de estados (Abierta, Pendiente, En gestión, Cerrada, Archivada)
- `CONV_CONVERSACIONES` — conversaciones; `Canal` admite `WHATSAPP` e `INTERNO`; `TelefonoWhatsApp` es nullable
- `CONV_MENSAJES` — mensajes; `TelefonoWhatsApp` es nullable; `MessageType` admite TEXT, IMAGE, AUDIO, VIDEO, DOCUMENT y más
- `CONV_ASIGNACIONES` — historial de asignaciones por técnico
- `CONV_ADJUNTOS` — metadata de archivos adjuntos con ruta local y mime type
- `CONV_ETIQUETAS` / `CONV_CONVERSACION_ETIQUETAS` — etiquetas (estructura lista, UI pendiente)
- `CONV_WEBHOOK_LOG` — log de payloads recibidos desde Meta

## Adjuntos y mensajes de voz

### Almacenamiento

Los archivos se guardan en:

```
App_Data/uploads/conversaciones/{IdConversacion}/{guid}{ext}
```

El nombre físico es un GUID para evitar colisiones. El nombre original se persiste en `CONV_ADJUNTOS.NombreArchivo`.

### Tipos soportados

| TipoArchivo | Cómo se muestra |
|---|---|
| `IMAGE` | imagen inline con preview |
| `AUDIO` | reproductor `<audio controls>` |
| `VIDEO` | link de descarga (visualización futura) |
| `DOCUMENT` | link de descarga con tamaño |

### Grabación de voz

El navegador captura audio via `MediaRecorder` (script `conversaciones.js`). Al detener la grabación, el blob se convierte a base64, se envía al servicio como `MemoryStream` y se guarda como `audio/webm`.

Tamaño máximo por archivo: **25 MB**.

## Endpoints

### Inbox y conversaciones

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/conversaciones` | lista con filtros (modo, search, técnico, estado, canal) |
| GET | `/api/conversaciones/{id}` | detalle de una conversación |
| GET | `/api/conversaciones/{id}/mensajes` | mensajes de una conversación |
| POST | `/api/conversaciones/{id}/mensajes` | enviar mensaje (texto) |
| POST | `/api/conversaciones/{id}/notas` | agregar nota interna |
| POST | `/api/conversaciones/{id}/asignacion` | asignar técnico |
| POST | `/api/conversaciones/{id}/estado` | cambiar estado |

### Adjuntos

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/api/conversaciones/{id}/adjuntos` | subir archivo (multipart/form-data, campo `archivo`) |
| GET | `/api/conversaciones/adjuntos/{idAdjunto}` | servir archivo para display o descarga |

### Chat interno

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/api/conversaciones` | — (se crea desde Blazor via `CreateInternalThreadAsync`) |

### WhatsApp webhook

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/conversaciones/whatsapp/webhook` | verificación del webhook (Meta) |
| POST | `/api/conversaciones/whatsapp/webhook` | recepción de mensajes entrantes |

## Puesta en marcha en una base nueva

Ejecutar antes de usar el módulo:

```
docs/conversaciones_modelo_inicial.sql
```

Si falta alguna tabla `CONV_*`, la aplicación registra el incidente en `AUX_ERR` y muestra un mensaje operativo con los pasos sugeridos.

## Configuración del canal WhatsApp

Pantalla disponible: `/conversaciones/configuracion`

Persistencia: `TA_CONFIGURACION`

Claves del módulo:

- `CONV_WHATSAPP_VERIFY_TOKEN`
- `CONV_WHATSAPP_ACCESS_TOKEN`
- `CONV_WHATSAPP_PHONE_NUMBER_ID`
- `CONV_WHATSAPP_BUSINESS_ACCOUNT_ID`
- `CONV_WHATSAPP_APP_SECRET`
- `CONV_WHATSAPP_API_VERSION`
- `CONV_WHATSAPP_PUBLIC_BASE_URL`
- `CONV_WHATSAPP_WEBHOOK_PATH`

Manual operativo: `docs/conversaciones_whatsapp_conexion.md`

### Manejo de errores y conservación de estado

La pantalla de configuración usa un patrón base del proyecto para errores de UI:

- el error técnico completo sigue registrándose en `AUX_ERR` y diagnóstico local
- la UI muestra un mensaje claro para el usuario
- cuando existe, se conserva un código de incidente para soporte
- si falla una operación, el formulario no debe perder automáticamente lo que el usuario ya escribió

Caso ya contemplado:

- si la sesión SQL activa no puede conectarse, la UI informa que no se pudo conectar a la base activa y sugiere revisar sesión, red, instancia y credenciales

Este patrón debe extenderse al resto de módulos para mantener una experiencia consistente.

## Issues pendientes

### Issue 8. Completar flujo de envío WhatsApp

- Confirmar credenciales Meta reales en producción
- Validar formato final del payload contra API real
- Manejar respuestas y errores de Meta con reintentos
- Persistir `whatsapp_message_id` en respuestas exitosas

### Issue 9. Completar flujo de webhook

- Validar payload real recibido desde Meta con firma HMAC (`App Secret`)
- Confirmar parseo de mensajes entrantes en todos los tipos (imagen, audio, documento)
- Descargar y guardar en `CONV_ADJUNTOS` los adjuntos que envíe el cliente

### Issue 11. Tiempo real con SignalR

- Evaluar incorporación de SignalR
- Actualizar inbox y chat sin refresh manual
- Notificar cambios de asignación y estado en tiempo real

### Issue 12. Pruebas manuales y de integración

- Alta de conversación entrante por webhook real
- Respuesta saliente contra Meta
- Envío y recepción de adjuntos por WhatsApp
- Chat interno entre dos técnicos con adjunto y audio
- Cambio de técnico y estado
- Verificar registro correcto en `AUX_ERR` ante fallos

### Issue 13. Etiquetas

- UI para alta y asignación de etiquetas desde el panel de contexto
- Filtro por etiqueta en el inbox
- La estructura de tablas (`CONV_ETIQUETAS`, `CONV_CONVERSACION_ETIQUETAS`) ya existe

### Issue 14. Mejoras de visualización de adjuntos

- Preview de video inline
- Lightbox para imágenes
- Indicador de progreso durante la subida de archivos grandes
