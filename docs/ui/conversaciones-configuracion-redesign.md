# Rediseño Conversaciones → Configuración (AlfaDesign)

**Estado del documento:** MIGRACIÓN EN PLANIFICACIÓN
**Fase actual:** C0 — Baseline + contrato de producto
**Rama de trabajo:** `main` (se trabaja directamente sobre `main`, sin rama nueva)

---

## 1. Base de partida

- Rama: `main`
- HEAD al iniciar C0: `d0e4d4dd4ea104b09035b4a5ea625aea2ef67e8a`
- `origin/main` al iniciar C0: `d0e4d4dd4ea104b09035b4a5ea625aea2ef67e8a` (coincide con HEAD)
- `main` es superconjunto estricto de la rama `Evelyn`: contiene AlfaDesign más reciente (Clientes Browse, Smart Search popover compartido, Data View footer, column width persistence), Conversaciones más reciente (multinúmero, WhatsApp Web por número, worker Node), y todas las migraciones SQL vigentes.
- Este documento parte de la auditoría previa de Conversaciones → Configuración (alcance solo lectura) y de la reconciliación de estado Git (Fase 0), ambas ya aprobadas conceptualmente.

## 2. Objetivo del rediseño

Rediseñar la pantalla `Conversaciones → Configuración` aplicando AlfaDesign v1, con foco en:

- navegación interna clara y escalable;
- separación real de categorías (canales, automatización, integraciones IA, operación, soporte);
- reorganización de formularios, credenciales y estados;
- baja carga cognitiva y progressive disclosure.

No es un cambio de colores/CSS: es una reorganización de arquitectura de información y de patrones de interacción, manteniendo el comportamiento funcional y el storage existentes.

## 3. Restricciones de producto (obligatorias durante todo el rediseño)

### 3.1 Flujo principal de WhatsApp — experiencia QR

Para el cliente final, el camino principal debe ser:

```
WhatsApp → Números → Agregar/configurar número → WhatsApp Business / WhatsApp Web
→ Escanear QR → Conectado → Usuarios habilitados → Operación
```

La conexión por QR debe sentirse simple, directa, guiada y comprensible para un usuario no técnico. Es el flujo más visible de la experiencia principal.

### 3.2 Meta Cloud API — no es el flujo principal

Meta Cloud API existe y puede estar en uso real. **No se elimina nada**: Access Token, App Secret, Verify Token, Graph API Version, WABA ID, Phone Number ID, Webhook, proveedor Meta, configuración Cloud y cualquier valor existente se preservan íntegramente.

Meta Cloud API deja de ser parte de la experiencia principal del cliente común y pasa a vivir conceptualmente en **Avanzado** y/o **Soporte**, según la arquitectura de permisos que se defina en fases posteriores.

**Ocultar de la experiencia principal ≠ eliminar del sistema.**

## 4. Regla de preservación de datos (dato productivo)

Toda configuración existente se considera dato productivo y potencialmente en uso. Durante el rediseño:

- no borrar;
- no resetear;
- no reemplazar;
- no inicializar en blanco;
- no migrar silenciosamente.

Alcance mínimo a preservar: `TA_CONFIGURACION`, claves `CONV_*`, `CONV_WHATSAPP_NUMEROS`, `CONV_WHATSAPP_NUMERO_USUARIOS`, `CONV_ADMINISTRADORES`, `CONV_ASISTENTE`, `CONV_REGLAS`, prioridades, horarios, SLA, prompts, automatizaciones, AlfaKnowledge, credenciales Meta/Instagram/Facebook/Mercado Libre, OAuth tokens, refresh tokens, webhooks, webhook routing token, App Secrets, Verify Tokens, Phone Number IDs, WABA IDs, sesiones WhatsApp Web, pairing, runtime, providers, administradores, usuarios habilitados, configuración legacy, appsettings fallback, compatibilidad histórica.

## 5. Regla crítica — oculto no significa vacío

Si una configuración no está visible en la sección que el usuario edita actualmente:

- no debe enviarse como `null`;
- no debe enviarse como string vacío;
- no debe volver a default;
- no debe eliminarse;
- no debe sobrescribirse accidentalmente.

Ejemplo: guardar la configuración de un número WhatsApp Web no debe modificar Meta Cloud API, Access Token, App Secret, Verify Token, webhook, Instagram, Facebook, Mercado Libre, automatizaciones, otros números ni configuración global.

## 6. Scopes reales a respetar

El guardado debe respetar el scope real de cada configuración, sin crear un "Guardar todo" que mezcle scopes heterogéneos:

- Base / empresa
- Base + canal
- Base + número
- Base + usuario
- Base central (routing multitenant)
- Runtime local / filesystem

## 7. Compatibilidad legacy

No retirar durante el restyle: Phone Number ID global legacy, claves `CONV_WHATSAPP_WEB_*` legacy, rutas webhook sin token, fallback en `appsettings`, columnas históricas de storage, bridges de compatibilidad, almacenamiento alternativo existente. Pueden documentarse como legacy, pero su retiro es una fase de migración explícita y separada, no parte del restyle.

