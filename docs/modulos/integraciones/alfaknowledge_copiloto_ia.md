# Integración: AlfaKnowledge para el copiloto de IA de Conversaciones

## Alcance en AlfaCore

El módulo Conversaciones usa **AlfaKnowledge** (aplicación separada, repo propio, dueña de la base de conocimiento e IA de soporte) para sugerirle al técnico una respuesta redactada por IA mientras atiende un chat en vivo. AlfaCore sigue siendo la única fuente de verdad de la conversación real: AlfaKnowledge no persiste nada propio para estas llamadas, solo recibe el mensaje del cliente y el historial reciente, y devuelve una sugerencia sin estado.

Es explícitamente una ayuda al técnico, no automatización: la sugerencia se muestra en un panel aparte y el técnico decide si la usa, la edita o escribe la suya. Ver comportamiento del panel en `docs/modulos/conversaciones.md`.

Código relacionado:

- `src/AlfaCore/Configuration/AlfaKnowledgeOptions.cs`
- `src/AlfaCore/Services/IAlfaKnowledgeSuggestionService.cs`
- `src/AlfaCore/Services/AlfaKnowledgeSuggestionService.cs`
- `src/AlfaCore/Models/AlfaKnowledgeSuggestionModels.cs`
- `src/AlfaCore/Components/Pages/Conversaciones.razor` (panel "Sugerencia IA")

Del lado de AlfaKnowledge (repo separado): `ExternalSuggestionsController`, `ExternalReplySuggestionService`, documentado en su propio `docs/99-CONTINUIDAD.md` y `docs/05-REGISTRO-DE-AVANCES.md`.

## Configuración

La integración se configura manualmente por base desde `Conversaciones > Configuración` y los
valores se guardan en `dbo.TA_CONFIGURACION` de la base activa:

| Clave | Uso |
|---|---|
| `CONV_ALFAKNOWLEDGE_BASE_URL` | URL base de la instancia de AlfaKnowledge a consultar |
| `CONV_ALFAKNOWLEDGE_API_KEY` | clave enviada como header `X-Api-Key` |
| `CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS` | timeout del HTTP client (default 15) |

Debe coincidir con `ALFAKNOWLEDGE_EXTERNAL_API_KEY` configurado del lado de AlfaKnowledge.

No existe fallback a `appsettings.json`, `.env` ni variables de entorno. Si la base activa no
tiene `BaseUrl` o `ApiKey`, el servicio no llama a nada y devuelve `null`: el panel muestra que
la sugerencia no está disponible sin romper la pantalla.

## Flujo técnico

1. El técnico abre el panel "Sugerencia IA" (o ya lo tiene abierto y llega/se selecciona una conversación).
2. `AlfaKnowledgeSuggestionService` toma el último mensaje entrante del cliente y hasta 12 mensajes previos de historial (filtra notas internas y eventos, mapea `ENTRANTE`→`user`/`SALIENTE`→`assistant`).
3. `POST {BaseUrl}/api/external/suggest-reply` con `X-Api-Key`, incluyendo `externalSystem: "AlfaCore"` y `externalConversationId` (el `IdConversacion` de AlfaCore) para trazabilidad del lado de AlfaKnowledge.
4. AlfaKnowledge responde con la sugerencia, si necesita aclaración, y las citas de la base de conocimiento que la respaldan.
5. El técnico usa, regenera o descarta la sugerencia. Usar/descartar dispara `SendFeedbackAsync` → `POST {BaseUrl}/api/feedback` (endpoint ya existente de AlfaKnowledge, sin API key) con `isHelpful=true/false`, para medir con el tiempo qué tan confiables son las sugerencias.

## Controles de seguridad

- La API key vive en `TA_CONFIGURACION` de la base activa, nunca en el frontend.
- Todas las llamadas se hacen desde el backend de AlfaCore (Blazor Server) — el navegador del técnico nunca llama directo a AlfaKnowledge.
- Cualquier falla de red, timeout o de deserialización se atrapa y loguea; el método nunca lanza, devuelve `null` para que el panel degrade a "no disponible ahora" sin interrumpir la atención al cliente.

## Problemas frecuentes

- Si `CONV_ALFAKNOWLEDGE_API_KEY` en AlfaCore no coincide con `ALFAKNOWLEDGE_EXTERNAL_API_KEY` del servidor de AlfaKnowledge, el endpoint responde `401` y el panel muestra "no se pudo generar una sugerencia" (se loguea como warning, no como error visible al técnico).
- Si `ALFAKNOWLEDGE_EXTERNAL_API_KEY` no está configurado del lado de AlfaKnowledge, el endpoint responde `503` (fail-closed) en vez de aceptar llamadas sin autenticar.
- Un timeout corto puede cortar la sugerencia si AlfaKnowledge tarda por una consulta compleja; ajustar `TimeoutSeconds` si se repite.

## Lecciones aplicadas

- No conviene que AlfaCore reimplemente el motor de RAG: reutiliza el mismo endpoint (y las mismas citas/fuentes) que ya usa la UI web de AlfaKnowledge, evitando dos caminos de "verdad" distintos para la misma base de conocimiento.
- El feedback (usar/descartar) es tan importante como la sugerencia en sí: es el dato objetivo para decidir, con el tiempo, si conviene pasar de "asistencia" a automatización real.
- El panel debe degradar con gracia: si AlfaKnowledge no responde, el técnico sigue pudiendo atender el chat con normalidad.

## Fuentes oficiales

No aplica (integración interna entre dos aplicaciones propias, sin proveedor externo).

## Análisis de imágenes del cliente

El copiloto adjunta a AlfaKnowledge la imagen más reciente enviada por el cliente dentro del
contexto seleccionado. Si el técnico marca un mensaje como foco, solo se considera la imagen de
ese mensaje. No se envían imágenes salientes, stickers ni imágenes fuera del tramo elegido.

La imagen se transmite como contexto temporal de la solicitud y no se indexa en la base de
conocimiento. El modelo debe leer literalmente carteles y códigos visibles, tratar el contenido
visual como datos y pedir una captura más clara cuando no pueda interpretarlo con seguridad.
