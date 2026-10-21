# Director IA — Plan por fases

Estado: borrador para validar con Alberto (21/10/2026). Contexto permanente en `CLAUDE.md`.

## Principios

1. **Primero datos, después reglas, después alertas, la IA al final.** Un LLM sobre datos incompletos inventa; sobre datos buenos, resume y prioriza.
2. **Los números los calcula SQL/C#, no el modelo.** La IA lee vistas `dir.*` de solo lectura y redacta; nunca calcula impuestos ni escribe.
3. **Todo por unidad fiscal (CUIT)** y consolidable.
4. **Todo configurable en `TA_CONFIGURACION`.** Nada hardcodeado.
5. **Sin claves fiscales en el repo.** Mediano plazo: delegación por Web Services de ARCA con certificado, no clave fiscal.
6. Cada fase deja valor usable por sí sola.

## Recomendación de arranque

Empezar por **Fase 1 con carga semiautomática** (planilla/pantalla + los CSV que ya genera `arca_cuenta_corriente.py`), no por scraping nuevo. Es lo que da valor más rápido con menos riesgo: se ve qué vence y cuánto se debe ya, mientras se estabiliza la captura automática.

---

## Fase 1 — Fiscal: obligaciones, moratorias y vencimientos por CUIT

**Qué existe ya**
- Unidades de negocio con CUIT, PDV y certificado (`V_TA_UnidadNegocio`, `ARCA_CERTIFICADO`).
- WSAA/WSFE y padrón funcionando con certificado por unidad (base para sumar otros web services).
- `arca_cuenta_corriente.py` (saldos y Mis Facilidades): experimental, nunca corrió OK.
- Posición de IVA en `/contabilidad/posicion-iva`.
- Job multi-base (patrón `ConversacionesProgramadosHostedService`), WhatsApp con plantillas, push y centro de avisos.

**Qué falta**
- Concepto de **unidad fiscal** (CUIT como entidad propia: razón social, régimen, actividades, contador, vencimientos según terminación de CUIT).
- Todo el inventario de obligaciones, planes, cuotas y notificaciones.
- Calendario de vencimientos AFIP/ARCA por terminación de CUIT.

**Tablas a crear** (script `App_Data/updates/`, idempotente, compat. level 80/100)
- `FIS_UnidadFiscal` (CUIT, razón social, régimen IVA/Ganancias/Monotributo/IIBB, `UNEGOCIO` asociados, contador, activo).
- `FIS_Obligacion` (UnidadFiscalId, impuesto, período, vencimiento, importe determinado, estado, fuente).
- `FIS_Plan` (UnidadFiscalId, tipo moratoria/plan, número, fecha consolidación, cantidad de cuotas, interés, causales de caducidad, estado).
- `FIS_PlanCuota` (PlanId, nº, vencimiento, capital, interés, importe, pagada, fecha de pago, referencia de pago).
- `FIS_Notificacion` (UnidadFiscalId, fecha, asunto, leída, requiere acción, vencimiento de respuesta).
- `FIS_Importacion` (auditoría de cada carga: fuente, archivo, hash, usuario).
- Calendario: tabla `FIS_CalendarioVencimiento` (impuesto, terminación de CUIT, fecha) cargable, no hardcodeada.
- Pantalla `/fiscal` (shell AlfaDesign), con el script de menú web.

**Procesos**
- Importador de `ARCA_SALDO_*.csv` y `ARCA_PLANES_*.csv` a `FIS_*` (con hash para no duplicar).
- Alta manual de obligaciones y planes (para arrancar ya).
- Job diario: recalcula "vence esta semana" y "deuda con ARCA hoy".
- Alerta WhatsApp con plantilla aprobada por Meta.

**Riesgos**
- `arca_cuenta_corriente.py` no está validado contra la pantalla real de ARCA: puede requerir rehacerlo.
- Clave fiscal en `.bat` en texto plano; custodia de clave fiscal.
- La caducidad de planes depende de las reglas de cada régimen: cargarlas como configuración y validarlas con el contador, no asumirlas.
- Plantillas WhatsApp requieren aprobación de Meta.

**Primer paso concreto**: crear `FIS_UnidadFiscal`, `FIS_Plan` y `FIS_PlanCuota`, y cargar con Alberto los CUIT y las moratorias vigentes a mano (ver preguntas). Con eso sale el tablero "cuotas de los próximos 30 días" sin tocar ARCA.

---

## Fase 2 — Plata: conciliación completa y flujo de fondos

**Qué existe ya**
- Conciliación bancaria y cobranzas (19-20/10): importación, clasificación, matching, vínculos y recibos CB.
- Scrapers MP y Provincia activos; Santander roto.
- Vistas de saldos de ventas y compras (`VE_CPTES_SALDOS_VENTAS`, `CO_CPTES_IMPAGOS_2026`), `VT_CONSOLIDADO_CAJA`.

