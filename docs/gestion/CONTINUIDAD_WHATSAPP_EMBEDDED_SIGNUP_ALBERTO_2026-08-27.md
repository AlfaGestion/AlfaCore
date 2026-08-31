# Continuidad para Alberto — Conversaciones, WhatsApp y Embedded Signup

Fecha de corte: 27/08/2026  
Repositorio: `C:\dev\AlfaCore`  
Rama: `main`  
Commit de corte: `9f5c272cd430eba43f7e585d8692e796719c083c`  
Estado Git al entregar: limpio y sincronizado con `origin/main`.

Este documento es autocontenido. Su objetivo es que Alberto pueda abrir una sesión nueva de Codex, leer un único handoff y continuar sin depender de Eve.

## 1. Reglas que deben leerse antes de tocar el proyecto

Lectura obligatoria:

- `AGENTS.md`
- `docs/CODEX_RULES.md`
- `docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md`
- `docs/CONFIGURACION_GLOBAL.md`

Para ubicar documentación:

- `docs/README.md`

Reglas críticas para esta continuidad:

- No conectarse ni ejecutar SQL contra `10.8.0.31 / ALFA_CENTRAL` sin una autorización nueva y explícita.
- No publicar en `alfacentral.ddns.net` sin una autorización nueva y explícita.
- No iniciar Meta, Embedded Signup, `/register`, webhooks ni mensajes reales automáticamente.
- No imprimir ni documentar App Secret, access tokens, PIN, state, authorization code, ciphertext ni Data Protection keys.
- Todo error relevante debe registrarse en `AUX_ERR` mediante el servicio centralizado.
- Antes de finalizar cualquier cambio ejecutar `python tools/catalogo/check_catalogo.py`.
- Los cambios de base productiva deben preservar datos, historial, usuarios y compatibilidad legacy.

## 2. Resumen ejecutivo del estado actual

Se rediseñó `Conversaciones → Configuración` con AlfaDesign y se dejó operativo el manejo multinúmero de WhatsApp Business/QR y WhatsApp Cloud API.

Además se construyó la fundación de Meta Embedded Signup:

- onboarding persistente;
- state single-use;
- ownership global de WABA y Phone Number ID;
- Vault cifrado con Data Protection;
- allowlist por Base;
- modos `STANDARD` y `BUSINESS_APP_COEXISTENCE`;
- discovery Graph;
- registro supervisado para Standard;
- importación operacional mediante el UPSERT existente;
- resolución de credencial runtime Vault/legacy;
- protección de routing cross-tenant;
- entorno LocalDB autónomo para desarrollo;
- recuperación de un activo Meta ya autorizado sin repetir Embedded Signup.

El estado actual importante es:

- Todo el código está en `origin/main`, commit `9f5c272`.
- Producción no recibió este rollout de Embedded Signup.
- `WorkerEnabled=false` debe mantenerse.
- Base permitida para pruebas supervisadas: solamente Base 84.
- En LocalDB DEV, el activo `AlfaNet Tester` fue recuperado correctamente y existe como `IdNumero=1`.
- No se debe volver a crear otro número Meta para continuar las pruebas.

## 3. Entornos: no confundirlos

### 3.1 Producción actual

- Sitio público: `https://alfacentral.ddns.net`
- Central productiva conocida: `10.8.0.31 / ALFA_CENTRAL`
- No fue autorizada para el trabajo local reciente.
- No se aplicó allí el schema ES.
- No se desplegó allí el commit actual como rollout de Embedded Signup.
- Los clientes distintos de Base 84 deben seguir usando comportamiento WhatsApp legacy.

### 3.2 Integración automatizada SQL

- Servidor: `(localdb)\MSSQLLocalDB`
- Catálogo: `ALFA_CENTRAL_TEST`
- Variable: `ALFACORE_ES_SQL_TEST_CONNECTION`
- Uso exclusivo: integration tests SQL.
- Fixtures: IDs artificiales, no Base 84 ni activos Meta reales.

