# Rollout productivo supervisado — Embedded Signup Base 84

Estado: plan preparado; no autoriza ejecutar SQL, publicar binarios ni modificar Meta.

## Alcance

Embedded Signup se habilita globalmente con una allowlist explícita que contiene únicamente Base 84. Para cualquier otra base la UI no muestra `Conectar WhatsApp` y el backend rechaza creación, callback, pipeline e importación antes de tocar onboarding, Vault u ownership. La mensajería sin ownership conserva el resolver legacy.

Configuración productiva no versionada:

```text
WhatsAppEmbeddedSignup__Enabled=true
WhatsAppEmbeddedSignup__WorkerEnabled=false
WhatsAppEmbeddedSignup__AllowedBaseIds__0=84
```

App ID, Config ID, Graph version, Business Portfolio ID, System User ID, conexión central y ruta del key ring se cargan por el mismo mecanismo seguro. App Secret nunca se versiona.

## Secuencia DB-before-code

1. Detener el inicio del deploy y obtener un backup verificable de `ALFA_CENTRAL`, con nombre/fecha y prueba de que puede restaurarse.
2. Confirmar `SELECT DB_NAME()` = `ALFA_CENTRAL` y que `dbo.bases` existe.
3. Ejecutar exclusivamente [2026-08-25-001__alfa_central_whatsapp_embedded_signup.sql](../base-datos/sql-referencia/2026-08-25-001__alfa_central_whatsapp_embedded_signup.sql).
4. Verificar las cuatro tablas, PK, FK, checks e índices. El script no inserta clientes ni modifica filas de `dbo.bases`.
5. Si la verificación falla, no publicar código y restaurar el backup si el DBA determina que hubo una modificación parcial no recuperable mediante rollback controlado.

## Data Protection

Ubicación propuesta: `C:\ProgramData\AlfaCore\DataProtectionKeys\WhatsAppEmbeddedSignup` en `SERVER-ALFACENTRAL`, fuera de la carpeta publicada.

- Identidad real del App Pool: lectura/escritura/modificación.
- Administradores: control administrativo.
- Sin acceso para grupos generales.
- `ApplicationName = AlfaCore.WhatsAppEmbeddedSignup`.
- Claves protegidas con DPAPI del servidor y persistentes entre recycle/deploy.
- No copiar claves, ciphertext, token ni PIN desde `DESKTOP-9BI34IH`.

Antes de Meta, realizar un round-trip con secreto ficticio, reciclar el App Pool y comprobar nuevamente su lectura sin imprimir secreto ni claves.

## Publicación

Generar el artefacto con `tools/publish-es-staging.ps1` (el nombre histórico del script no implica un destino remoto). Desplegar el contenido inventariado sobre la carpeta productiva mediante el procedimiento vigente y conservar el paquete/binarios anteriores para rollback.

La configuración no versionada debe contener la allowlist anterior, `WorkerEnabled=false`, la conexión a `ALFA_CENTRAL` y el key ring productivo. No regenerar callbacks ni ejecutar onboarding durante el arranque.

## Smoke legacy obligatorio

Antes de Base 84 elegir un número legacy existente, sin ownership, y comprobar sin cambiar su configuración:

- inbound por su webhook actual;
- conversación y mensaje persistidos en su base correcta;
- respuesta outbound legacy;
- estado del mensaje/webhook.

Si falla cualquier punto: detener pruebas, restaurar binarios/configuración anterior y no abrir Embedded Signup. El schema ES puede permanecer vacío si fue aplicado correctamente; restaurar DB solo si el DBA lo considera necesario.

## Base 84 y Meta (manual y posterior)

1. Entrar manualmente a Base 84 y confirmar que aparece `Conectar WhatsApp`; verificar que no aparece en otra base.
2. Repetir Embedded Signup desde producción. No reutilizar onboarding/Vault local.
3. Si Meta devuelve WABA `1547539197385596` y Phone Number ID `1195619520311268`, reutilizarlos idempotentemente.
4. Asegurar en Base 84 PublicBaseUrl, WebhookPath, VerifyToken y WebhookToken. El callback debe ser `https://alfacentral.ddns.net/api/conversaciones/whatsapp/webhook/{WebhookTokenBase84}`.
5. Modificar únicamente la WABA incorporada por Base 84 y releer `subscribed_apps` hasta confirmar app + `override_callback_uri` de Base 84.
6. Detenerse. El usuario envía manualmente `Pruebaaa 2`.
7. Confirmar ingreso en Base 84/IdNumero AlfaNet Tester y ausencia de datos nuevos en Base 106.

No modificar aún la evidencia Base 106: IdNumero 23, conversación 10376 y mensajes 50745/50746.

## Rollback

- Binarios: restaurar carpeta/paquete anterior y configuración previa; reciclar App Pool.
- Meta: si ya se hubiera aplicado override, restaurar explícitamente el callback anterior confirmado antes del rollout.
- DB: el primer recurso es detener el código nuevo; las tablas ES vacías son compatibles. Restaurar el backup solo con decisión del DBA, especialmente si ya contienen una nueva credencial productiva.
- Conservar logs, inventarios y evidencia para auditoría.

## Intervención humana requerida

- DBA: backup, ejecución/verificación del schema y eventual restore.
- Administrador IIS: identidad del App Pool, key ring/ACL, configuración secreta, deploy y recycle.
- Operador funcional: smoke legacy y selección de Base 84.
- Usuario autorizado Meta: Embedded Signup y confirmación de activos.
- Usuario: envío manual de `Pruebaaa 2`.