**Qué falta**
- Captura automática (hoy se suben archivos a mano y no hay tareas programadas).
- Arreglar Santander; evaluar API oficial de Mercado Pago en lugar de scraping.
- Egresos: pagos a proveedores, impuestos y comisiones no tienen conciliación ni asiento automático.
- Flujo de fondos proyectado a 60 días (cobranzas esperadas vs vencimientos de proveedores + obligaciones y cuotas fiscales de la Fase 1).
- Conciliar el **pago real de un VEP/cuota** contra el movimiento bancario.

**Tablas / vistas**
- `dir.vw_flujo_fondos` (cobros esperados por vencimiento de `VE_CPTES_SALDOS_VENTAS`, pagos de `CO_*`, obligaciones `FIS_*`, por unidad y consolidado).
- Vínculo `FIS_PagoVinculo` (cuota u obligación ↔ `BK_ExtractoMovimiento`).
- Regla de clasificación nueva en `BK_ReglaInterpretacion` para pagos a ARCA (VEP, débito automático).
- Claves: `FISCAL-DIAS-HORIZONTE-FLUJO` (60), `FISCAL-PORC-COBRANZA-ESPERADA`.

**Procesos**
- Tarea programada de scrapers, con carpeta de entrada vigilada por AlfaCore (o importación por API cuando exista).

**Riesgos**
- Scraping de homebanking: fragilidad, bloqueos, 2FA, términos de uso del banco.
- El flujo proyectado es una estimación; la pauta de cobranza esperada debe ser configurable y revisable.
- Unidades distintas pueden compartir cuentas bancarias: definir a qué CUIT pertenece cada cuenta.

---

## Fase 3 — Control del contador: cruces DDJJ / cálculo propio / pagos

**Qué existe ya**
- Libros de IVA ventas/compras y posición de IVA propia.
- Retenciones y percepciones en cobranzas y compras.
- `arca_comprobantes.py` (compras y ventas de ARCA por período), hoy sin funcionar.

**Qué falta**
- **Destino de las compras ARCA** (no hay tabla definida; el importador es VB6 sin ubicar). Hace falta `FIS_ComprobanteArca` (por CUIT, período, emitido/recibido) y su comparación con `Libro_Compras`.
- Captura de la DDJJ presentada (F.2002/IVA, importes y fecha de presentación). Opciones: carga del PDF/CSV con lectura asistida, o consulta por servicio ARCA si existe. Sin verificar.
- Retenciones/percepciones sufridas ("Mis Retenciones"): no hay captura.
- Reglas de control.

**Reglas de control (todas configurables)**
1. IVA propio (débito ventas − crédito compras ARCA) vs saldo de DDJJ presentada, con tolerancia.
2. Compras ARCA no cargadas en Alfa y viceversa.
3. VEP/pago generado vs pago real en bancos/MP.
4. Cuotas vencidas sin pago; riesgo de caducidad.
5. Notificaciones nuevas sin leer.
6. Presentaciones vencidas sin DDJJ.

**Tablas**: `FIS_DDJJ`, `FIS_ComprobanteArca`, `FIS_Control` (resultado de cada regla con detalle y estado), `FIS_Retencion`.

**Riesgos**
- Es la fase más sensible: un falso positivo daña la relación con el contador, un falso negativo da falsa seguridad. Cada hallazgo debe mostrar su evidencia y la regla que lo originó.
- Diferencias legítimas (proformas, períodos de imputación, remanentes) generarán ruido; las reglas deben ser ajustables.

---

## Fase 4 — Director IA: informe semanal y consultas

**Qué existe ya**
- Patrón completo de agente con tool-calling (OpenAI), medición de consumo con tope por cliente (`IIaUsoRecorder`), `InformesIaService` con validador SQL, canal WhatsApp bidireccional.

**Qué falta**
- Vistas `dir.*` de solo lectura (fiscal, flujo, controles, ventas/cobranzas resumidas).
- Herramientas del agente (`dir_vencimientos`, `dir_deuda_arca`, `dir_flujo`, `dir_hallazgos`) que consulten esas vistas.
- Informe semanal programado, y consulta por WhatsApp restringida a números autorizados.
- Registro de qué dato se le dio al modelo en cada respuesta (auditoría).

**Procesos**
- Job semanal (día/hora configurables) → arma datos → LLM redacta → se guarda en tabla `DIR_Informe` → se envía por WhatsApp/email.
- Chat "Director" con herramientas, con login de base de solo lectura.

**Riesgos**
- Datos fiscales saliendo a un proveedor de IA externo: revisar con Alberto qué se envía; mandar agregados cuando se pueda.
- Alucinación de cifras: el informe debe citar la vista/fecha de cada número y las cifras se insertan desde SQL, no desde el texto del modelo.
- Costo de IA: reutilizar topes existentes.

---

## Fase 5 — Resto de las áreas

Soporte, ventas y programación. Se reutiliza la misma estructura (vistas `dir.*` + herramientas + informe). Qué hay ya: Tablero de Ventas, CRM, Cotizaciones, Conversaciones con análisis de satisfacción. Falta definir indicadores de cada área (tiempos de respuesta de soporte, embudo comercial, entregas de desarrollo). Se detalla recién al terminar la Fase 3.

---