### 3.3 Desarrollo supervisado ES Local

- Central: `(localdb)\MSSQLLocalDB / ALFA_CENTRAL_DEV`
- Tenant: `(localdb)\MSSQLLocalDB / ALFACORE_ES_TENANT_DEV`
- Base: `84 / ES_DEV_BASE_84`
- Cliente local: `ES_LOCAL`
- IdWeb: `ALFANET`
- URL: `https://localhost:7055`
- Ruta directa:
  `https://localhost:7055/ALFANET/84/conversaciones/configuracion?seccion=canales&subseccion=whatsapp-api`
- Key ring:
  `%LOCALAPPDATA%\AlfaCore\DataProtectionKeys\WhatsAppEmbeddedSignup`
- Feature flag: habilitado solamente para Base 84.
- Worker ES: apagado.
- Hosted services ajenos al entorno mínimo: deshabilitados solamente cuando `AlfaCoreEsLocal:Enabled=true` y el entorno es Development.

Credenciales exclusivamente locales:

```text
Usuario: eslocal@alfacore.dev
Password: AlfaCore-ES-84!
```

No existe bypass de autenticación. El launcher crea usuarios DEV utilizando los verificadores/codecs reales de AlfaCore.

## 4. Cómo iniciar el entorno local

Desde el explorador, doble clic en:

```text
Run-AlfaCore-ES-Local.cmd
```

O desde PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-alfacore-es-local.ps1
```

El launcher:

1. valida Development y LocalDB;
2. crea/verifica idempotentemente `ALFA_CENTRAL_DEV`;
3. crea/verifica `ALFACORE_ES_TENANT_DEV`;
4. aplica únicamente los bootstraps DEV;
5. crea/reutiliza el login DEV;
6. fija conexiones central/tenant a LocalDB dentro del proceso;
7. habilita ES solamente para Base 84;
8. mantiene `WorkerEnabled=false`;
9. inicia HTTP 5055 y HTTPS 7055;
10. nunca abre Meta por sí solo.

Si falta el App Secret local, cargarlo con user-secrets, sin pegarlo en chats o commits:

```powershell
dotnet user-secrets set "WhatsAppEmbeddedSignup:AppSecret" "<APP_SECRET>" --project src/AlfaCore/AlfaCore.csproj
```

La aplicación quedó iniciada en esta computadora al cerrar la sesión, pero Alberto debe asumir que los procesos locales pueden no sobrevivir y volver a ejecutar el launcher.

## 5. Estado real de LocalDB DEV al cierre

Última verificación segura:

```text
Central=ALFA_CENTRAL_DEV
Tenant=ALFACORE_ES_TENANT_DEV
Base=84
Onboardings=3
Último onboarding cronológico=CANCELLED / CANCELLED
WabaOwnerships=2
PhoneOwnerships=2
VaultCredentials=3
ActiveNumbers=1
ActiveApiNumbers=1
```

Número operacional recuperado:

```text
IdNumero=1
PhoneNumberId=1195619520311268
Nombre=AlfaNet Tester
Activo=true
Usuarios=ninguno
```

El último onboarding cronológico es `CANCELLED` porque Eve abrió y abandonó un Embedded Signup después del onboarding exitoso. No eliminarlo ni repararlo. La recuperación busca específicamente el último onboarding histórico `READY / READY` válido.

Comando de diagnóstico seguro:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:AlfaCoreEsLocal__Enabled='true'
$env:WhatsAppEmbeddedSignup__AllowedBaseIds__0='84'
$env:WhatsAppEmbeddedSignup__WorkerEnabled='false'
$env:ConnectionStrings__AlfaCentral='Server=(localdb)\MSSQLLocalDB;Initial Catalog=ALFA_CENTRAL_DEV;Integrated Security=True;TrustServerCertificate=True'
$env:ConnectionStrings__AlfaGestion='Server=(localdb)\MSSQLLocalDB;Initial Catalog=ALFACORE_ES_TENANT_DEV;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tools/AlfaCore.EsLocalTool/AlfaCore.EsLocalTool.csproj -- inspect-state
```

