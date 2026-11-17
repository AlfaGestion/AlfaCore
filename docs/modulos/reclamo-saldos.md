# Reclamo de saldos (Ventas, D6008)

Reemplaza en la web a "Notificación de Saldos de Clientes" del legacy (`RP_SaldosDeClientes`, clave `D6008`).
Ruta `/ventas/reclamo-saldos`. Componente `ReclamoSaldos.razor`, servicio `ReclamoSaldosService`,
reglas puras en `ReclamoSaldosPlantilla` (con tests en `ReclamoSaldosPlantillaTests`).

## Qué hace
1. Lista clientes con saldo (`Vt_Clientes` + `VE_SALDOSCTA`; detalle en `VE_CPTES_IMPAGOS`), con filtros de texto,
   unidad de negocio, saldo mínimo, atraso mínimo y bajas. Respeta la unidad de negocio del usuario (no admin).
2. Elige el mensaje por **tramo de atraso** (días del comprobante vencido más antiguo): hasta N1 → tramo 1,
   hasta N2 → tramo 2, más → tramo 3.
3. Arma el mensaje con variables y el **detalle de la deuda en el cuerpo** (tabla HTML en email; una línea en WhatsApp).
4. Envía por email, WhatsApp o ambos. Siempre se puede **simular** (no envía nada) o **enviar de prueba** a un
   mail/celular propio (ningún cliente recibe nada).

## Destinatarios
- Email: contactos de `MA_CONTACTOS` / `MA_CONTACTOS_CUENTAS` marcados como encargados de pagos/cobranzas
  (`mailPGCB=1`); si no hay, el `MAIL` del cliente. Se descartan direcciones inválidas.
- WhatsApp: `Celular` del contacto de cobranzas; si no, `FAX` del cliente (como el legacy). Se descartan
  placeholders tipo `0000-0000`.

## Variables del mensaje
`<NOMBRE> <DETALLE> <FORMADEPAGO> <SALDO> <VENCIDO> <CANTIDAD> <DIAS> <FECHA> <FECHAANTIGUA>`.
Si el mensaje no trae `<DETALLE>` la tabla se agrega al final.

## Pantalla
Mismo modelo que Cobranzas: búsqueda y filtros en el header (texto libre sobre cualquier columna; saldo mínimo, atraso,
unidad, tramo, solo con email/WhatsApp, bajas), columnas ordenables, paginado de 50 y tarjetas (clientes, saldo,
vencido, clientes por tramo, sin contacto, seleccionados). La vista previa del email se muestra en un iframe aislado
con fondo blanco, para que se vea como lo recibe el cliente y no herede el tema oscuro.

## Variables nuevas para el email
- `<TARJETAS>`: cuatro tarjetas (saldo, vencido, comprobantes, días de atraso). No se agrega sola: ponela en el mensaje
  (los mensajes ya cargados no se tocan).
- `<PORTAL>` (opcional): botón "Ver mi cuenta en el portal". Usa el acceso automático del Portal Cliente
  (`PortalClienteAutoLoginService`): el cliente entra sin usuario ni clave. A diferencia de las invitaciones (un uso,
  60 min), este enlace es **reutilizable y vence a los `RECLAMO-SALDOS-PORTAL-DIAS` días** (15 por defecto), porque los
  antivirus y vistas previas de correo abren el enlace antes que la persona y lo quemarían. Es personal: quien tenga el
  mail accede a esa cuenta hasta que venza.
  - En vista previa y simulación **no se crea ningún token** (se muestra un enlace de ejemplo). Se genera uno por
    cliente solo al enviar de verdad un mail.
  - Requiere la URL pública del sistema (Conversaciones → Configuración); si falta, la vista previa avisa y `<PORTAL>` va vacío.
  - No aplica a WhatsApp (las variables de plantilla las define Meta).

## Número de WhatsApp y plantillas
En "Mensaje y configuración" se elige el **número de WhatsApp que envía** (clave `RECLAMO-SALDOS-WA-NUMERO`; con un solo número activo se usa ese).
El desplegable de plantillas muestra solo las aprobadas de la cuenta (WABA) de ese número, y el envío crea la conversación con ese número.
Antes de un envío real se consulta a Meta que la plantilla exista en esa cuenta; si no, se corta con un aviso claro.

