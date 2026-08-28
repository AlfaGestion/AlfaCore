# Pruebas locales de WhatsApp Embedded Signup

## Primera ejecución

Requisitos instalados:

- SDK .NET correspondiente al `TargetFramework` del proyecto;
- SQL Server Express LocalDB con instancia `MSSQLLocalDB`;
- Windows con soporte para el certificado HTTPS de desarrollo de .NET.

No se instala software automáticamente.

El único secreto necesario se guarda con .NET user-secrets, nunca en Git. Desde la raíz del repositorio, ejecutar una sola vez:

```powershell
dotnet user-secrets set "WhatsAppEmbeddedSignup:AppSecret" "<APP_SECRET>" --project src/AlfaCore/AlfaCore.csproj
```

Para quitarlo:

```powershell
dotnet user-secrets remove "WhatsAppEmbeddedSignup:AppSecret" --project src/AlfaCore/AlfaCore.csproj
```

No pegar App Secret, tokens, PIN ni contenido del Vault en tickets, commits o logs.

## Uso habitual

1. Ejecutar `git pull`.
2. Hacer doble clic en `Run-AlfaCore-ES-Local.cmd`.
3. Iniciar sesión con las credenciales DEV que muestra el launcher (`eslocal@alfacore.dev` / `AlfaCore-ES-84!`).
4. Ingresar a Base 84.
5. Abrir `Configuración → WhatsApp API`.

El launcher valida .NET, LocalDB, scripts, certificado y puerto 7055; prepara idempotentemente `ALFA_CENTRAL_DEV`; crea `ALFACORE_ES_TENANT_DEV` vacío para impedir fallback remoto; verifica schema ES y Base 84; habilita ES solo para Base 84; mantiene el worker apagado; usa un key ring propio en `%LOCALAPPDATA%`; sobrescribe dentro del proceso las conexiones central/tenant con LocalDB e inicia `https://localhost:7055`.

La primera navegación requiere el login DEV mostrado por el launcher. Nunca se consulta `ALFA_CENTRAL` para completarlo.

El bootstrap crea idempotentemente una identidad exclusivamente local en `ALFA_CENTRAL_DEV` y su usuario interno administrador en `ALFACORE_ES_TENANT_DEV`. Ambas operaciones exigen Development, `AlfaCoreEsLocal:Enabled=true`, LocalDB, Base permitida 84 y worker ES apagado. No existe bypass de autenticación ni se reutilizan usuarios o contraseñas productivas.

Cada desarrollador realiza su propia autorización:

`Conectar WhatsApp → autorización Meta → Vault local del desarrollador`.

No se copian Vault, tokens ni claves Data Protection entre equipos.

## Simular webhook

Con AlfaCore local iniciado, hacer doble clic en `Simular-Webhook-ES-Local.cmd`.

Opcionalmente:

```powershell
Simular-Webhook-ES-Local.cmd -Fixture status-delivered
Simular-Webhook-ES-Local.cmd -PhoneNumberId 900000000000099
```

El simulador usa fixtures ficticias y comprueba callback Base 84 + ownership Base 84, además de un callback artificial `1900000106` + ownership Base 84 que debe ser bloqueado. Ese identificador no representa ni consulta Base 106 real.

## Límites de localhost

Meta **no puede enviar webhooks reales a localhost**. El simulador valida el pipeline HTTP y el guard local, pero no reemplaza la prueba pública posterior al deploy.

Embedded Signup puede abrir Meta desde `https://localhost:7055` si Meta admite ese origen. El launcher nunca inicia Meta automáticamente.

## Datos locales

- Central ES: `(localdb)\MSSQLLocalDB / ALFA_CENTRAL_DEV`.
- Tenant vacío: `(localdb)\MSSQLLocalDB / ALFACORE_ES_TENANT_DEV`.
- Key ring: `%LOCALAPPDATA%\AlfaCore\DataProtectionKeys\WhatsAppEmbeddedSignup`.
- Secrets: almacén local de .NET user-secrets.

Los scripts nunca limpian automáticamente onboardings, Vault ni claves existentes. No se incluyó reset automático en esta etapa.
## Workers ajenos al entorno mínimo

El launcher activa `AlfaCoreEsLocal:Enabled=true`. Solo en `Development`, este modo no registra actualizaciones automáticas de base, Compra IA, recordatorios de pruebas de módulos, facturación, automatizaciones temporizadas de Conversaciones ni el inbox de WhatsApp Web/QR. Son procesos operativos ajenos a la prueba de WhatsApp Cloud API Embedded Signup y requieren estructuras que deliberadamente no forman parte de las bases LocalDB mínimas. Los servicios de UI de Login, Conversaciones y Configuración siguen disponibles; el hosted service de Embedded Signup sigue registrado y respeta `WorkerEnabled=false`.

Development normal y Production conservan el registro habitual de esos hosted services.
