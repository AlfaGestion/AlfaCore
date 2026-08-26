# Manual operativo — Conversaciones con WhatsApp Cloud API en SaaS

## Objetivo

Esta guía permite configurar y validar WhatsApp Cloud API en AlfaCore sin mezclar credenciales,
bases ni números. Incluye el cambio seguro de la Base pública HTTPS, la operación multinúmero,
la asignación de usuarios, la administración del módulo y la habilitación gradual de IA.

Está pensada para realizar el trabajo acompañado por soporte o por GPT. Nunca se deben pegar en
un chat, captura o documento el Access Token, App Secret, Verify Token ni el token del webhook de
una base real.

## 1. Conceptos que no se deben confundir

| Dato | Quién lo define | Para qué sirve |
|---|---|---|
| Base pública HTTPS | Infraestructura de AlfaCore | Dominio público al que Meta enviará los webhooks. |
| Token del webhook de la base | AlfaCore SaaS | Identifica la base antes de abrir una sesión de usuario. Va en la ruta del callback. |
| Verify Token | El equipo que configura la integración | Meta lo envía durante la verificación y AlfaCore lo compara. No es el Access Token. |
| Access Token | Meta | Autoriza llamadas salientes de AlfaCore a Graph API. |
| Phone Number ID | Meta | Identifica técnicamente el número que envía o recibe. No es el número telefónico visible. |
| WABA ID | Meta | Identifica la cuenta WhatsApp Business Account. |
| App Secret | Meta | Se reserva para validar la firma de los webhooks. |

En SaaS, el callback tiene esta forma:

```text
https://DOMINIO_PUBLICO/api/conversaciones/whatsapp/webhook/TOKEN_WEBHOOK_BASE
```

El Verify Token se carga en un campo separado en Meta. El Access Token nunca va en la URL.

## 2. Estado conocido de AlfaCore

AlfaCore actualmente soporta:

- recepción y verificación del webhook con token por base;
- registro de eventos en `CONV_WEBHOOK_LOG`;
- varios Phone Number ID dentro de una base;
- conversación e historial separados por número receptor;
- filtro del inbox por número, persistido por usuario;
- asignación de uno o más usuarios a cada número;
- administradores propios de Conversaciones;
- envío por el número asociado a la conversación;
- respuesta fija fuera de horario;
- copiloto de IA y bot autónomo con guardarraíles.

Las credenciales Meta (`Access Token` y WABA) son globales para la base. Antes de incorporar un
número perteneciente a otra WABA o que necesite otro Access Token, se debe validar el diseño con
desarrollo.

## 3. Inventario previo obligatorio

Completar antes de modificar Meta o AlfaCore. No anotar secretos completos.

| Campo | Valor o referencia |
|---|---|
| Cliente/base | |
| Base pública actual | |
| Base pública nueva | |
| Aplicación de Meta | |
| WABA ID | |
| Número visible | |
| Phone Number ID | |
| Access Token estable confirmado | Sí / No |
| Verify Token coincidente | Sí / No |
| Token de webhook de la base confirmado | Sí / No |
| Usuarios asignados | |
| Administradores de Conversaciones | |
| Fecha y responsable del cambio | |

Antes de continuar:

- comprobar que existe una copia de seguridad reciente;
- conservar la URL anterior durante el período de prueba;
- comprobar DNS, certificado HTTPS, firewall y publicación de la aplicación;
- acordar una ventana de cambio y un responsable de rollback;
- registrar el último mensaje entrante y saliente correcto como línea base.

## 4. Cambio seguro de Base pública HTTPS

### 4.1 Preparar el dominio nuevo

1. Publicar AlfaCore en el dominio nuevo sin retirar el anterior.
2. Abrir la nueva Base pública HTTPS desde una red externa.
3. Verificar que el certificado sea válido, no esté vencido y contenga el nombre del dominio.
4. Confirmar que no haya redirecciones hacia `localhost`, una IP privada o el dominio anterior.
5. Obtener de AlfaCore el callback completo con token de base.

### 4.2 Probar la verificación antes de cambiar Meta

Abrir una URL de prueba con valores reales, sin guardar el Verify Token en el historial del
navegador si el equipo es compartido:

```text
https://DOMINIO_PUBLICO/api/conversaciones/whatsapp/webhook/TOKEN_WEBHOOK_BASE?hub.mode=subscribe&hub.verify_token=VERIFY_TOKEN&hub.challenge=12345
```

Resultado esperado:

```text
12345
```