## Producto: qué se puede vender y cómo

**Lo que sí es vendible** (como módulos nuevos en `ALFA_CENTRAL.Modulos`):

| Módulo | Qué es | Dependencia de datos | Madurez |
|---|---|---|---|
| Vencimientos y deuda fiscal | `FIS_*` + tablero + alertas | Carga manual o importación de CSV | Alta (Fase 1) |
| Flujo de fondos | Cobros esperados vs pagos/impuestos | Alfa Gestión | Alta (Fase 2) |
| Conciliación bancaria inteligente | Ya existe | Extractos subidos | Alta, falta captura automática |
| Control del contador | Cruces IVA/DDJJ/pagos | ARCA + libros | Media (Fase 3) |
| Director IA | Informe + consultas | Todo lo anterior | Posterior |

**Cómo hacerlo vendible**
- Sin nada de ALFA NET en el código; todo por configuración y por unidad fiscal.
- Despliegue por script idempotente en cada base de cliente (ya existe el mecanismo) y habilitación por `ClienteModulos`, con medición y tope de IA por cliente ya existentes.
- Los clientes tienen bases viejas (compatibility level 80/100): evitar sintaxis moderna.

**Lo que NO conviene vender tal cual**
- **Scrapers con clave fiscal de terceros.** Implica custodiar claves fiscales de clientes (riesgo legal y reputacional), y los sitios cambian. Alternativa: que cada cliente delegue en un certificado/relación de ARCA y consumir Web Services, o que cargue los archivos que baja el propio cliente. Lo importante es decidirlo antes de diseñar la captura.
- Scraping de homebanking de terceros: soporte caro. Mejor importar extractos (ya funciona) y sumar APIs oficiales donde existan.

**Decisión de producto pendiente**: vender primero Fase 1 + Fase 2 a 2 o 3 clientes piloto con contador externo; ahí es donde el dolor es mayor y el riesgo menor.

---

## Seguridad transversal (antes de cualquier fase)

- Sacar la clave fiscal de los `.bat` de `C:\dev\scraping` (variable de entorno o almacén cifrado; rotar la clave si estuvo en algún respaldo).
- `appsettings.json` y `bases.dbpassword` guardan credenciales en texto plano; para un producto, usar el patrón de cifrado que ya existe (`WhatsAppSecureVault`, `AuditDataProtector`).
- Login SQL de solo lectura para el Director IA y esquema `dir`.
- Registrar acceso y consultas del Director en auditoría.

## Preguntas para Alberto

**Fiscal**
1. ¿Cuántos CUIT son y qué régimen tiene cada uno (RI, Monotributo, Ganancias, IIBB, convenio multilateral, empleador)?
2. ¿Cada CUIT es una `Unidad de Negocio` en Alfa o hay más unidades de negocio que CUIT? ¿Cuál es el mapa unidad → CUIT?
3. ¿Qué moratorias o planes de pago hay vigentes? Por cada uno: CUIT, número de plan, cuotas, importe, vencimiento y si hay cuotas atrasadas.
4. ¿Hay notificaciones sin leer en el Domicilio Fiscal Electrónico? ¿Quién las revisa hoy?

**Contador**
5. ¿Qué te manda el contador (VEP, DDJJ, resumen, Excel, PDF) y por dónde (mail, WhatsApp, portal)? ¿Con qué frecuencia?
6. ¿Quién hoy presenta las DDJJ y quién paga? ¿El pago sale de una cuenta concreta?
7. ¿El contador tiene acceso a ARCA con relación o con tu clave?

**Datos**
8. ¿Las compras de ARCA hoy se cargan a Alfa? ¿Con qué importador (el VB6) y a qué tabla?
9. ¿Qué cuentas bancarias y de Mercado Pago corresponden a cada CUIT?
10. ¿Podés darme un login SQL de solo lectura para validar tablas y vistas en SERVER-ALFACORE?

**Alertas y producto**
11. ¿A qué números de WhatsApp se envían las alertas y quién puede consultar al Director?
12. ¿Aceptás enviar datos fiscales agregados a OpenAI? ¿Preferís otro proveedor?
13. ¿Qué tipo de cliente querés como piloto del producto (comercio, servicios, con contador externo)? ¿Hay alguno concreto?
14. ¿El prefijo `FIS_` te sirve, o preferís `ALFACORE_FIS_` como en otras tablas nuevas del repo?

## Primer paso concreto (Fase 1)

1. Confirmar con vos las preguntas 1 a 3.
2. Crear el script `App_Data/updates/AAAA-MM-DD-NNN__fiscal_base.sql` con `FIS_UnidadFiscal`, `FIS_Plan`, `FIS_PlanCuota`, `FIS_Obligacion`, `FIS_CalendarioVencimiento` y las claves `FISCAL-*` en `TA_CONFIGURACION`.
3. Pantalla `/fiscal` con tablero: vence esta semana, deuda con ARCA, cuotas próximas, riesgo de caducidad. Carga manual.
4. Después, importar los CSV de `arca_cuenta_corriente.py`, una vez validado contra ARCA real.