## 6. Activos Meta supervisados

Identificadores públicos/no secretos:

```text
Meta App ID: 1436083307772786
Business Portfolio ID AlfaNet: 792034091131840
System User ID: 61574820802043
Embedded Signup Config ID correcto: 1753413148641744

WABA: 1547539197385596
Phone Number ID: 1195619520311268
Display phone: +1 555-482-7373
Verified name: AlfaNet Tester
Estado Meta comprobado: Registered
```

No hardcodear estos valores dentro de lógica de dominio. Los cuatro primeros pertenecen a configuración tipada. WABA y Phone Number ID son activos de validación, no defaults productivos.

La auditoría read-only comprobó:

- credencial Vault presente y descifrable;
- contexto Base 84/WABA/Phone correcto;
- ownership WABA y Phone de Base 84;
- `GET /1547539197385596/phone_numbers` exitoso;
- teléfono encontrado con nombre y número esperados;
- estado `Registered`.

Comando de auditoría read-only disponible:

```powershell
dotnet run --project tools/AlfaCore.EsLocalTool/AlfaCore.EsLocalTool.csproj -- audit-existing-meta 1547539197385596 1195619520311268
```

Debe ejecutarse solamente con las guardas/variables ES Local anteriores. No registra tokens y el routing provider del comando rechaza modificaciones de webhooks.

## 7. Recuperar WhatsApp conectado

Se implementó el caso:

```text
onboarding histórico READY
+ ownership correcto
+ Vault válido
+ Graph read-only confirma el activo Registered
+ fila operacional inexistente o inactiva
→ recuperar mediante SaveWhatsAppNumeroAsync
```

UI antes de recuperar:

```text
Encontramos un WhatsApp ya conectado
AlfaNet Tester · +1 555-482-7373

Este WhatsApp ya está autorizado en Meta. Podés volver a agregarlo a AlfaCore sin configurarlo nuevamente.

[Recuperar WhatsApp] [Conectar otro WhatsApp]
```

La acción repite todo el preflight antes de escribir y utiliza:

```text
PhoneNumberId = resultado Graph validado
Nombre = VerifiedName validado
Activo = true
Usuarios = []
```

No cambia el onboarding `READY`, no ejecuta `/register`, no toca webhooks y no crea activos Meta.

Idempotencia:

- `SaveWhatsAppNumeroAsync` busca por `PhoneNumberId` con locking transaccional;
- adopta/reactiva la fila existente;
- preserva usuarios cuando adopta una fila y `Usuarios=[]`;
- preserva historial/relaciones porque nunca elimina la fila;
- la recuperación exige terminar con exactamente una fila activa para el Phone Number ID.

Estado visual posterior validado con navegador real:

- `WhatsApp conectados = 1`;
- banner `WhatsApp conectado`;
- `AlfaNet Tester`;
- `+1 555-482-7373`;
- Phone Number ID solo en Información técnica;
- `Sin usuarios asignados`;
- botón `Asignar usuarios` disponible;
- CTA de recuperación oculto;
- sin duplicados después de recargar.

Archivos principales:

- `src/AlfaCore/Services/WhatsAppEmbeddedOperationalImportService.cs`
- `src/AlfaCore/Services/WhatsAppEmbeddedSignupContracts.cs`
- `src/AlfaCore/Services/WhatsAppEmbeddedSignupStore.cs`
- `src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor`
- `tools/AlfaCore.EsLocalTool/Program.cs`

## 8. Definición visual de “Conectado”

Se corrigió una contradicción previa: la UI mostraba `WhatsApp conectado` por un onboarding histórico `READY` aunque hubiera cero números operativos.

Regla actual:

- `Conectado` exige al menos un número operacional activo.
- Onboarding/Vault/ownership/configuración técnica por sí solos no significan conexión operacional.
- `READY` sin número permite recuperación o un nuevo onboarding.
- Estados recuperables muestran progreso/acción correspondiente.
- Cancelled/Expired/Failed/inconsistente permiten iniciar nuevamente.

