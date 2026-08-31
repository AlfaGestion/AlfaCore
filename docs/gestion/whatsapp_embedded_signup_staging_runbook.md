# Staging de WhatsApp Embedded Signup

Estado: **reemplazado por la decisión ES-3C.5**. Se conserva como referencia técnica; el rollout vigente está en [whatsapp_embedded_signup_base84_rollout.md](./whatsapp_embedded_signup_base84_rollout.md). No autoriza cambios remotos, publicación, operaciones Meta ni escrituras en producción.

## Topología objetivo

`es-dev.alfacentral.ddns.net` → IIS `AlfaCore ES DEV` → SQL Server DEV de red / `ALFA_CENTRAL_DEV`.

Producción `alfacentral.ddns.net` debe conservar sitio, bindings, carpeta, App Pool y base sin cambios. La LocalDB de `DESKTOP-9BI34IH` tampoco se expone ni se copia al servidor.

## DNS y certificado

- Crear un registro público `A` para `es-dev.alfacentral.ddns.net` hacia la misma IP pública/NAT que entrega el frontend de `SERVER-ALFACENTRAL`; si la infraestructura usa un nombre canónico estable, puede utilizarse `CNAME` hacia ese nombre en lugar de duplicar la IP.
- Validar después con `Resolve-DnsName es-dev.alfacentral.ddns.net` desde una red externa y confirmar que HTTPS llega al servidor previsto.
- Emitir un certificado Let's Encrypt independiente cuyo SAN incluya exactamente `es-dev.alfacentral.ddns.net`.
- No reutilizar el certificado de `alfacentral.ddns.net`: no cubre el subdominio staging.

DNS, NAT y emisión del certificado requieren intervención del administrador de infraestructura.

## IIS y App Pool

- Sitio: `AlfaCore ES DEV`.
- Carpeta física independiente, por ejemplo `C:\inetpub\AlfaCore-ES-DEV`; nunca la carpeta productiva.
- App Pool independiente, `AlfaCore-ES-DEV`, `No Managed Code`, pipeline integrado, identidad conocida y dedicada.
- Binding exclusivo `https`, host `es-dev.alfacentral.ddns.net`, SNI habilitado y certificado staging.
- Logs IIS y logs de aplicación separados de producción.
- La identidad del pool necesita lectura/ejecución sobre el publish, escritura solo en carpetas operativas necesarias y lectura/escritura sobre el key ring. No otorgar permisos generales.

## SQL DEV

Crear un catálogo vacío `ALFA_CENTRAL_DEV` en un SQL Server DEV accesible desde `SERVER-ALFACENTRAL`. No restaurar ni consultar `ALFA_CENTRAL`.

Desde `docs/base-datos/sql-test`, ejecutar con modo sqlcmd:

```powershell
sqlcmd -S <SERVIDOR_DEV> -d ALFA_CENTRAL_DEV -E -b -i bootstrap_alfa_central_dev_embedded_signup.sql
```

Si se usa autenticación SQL, suministrarla fuera del repositorio. El bootstrap aborta salvo que `DB_NAME()` sea exactamente `ALFA_CENTRAL_DEV`, crea el contrato mínimo compatible de `dbo.bases`, agrega `84 / ES_DEV_BASE_84` y aplica el esquema ES oficial mediante `:r`. No es una migración automática.

El contrato mínimo de `dbo.bases` incluye `id`, `idcliente`, `nombre`, `dbserver`, `dbname`, `dbuser`, `dbpassword` y `WebhookToken`, porque `CentralBasesService` los consume para identidad/routing. El seed completa únicamente `id` y `nombre`: deliberadamente no copia conexiones ni identidad productiva. Antes de validar Base 84, infraestructura debe proveer un tenant DEV/operativo autorizado y cargar esos valores por un mecanismo seguro y acotado. Sin ese dato, el health de identidad Base 84 queda bloqueado; no debe usarse producción como fallback.

## Configuración staging no versionada

Configurar mediante variables de entorno del App Pool o un archivo local excluido de Git. Nunca guardar App Secret ni connection strings reales en el repositorio.

```text
ASPNETCORE_ENVIRONMENT=Staging
WhatsAppEmbeddedSignup__Enabled=true
WhatsAppEmbeddedSignup__WorkerEnabled=false
WhatsAppEmbeddedSignup__AppId=1436083307772786
WhatsAppEmbeddedSignup__EmbeddedSignupConfigId=1753413148641744
WhatsAppEmbeddedSignup__GraphApiVersion=v26.0
WhatsAppEmbeddedSignup__BusinessPortfolioId=792034091131840
WhatsAppEmbeddedSignup__SystemUserId=61574820802043
WhatsAppEmbeddedSignup__CentralConnectionString=<SQL DEV / ALFA_CENTRAL_DEV>
WhatsAppEmbeddedSignup__DataProtectionKeysPath=<RUTA ABSOLUTA KEY RING STAGING>
WhatsAppEmbeddedSignup__CallbackBaseUrl=https://es-dev.alfacentral.ddns.net
WhatsAppEmbeddedSignup__AppSecret=<SECRET STORE DEL SERVIDOR>
```

