# WhatsApp Embedded Signup — Fundación ES-1/ES-1.5

El rollout vigente, limitado por allowlist a Base 84, se documenta en [whatsapp_embedded_signup_base84_rollout.md](../../gestion/whatsapp_embedded_signup_base84_rollout.md). El runbook de staging separado quedó reemplazado por esta decisión.

## Alcance

ES-1 y ES-1.5 incorporan configuración tipada, state machine, persistencia central versionada, stores Dapper, claiming, vault persistente y logging seguro. No habilitan UI, OAuth/callback, Graph API, intercambio de códigos, registro de teléfonos ni cambios en webhooks.

`WhatsAppEmbeddedSignup:Enabled` permanece en `false`.

## Decisiones confirmadas

- AlfaNet opera como Technology Provider.
- `CreditMode = CustomerPaysMeta`.
- No existe Credit Sharing ni billing Meta en AlfaCore.
- Una base soporta N Business, N WABA y N Phone Number ID.
- Los números operativos continúan exclusivamente en `CONV_WHATSAPP_NUMEROS`.
- El ownership central es seguridad/routing SaaS, no un modelo paralelo.

### Modos de onboarding

El onboarding persiste explícitamente `STANDARD` o `BUSINESS_APP_COEXISTENCE` en
`WhatsAppEmbeddedOnboarding.ModoOnboarding`; el modo nunca se infiere por heurísticas.

- `STANDARD`: podrá avanzar a descubrimiento, registro e importación cuando ES-3 implemente esas operaciones.
- `BUSINESS_APP_COEXISTENCE`: conserva WhatsApp Business en el teléfono, usa
  `featureType=whatsapp_business_app_onboarding` y tiene prohibido pasar por registro del teléfono.

Ambos modos convergen en `CONV_WHATSAPP_NUMEROS`, ownership central y vault. Los eventos futuros
`history`, `smb_app_state_sync` y `smb_message_echoes` deberán deduplicarse por base, número e
identificador externo. En esta etapa no se procesa ni se afirma una importación real de historial.

## Configuración y secretos

App ID, Business Portfolio ID, System User ID y Configuration ID son configuración de entorno, no constantes de dominio. El WABA y Phone Number ID de validación son fixtures, nunca defaults productivos.

No existen propiedades versionadas para App Secret, Access Token, Business Token, System User Token ni PIN. `IWhatsAppCredentialVault` e `IWhatsAppPhonePinVault` guardan referencias opacas y ciphertext; las entidades no persisten el secreto plano.

`DataProtectionKeysPath` define el directorio externo y absoluto del key ring. Al habilitar la capability, su ausencia impide iniciar la aplicación. En Windows las claves se protegen además con DPAPI y usan el nombre de aplicación `AlfaCore.WhatsAppEmbeddedSignup`. El directorio debe estar fuera del repositorio, con ACL exclusiva de la identidad de AlfaCore, backup y restauración documentada. Con `Enabled=false` puede permanecer vacío; el vault falla de forma cerrada si se intenta usar sin esa configuración.

## Persistencia central

Script manual:

`docs/base-datos/sql-referencia/2026-08-25-001__alfa_central_whatsapp_embedded_signup.sql`

Destino exclusivo: `ALFA_CENTRAL`. No pertenece a `App_Data/updates`, porque ese mecanismo actualiza bases cliente.

El script no se ejecutó en ES-1/ES-1.5. La conexión local identificada apunta a servidor `10.8.0.31`, catálogo `ALFA_CENTRAL`, pero no contiene una marca que demuestre inequívocamente que sea un ambiente aislado de desarrollo/test. Por seguridad no se abrió conexión ni se ejecutó DDL/DML contra ese destino.

Tablas:

- `WhatsAppEmbeddedOnboarding`: estado durable, hash de state, consumo único, expiración, retry y lease.
- `WhatsAppWabaOwnership`: ownership global de WABA por base.
- `WhatsAppPhoneOwnership`: ownership global de Phone Number ID, WABA y base.
- `WhatsAppSecureVault`: referencia opaca, contexto, ciphertext, expiración y revocación; sin columnas de secretos planos.

La unicidad se garantiza en SQL. Los stores de ownership usan transacciones `SERIALIZABLE` y `UPDLOCK/HOLDLOCK`; el claiming usa `UPDLOCK`, `READPAST`, `ROWLOCK` y lease. Un conflicto cross-tenant nunca adopta ni mueve el activo. Archivar el número operativo no libera ownership.

La reaplicación sobre un esquema completo es idempotente: tablas e índices se crean solo si no existen. El script no repara automáticamente un esquema parcial o divergente; ese caso exige auditoría y corrección controlada. El rollback manual debe respetar dependencias: vault y phones, WABA y finalmente onboarding, y nunca ejecutarse sobre datos productivos sin una migración aprobada.

## State, vault y logging

El state usa 256 bits aleatorios y solo persiste su SHA-256 hexadecimal. Se liga a onboarding, base y usuario, expira y se consume atómicamente una vez.