Código:

- `WhatsAppEmbeddedConnectionUiStateResolver` en `WhatsAppEmbeddedSignupModels.cs`.

## 9. Estado de WhatsApp Business/QR

Etapa 4 quedó aprobada y cerrada:

- QR real: OK;
- worker Node/Baileys: OK;
- pairing QR: OK;
- polling: OK;
- reconexión QR: OK;
- cierre de sesión: OK;
- historial preservado: OK;
- asignación de usuarios: implementada;
- cleanup en memoria de altas provisionales: implementado;
- nueva sesión cancelada después de pairing: `Activo=false`, sin DELETE;
- reconectar y cancelar: conserva sesión existente.

Pendiente explícito que no debe confundirse con QR:

```text
Pendiente post-rediseño — WhatsApp Business:
diagnosticar pairing mediante PHONE_NUMBER / código de vinculación.
```

No modificar el flujo QR ya validado al investigar ese pendiente.

## 10. Estado de WhatsApp Cloud API y multinúmero

Implementado:

- listado único de números Cloud API;
- alta manual `Agregar por Phone Number ID`;
- validación obligatoria: solo dígitos, trim, sin longitud inventada;
- archivado `Quitar de AlfaCore` mediante `Activo=false`, nunca DELETE;
- UPSERT/reactivación por Phone Number ID;
- asignación de usuarios con diálogo AlfaDesign y clon completo del DTO;
- callback tokenizado oculto en Read y copiable sin regenerar;
- secretos mostrados solo como Configurado/No configurado;
- WABA/Graph/Base HTTPS agrupados como configuración avanzada;
- IDs internos movidos a Información técnica;
- compatibilidad multinúmero y separación de historiales.

Pendiente:

- validación remota del Phone Number ID manual;
- el alta manual valida formato, no existencia real en Meta.

## 11. Embedded Signup: decisiones de arquitectura

Modelo:

```text
1 Base → N WABA → N números
```

Todos los números Cloud API terminan en `CONV_WHATSAPP_NUMEROS`; no existe un modelo paralelo para Embedded Signup.

Billing confirmado:

```text
CreditMode = CustomerPaysMeta
```

Cada cliente posee su Business/WABA, configura su pago y paga directamente a Meta. AlfaNet no comparte crédito ni ejecuta `whatsapp_credit_sharing_and_attach`. Falta de pago debe ser `ACTION_REQUIRED / CUSTOMER_PAYMENT_SETUP_REQUIRED`, no error técnico.

Modos:

- `STANDARD`: WhatsApp nuevo/Cloud API.
- `BUSINESS_APP_COEXISTENCE`: conservar WhatsApp Business App y usar AlfaCore.

Contrato JS actual:

Standard:

```javascript
extras: {
  sessionInfoVersion: "3"
}
```

Coexistence:

```javascript
extras: {
  setup: {},
  featureType: "whatsapp_business_app_onboarding",
  sessionInfoVersion: "3"
}
```

Listener esperado:

- Standard: `FINISH`.
- Coexistence: `FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING`.

Garantía: Coexistence nunca puede entrar en `RegisterPhone`.

Estado de validación:

- Standard fue completado realmente y llegó a autorización/vault.
- El activo Standard fue registrado/importado de forma supervisada en DEV.
- Coexistence tiene contrato y guardas implementados, pero todavía necesita cierre funcional manual completo. No presentarlo como completamente validado.

## 12. SQL y seguridad de Embedded Signup

Tablas centrales:

- `WhatsAppEmbeddedOnboarding`
- `WhatsAppWabaOwnership`
- `WhatsAppPhoneOwnership`
- `WhatsAppSecureVault`

Script de referencia productivo:

- `docs/base-datos/sql-referencia/2026-08-25-001__alfa_central_whatsapp_embedded_signup.sql`

Bootstraps solo TEST/DEV:

- `docs/base-datos/sql-test/bootstrap_alfa_central_test_embedded_signup.sql`
- `docs/base-datos/sql-test/bootstrap_alfa_central_dev_embedded_signup.sql`
- `docs/base-datos/sql-test/bootstrap_alfacore_es_tenant_dev_auth.sql`

Los scripts TEST/DEV tienen guardas de catálogo y LocalDB. No moverlos a `App_Data/updates` ni tratarlos como migraciones productivas.

Integration tests verificados:

- state single-use;
- ownership WABA;
- ownership Phone;
- concurrencia con un solo owner;
- claiming/lease y recovery;
- Vault round-trip después de recrear scopes/key ring;
- ausencia de plaintext SQL.

Último resultado de suite completa:

```text
77 passed / 0 failed / 0 skipped
```

## 13. Routing webhook y evidencia crítica Base 106

Durante una prueba real anterior, el primer inbound del número nuevo terminó incorrectamente en Base 106 porque el dominio público todavía ejecutaba código anterior y no tenía el routing/ownership nuevo desplegado.

Evidencia que NO se debe borrar todavía:

```text
Base 106
IdNumero 23
IdConversacion 10376
IdMensaje 50745
IdMensaje 50746
```

La respuesta automática observada fue:

```text
¡Hola! Gracias por escribirnos. 🙂 Ya recibimos tu mensaje y en breve te responderemos.
```

No asumir que el routing productivo quedó corregido: el código local/versionado contiene las protecciones, pero falta desplegar y demostrarlo públicamente.

Meta no puede enviar webhooks reales a localhost. `Simular-Webhook-ES-Local.cmd` valida el pipeline local con fixtures ficticios y un caso cross-tenant bloqueado, pero no reemplaza la prueba pública.

## 14. Rollout productivo preparado, no autorizado

Documento vigente:

- `docs/gestion/whatsapp_embedded_signup_base84_rollout.md`

Decisión actual:

- no habrá staging separado;
- Embedded Signup se habilitará en el AlfaCore publicado solamente para Base 84;
- demás clientes continúan legacy;
- secuencia obligatoria DB-before-code;
- `WorkerEnabled=false` durante el rollout supervisado.

Configuración conceptual productiva:

```text
WhatsAppEmbeddedSignup__Enabled=true
WhatsAppEmbeddedSignup__WorkerEnabled=false
WhatsAppEmbeddedSignup__AllowedBaseIds__0=84
```

Key ring propuesto:

```text
C:\ProgramData\AlfaCore\DataProtectionKeys\WhatsAppEmbeddedSignup
```

Debe quedar fuera del publish, con ACL mínima para la identidad real del App Pool, DPAPI del servidor y persistencia entre deploy/recycle. No copiar el key ring local.

Antes de cualquier onboarding productivo:

1. backup verificable de `ALFA_CENTRAL`;
2. aplicar/verificar schema ES;
3. desplegar binarios/configuración;
4. smoke test inbound/outbound de un WhatsApp legacy;
5. si falla, rollback y detenerse;
6. recién después repetir Embedded Signup manual en Base 84;
7. configurar/verificar override únicamente para la WABA Base 84;
8. detenerse antes del mensaje;
9. el usuario envía manualmente `Pruebaaa 2`;
10. demostrar Base 84 correcta y ausencia de datos nuevos en Base 106.

Nada de esto está autorizado por este handoff. Alberto debe pedir autorización explícita antes de ejecutar producción o Meta.

## 15. Documentación principal para profundizar

- Manual usuario SaaS:
  `src/AlfaCore/Docs/manual_conversaciones_whatsapp_saas.md`
- Integración Cloud API:
  `docs/modulos/integraciones/whatsapp_cloud_api.md`
- Arquitectura Embedded Signup:
  `docs/modulos/integraciones/whatsapp_embedded_signup.md`
- Rediseño y bitácora técnica:
  `docs/ui/conversaciones-configuracion-redesign.md`
- Pruebas locales:
  `docs/gestion/whatsapp_embedded_signup_local_testing.md`
