# Continuidad Codex

> Para continuar específicamente Conversaciones, WhatsApp y Embedded Signup desde el corte del 27/08/2026, usar como fuente principal [CONTINUIDAD_WHATSAPP_EMBEDDED_SIGNUP_ALBERTO_2026-08-27.md](./CONTINUIDAD_WHATSAPP_EMBEDDED_SIGNUP_ALBERTO_2026-08-27.md). Ese documento reemplaza las notas históricas de este archivo cuando exista una diferencia sobre ese alcance.

## Objetivo de este archivo

Este archivo resume el estado actual del trabajo para poder continuarlo desde otra PC o en una nueva conversación sin tener que reconstruir todo el contexto.

Uso sugerido al retomar:

```text
Leé docs/CONTINUIDAD_CODEX.md y continuemos desde ahí.
```

---

## Actualización 2026-09-22: Mercado Pago Point (terminal física) en el Punto de Venta

Se agregó el cobro con terminal física Mercado Pago Point al Punto de Venta web, y de paso se
resolvió (para el caso de LANUEVA) el problema de fondo que motivó todo esto: una base cliente
corriendo sobre un motor SQL Server demasiado viejo para el código actual.

### Por qué arrancó esto

Mientras se probaba el Punto de Venta en la base **LANUEVA** (`10.8.0.105`, ver más abajo), el
usuario preguntó si un proyecto propio (`C:\dev\AlfaMercadoPagoPoint`, Python + un puerto a .NET
Framework 4.8/COM para el sistema de escritorio VB6) servía como base para integrar Mercado Pago
Point en AlfaCore. Se decidió **portar en vez de arrancar de cero**: el puerto .NET ya resolvía en
C# async limpio y con tests las partes difíciles (cliente HTTP con reintentos, orders, y sobre todo
`PaymentFlowController`, el state machine de polling que crea la orden, espera la aprobación y evita
doble cobro). Solo se descartó la fachada COM/WinForms (`MercadoPagoPoint.cs` + `Forms/
FrmPagoPoint.cs`); el resto se portó casi literal cambiando `Newtonsoft.Json` por `System.Text.Json`.

### Qué se armó (código nuevo)

- `src/AlfaCore/Services/MercadoPagoPoint/` — librería portada: `MercadoPagoPointOptions`,
  `Models/` (`Order`, `PaymentInfo`, `PaymentResult`, `PosInfo`, `TerminalInfo`, `ResultadoPago`),
  `MercadoPagoHttpClient` (vía `IHttpClientFactory`, cliente nombrado `"MercadoPago"` registrado en
  `Program.cs`), `OrdersService`/`TerminalsService`/`PosService`, y `PaymentFlowController` (mismo
  algoritmo original, con `IPaymentFlowInteraction` adaptado para un consumidor headless en vez de
  diálogos WinForms: nunca da un cobro por perdido solo ante timeout, nunca reintenta un rechazo sin
  que el cajero lo pida de nuevo).
- `IMercadoPagoPointConfigService` / `MercadoPagoPointConfigService` — config en
  `TA_CONFIGURACION` (grupo `MERCADOPAGO`): `MERCADOPAGO_ACCESS_TOKEN`, `MERCADOPAGO_TERMINAL_ID`,
  `MERCADOPAGO_POS_EXTERNAL_ID` (informativo), `MERCADOPAGO_WEBHOOK_SECRET`, y
  `MERCADOPAGO_CODIGO_MEDIO_PAGO` (ver más abajo, agregada en un segundo paso). `ResolveOptionsAsync`
  solo exige el Access Token -- el Terminal ID se descubre recién con "Listar terminales" (si se
  exigían los dos de entrada quedaba un huevo-y-gallina: no se podía listar terminales sin ya
  tener el terminal ID).
- `IMercadoPagoPointPosService` / `MercadoPagoPointPosService` — orquestador: `CobrarAsync` corre
  el `PaymentFlowController` completo y persiste en `dbo.MP_POINT_ORDENES` (tabla nueva) la orden
  creada y el resultado final; `SolicitarCancelacion` pide la cancelación prolija (no aborta el
  `Task`, hace que el propio loop de espera la detecte); `VincularComprobanteAsync` enlaza la orden
  aprobada al comprobante recién grabado, para trazabilidad; `ActualizarDesdeWebhookAsync` nunca
  confía en el body del webhook -- siempre vuelve a pedir el estado real a la API.
