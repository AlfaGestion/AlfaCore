# Ayuda global de usuario y chat documental

El icono **?** de `MainLayout` abre una única instancia de `AyudaContextualDialog`, junto a las notificaciones de la barra global. El acceso Ayuda del menú lateral abre el mismo panel en las pantallas legacy. No navega ni cambia datos de la operación y funciona también en `?directo=1`, sin enlaces a otros módulos. Se eliminaron los botones locales de Conciliación y Cobranza rápida.

## Manuales y contexto

La fuente única de cada tema es un manual funcional de `src/AlfaCore/Docs/`, incluido como EmbeddedResource. `AyudaManuales` registra explícitamente General, Consultas, Conciliación y Cobranzas; no admite rutas aportadas por el usuario. Divide por encabezados de nivel 2 y 3, genera identificadores estables y deshabilita HTML en Markdown. La ruta selecciona el tema; si no existe una guía específica, abre General. Los accesos `/ayuda` existentes se conservan.

## Chat

`IAyudaIaService` recupera hasta seis artículos por coincidencia de términos, priorizando títulos y el tema en empates. Para repreguntas incorpora la pregunta anterior hasta ocho artículos en total. Acota cada fragmento a 3500 caracteres, la pregunta a 2000, el historial enviado a cuatro pares y la respuesta a 2200 tokens. La conversación de la ventana conserva hasta doce pares; vive en memoria del componente y no se persiste.

Solo se envían pregunta, tema, historial reciente y fragmentos del catálogo. No se envían automáticamente nombres de clientes, comprobantes, saldos o movimientos. No hay herramientas de ejecución ni SQL. La respuesta estructurada debe citar IDs de los artículos enviados; fuentes inventadas o vacías se sustituyen por una respuesta de documentación insuficiente. Esta validación verifica las referencias, no garantiza la exactitud semántica de la IA: las fuentes quedan visibles para revisión. Se presenta la respuesta como texto, nunca HTML generado.

Reutiliza `OPENAI_API_KEY` y `OPENAI_MODEL` del mecanismo existente del servidor; conserva el fallback general de AlfaCore. No agrega configuración paralela ni claves en la base. Sin conexión configurada se mantienen disponibles las guías. El consumo se registra como `AYUDA` a través de `IIaUsoRecorder`; los fallos relevantes pasan por `IAppEventService` y AUX_ERR sin adjuntar preguntas ni credenciales.

Exige sesión autorizada antes del envío y la vuelve a verificar antes de presentar la respuesta. La solicitud admite cancelación y límite de 90 segundos. Cerrar el panel elimina la conversación y cancela la solicitud; navegar cierra la ayuda y cambiar de base o usuario recrea el componente. Abrir una fuente conserva el chat mientras la ventana siga abierta.

Implementación HTTP compatible con el cliente existente de AlfaCore. Referencia oficial: [JSON mode y validación de respuestas](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=chat).

## Incorporar otro módulo

1. Revisar documentación existente. Guardar el manual funcional en `src/AlfaCore/Docs/` y organizarlo por tareas verificadas.
2. Incluir el recurso en `AlfaCore.csproj` y registrar clave, nombre y recurso en `AyudaManuales.Temas`.
3. Asociar su ruta en `TemaParaRuta`; el icono global ya está disponible. No agregar botones locales ni duplicar manuales.
4. Actualizar documentación al cambiar funcionalidad. Probar búsqueda, preguntas, fuentes, cancelación, tamaños de pantalla y conservación de la operación activa.

Las pruebas usan respuestas HTTP controladas para verificar payload, límites, fuentes inválidas, autorización, cambio de sesión, medición y registro de errores; el renderizado del panel se comprueba en varios anchos de pantalla.