- Rollout Base 84:
  `docs/gestion/whatsapp_embedded_signup_base84_rollout.md`
- Runbook staging histórico:
  `docs/gestion/whatsapp_embedded_signup_staging_runbook.md`

El runbook de staging es histórico: prevalece la decisión posterior de rollout directo allowlisted para Base 84.

## 16. Archivos de código clave

UI y navegación:

- `src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor`
- `src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor.css`
- `src/AlfaCore/Components/Layout/MainLayout.razor`

Configuración y modelos ES:

- `src/AlfaCore/Configuration/WhatsAppEmbeddedSignupOptions.cs`
- `src/AlfaCore/Configuration/AlfaCoreEsLocalOptions.cs`
- `src/AlfaCore/Models/WhatsAppEmbeddedSignupModels.cs`

Persistencia y seguridad:

- `src/AlfaCore/Services/WhatsAppEmbeddedSignupStore.cs`
- `src/AlfaCore/Services/WhatsAppAssetOwnershipStore.cs`
- `src/AlfaCore/Services/WhatsAppSecureVault.cs`
- `src/AlfaCore/Services/WhatsAppEmbeddedSignupStateProtector.cs`
- `src/AlfaCore/Services/WhatsAppRuntimeCredentialResolver.cs`
- `src/AlfaCore/Services/WhatsAppWebhookTenantGuard.cs`

Meta/pipeline:

- `src/AlfaCore/Services/MetaOAuthClient.cs`
- `src/AlfaCore/Services/MetaWhatsAppManagementClient.cs`
- `src/AlfaCore/Services/WhatsAppEmbeddedSignupOrchestrator.cs`
- `src/AlfaCore/Services/WhatsAppEmbeddedOperationalImportService.cs`
- `src/AlfaCore/Services/WhatsAppEmbeddedSignupContracts.cs`

Operación WhatsApp:

- `src/AlfaCore/Services/ConversacionesConfigService.cs`
- `src/AlfaCore/Services/ConversacionesService.cs`
- `src/AlfaCore/Models/ConversacionesConfiguracionModels.cs`

JavaScript:

- buscar `whatsappEmbeddedSignup.js` dentro de `wwwroot`;
- el módulo usa versionado/cache-busting Development;
- no volver a convertir el callback directo de `FB.login` en `async`.

Entorno local:

- `Run-AlfaCore-ES-Local.cmd`
- `tools/run-alfacore-es-local.ps1`
- `tools/AlfaCore.EsLocalTool/Program.cs`
- `Simular-Webhook-ES-Local.cmd`

Tests:

- `tests/AlfaCore.Tests/WhatsAppEmbeddedSignupFoundationTests.cs`
- `tests/AlfaCore.Tests/WhatsAppEmbeddedSignupAuthorizationTests.cs`
- `tests/AlfaCore.Tests/WhatsAppEmbeddedSignupSqlIntegrationTests.cs`
- `tests/AlfaCore.Tests/MetaWhatsAppManagementClientTests.cs`

## 17. Verificación estándar

Suite completa incluyendo SQL TEST:

```powershell
$env:ALFACORE_ES_SQL_TEST_CONNECTION='Server=(localdb)\MSSQLLocalDB;Initial Catalog=ALFA_CENTRAL_TEST;Integrated Security=True;TrustServerCertificate=True'
dotnet test tests/AlfaCore.Tests/AlfaCore.Tests.csproj -c Release --no-restore -v minimal
```

Build aislado:

```powershell
dotnet build src/AlfaCore/AlfaCore.csproj -c Release --no-restore -v minimal /p:OutputPath=C:\dev\AlfaCore\artifacts\verify-build\ /p:UseAppHost=false
```

Catálogo y diff:

```powershell
python tools/catalogo/check_catalogo.py
git diff --check
```

Últimos resultados:

```text
Tests: 77 passed / 0 failed / 0 skipped
Build Release: 0 errores
Catálogo: OK
git diff --check: OK
UI automatizada real de recuperación/reapertura: OK
```