- Webhook `POST /api/mercadopago/point/webhook/{token}` en `Program.cs` (mismo esquema de
  resolución de tenant que WhatsApp/MercadoLibre, `TryResolveWebhookTenantAsync`): valida firma
  HMAC-SHA256 del header `x-signature` (mismo algoritmo que `alfampoint/webhooks.py` del proyecto
  original) antes de tocar nada.
- Migraciones: `src/AlfaCore/App_Data/updates/2026-09-22-001__mercadopago_point_base.sql` (tabla
  `dbo.MP_POINT_ORDENES` + 4 claves de config) y
  `2026-09-22-002__mercadopago_point_codigo_medio_pago.sql` (la 5ª clave, agregada aparte porque se
  sumó después de que la primera ya había corrido en alguna base).
- Pantalla de configuración: **Configuración General > Ventas**, sección "Mercado Pago Point
  (terminal física)" -- credenciales, URL de webhook para copiar/registrar en Mercado Pago, y
  diagnóstico (listar terminales/POS, cobro de prueba) integrado ahí, no en una pantalla aparte.
- Enganche real en `VentasPuntoVenta.razor`: al elegir el medio de pago vinculado (ver siguiente
  sección) para un importe, en vez de agregar la línea al toque entra en un sub-estado "Esperando
  pago en la terminal..." con botón Cancelar; la línea de pago (`PuntoVentaPaymentLineDto`, sin
  tocar su schema) se agrega recién si Mercado Pago aprueba.

### Corrección importante: no hay ningún código fijo tipo "MPPOINT"

El primer diseño (anoche) asumía que el cliente iba a dar de alta un medio de pago nuevo en
`dbo.MA_CUENTAS` con `MEDIODEPAGO='MPPOINT'`. Probando contra una base real (10.8.0.10/ALFANET) se
confirmó que **no sirve**: los clientes ya tienen sus propios medios de pago para Mercado Pago/QR
(ej. "MercadoPago" código `MP`, "Qr MercadoPago" código `QR`) clasificados contablemente como
`MedioDePago='EF'` (efectivo-equivalente para el cierre de caja), igual que cualquier otro medio de
pago que liquida el mismo día -- ese campo no sirve para distinguir "cuál dispara el cobro real con
la terminal".

Se corrigió con un vínculo explícito y configurable: `MERCADOPAGO_CODIGO_MEDIO_PAGO` guarda el
`Codigo` (no el `MedioDePago`) del medio de pago elegido por el admin desde un `<select>` en la
pantalla de configuración (poblado con `IPuntoVentaService.GetPaymentMethodsAsync()`, el mismo
catálogo que ya usa el selector del POS). `IsMercadoPagoPointMethod` en `VentasPuntoVenta.razor`
compara contra ese código cargado (`_mpCodigoMedioPago`, cargado en `OnInitializedAsync`), no contra
ninguna constante hardcodeada.

### SQL Server viejo (LANUEVA) -- diagnóstico y arreglos

Antes de llegar al Punto de Venta, LANUEVA (`10.8.0.105\ALFANET` en ese momento) venía con un motor
**SQL Server 2008 RTM** real (no solo un `compatibility_level` bajo -- se confirmó con `@@VERSION`).
Eso rompía migraciones en cadena (`THROW`/re-throw necesitan compat >=110, `CREATE OR ALTER` necesita
2016 SP1+) y hasta una query en runtime (`InterfacesService.SearchAsync` usaba `OFFSET/FETCH`, que
2008 no soporta en absoluto). Se hicieron 3 rondas de fixes:

1. `THROW`/re-throw -> patrón clásico `RAISERROR` en 11 migraciones.
2. `CREATE OR ALTER` -> patrón clásico `DROP` + `CREATE` en 6 migraciones más.
3. `InterfacesService.SearchAsync` reescrito con `ROW_NUMBER()` en vez de `OFFSET/FETCH`.

Se armó además `ISqlServerEngineCheckService` (`AlfaCore.Services`), que detecta la versión real del
motor de la base activa y muestra un aviso persistente (global, en `MainLayout.razor`, y detallado en
`/actualizaciones`) cuando está por debajo de SQL Server 2016 -- **en vez de seguir parchando caso
por caso**, se decidió explícitamente avisar y frenar ahí.

**Importante**: entre esa investigación y probar el Punto de Venta, el usuario migró/actualizó el
motor de LANUEVA a **SQL Server 2016 SP1** (confirmado, `13.0.4001.0`) en un server nuevo, ahora en
`10.8.0.105` **sin instancia con nombre** (antes era `10.8.0.105\ALFANET`). Quien retome debe usar
esa dirección nueva para conectarse a esa base.