## 8. Principios UX a aplicar

Claridad, consistencia, baja carga cognitiva, progressive disclosure, jerarquía, descubribilidad, separación de responsabilidades.

Evitar: formularios enormes, scroll extremo, exceso de tabs horizontales, configuración técnica junto a opciones simples, estados mezclados con inputs, ayuda técnica ocupando media pantalla, secretos visibles permanentemente, acciones peligrosas con el mismo peso visual que Guardar, datos SQL visibles para usuarios comunes.

## 9. Niveles de experiencia (conceptuales, sin permisos todavía)

- **Principal**: canales, números, conectar WhatsApp por QR, estado de conexión, usuarios habilitados, horarios, bienvenida, automatizaciones habituales, reglas, estado general.
- **Avanzado**: Graph API version, IDs técnicos, parámetros técnicos, APIs, webhooks, configuración Meta Cloud, timeouts, parámetros avanzados.
- **Soporte**: runtime, PID, paths, storage, errores, logs, compatibilidad legacy, appsettings/database, diagnóstico técnico.

No se crean permisos nuevos en C0; solo se define la arquitectura conceptual.

## 10. Arquitectura auditada (hipótesis de IA, sujeta a refinamiento en C1)

```
Configuración
├─ Resumen
├─ Canales
│  ├─ WhatsApp
│  │  ├─ Números
│  │  │  ├─ Número A (Estado, Vincular QR, Usuarios, Sesión)
│  │  │  └─ Número B ...
│  │  ├─ General
│  │  └─ Avanzado / Soporte (Meta Cloud API)
│  ├─ Instagram
│  ├─ Facebook
│  └─ Mercado Libre
├─ Automatización
├─ Integraciones IA
├─ Operación y accesos
└─ Soporte
```

Esta hipótesis de IA **no modifica modelos ni storage**. La UI se adapta a los datos reales existentes, no al revés.

WhatsApp Web (QR) y Meta Cloud API son dos experiencias distintas y no deben mezclarse en el mismo formulario principal.

Instagram y Facebook pueden compartir patrón UX Meta cuando corresponda, sin unificar storage ni crear un modelo global de cuenta Meta sin una fase backend explícita.

Mercado Libre permanece dentro de Canales, separando conceptualmente Cuenta/OAuth, Credenciales, Webhook y Diagnóstico. No se modifica el OAuth actual en esta fase.

Automatización (horarios, bienvenida, reglas, auto-cierre, SLA) se separa conceptualmente de Integraciones IA (AlfaKnowledge, informes).

Operación y accesos agrupa prioridades, usuarios por número y administradores, sin mezclar runtime técnico (que va a Soporte).

La pestaña "Herramientas" actual está vacía; sus funciones reales existentes (AnyDesk, runtime Web) deben reclasificarse en Operación/Soporte, no mantenerse vacía por compatibilidad visual.

## 11. Navegación futura

Subnavegación lateral interna para Configuración (no es un sidebar global, es navegación propia del workspace), sobre:

```
App Top Bar (compartida AlfaDesign)
+ Context Toolbar (compartida)
+ Configuración Workspace
    ├─ Internal Settings Navigation
    └─ Settings Content
```

No se implementa en C0. Deep links futuros a evaluar en C1 (ejemplo conceptual: `/conversaciones/configuracion/canales/whatsapp`, `/conversaciones/configuracion/canales/whatsapp/numeros`), sin implementarse todavía.

## 12. Estado vs configuración vs runtime vs diagnóstico vs acción vs ayuda

La UI futura debe distinguir visualmente estos conceptos sin mezclarlos:

- **Configuración**: ej. proveedor = WhatsApp Web.
- **Estado de configuración**: ej. credenciales completas.
- **Runtime**: ej. sesión conectada.
- **Diagnóstico**: ej. PID/error/última conexión.
- **Acción**: ej. generar QR.
- **Ayuda**: ej. cómo configurar.

"Configurado" no equivale a "Operativo": muchos estados actuales solo indican presencia de campos, no un health check real. No se debe llamar "Operativo" sin verificación real.

## 13. Contrato de secretos (objetivo, sin implementar en C0)

Por defecto no se muestra el valor completo; se muestra "Configurado"/"No configurado". Acciones futuras (según permisos): Reemplazar, Mostrar, Copiar. El modelo futuro debe permitir "mantener valor existente" sin devolver el secreto completo al navegador — esto requiere una fase backend específica (no en C0). El rediseño visual no debe borrar un valor existente solo porque el input no lo carga.

## 14. Ayuda y documentación

Reclasificar la ayuda actual (hoy dominante) en: Help inline (una frase junto al campo), Help drawer/collapsible (guías extensas), Documentación (manual externo/interno), Soporte (detalle SQL/storage). No se borra documentación existente; se reubica progresivamente.