El vault separa propósitos de credenciales y PIN, acepta contexto mínimo, permite expiración/revocación y no genera PIN. Para registro telefónico solo acepta seis dígitos ASCII. El roundtrip requiere conservar el mismo key ring y application name.

No deben registrarse state completo, authorization code, token, App Secret o PIN. `WhatsAppEmbeddedSignupErrorLogger` acepta únicamente IdOnboarding, IdBase, paso, IDs Meta no secretos, ErrorCode y RetryCount; normaliza esos valores, genera IncidentId y escribe mediante `IAuxErrRepository`. No acepta excepciones, mensajes crudos ni secretos. El consumidor futuro deberá activar el contexto de la base correspondiente antes de escribir `AUX_ERR`.

## Estados y procesamiento

Flujo preparado: `Started → Authorized → DiscoveringAssets → ValidatingOwnership → ConfiguringAccess → SubscribingWabas → CheckingCustomerPayment → DiscoveringPhones → RegisteringPhones/Importing → Ready`.

`ActionRequired` representa acción humana y no error técnico. Si falta configuración de pago del cliente: `CUSTOMER_PAYMENT_SETUP_REQUIRED`. No existe ejecución de Credit Sharing.

El hosted service permanece inactivo mientras `Enabled=false`. No se incorporaron Hangfire ni Quartz. Las implementaciones Meta HTTP siguen ausentes y no simulan éxito.

## Validación ES-1.5

Las pruebas SQL son opt-in mediante `ALFACORE_ES_SQL_TEST_CONNECTION` y rechazan catálogos cuyo nombre no contenga `TEST`, `DEV` o `LOCAL`; nunca usan `appsettings.json` como fallback. La base aislada debe tener aplicado el script y al menos dos bases fixture.

La suite integrada valida:

- ownership idempotente, conflicto cross-tenant y concurrencia entre dos bases;
- state ligado a base/usuario, expiración y consumo único;
- dos consumidores, lease recovery, `NextAttemptUtc` y exclusión de estados terminales;
- roundtrip de credencial y PIN tras recrear provider/servicio, verificando que SQL no contenga el valor plano.

En ES-1.5 estas pruebas quedan explícitamente omitidas porque no se proporcionó una conexión certificada como aislada. No existe aún evidencia empírica SQL que pueda presentarse como aprobada.

## Pendiente posterior

1. Aplicar el script central de forma controlada en una `ALFA_CENTRAL` aislada.
2. Ejecutar y conservar evidencia de las pruebas SQL opt-in.
3. Implementar OAuth/callback y validación state real.
4. Implementar clientes Graph, discovery y procesamiento por pasos.
5. Implementar UI y polling de progreso bajo aprobación expresa.

## Bootstrap aislado para ES-1.6

El bootstrap manual de test está en:

`docs/base-datos/sql-test/bootstrap_alfa_central_test_embedded_signup.sql`

No crea la base ni se ejecuta automáticamente. Procedimiento autorizado:

1. En un SQL Server DEV/LOCAL expresamente indicado, crear manualmente una base vacía llamada `ALFA_CENTRAL_TEST`.
2. Seleccionar `ALFA_CENTRAL_TEST` como catálogo actual.
3. Ejecutar el bootstrap completo. Sus guards abortan en `ALFA_CENTRAL`, en catálogos sin `TEST/DEV/LOCAL` y en cualquier nombre distinto de `ALFA_CENTRAL_TEST`.
4. Configurar `ALFACORE_ES_SQL_TEST_CONNECTION` solamente en la terminal que ejecutará las pruebas; no modificar las conexiones productivas de AlfaCore.
5. Ejecutar `dotnet test tests/AlfaCore.Tests/AlfaCore.Tests.csproj -c Release`.

La tabla mínima `dbo.bases` contiene `id int` como PK, única columna requerida por stores/FK/tests, y `nombre nvarchar(100)` para identificar de forma inequívoca los fixtures `ES_TEST_TENANT_A` y `ES_TEST_TENANT_B`. Sus IDs reservados son `1900000001` y `1900000002`. No contiene servidores, bases cliente, usuarios, contraseñas ni datos reales.

Los tests de ownership generan WABA y Phone Number IDs numéricos artificiales con prefijo `9999`, validan concurrencia y eliminan solo esos IDs. Los tests de onboarding eliminan solamente sus propios GUID con `IdCliente='TEST'`. Los assets Meta reales de referencia no se usan como fixtures destructivos.

## Pipeline de gestión posterior a AUTHORIZED

El cliente `MetaWhatsAppManagementClient` usa un `HttpClient` dedicado y obtiene la credencial exclusivamente mediante la referencia opaca del onboarding y `WhatsAppSecureVault`. No reutiliza el token ni el WABA de la configuración manual de Conversaciones. La versión Graph proviene de `WhatsAppEmbeddedSignupOptions.GraphApiVersion`; requests, responses y secretos no se registran.

El procesamiento durable posterior a la autorización ejecuta, por estado: discovery autorizado de Business/WABA/teléfonos, reserva global de ownership, comprobación de acceso, suscripción idempotente de la aplicación a la WABA, evaluación de pago y readiness del teléfono. Los hints del evento de Meta se usan solo para correlación; los activos operables deben confirmarse mediante Graph.

