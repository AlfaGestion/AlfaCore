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
3. Iniciar sesión si el entorno local dispone de identidad de desarrollo.
4. Ingresar a Base 84.
5. Abrir `Configuración → WhatsApp API`.

El launcher valida .NET, LocalDB, scripts, certificado y puerto 7055; prepara idempotentemente `ALFA_CENTRAL_DEV`; crea `ALFACORE_ES_TENANT_DEV` vacío para impedir fallback remoto; verifica schema ES y Base 84; habilita ES solo para Base 84; mantiene el worker apagado; usa un key ring propio en `%LOCALAPPDATA%`; sobrescribe dentro del proceso las conexiones central/tenant con LocalDB e inicia `https://localhost:7055`.

La primera navegación puede requerir login. Si el checkout no tiene fixtures locales de autenticación, AlfaCore igualmente queda iniciado y el seed Base 84 queda disponible en el central DEV; nunca se consulta `ALFA_CENTRAL` para completar el login.

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