`ConnectionStrings__AlfaCentral` también debe apuntar al central DEV si staging utilizará esta tabla para login, selección y routing. El resto de las conexiones necesarias deben apuntar a destinos DEV autorizados por infraestructura; no se copian desde producción en este runbook y no existe fallback permitido hacia `ALFA_CENTRAL`.

## Data Protection y Vault

- Crear un key ring vacío y persistente exclusivo de staging.
- Mantener `ApplicationName = AlfaCore.WhatsAppEmbeddedSignup` (lo fija `Program.cs`).
- Otorgar lectura/escritura a la identidad dedicada del App Pool y administración a Administradores; no copiar el key ring de Eve.
- No migrar ciphertext de LocalDB. Repetir Embedded Signup en staging para crear una credencial protegida con el key ring staging.
- Antes de Meta, guardar un secreto ficticio mediante el Vault, recuperarlo, reciclar el App Pool y volver a recuperarlo. No imprimir secreto ni material criptográfico.

## Publish reproducible

Ejecutar localmente:

```powershell
powershell -ExecutionPolicy Bypass -File tools/publish-es-staging.ps1
```

El resultado queda en `artifacts/es-staging-publish`, nunca se copia automáticamente a un servidor. Incluye:

- `build-version.txt`: commit, dirty state, fecha UTC y versión del módulo ES;
- `build-inventory.sha256`: inventario SHA-256 de todos los archivos publicados.

Para un artefacto candidato a despliegue, el árbol debe estar limpio o la excepción debe quedar aprobada y documentada. Comparar el inventario después de copiar al staging.

## Política de compatibilidad y rollout

La estrategia autorizada es DB-before-code:

1. crear/verificar las cuatro tablas ES;
2. validar la conexión y el key ring;
3. desplegar código;
4. habilitar ES solo en staging.

El código consulta explícitamente la capacidad del esquema. Con ES deshabilitado y esquema ausente conserva el mecanismo legacy. Con ES habilitado y esquema ausente detiene la operación con error controlado. Un número con ownership siempre exige Vault; la ausencia de credencial nunca habilita fallback legacy.

## Health checklist previo a Meta

- [ ] `GET /` devuelve 200.
- [ ] `/manifest.webmanifest` devuelve 200 y `application/manifest+json`.
- [ ] Login funciona.
- [ ] `/ALFANET/84/...` adopta Base 84 y la identidad queda autorizada.
- [ ] `DB_NAME()` devuelve `ALFA_CENTRAL_DEV`.
- [ ] Las cuatro tablas ES y el seed 84 están presentes.
- [ ] Vault ficticio completa round-trip sin plaintext en SQL/logs.
- [ ] El mismo secreto ficticio se recupera después de reciclar el App Pool.
- [ ] `WorkerEnabled=false` efectivo.
- [ ] Hashes del publish coinciden con `build-inventory.sha256`.

## Checklist Meta posterior (requiere autorización humana)

- [ ] Repetir Embedded Signup desde staging para Base 84; no reutilizar onboarding/token local.
- [ ] Autorizar por separado discovery, ownership, register/readiness e import operativo.
- [ ] Generar WebhookToken y Verify Token de Base 84 solo tras health aprobado.
- [ ] Verificar challenge público del callback staging tokenizado.
- [ ] Configurar y releer `subscribed_apps`/override de WABA `1547539197385596` hacia staging.
- [ ] Detenerse cuando Meta confirme el override. La prueba `Pruebaaa 2` la realiza manualmente el usuario.

No borrar ni modificar la evidencia de Base 106: IdNumero 23, conversación 10376 y mensajes 50745/50746.

## Rollback

1. Quitar el override WABA o restaurar explícitamente el callback anterior confirmado.
2. Detener sitio y App Pool staging.
3. Conservar logs, inventario y key ring para auditoría.
4. Retirar binding/certificado/DNS únicamente con aprobación de infraestructura.
5. No tocar sitio, binarios, bindings ni base productivos.

## Intervenciones humanas necesarias

- Proveer SQL Server DEV de red y credencial limitada.
- Crear DNS/NAT, certificado, sitio, App Pool, ACL y secret store.
- Autorizar/cargar App Secret en el servidor.
- Validar login y Base 84.
- Ejecutar Embedded Signup y autorizar cada mutación Meta posterior.
- Ejecutar manualmente el mensaje final de prueba.