Si no devuelve exactamente el challenge, no cambiar todavía el callback en Meta.

### 4.3 Cambiar el callback

1. Entrar a Meta for Developers con una cuenta autorizada.
2. Seleccionar la aplicación correcta y el producto WhatsApp.
3. Abrir la configuración de webhooks de WhatsApp.
4. Reemplazar la Callback URL por la URL tokenizada de la base.
5. Informar el mismo Verify Token configurado en AlfaCore.
6. Completar la verificación.
7. Confirmar que estén suscriptos los eventos de mensajes y estados necesarios.
8. No retirar todavía el dominio anterior.

### 4.4 Validar tráfico real

1. Enviar un mensaje desde un teléfono externo al número de WhatsApp.
2. Confirmar que aparece una sola vez en la base correcta.
3. Revisar que el webhook registre el Phone Number ID esperado.
4. Responder desde AlfaCore.
5. Confirmar que la respuesta llega una sola vez.
6. Revisar `CONV_WEBHOOK_LOG` y `AUX_ERR`.
7. Repetir con texto y un adjunto pequeño.

### 4.5 Rollback

Si deja de entrar tráfico después del cambio:

1. volver a registrar en Meta el callback anterior;
2. verificar nuevamente con el mismo Verify Token correspondiente;
3. enviar un mensaje de control;
4. conservar logs y horarios del incidente;
5. no borrar registros ni modificar conversaciones para ocultar el problema;
6. revisar DNS, certificado, proxy, token de base y selección de aplicación/WABA.

Retirar el dominio anterior únicamente cuando el nuevo haya recibido y respondido tráfico real
durante el período de observación acordado.

## 5. Configuración inicial en Meta

La interfaz de Meta cambia con frecuencia. Si los nombres visuales no coinciden exactamente, usar
como guía los conceptos e IDs y consultar los enlaces oficiales del final.

1. Ingresar a Meta for Developers.
2. Crear o seleccionar la aplicación empresarial correcta.
3. Agregar o abrir el producto WhatsApp.
4. Confirmar la WhatsApp Business Account correspondiente al cliente.
5. Incorporar y verificar el número telefónico.
6. Esperar la aprobación del display name antes de considerar productivo el envío.
7. Registrar el WABA ID y Phone Number ID, sin confundirlos con el teléfono visible.
8. Crear un usuario del sistema y un token estable con los permisos requeridos para WhatsApp.
9. Evitar tokens temporales para producción.
10. Configurar el callback tokenizado y el Verify Token.
11. Suscribir eventos de mensajes y estados.
12. Realizar una prueba entrante y una saliente.

Si Meta devuelve `(#131037) WhatsApp provided number needs display name approval before message
can be sent`, AlfaCore ya llegó a Meta correctamente, pero Meta bloquea el envío hasta aprobar el
nombre visible del número.

## 6. Configuración dentro de AlfaCore

Ruta habitual:

```text
/conversaciones/configuracion
```

### 6.1 Canal WhatsApp

1. Abrir `Configuración → Canales → WhatsApp API`.
2. Si el alta automática está habilitada en Development, usar `Conectar con Meta`. El asistente autoriza la cuenta; la importación automática de números quedará para una etapa posterior.
3. `Agregar por Phone Number ID` continúa disponible como alternativa manual.
4. Seleccionar `Integración Meta`.
5. Elegir `Editar integración`.
6. Revisar proveedor, WABA ID, Base pública HTTPS y versión de Graph API.
7. Usar `Reemplazar` únicamente para la credencial que realmente deba cambiarse.
8. Guardar desde la barra superior.
9. Usar `Copiar callback`; AlfaCore no muestra en pantalla la URL tokenizada completa.

### 6.2 Secretos

- No compartir Access Token, App Secret, Verify Token ni token de base por chat.
- No guardarlos en capturas, tickets, commits ni manuales.
- Si un secreto fue expuesto, rotarlo en Meta o AlfaCore según corresponda.
- AlfaCore muestra únicamente `Configurado` o `No configurado`; no muestra el valor persistido.
- Un secreto solo se modifica después de elegir `Reemplazar` y escribir un valor nuevo.
- Cancelar la edición o cancelar el reemplazo conserva exactamente la credencial anterior.

## 7. Alta de varios números Meta Cloud API

Para cada número:

1. Abrir `Configuración → Canales → WhatsApp API`.
2. Elegir `Agregar por Phone Number ID`.
3. Escribir un nombre operativo claro, por ejemplo `Soporte AlfaNet - API`.
4. Cargar el Phone Number ID, no el teléfono visible.
5. Guardar desde la barra superior.
6. Seleccionar el número creado y elegir `Asignar usuarios` si debe ser restringido.
7. Enviar un mensaje real al número para que AlfaCore confirme el Phone Number ID del webhook.

Reglas:

- el mismo cliente escribiendo a dos números genera dos conversaciones separadas;
- el historial queda ligado al número receptor;
- el filtro superior no cambia el número de la conversación;
- un número sin usuarios asignados queda visible para todos;
- la clasificación entre WhatsApp API y WhatsApp Business depende de sus datos reales de proveedor/sesión, no de usuarios o predeterminado; el listado operativo muestra los registros activos;
- no reutilizar como global un Phone Number ID perteneciente a otro cliente.

Para quitar un número del listado, seleccionarlo y usar `Quitar de AlfaCore` desde el menú de opciones. La acción no lo elimina de Meta ni borra conversaciones, mensajes o historial. Si se vuelve a agregar el mismo Phone Number ID, AlfaCore reactiva el registro existente.

## 8. Usuarios y administradores

### 8.1 Usuarios por número

En el detalle de cada número, elegir `Asignar usuarios`, marcar los agentes y guardar el diálogo.

Para restringir realmente un número debe tener al menos un usuario asignado. Dejarlo sin usuarios
significa acceso abierto por compatibilidad con instalaciones anteriores.

### 8.2 Administrador de Conversaciones

En `Operación y accesos`, marcar a los responsables del módulo. Un administrador de
Conversaciones ve y puede atender todos los números, aunque no figure en cada asignación.

Este rol es independiente del administrador general del sistema.

La política efectiva de Configuración es:

- un administrador general del sistema puede administrar Conversaciones;
- un administrador de Conversaciones también puede abrir y modificar la configuración;
- cuando todavía no hay administradores de Conversaciones, el primero debe ser designado por un
  administrador general;
- los agentes normales no pueden modificar números, credenciales, asignaciones, administradores ni
  automatizaciones;
- ser administrador general no concede automáticamente acceso operativo a todos los números: para
  atenderlos debe figurar también como administrador de Conversaciones o como usuario del número.

### 8.3 Matriz mínima de prueba

Preparar tres cuentas de prueba:

| Perfil | Número A | Número B | Resultado esperado |
|---|---:|---:|---|
| Administrador de Conversaciones | Sí | Sí | Ve y responde ambos. |
| Agente A | Sí | No | Solo ve y responde A. |
| Agente B | No | Sí | Solo ve y responde B. |
| Usuario sin asignación | No | No | No ve ninguno de los restringidos. |

Para cada perfil probar:

- ingreso normal al inbox;
- filtro superior;
- URL directa a una conversación conocida;
- envío de texto;
- envío de adjunto;
- plantilla fuera de la ventana de 24 horas;
- reacción;
- acceso directo a Configuración;
- intento de elegir un número no autorizado.

No aprobar la etapa solo porque una opción esté oculta: los intentos directos también deben ser
rechazados por el servidor.

> Control previo a producción: el filtrado del inbox y de los selectores ya está implementado. La
> matriz no se considera aprobada hasta confirmar también los rechazos server-side de todos los
> caminos de envío y la autorización para modificar la pantalla de Configuración.

## 9. Prueba multinúmero

| Prueba | Número A | Número B | Resultado |
|---|---|---|---|
| Mensaje entrante | | | |
| Phone Number ID detectado | | | |
| Conversación separada | | | |
| Historial correcto | | | |
| Texto saliente | | | |
| Un solo mensaje recibido | | | |
| Adjunto saliente | | | |
| Estado enviado/entregado/leído | | | |
| Usuario no autorizado bloqueado | | | |

Si uno de los números tiene el display name pendiente, se puede aprobar recepción, pero no cerrar
la prueba saliente de ese número.

## 10. IA y automatizaciones

Habilitar de menor a mayor autonomía.

### 10.1 Copiloto manual

1. Confirmar que el módulo AlfaKnowledge esté activo.
2. Configurar URL, API key y Knowledge Base ID.
3. Confirmar `OPENAI_API_KEY` en el servidor.
4. Cargar instrucciones del asistente.
5. Abrir manualmente el asistente en una conversación de prueba.
6. Revisar resumen, intención, sentimiento, sugerencia y fuentes.
7. Editar antes de enviar durante la primera etapa.

### 10.2 Fuera de horario sin IA