## 18. Próximos pasos recomendados

Orden seguro:

1. Ejecutar `git pull` y confirmar que `HEAD` incluye `9f5c272` o un descendiente.
2. Leer este documento y los documentos principales de la sección 15.
3. Ejecutar el launcher ES Local y confirmar el estado actual de Base 84.
4. Probar el diálogo `Asignar usuarios` sobre `IdNumero=1` sin enviar mensajes.
5. Decidir con el responsable si se cierra primero Coexistence o se prepara el rollout productivo.
6. Para Coexistence: auditar el flujo real hasta `FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING`; nunca ejecutar `/register`.
7. Para producción: seguir exclusivamente el runbook Base 84 y pedir autorización en cada frontera destructiva/remota.
8. Después del deploy, hacer primero smoke legacy.
9. Recién luego repetir autorización productiva Base 84, configurar routing de su WABA y realizar la prueba manual `Pruebaaa 2`.
10. Solo cuando el routing correcto esté demostrado, pedir autorización para limpiar la evidencia incorrecta de Base 106.

También permanece pendiente fuera de ES:

- WhatsApp Business pairing por número/código (`PHONE_NUMBER`);
- validación remota del Phone Number ID manual;
- cierre funcional completo de Coexistence;
- rollout público y prueba real de webhook Base 84;
- revisión final de IA/automatizaciones del plan original si aún forma parte del alcance comercial.

## 19. Cosas que Alberto no debe hacer por inferencia

- No copiar usuarios, conexiones, Vault o datos desde producción a LocalDB.
- No reutilizar el Vault/key ring local en producción.
- No activar el worker ES para “ver si sigue”.
- No limpiar onboardings READY/CANCELLED/STARTED históricos automáticamente.
- No borrar ownership para destrabar una prueba.
- No crear otro WABA o teléfono Meta si el activo existente es reutilizable.
- No ejecutar `/register` para Coexistence.
- No cambiar callbacks de otras WABA/clientes.
- No usar credencial legacy si el activo tiene ownership/Vault ES.
- No borrar la evidencia Base 106 antes de demostrar el routing correcto.
- No asumir que localhost valida webhooks reales.

## 20. Prompt listo para una nueva sesión de Codex

Copiar y pegar lo siguiente:

```text
Voy a continuar el proyecto Conversaciones/WhatsApp Embedded Signup de AlfaCore que trabajó Eve.

Repositorio: C:\dev\AlfaCore
Rama: main
Commit de corte conocido: 9f5c272cd430eba43f7e585d8692e796719c083c

Antes de actuar:
1. Leé AGENTS.md y todos sus documentos obligatorios.
2. Leé completo docs/gestion/CONTINUIDAD_WHATSAPP_EMBEDDED_SIGNUP_ALBERTO_2026-08-27.md.
3. Leé los documentos que ese handoff marca como principales.
4. Verificá git status y no pises cambios ajenos.

Reglas:
- No conectarte a 10.8.0.31 / ALFA_CENTRAL.
- No publicar producción.
- No iniciar Meta, /register, webhooks ni mensajes automáticamente.
- No mostrar secretos.
- Trabajar primero en LocalDB DEV/TEST.
- Mantener WorkerEnabled=false.
- Embedded Signup solo está permitido para Base 84.

Primero informame brevemente:
- qué entendiste del estado actual;
- qué está validado;
- qué queda pendiente;
- cuál proponés como siguiente paso seguro.

No hagas cambios hasta terminar esa lectura y auditoría inicial.
```

## 21. Punto de control final

Al momento de crear este handoff:

- código y documentación previa: commit/push OK en `origin/main`;
- activo local recuperado: OK;
- `IdNumero=1`: confirmado;
- UI reabierta y consistente: OK;
- no se enviaron mensajes;
- no se ejecutó `/register` durante la recuperación;
- Meta solo recibió consultas read-only de discovery estrictamente necesarias;
- producción no fue contactada ni modificada.

