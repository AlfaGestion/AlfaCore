# Plantillas WhatsApp: traslado aislado desde main

## Inventario previo

- Checkout original: `C:/dev/AlfaCore`, rama `hotfix/webhook-outcome-diag-58b04f6`.
- HEAD original: `ad6de8b79c1cb9257d1cf5a15da29079ac53ef10`.
- Main local actual: `84a892d634142c909a812080af3838f11915246f`.
- Merge-base: `58b04f62a899ca569a5f2a9d499660fc771433cd`.
- Working tree original: 9 archivos modificados, 3 nuevos, ninguno staged.
- Respaldo externo: `C:/dev/AlfaCore-template-transfer-backup-20260918-105852`.
  Contiene diff binario completo, diff hotfix/main, copias de los 12 archivos,
  manifiesto SHA-256 y parche funcional separado.
- Worktree nuevo: `C:/dev/AlfaCore-whatsapp-template-hardening`.
- Rama nueva: `fix/whatsapp-template-hardening`, creada exactamente desde main.

## Cambios funcionales trasladados

| Archivo | Parte trasladada y motivo |
|---|---|
| `src/AlfaCore/Services/ConversacionesService.cs` | Validación WABA/número; resolución PhoneNumberId; estado actual previo al envío; sync paginado con control de recurso/ciclos; BODY/componentes remotos; posiciones de parámetros; protección de borradores presentados; ID requerido al crear; errores accionables. |
| `src/AlfaCore/Services/WhatsAppTemplateValidation.cs` | Validador y explicación de errores; impide envío incompleto de componentes no soportados. |
| `src/AlfaCore/Services/MetaWhatsAppManagementClient.cs` | Conserva components y restringe paginación al recurso solicitado, detectando ciclos. |
| `src/AlfaCore/Services/WhatsAppEmbeddedSignupContracts.cs` | Metadata opcional ComponentsJson. |
| `src/AlfaCore/Models/ConversacionesModels.cs` | ComponentesMetaJson en DTO. |
| `src/AlfaCore/Components/Pages/Conversaciones.razor` | Idioma/categoría visibles, aviso de componentes no soportados y cantidad exacta de valores. |
| `src/AlfaCore/Components/Pages/ConversacionesPlantillas.razor` | Última sincronización visible. |
| `tests/AlfaCore.Tests/WhatsAppTemplateTests.cs` | Tests funcionales anteriores y nuevo caso explícito de mismatch. |
| `tests/AlfaCore.Tests/MetaWhatsAppManagementClientTests.cs` | Componentes/paginación y aislamiento de recurso. |
| `tests/AlfaCore.Tests/WhatsAppTenantIsolationTests.cs` | Solo el test TemplateCredentialFromBaseBIsUnavailableToBaseA; mantiene intactas las pruebas de webhook propias de main. |

## Exclusiones deliberadas

- Commits exclusivos de hotfix `860c4f3` y `ad6de8b`: no son ancestros de la rama nueva.
- `Program.cs` y `AppExceptionLoggingMiddleware.cs`: idénticos a main. Se conserva
  el logging de main, sin importar el diagnóstico inline de la hotfix.
- `WhatsAppEmbeddedSignupSqlIntegrationTests.cs`: idéntico a main; no se trasladan
  cambios de configuración, fixtures ni guardias de la tarea anterior.
- Informe `conversaciones_plantillas_auditoria_2026-09-18.md`: permanece respaldado
  y sin cambios en el checkout original. Describe resultados y un incidente SQL
  históricos de aquella tarea; no se importa como si correspondiera a esta rama.
- 125 líneas de corrección pura de mojibake de `ConversacionesService.cs`:
  excluidas todas. Se preservaron los literales preexistentes de main por pedido
  explícito del usuario. Ninguna corrección de encoding era inseparable.
  Los archivos modificados/nuevos se guardan en UTF-8 sin BOM.

## Cobertura nueva y límite solicitado

`TemplateConversationPhoneMismatchRejectsStalePhoneWithoutGlobalFallback` ejecuta
por reflexión el método real `ResolveTemplateConversationPhoneAsync`: conversación
7 con `phone-STALE`, registro asociado con `phone-A`, global `phone-GLOBAL`.
Comprueba InvalidOperationException y texto de mismatch, consulta al número 7,
configuración global y conversación sin alteraciones, y cero requests HTTP.
No duplica la lógica de resolución. Es una prueba del método, no una ejecución
completa del envío público con SQL.

No se agregó el test end-to-end de gate no APPROVED: `SendTemplateMessageAsync`
requiere `RequireConversationAsync`/`GetTemplateAsync` y actualiza estado mediante
SqlConnection/SqlCommand concretos antes del gate. La clase es sealed y no expone
un repositorio inyectable para reemplazarlos. Implementar ese test sin SQL mutante
requeriría cambiar arquitectura o extraer lógica productiva. Se respeta el límite
del pedido; se conserva la cobertura de sincronización de PENDING, REJECTED,
PAUSED, DISABLED e IN_APPEAL sin presentarla como cobertura end-to-end.

## Comparación funcional y seguridad

Se comprobó igualdad del servicio previo y el trasladado normalizando únicamente
los 125 cambios de mojibake. Los otros siete archivos funcionales compartidos son
idénticos al trabajo original; el test de tenant se aplicó como hunk independiente
sobre main. La única ampliación de cobertura es el mismatch.
No se perdió lógica de WABA, PhoneNumberId, credenciales, idioma, parámetros,
paginación, estados, componentes ni errores.

El checkout original se verificó contra sus hashes SHA-256 y HEAD iniciales.
No hubo merge, rebase, reset, clean ni descarte de cambios. No se ejecutaron tests
SQL, SQL mutante, llamadas reales a Meta ni deploy; no se tocaron Base84/Base106,
tokens, callbacks, leases o READPAST.

## Preparación y siguiente paso

El primer build sin restore falló con NETSDK1004 por ausencia de assets en el
worktree recién creado. Se ejecutó `dotnet restore AlfaCore.sln` para generarlos,
sin cambiar dependencias, y luego se repitió el build solicitado.

Resultados en el worktree nuevo:

| Verificación | Aprobados | Fallidos | Omitidos |
|---|---:|---:|---:|
| WhatsAppTemplateTests (incluye mismatch) | 30 | 0 | 0 |
| WhatsApp/Conversacion excluyendo WhatsAppEmbeddedSignupSqlIntegrationTests | 155 | 0 | 0 |

Build `dotnet build AlfaCore.sln --no-restore`: 0 errores, 3 warnings CS1998
preexistentes (CostosLoteDetalle:452, AuditoriaUsuarios:1110 y :1344).
Catálogo: 74 rutinas, 0 advertencias, 0 errores. `git diff --check`: correcto.
La suite amplia incluye los tests de aislamiento tenant; sus 155 casos incluyen
los 30 focalizados, no son cantidades aditivas. Los tests SQL quedaron excluidos
por filtro, no contabilizados como omitidos por el runner.

Desde el código, el alcance trasladado queda orientado a una prueba controlada
de plantilla BODY aprobada. Esa prueba real no forma parte de esta tarea y no se
ejecutó. Siguen fuera de alcance reconciliación general, múltiples integraciones
Legacy por base y soporte de HEADER variable/media/buttons.