1. Configurar días y horario de atención.
2. Cargar un mensaje fijo.
3. Activar la respuesta fuera de horario.
4. Probar fuera del horario configurado.
5. Confirmar que no repita el aviso ante cada mensaje del cliente.

### 10.3 Bot autónomo

Activarlo primero en una base o número de prueba con:

- solo conversaciones sin asignar;
- máximo bajo de respuestas;
- palabras de escalado;
- demora para permitir intervención humana;
- solo fuera de horario, si corresponde;
- base de conocimiento revisada;
- instrucciones que prohíban inventar precios, condiciones o políticas.

Comprobar:

- caso resuelto con respaldo;
- caso sin información suficiente;
- pedido de humano;
- conversación ya asignada;
- ventana de envío vencida;
- límite de respuestas;
- auditoría y consumo aproximado de tokens;
- apagado inmediato del bot.

El SLA es independiente del bot. En el comportamiento actual no procesa conversaciones en estado
`PENDIENTE` ni `EN_GESTION`.

## 11. Diagnóstico

| Síntoma | Revisar |
|---|---|
| Meta no verifica el callback | Dominio, HTTPS, token de base, Verify Token y challenge. |
| El mensaje entra en otra base | URL sin token o token de base incorrecto. |
| Entra pero no envía | Access Token, Phone Number ID, WABA, ventana y estado del display name. |
| Error `#131037` | Aprobación del display name en Meta. |
| Se mezcla historial | Phone Number ID del webhook e `IdNumeroWhatsApp` de la conversación. |
| Un usuario ve demasiado | El número quedó sin usuarios o el usuario es administrador. |
| La IA no aparece | Módulo AlfaKnowledge y configuración. |
| La IA no analiza | Variable `OPENAI_API_KEY` en el servidor. |
| El bot no responde | Guardarraíles, horario, asignación, ventana, límite y auditoría. |

Ante errores técnicos revisar `AUX_ERR` y `CONV_WEBHOOK_LOG`. No borrar registros antes de guardar
la evidencia necesaria para soporte.

## 12. Checklist final de aceptación

- [ ] El dominio nuevo responde por HTTPS con certificado válido.
- [ ] El callback incluye el token de la base en SaaS.
- [ ] La prueba `hub.challenge` devuelve exactamente el challenge.
- [ ] Meta tiene el callback y Verify Token correctos.
- [ ] El primer mensaje entrante llega a la base esperada.
- [ ] No hay mensajes duplicados.
- [ ] La respuesta sale por el Phone Number ID correcto.
- [ ] Cada número conserva su propio historial.
- [ ] Hay al menos dos números probados de extremo a extremo.
- [ ] Cada número restringido tiene usuarios asignados.
- [ ] El administrador ve ambos números.
- [ ] Los agentes solo ven los números asignados.
- [ ] Los intentos directos no autorizados son rechazados.
- [ ] `AUX_ERR` no contiene errores nuevos del circuito.
- [ ] `CONV_WEBHOOK_LOG` registra los eventos esperados.
- [ ] El copiloto fue probado antes de habilitar automatizaciones.
- [ ] La respuesta fija fuera de horario fue probada.
- [ ] El bot fue probado en un entorno controlado.
- [ ] Se registraron límites, guardarraíles y procedimiento de apagado.
- [ ] Se documentó el responsable y la fecha de aprobación.

## 13. Registro de aprobación

| Dato | Valor |
|---|---|
| Base/cliente | |
| Fecha | |
| Responsable AlfaCore | |
| Responsable Meta | |
| Número A | |
| Número B | |
| Administrador probado | |
| Agente A probado | |
| Agente B probado | |
| IA habilitada | No / Copiloto / Fuera de horario / Bot |
| Observaciones | |

## 14. Referencias

- Documentación técnica interna: `docs/modulos/integraciones/whatsapp_cloud_api.md`
- Conexión y modelo multinúmero: `docs/modulos/conversaciones_whatsapp_conexion.md`
- Manual de IA: `src/AlfaCore/Docs/manual_alfaknowledge_conversaciones.md`
- Meta — plataforma: <https://developers.facebook.com/documentation/business-messaging/whatsapp/about-the-platform>
- Meta — primeros pasos: <https://developers.facebook.com/documentation/business-messaging/whatsapp/get-started>
- Meta — webhooks: <https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/overview>
- Meta — envío de mensajes: <https://developers.facebook.com/documentation/business-messaging/whatsapp/messages/send-messages>