La política de facturación permanece `CustomerPaysMeta`. Un estado de pago desconocido no se transforma por heurística en error ni en acción requerida. Solo una respuesta inequívoca de Meta puede producir `CUSTOMER_PAYMENT_SETUP_REQUIRED`; AlfaCore nunca comparte ni adjunta una línea de crédito.

El alta supervisada se detiene antes de `SaveWhatsAppNumeroAsync`. `READY_FOR_IMPORT_APPROVAL` significa que el activo está listo para que una aprobación posterior autorice el UPSERT operativo. `REGISTRATION_REQUIRED` significa que un onboarding `STANDARD` descubrió un teléfono que todavía requiere registro; esta etapa no ejecuta `/register`. El modo `BUSINESS_APP_COEXISTENCE` nunca puede ingresar al registro telefónico.

Para diagnóstico local existe `tools/AlfaCore.EsSupervisedRunner`. El runner exige confirmación explícita, valida `(localdb)\\MSSQLLocalDB / ALFA_CENTRAL_DEV`, `IsLocalDB=1`, Base 84, onboarding `STANDARD` y `WorkerEnabled=false`. No realiza UPSERT, no registra teléfonos y no habilita el hosted worker.

### Resultado supervisado Base 84

El onboarding `5bad6682-238b-4230-888f-c7b112fa9edd` avanzó hasta `RegisteringPhones / REGISTRATION_REQUIRED`. Graph confirmó WABA `1547539197385596` y Phone Number ID `1195619520311268`, con nombre visible `AlfaNet Tester`, teléfono `+1 555-482-7373`, calidad `UNKNOWN` y registro pendiente. El ownership de ambos activos quedó reservado para Base 84 y la suscripción de la aplicación a la WABA fue asegurada. No se invocó `SaveWhatsAppNumeroAsync` ni se escribió en `CONV_WHATSAPP_NUMEROS`.

## ES-2 — autorización mínima con Meta

ES-2 incorpora únicamente `Start → FB.login → code → /oauth/access_token → vault → Authorized`. No descubre Business/WABA/teléfonos, no reserva ownership, no registra números, no suscribe webhooks y no activa el worker posterior.

El launcher está encapsulado en `wwwroot/js/whatsappEmbeddedSignup.js`. Carga el SDK oficial desde `https://connect.facebook.net/es_LA/sdk.js`, usa `config_id`, `response_type='code'`, `override_default_response_type=true` y `extras.sessionInfoVersion='3'`. Cada ejecución conserva code/session dentro de su closure, espera ambos eventos sin asumir orden y evita doble submit. Solo acepta mensajes desde `https://www.facebook.com` y `https://web.facebook.com`.

La finalización vuelve al backend mediante JS interop del circuito Blazor autenticado. Se exige permiso de administración de Conversaciones antes de iniciar y completar. El backend valida IdOnboarding, IdBase activo, usuario, hash de state, expiración y consumo único. No existe endpoint público, redirect URI ni querystring de AlfaCore con code; la protección CSRF combina el circuito interactivo con el state aleatorio ligado a base/usuario.

`MetaOAuthClient` utiliza `https://graph.facebook.com/{GraphApiVersion}/oauth/access_token`, confirmado en el sample oficial de Meta, y entrega la respuesta directamente a `IWhatsAppCredentialVault`. Onboarding conserva solo la referencia opaca. El cliente HTTP nombrado no registra requests para evitar exposición de query parameters.

Configuración Development no versionada requerida:

- `WhatsAppEmbeddedSignup__AppSecret`: secreto privado de la Meta App.
- `WhatsAppEmbeddedSignup__Enabled=true`.
- `WhatsAppEmbeddedSignup__DataProtectionKeysPath`: directorio absoluto y persistente con ACL privada.
- `WhatsAppEmbeddedSignup__CentralConnectionString`: conexión dedicada al catálogo central aislado del runtime. Para la prueba manual supervisada en Development apunta a `(localdb)\MSSQLLocalDB / ALFA_CENTRAL_DEV`; evita reemplazar la conexión central normal de AlfaCore.

La prueba manual supervisada vigente usa la Base AlfaCore `84` (`ALFANET EN VB6`). En `ALFA_CENTRAL_DEV`, esa identidad se representa únicamente mediante el seed ficticio `84 / ES_DEV_BASE_84`; no se copian nombre, conexión, usuarios ni datos productivos. El seed reproducible está en `docs/base-datos/sql-test/seed_es_supervisado_base_84.sql`. La Base `106` queda solamente como antecedente histórico DEV y no existe fallback entre ambas.

Los integration tests permanecen separados: leen exclusivamente `ALFACORE_ES_SQL_TEST_CONNECTION`, cuyo catálogo debe ser `ALFA_CENTRAL_TEST`, y usan los fixtures artificiales reservados `1900000001` y `1900000002`.

La configuración versionada conserva `Enabled=false` y no contiene App Secret. La prueba manual requiere HTTPS, usuario con rol en la Meta App y el dominio agregado a Allowed Domains for JavaScript SDK.