## 15. Acciones de riesgo

Acciones como Stop sesión, reset, desvincular, regenerar QR, eliminar regla, reemplazar credencial deben usar `AlfaConfirmDialog` y jerarquía visual destructiva adecuada en fases futuras. No se ejecutan automáticamente ni se implementan en C0.

## 16. Contrato AlfaDesign a reutilizar

`AlfaButton`, `AlfaIconButton`, `AlfaInput`, `AlfaSelect`, `AlfaCheckbox`, `AlfaTag`, `AlfaTabs`, `AlfaActionMenu`, `AlfaDialog`, `AlfaConfirmDialog`, `AlfaLookup`, `AlfaNotification`, `AlfaEmptyState`. No crear versiones específicas de Conversaciones sin necesidad real.

Patrones posibles todavía no formalizados por AlfaDesign (a decidir en C1, no crear en C0): Settings Internal Navigation, Secret Input, Settings Section, Configuration Status, Diagnostic Result, Help Drawer, Number/Session Manager, dirty-state contextual.

Configuración **no** copia el layout de Contactos/Usuarios/Técnicos/Clientes; reutiliza shell, componentes, tokens, dialogs y notificaciones, pero su Information Architecture es propia.

## 17. Guardado (estrategia aprobada conceptualmente)

Híbrido por sección. Ejemplos: Guardar WhatsApp General, Guardar Automatizaciones, Guardar un número, Guardar accesos, Guardar credencial avanzada. Test/Verify es siempre una acción separada de Save. No se implementa "Guardar todo".

## 18. Dirty state (objetivo futuro, no implementado en C0)

Dirty state por sección, `NavigationLock`, Guardar, Descartar, detección de no-op, guard de doble submit.

## 19. Responsive objetivo

Diseñar para 2048 / 1440 / 1024. En 1024: compact shell, navegación interna usable, sin scroll horizontal global, formularios legibles, secretos no desbordados, cards contenidas, acciones accesibles. La subnav interna puede necesitar colapsarse o convertirse en selector — a decidir en C1.

## 20. Riesgos de seguridad confirmados (registro, no corrección)

Confirmados en el código real de `main` durante la Fase 0 de reconciliación:

1. `WebInstanceName` / path traversal en sesiones WhatsApp Web (sin containment de path).
2. Configuración de Conversaciones sin autorización robusta (`[Authorize]`/`RequireAuthorization` ausente).
3. Secretos completos enviados y renderizados en el cliente.
4. OAuth de Mercado Libre sin `state` ni identificador de tenant.
5. WhatsApp acepta webhook sin validar firma cuando no hay App Secret configurado.
6. Webhook de Mercado Libre sin verificación de autenticidad.
7. Rutas webhook legacy con fallback siguen habilitadas.
8. Secretos almacenados en texto plano (sin cifrado en reposo).

Estos riesgos **no se corrigen dentro de esta fase ni accidentalmente durante el restyle**. Se corrigen en fases backend explícitas (ver sección 22). El rediseño no debe empeorar ni ampliar la exposición existente, ni ocultar estos problemas fingiendo que están resueltos.

## 21. Funcionalidad que no debe tocarse durante el rediseño

Routing de webhooks, token multibase, claves `CONV_*`, fallback legacy, tablas y relaciones existentes, asociación `IdNumeroWhatsApp`, usuarios por número, administradores, providers, integración Meta, WhatsApp Web, worker Node, OAuth actual (hasta fase explícita), hosted services, runtime, filesystem, auditoría (`AUX_ERR`/`IAppEventService`), compatibilidad con bases antiguas, regla `?directo=1`, navegación funcional completa del módulo.

## 22. Plan de fases (preliminar, sujeto a reorganización en C1)

**Fases UX/producto:**

- **C0** — Baseline + contrato de producto (esta fase).
- **C1** — Shell + Information Architecture + navegación interna.
- **C2** — Resumen de Configuración + Canales.
- **C3** — WhatsApp orientado a números + experiencia QR.
- **C4** — Instagram / Facebook / Mercado Libre.
- **C5** — Automatizaciones + Integraciones IA.
- **C6** — Operación + accesos.
- **C7** — Soporte / ayuda / diagnóstico.
- **C8** — Dirty state + guardado contextual.
- **C9** — Responsive + auditoría legacy + documentación final.

**Fases de seguridad backend (separadas, explícitas, no acopladas al restyle):**

- **SEC-1** — Autorización de Configuración + contrato de secretos.
- **SEC-2** — Path containment de WhatsApp Web (`WebInstanceName`).
- **SEC-3** — OAuth state/tenant + autenticidad de webhooks (Mercado Libre, firma WhatsApp obligatoria).
- **SEC-4** — Storage de secretos / migración a almacenamiento protegido, si se aprueba.

El orden definitivo de las fases SEC se decide antes de ejecutar cada una. Esta lista es preliminar; C1 puede reorganizarla una vez definida la IA final.
