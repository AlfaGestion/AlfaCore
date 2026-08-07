# Integración: AlfaKnowledge

## Objetivo

AlfaKnowledge funciona como copiloto del módulo Conversaciones. Cuando llega un mensaje del
cliente, AlfaCore puede pedir una respuesta sugerida basada exclusivamente en conocimiento
indexado y devolverla al técnico con sus fuentes.

La integración es asistida:

- no envía mensajes automáticamente;
- el técnico puede usar, editar o descartar la sugerencia;
- AlfaCore conserva la conversación real;
- AlfaKnowledge registra la interacción y el feedback para medir calidad.

## Contrato

AlfaCore llama:

```text
POST {CONV_ALFAKNOWLEDGE_BASE_URL}/api/external/suggest-reply
X-Api-Key: {CONV_ALFAKNOWLEDGE_API_KEY}
```

Envía:

- mensaje entrante más reciente o mensaje marcado por el técnico;
- hasta 60 mensajes entrantes o salientes del alcance seleccionado;
- instrucción escrita por el técnico, cuando usa el chat del asistente;
- hasta 12 turnos recientes del chat técnico/IA;
- sistema externo `AlfaCore`;
- identificador de conversación;
- límite de cuatro fragmentos.

AlfaKnowledge devuelve:

- `interactionId`;
- respuesta sugerida;
- suficiencia de contexto;
- pregunta aclaratoria, cuando corresponde;
- citas utilizadas.

El feedback se registra mediante:

```text
POST {CONV_ALFAKNOWLEDGE_BASE_URL}/api/feedback
```

## Configuración

La configuración se carga manualmente por base desde `Conversaciones > Configuración` y se guarda
en `dbo.TA_CONFIGURACION` de la base activa:

- `CONV_ALFAKNOWLEDGE_BASE_URL`
- `CONV_ALFAKNOWLEDGE_API_KEY`
- `CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS`

La clave debe coincidir con `ALFAKNOWLEDGE_EXTERNAL_API_KEY` en la instalación de AlfaKnowledge.
No documentar ni copiar su valor real.

No hay fallback a `appsettings.json`, `.env` ni variables de entorno. Si una base no tiene estas
claves, el copiloto queda deshabilitado solo en esa base.

## Estado productivo al 2026-07-29

- AlfaKnowledge: `http://10.8.0.32:5000`.
- AlfaCore público: `https://alfanetweb.ddns.net/`.
- AlfaCore nuevo corre como servicio Windows `AlfaCore`, con backend en puerto `5056`.
- IIS publica el sitio y deriva tráfico al backend mediante URL Rewrite.
- La versión anterior permanece temporalmente disponible en `5055` para rollback.
- Respaldo previo: `C:\AlfaCore\DeployBackups\AlfaCore-pre-knowledge-20260729-1512`.
- Versión desplegada: `C:\Program Files\Alfa Gestion\AlfaCore-20260729`.

Validación realizada:

- endpoint de AlfaKnowledge protegido con API key;
- migración SQL `012` aplicada;
- sugerencia productiva con contexto suficiente y cita;
- feedback registrado;
- recurso CSS público con el panel `Sugerencia IA`;
- servicio `AlfaCore` en estado `RUNNING`.
- claves de AlfaKnowledge cargadas en `TA_CONFIGURACION` de la base habilitada.

## Prueba funcional

1. Entrar a `https://alfanetweb.ddns.net/`.
2. Abrir `Conversaciones`.
3. Seleccionar una conversación con un mensaje entrante de texto.
4. Pulsar `Sugerencia IA`.
5. Verificar respuesta sugerida y fuentes.
6. Usar o descartar la sugerencia.
7. Confirmar que la atención normal sigue funcionando aunque AlfaKnowledge no responda.

El panel permite elegir:

- **Tramo actual**: mensajes posteriores al último cierre detectado; si la conversación está
  cerrada, toma el tramo comprendido entre los dos últimos cierres;
- **Toda la conversación**: todos los mensajes cargados del hilo;
- **Mensaje marcado**: un único mensaje elegido desde la acción con estrellas de su burbuja.

El técnico también puede escribirle instrucciones a la IA, por ejemplo para resumir el caso,
detectar información faltante o pedir otra redacción. Ese intercambio se mantiene separado de la
conversación del cliente y nunca se envía automáticamente.

Comportamiento del panel:

- queda abierto y acoplado al lateral hasta que el técnico lo cierre explícitamente;
- en escritorio el panel es una tercera columna real del área de conversaciones: el hilo se adapta
  y ningún mensaje queda debajo del asistente;
- usar o descartar una respuesta no cierra el asistente;
- al cerrar el panel se cancela cualquier consulta en curso y no se generan nuevas consultas
  mientras permanezca cerrado;
- cada fuente abre una pestaña nueva; para documentos locales usa el visor de AlfaKnowledge y
  para fuentes sin URL directa abre la búsqueda por título;
- `Abrir AlfaKnowledge completo` lleva a la aplicación original con Chat, Historial, Buscar,
  Artículos, Curado y Administración.

Las instrucciones del técnico se usan como consulta documental principal. El historial del cliente
se conserva como contexto de redacción, pero ya no contamina la búsqueda con temas anteriores.

En la sugerencia automática, una pregunta nueva y completa se busca por sí sola para que temas
anteriores del hilo no desvíen la documentación recuperada. El historial se incorpora a la búsqueda
solo cuando el mensaje es una repregunta o referencia breve, por ejemplo `¿y eso dónde?`.

El panel distingue:

- servidor sin configuración;
- conversación sin mensajes entrantes de texto;
- AlfaKnowledge sin respuesta;
- sugerencia generada correctamente.

## Rollback operativo

Si la versión nueva presenta un problema:

1. cambiar en el `web.config` de la instalación anterior `127.0.0.1:5056` por
   `127.0.0.1:5055`;
2. validar nuevamente `https://alfanetweb.ddns.net/`;
3. conservar la carpeta nueva y los logs para diagnóstico;
4. no borrar el respaldo previo.

El cambio de proxy basta para volver al backend anterior; no requiere restaurar datos.