### Otros hallazgos de esa sesión de SQL Server viejo (sin tocar, documentados nomás)

- `scripts/actualizar_instalacion.bat`: se agregó SERVER-ALFAWEB (`10.8.0.32`) como segundo destino
  de despliegue sugerido (pruebas), junto a SERVER-ALFACENTRAL (`10.8.0.53`\`\`, producción).
- Impresión directa (pedido, no implementado): alcanza con que la PC de cada caja abra Chrome/Edge
  con el flag `--kiosk-printing` (config de SO, cero cambios de código) -- comentario dejado en
  `VentasPuntoVenta.razor` junto a `alfaPosPrintHtml`. Si algún día hace falta más de una impresora
  por caja (A4 + térmica), eso sí requeriría un agente de impresión local aparte.

### Interacción con los atajos de teclado (trabajo en paralelo de otro compañero)

En paralelo, otro colega agregó atajos de teclado al POS (F2/F3/Ctrl+F/Ctrl+B/Ctrl+G/Esc, cliente
eventual, `QuickClienteModal`). El merge automático combinó bien ambos cambios sin conflicto de
texto, pero se encontraron y corrigieron dos huecos reales de interacción (commit `d8dad02`):

1. Los atajos que confirman la venta (F2, luego también Ctrl+G) llamaban a `ConfirmChargeAsync`
   directo, sin pasar por el chequeo de `_mpCobrando` que ya tenían los botones -- se agregó el
   mismo guard dentro de la función.
2. Cerrar el modal de cobranza (botón o `Esc`) solo lo ocultaba -- la espera de Mercado Pago seguía
   corriendo sola en el servidor (Blazor Server no la aborta porque el modal se oculte). Ahora
   `CloseChargeModal` pide la cancelación prolija si hay un cobro en curso.

### Qué falta probar (pendiente real)

**No se llegó a validar una aprobación real de punta a punta.** Se confirmó que:

- lee terminales/POS reales de la cuenta (botones "Listar terminales"/"Listar puntos de venta"),
- crea la orden correctamente en Mercado Pago (quedó en estado `created`),

pero el terminal físico se apagó antes de completar una prueba tocándolo. Quien retome debería:

1. Confirmar que el medio de pago vinculado (`MERCADOPAGO_CODIGO_MEDIO_PAGO`) esté bien elegido en
   Configuración General > Ventas para la base de prueba.
2. Hacer un cobro real desde `VentasPuntoVenta.razor` (no solo desde el diagnóstico) tocando la
   terminal físicamente, y confirmar que la venta se graba recién tras la aprobación.
3. Confirmar que `dbo.MP_POINT_ORDENES` queda con el `IdComprobante` enlazado.
4. Registrar la URL de webhook en el panel de Mercado Pago (la pantalla de configuración ya la
   muestra con botón Copiar) y confirmar que una notificación real llega y actualiza el estado antes
   de que termine el polling.

### Commits de esta etapa (orden cronológico)

- `63f1586` Reemplaza THROW por RAISERROR en 11 migraciones para soportar compat level viejo
- `12f79b2` Reemplaza CREATE OR ALTER por DROP+CREATE en 6 migraciones (SQL Server 2008)
- `1ee9f55` Avisa cuando una base corre en un motor SQL Server viejo, en vez de seguir parchando
- `fe733ee` Integra Mercado Pago Point (terminal física) en el Punto de Venta web
- `d8dad02` Evita que los atajos de teclado nuevos interfieran con un cobro de Mercado Pago Point en curso
- (commits del compañero, atajos de teclado: `81b4e6e`, `99c4bb3`, y sus merges)
- último de esta sesión: vínculo configurable medio de pago <-> Point (`MERCADOPAGO_CODIGO_MEDIO_PAGO`)

### Archivos principales de esta etapa

- `src/AlfaCore/Services/MercadoPagoPoint/` (carpeta completa)
- `src/AlfaCore/Services/IMercadoPagoPointConfigService.cs` / `MercadoPagoPointConfigService.cs`
- `src/AlfaCore/Services/IMercadoPagoPointPosService.cs` / `MercadoPagoPointPosService.cs`
- `src/AlfaCore/Services/ISqlServerEngineCheckService.cs` / `SqlServerEngineCheckService.cs`
- `src/AlfaCore/Services/InterfacesService.cs` (paginación reescrita)
- `src/AlfaCore/Components/Pages/ConfiguracionGeneralVentas.razor`
- `src/AlfaCore/Components/Pages/VentasPuntoVenta.razor`
- `src/AlfaCore/Components/Layout/MainLayout.razor` (aviso global de motor viejo)
- `src/AlfaCore/Components/Pages/Actualizaciones.razor`
- `src/AlfaCore/App_Data/updates/2026-09-22-001__mercadopago_point_base.sql`
- `src/AlfaCore/App_Data/updates/2026-09-22-002__mercadopago_point_codigo_medio_pago.sql`
- `scripts/actualizar_instalacion.bat`

---

## Estado general

Se trabajó sobre el repo `AlfaCore` en mejoras de:

- Auditoría de usuarios
- visor de comprobantes
- manejo de sesiones SQL
- documentación y manuales
- control centralizado de backups de clientes (sesión actual, ver sección propia más abajo)

No se rehizo la arquitectura general.  
Se trabajó sobre la base actual del proyecto.

---

## Actualización 2026-08-18: Conversaciones + WhatsApp Web por número

Se avanzó sobre el módulo `Conversaciones` para ordenar la configuración y pasar de una
sesión WhatsApp Web global a un esquema de **sesión Web por número**.

### Objetivo de esta etapa

Permitir que el usuario pueda:

- configurar un número;
- vincular ese número con QR o código;
- agregar otro número después;
- evitar que mensajes o runtime se mezclen entre bases o líneas.

### Cambios ya implementados

1. **Reorden de configuración**
   - se separó la pantalla en tabs:
     - `Canales`
     - `IA y automatizaciones`
     - `Operación`
     - `Herramientas`

2. **Apertura manual del asistente**
   - quedó alineado el criterio funcional para que el asistente no se abra solo al entrar a
     una conversación, evitando consumo innecesario de créditos.

3. **WhatsApp Web real**
   - se implementó flujo de sesión Web con worker Node;
   - se soporta:
     - QR
     - código de texto opcional

4. **Aislamiento por base**
   - el inbox Web se restringe a:
     `App_Data/whatsapp-web/{baseId}/...`
   - esto se hizo para que no vuelva a pasar la mezcla entre la base local y otra base.

5. **Multi-número**
   - `CONV_WHATSAPP_NUMEROS` pasó a ser la entidad que guarda la sesión Web de cada número;
   - se agregaron campos de pairing/runtime/instancia al modelo y a la persistencia;
   - la UI para generar QR quedó dentro de la tarjeta de cada número.

6. **Envío y recepción**
   - el envío Web usa `IdNumeroWhatsApp` de la conversación;
   - el inbox Web resuelve `instanceName` y la traduce al número correcto antes de grabar.

### Commits importantes de esta etapa

Branch:

- `conversaciones-asistente-espera-horario`

Commits relevantes:

- `1bc88e7` `Reordena configuracion de conversaciones`
- `f624500` `Add WhatsApp Web session flow for conversaciones`
- `bf2afc9` `Fix WhatsApp Web session isolation by base`
- `f56366c` `Support multiple WhatsApp Web sessions per number`

### Script SQL agregado

Se creó:

- `src/AlfaCore/App_Data/updates/2026-08-18-010__conversaciones_whatsapp_web_por_numero.sql`

Ese script agrega a `dbo.CONV_WHATSAPP_NUMEROS` las columnas Web nuevas:

- `WebSessionMode`
- `WebPhoneNumber`
- `WebSessionStatus`
- `WebDisplayName`
- `WebInstanceName`
- `WebPairingToken`
- `WebPairingCode`
- `WebPairingQrPayload`
- `WebPairingGeneratedAtUtc`
- `WebPairingExpiresAtUtc`
- `WebRuntimeState`
- `WebLastError`
- `WebWorkerProcessId`
- `WebRuntimeUpdatedAtUtc`

### Problemas detectados en esta etapa

#### 1. Mezcla de mensajes entre bases

Situación reportada por el usuario:

- una sesión Web configurada en la base local `AW_112012806` dejaba ver mensajes también en
  otra base (`ALFANET2007` / `10.8.0.31`).

Acción tomada:

- aislamiento por `baseId`;
- objetivo explícito: “que no vuelva a pasar”.

#### 2. Bases sin columnas nuevas

En bases donde todavía no corrió el update SQL, `GetWhatsAppNumeros` fallaba con errores:

- `El nombre de columna 'WebSessionMode' no es válido`

Se dejó compatibilidad en código para que:

- la pantalla pueda abrir aunque falten esas columnas;
- el guardado del número normal no dependa de la migración;
- el guardado de la **sesión Web** sí exija correr el script y devuelva un mensaje claro.

#### 3. Build roto por archivos runtime

Problema:

- MSBuild intentaba copiar archivos temporales de:
  `src/AlfaCore/App_Data/whatsapp-web/...`
- fallaba con errores tipo:
  `No se pudo copiar ... pre-key-65.json porque no se encontró`

Acción tomada:

- se excluyó `App_Data\whatsapp-web\**` del `.csproj`.

### Estado actual real al cerrar esta sesión

Hay **dos cambios locales todavía sin commit/push**:

- `src/AlfaCore/AlfaCore.csproj`
- `src/AlfaCore/Services/ConversacionesConfigService.cs`

Estos cambios corresponden a:

1. exclusión de `App_Data\whatsapp-web\**` del build/publish;
2. fallback para bases que todavía no tengan las columnas Web nuevas.

Verificación:

- `dotnet build C:\dev\AlfaCore\AlfaCore.sln` terminó OK al final de la sesión.

### Bug abierto al cierre

El usuario fue a:

- `Conversaciones > Configuración > Operación > Números de WhatsApp`

completó:

- `Nombre del número nuevo`
- `Phone Number ID`

y al hacer clic en:

- `Agregar número`

reportó que **“no lo tomó”**.

Estado de este bug:

- todavía no está diagnosticado por completo;
- en los logs revisados no apareció un `SaveWhatsAppNumero` nuevo correspondiente a esa prueba;
- eso sugiere:
  - UI Blazor vieja en memoria, o
  - el click no está llegando al backend como se espera.

### Qué debe hacer primero quien retome

1. Confirmar que AlfaCore esté corriendo con la versión nueva.
   - pedir `Ctrl + F5`
   - si sigue igual, reiniciar el proceso

2. Repetir la prueba mínima:
   - ir a `Operación > Números de WhatsApp`
   - completar nombre + ID
   - hacer clic en `Agregar número`

3. Si vuelve a fallar:
   - revisar `src/AlfaCore/App_Data/diagnostics/app-events-202608.jsonl`
   - buscar:
     - `Action":"SaveWhatsAppNumero"`
     - `Action":"GetWhatsAppNumeros"`

4. Si el número se guarda:
   - debe aparecer una tarjeta editable arriba del formulario “nuevo”;
   - recién ahí probar:
     - `Generar QR`
     - `Refrescar estado`
     - `Detener sesión`

5. Antes de usar sesión Web real en una base no migrada:
   - correr:
     `src/AlfaCore/App_Data/updates/2026-08-18-010__conversaciones_whatsapp_web_por_numero.sql`

### Archivos principales de esta etapa

- `src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor`
- `src/AlfaCore/Models/ConversacionesConfiguracionModels.cs`
- `src/AlfaCore/Models/ConversacionesModels.cs`
- `src/AlfaCore/Services/ConversacionesConfigService.cs`
- `src/AlfaCore/Services/ConversacionesService.cs`
- `src/AlfaCore/Services/IConversacionesConfigService.cs`
- `src/AlfaCore/Services/IWhatsAppWebSessionService.cs`
- `src/AlfaCore/Services/WhatsAppWebSessionService.cs`
- `src/AlfaCore/AlfaCore.csproj`
- `src/AlfaCore/App_Data/updates/2026-08-18-010__conversaciones_whatsapp_web_por_numero.sql`

---

## Actualización 2026-08-05: AlfaCore modular (catálogo de módulos + panel Administrar)

Se abrió un análisis de arquitectura (todavía sin código) para convertir AlfaCore en un ERP
modular estilo Odoo: base de datos siempre completa por cliente (como ya funciona hoy), pero
con módulos que se activan/venden por separado. Conversaciones sería el primer módulo armado
con este esquema, en vez de separarse como producto aparte.

Estado: solo análisis y decisiones de diseño, cero código escrito.

Detalle completo, decisiones tomadas, hallazgos técnicos y próximos pasos:

- `docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md`

---

## Actualización 2026-07-29: copiloto AlfaKnowledge

Se completó la integración asistida entre Conversaciones y el repositorio separado
`C:\dev\AlfaKnowledge`.

Estado:

- AlfaKnowledge productivo publicado en `http://10.8.0.32:5000`;
- endpoint `POST /api/external/suggest-reply` protegido con API key;
- migración SQL `012` verificada en `kb.QueryInteractions`;
- AlfaCore productivo actualizado mediante despliegue paralelo en
  `C:\Program Files\Alfa Gestion\AlfaCore-20260729`;
- servicio Windows `AlfaCore` ejecutando el backend nuevo en puerto `5056`;
- IIS público derivando `https://alfanetweb.ddns.net/` hacia `5056`;
- backend anterior conservado temporalmente en `5055` para rollback;
- prueba productiva de sugerencia y feedback completada correctamente;
- fallos de integración registrados mediante `IAppEventService` en `AUX_ERR`.
- configuración de AlfaKnowledge fijada también en el entorno del servicio Windows, porque la
  primera prueba visual mostró que el proceso no estaba tomando esos valores desde `.env`.
- panel ajustado para diferenciar falta de configuración, conversación sin texto entrante y fallo
  real del servicio, en lugar de mostrar el mismo mensaje genérico para todos los casos.
- binding de `AlfaKnowledgeOptions` endurecido con lectura explícita de variables de entorno luego
  de confirmar visualmente que `IOptions` recibía la sección vacía.
- asistente ampliado con tres alcances (`tramo actual`, `toda la conversación` y `mensaje
  marcado`) y chat técnico/IA dentro del panel;
- el tramo actual se delimita con los eventos internos de cierre ya existentes, sin agregar
  tablas ni duplicar el historial;
- el botón de estrellas de cada mensaje permite usarlo como foco explícito;
- las respuestas de la IA siguen siendo borradores: solo pasan al compositor mediante
  `Llevar a respuesta`.
- el panel IA reserva una franja fija del viewport, permanece abierto hasta cierre explícito y
  cancela la petición activa al cerrarse;
- las fuentes son enlaces en pestaña nueva y existe acceso directo a AlfaKnowledge completo;
- las preguntas escritas por el técnico recuperan documentación por la pregunta actual, dejando
  el historial del cliente como contexto secundario. Esto corrigió respuestas contaminadas por
  temas anteriores del mismo hilo.

Documentación técnica:

- `docs/modulos/integraciones/alfaknowledge.md`

Próximo paso funcional:

- validar con un técnico los tres alcances y el chat contextual en una conversación real;
- empezar a reunir feedback antes de evaluar cualquier automatización.

---

## Decisiones importantes ya tomadas

### 1. Manuales

Se definió este criterio:

- `src/AlfaCore/Docs/` = manuales funcionales que consume o puede consumir la aplicación
- `docs/` = documentación técnica, catálogo, reglas y archivos puente

### 2. Manual principal

`src/AlfaCore/Docs/manual_usuario.md`

Ya no debe considerarse un manual de Compras.

Ahora debe ser y ya fue reescrito como:

- **Manual General de AlfaCore**

Su función es explicar:

- qué es AlfaCore
- base activa
- navegación
- menú
- filtros
- grillas
- exportaciones
- ayuda general

### 3. Manuales por módulo

Se acordó trabajar con un manual específico por módulo importante.

Ejemplo ya creado:

- `src/AlfaCore/Docs/manual_auditoria_usuarios.md`

### 4. Archivos puente en `docs/`

Se dejaron archivos puente para consulta rápida desde el repositorio:

- `docs/manual_usuario.md`
- `docs/manual_auditoria_usuarios.md`

Estos no son la fuente principal del contenido.

---

## Cambios funcionales realizados

### Auditoría de usuarios

Se hicieron mejoras importantes en el módulo.

#### Nuevo control agregado

Se agregó al combo `Tipo control`:

- `Posibles comprobantes duplicados`

Objetivo:

- detectar comprobantes de compras potencialmente duplicados

Fuente principal:

- `C_MV_Cpte`

Cruce informativo:

- `MV_ASIENTOS`

Archivos tocados:

- `src/AlfaCore/Services/AuditoriaService.cs`
- `src/AlfaCore/Models/AuditoriaModels.cs`
- `src/AlfaCore/Components/Pages/AuditoriaUsuarios.razor`
- `docs/CATALOGO_RUTINAS.md`

#### Control “Comprobantes iniciados y no grabados”

Se ajustó la vista para mostrar mejor datos de cancelación.

Cambios:

- se quitaron columnas genéricas que no aportaban
- se agregaron:
  - hora cancelación
  - minutos hasta cancelación
  - importe al cancelar
  - traza original del sistema

También se agregaron KPI específicos para ese control:

- comprobantes cancelados
- promedio min. cancelación
- máx. min. cancelación
- importe total cancelado
- usuarios involucrados

#### Exportación desde Auditoría

Se agregó:

- exportar a `PDF`
- exportar a `Excel`

Archivos tocados para esto:

- `src/AlfaCore/Services/AuditoriaExcelExporter.cs`
- `src/AlfaCore/Program.cs`
- `src/AlfaCore/Components/Pages/AuditoriaUsuarios.razor`

#### Compatibilidad SQL vieja

En `HIERROSUR` apareció error por `TRY_CONVERT`.

Se corrigió reemplazando funciones modernas por lógica compatible con SQL Server más viejo:

- sin `TRY_CONVERT`
- sin depender de `CONCAT` para ese parseo crítico

Archivo principal corregido:

- `src/AlfaCore/Services/AuditoriaService.cs`

---

## Cambios en visor de comprobantes

Se trabajó sobre:

- `src/AlfaCore/Components/Shared/ComprobanteViewer.razor`
- `src/AlfaCore/Components/Shared/ComprobanteViewer.razor.css`

Cambios hechos:

- ocultar importes en cero
- priorizar solapa inicial útil
- renombrar `Observaciones` a `Otros conceptos`
- mejorar layout de totales

Tema pendiente:

- revisar si en algunos casos CSS global sigue forzando visual vertical en ciertos navegadores o anchos específicos

---

## Cambios en sesiones SQL

Se detectó un problema probable con desaparición de conexiones en `sessions.json`.

Causa probable identificada:

- `SessionService` registrado como `Scoped`
- cada instancia carga `sessions.json` una vez
- luego guarda todo el archivo con su copia en memoria
- eso puede pisar sesiones agregadas por otra instancia o circuito

También existe fallback a regenerar desde config si el JSON falla.

Importante:

- este diagnóstico quedó identificado
- no necesariamente quedó completamente resuelto en esta etapa

Archivo a revisar si se retoma ese tema:

- `src/AlfaCore/Services/SessionService.cs`

---

## Control centralizado de backups de clientes

### Por qué

`EM_Backup.vbp` (proyecto VB6, repo `NMC_CONT_DEV`) generaba un `.SQL` que registraba
un **linked server** (`sp_addlinkedserver` a `alfanet.ddns.net`) con usuario y clave en
texto plano, para que el SQL Server de **cada cliente** escribiera directo en la base de
control central. Riesgo: credencial compartida en texto plano en disco de cada cliente,
y un linked server permanente de cada SQL Server de cliente hacia un host público.

Además, la subida del `.BAK` por FTP (`alfaftp.ddns.net`) viene fallando.

### Lo hecho hasta ahora

1. **`AlfaArchivos`** (repo aparte, `C:\Dev\AlfaArchivos`): es la alternativa HTTP al FTP
   que falla (mismo storage `E:\FTP`, mismo usuario/clave que el FTP). Se hizo hardening:
   límite de tamaño de subida (para `.BAK` grandes), fix de un bug de path traversal en
   `SafePath`, CSRF en los formularios, rate-limit en `/Login`. Compilado y probado
   (login, subida, borrado, traversal bloqueado). Commiteado en ese repo.

2. **`AlfaCore`** (este repo): se agregó el reemplazo del linked server.
   - Tabla nueva `dbo.ALFACORE_BACKUPS_CONTROL` en `ALFA_CENTRAL`
     (`docs/base-datos/sql-referencia/backups_control_modelo_inicial.sql`), vinculada
     lógicamente a `dbo.bases` (sin FK dura, tipo de `bases.id` no confirmado).
   - `ICentralBackupControlService` / `CentralBackupControlService.cs` (Dapper).
   - Endpoint `POST /api/vb6/backup-status` en `Program.cs`, protegido con header
     `X-Api-Key` contra `BackupStatus:ApiKey`.
   - De paso: la connection string de `ConnectionStrings:AlfaCentral` estaba
     hardcodeada en texto plano en `appsettings.json` (commiteada al repo). Se movió a
     `.env` (gitignored), seguido el mismo mecanismo que ya usaba el proyecto para
     `OPENAI_API_KEY` / `PushNotifications`. Se generó una `BackupStatus__ApiKey` nueva
     (random, 256 bits) también en `.env`.
   - Todo compiló limpio y pasó `python tools/catalogo/check_catalogo.py`.
   - Commit local: `621a839` — no se hizo push.

### Lo que falta (en orden)

1. **Urgente, fuera de mi alcance**: rotar la password del login `ALFA_CENTRAL` en el
   SQL Server real (`149.46.4.90`). Quedó expuesta en el historial de git (el repo tiene
   remoto en GitHub), así que sacarla de `appsettings.json` no alcanza.
2. Correr `backups_control_modelo_inicial.sql` contra `ALFA_CENTRAL`.
3. Cargar el `BackupStatus__ApiKey` real en el `.env` del servidor donde corre `AlfaCore`
   en producción (el valor generado hoy solo existe en el `.env` local de esta PC).
4. Tocar `ModBackup.bas` (`NMC_CONT_DEV`) para:
   - sacar el bloque `sp_addlinkedserver` / `sp_addlinkedsrvlogin` del `.SQL` generado
   - reemplazar la subida por FTP (`cFTP`/`mFTP`) por HTTP contra `AlfaArchivos` (login +
     `POST /upload` con token CSRF)
   - agregar el `POST /api/vb6/backup-status` al final del proceso de backup, reusando
     el patrón `WinHttp.WinHttpRequest.5.1` que ya existe en `ModAlfaCore.bas`
5. (Fase 2, no arrancada) pantalla en AlfaCore para ver el estado de backups por cliente
   y alertas de espacio en disco.

### Archivos tocados en esta etapa (además de los ya listados abajo)

- `docs/base-datos/sql-referencia/backups_control_modelo_inicial.sql` (nuevo)
- `src/AlfaCore/Models/BackupsControlModels.cs` (nuevo)
- `src/AlfaCore/Services/ICentralBackupControlService.cs` (nuevo)
- `src/AlfaCore/Services/CentralBackupControlService.cs` (nuevo)
- `src/AlfaCore/Program.cs`
- `src/AlfaCore/appsettings.json`
- `.env.example`
- `.env` (no versionado)

---

## Documentación creada o ajustada

### Manual general

- `src/AlfaCore/Docs/manual_usuario.md`

### Manual específico

- `src/AlfaCore/Docs/manual_auditoria_usuarios.md`

### Puentes en docs

- `docs/manual_usuario.md`
- `docs/manual_auditoria_usuarios.md`

---

## Archivos importantes modificados recientemente

- `src/AlfaCore/Services/AuditoriaService.cs`
- `src/AlfaCore/Models/AuditoriaModels.cs`
- `src/AlfaCore/Components/Pages/AuditoriaUsuarios.razor`
- `src/AlfaCore/Services/AuditoriaExcelExporter.cs`
- `src/AlfaCore/Program.cs`
- `src/AlfaCore/Components/Shared/ComprobanteViewer.razor`
- `src/AlfaCore/Components/Shared/ComprobanteViewer.razor.css`
- `src/AlfaCore/Docs/manual_usuario.md`
- `src/AlfaCore/Docs/manual_auditoria_usuarios.md`
- `docs/manual_usuario.md`
- `docs/manual_auditoria_usuarios.md`

---

## Próximos pasos sugeridos

### Opción 1. Ayuda contextual por módulo

Preparar `Ayuda.razor` para abrir:

- manual general por defecto
- manual de Auditoría de usuarios por `topic` específico

Ejemplo deseado:

- `/ayuda`
- `/ayuda?topic=consultas`
- `/ayuda?topic=auditoria-usuarios`

### Opción 2. Crear más manuales por módulo

Seguir el mismo criterio para:

- tareas
- conversaciones
- interfaces
- costos
- seguridad

### Opción 3. Revisar SessionService

Arreglar de forma definitiva el riesgo de sobrescritura de `sessions.json`.

### Opción 4. Revisión visual final del visor de comprobantes

Confirmar en navegador:

- distribución horizontal de totales
- solapa inicial correcta

---

## Cómo retomar rápido

Si retomás desde otra conversación, usar algo así:

```text
Leé docs/CONTINUIDAD_CODEX.md.
Quiero continuar desde ese estado.
El próximo paso es: [describir la tarea].
```

---

## Verificación usada durante esta etapa

Antes de cerrar cambios, se estuvo ejecutando:

```text
dotnet build src/AlfaCore/AlfaCore.csproj
python tools/catalogo/check_catalogo.py
```

El chequeo de catálogo debe seguir haciéndose antes de finalizar nuevas tareas.