## Pestaña Enviados
Historial de la bitácora (`ALFACORE_RECLAMO_SALDO_ENVIO`): fecha, cliente, canal, destino, tramo, saldo, asunto, estado (con el
motivo si falló) y usuario. Filtros por fecha, texto, canal, estado y envíos de prueba; últimos 500. Las simulaciones no se
registran. "Ver enviado" muestra el texto tal como salió y la foto de los comprobantes reclamados; los WhatsApp tienen un enlace
a su conversación en Conversaciones. Cada acción de "Enviar" comparte un lote. Todo esto se guarda desde el script
`2026-11-17-051` (los envíos anteriores no lo tienen); el token del enlace al portal se guarda enmascarado. Columna **Seguimiento** (calculada al consultar, no se guarda): estado del WhatsApp (enviado a Meta / entregado / leído / falló),
si el cliente **respondió** en la conversación, si **abrió el enlace al portal** del mail (se asocia el acceso del mismo
cliente creado minutos antes del envío), lo **cobrado** al cliente desde el envío (`VE_COBRANZAS_REALIZADAS`) y su saldo de hoy contra el reclamado.
"Cobró" es por cliente, no por comprobante: no distingue si pagó justo lo reclamado. No lee respuestas: las de WhatsApp están en Conversaciones y las de email llegan a la casilla "responder a".

## Historial del cliente (línea de tiempo)
Botón **Historial** en Clientes y en Enviados: reclamos enviados (también los con error y las pruebas, marcados), mensajes de las
conversaciones de WhatsApp asociadas (respuestas del cliente y mensajes del equipo, con enlace a la conversación), ingresos al
portal y cobranzas registradas desde el primer reclamo real. Hasta 12 meses, más reciente primero; solo lectura. Los mensajes
y cobranzas se muestran solo si el cliente tiene al menos un reclamo real enviado.

## Forma de pago y cambio de email
- `<FORMADEPAGO>` sale primero de "Formas de pago" de la unidad de negocio del último comprobante pendiente del cliente
  (Archivos → Unidades de negocio, clave `FORMADEPAGO_UN_<unidad>`, la misma del mail de facturación). Si está vacía, usa el
  texto general de la pestaña de configuración.
- En "Ver mensaje" el email de destino es editable y hay un botón para enviar solo ese mensaje (con "Solo simular" tildado
  por defecto). El cambio vale para ese envío; no modifica los datos del cliente.

## Configuración (`TA_CONFIGURACION`, grupo `COBRANZAS`, prefijo `RECLAMO-SALDOS-`)
`DIAS-HASTA1`, `DIAS-HASTA2`, `ASUNTO1..3`, `MENSAJE1..3` (largos en `ValorAux`), `FORMA-PAGO`
(texto general; respaldo: `CONV_COBRANZA_FORMA_PAGO`), `SALDO-MINIMO`,
`DIAS-ENTRE-ENVIOS`, `WA-PLANTILLA` (id de plantilla). Se editan en la pestaña "Mensaje y configuración".

## Email
Usa `CorreoSalienteService` (misma cuenta que Alfa Gestión: usuario → general de la base → respaldo con tope).

## WhatsApp
Requiere plantilla **aprobada por Meta** (si el cliente no escribió en 24 h). Se elige en la configuración; las
variables `{{N}}` se completan por el mapeo del catálogo (`cliente.nombre`, `cobranza.detalleDeuda`,
`pago.formaPago`); sin mapeo, en ese orden. Cada variable va en una línea (límite de Meta). Envío por
`ConversacionesService.SendTemplateMessageAsync`.

## Bitácora y reglas de seguridad
- `ALFACORE_RECLAMO_SALDO_ENVIO`: cada envío (canal, destino, tramo, estado). Alimenta "Último reclamo" y evita
  reclamar dos veces en `DIAS-ENTRE-ENVIOS` días (se puede forzar). Las pruebas no cuentan.
- No escribe ventas ni contabilidad. Menú/permiso web propio (`ALFACORE_MENU_WEB` / `ALFACORE_TAREAS_WEB`), clave `D6008`.

## Pendientes
- Adjuntar el estado de cuenta en PDF al email.
- Tarea programada (el legacy tenía "Tarea Programada").
- Un solo mensaje por cliente aunque tenga deuda en varias unidades fiscales (hoy se detalla todo junto).
