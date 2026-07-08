# PWA y notificaciones push

## Configuración VAPID

La aplicación lee las claves desde `IConfiguration` usando la sección `PushNotifications`.

Orden habitual de carga en ASP.NET Core:

- `appsettings.json`
- `appsettings.{Environment}.json`, por ejemplo `appsettings.Development.json` o `appsettings.Production.json`
- variables de entorno
- `.env` local, porque AlfaCore ejecuta `DotEnvLoader.LoadIfPresent(...)` al iniciar

El código que enlaza esta configuración está en `Program.cs`:

```csharp
builder.Services.Configure<PushNotificationsOptions>(
    builder.Configuration.GetSection(PushNotificationsOptions.SectionName));
```

```json
"PushNotifications": {
  "Subject": "mailto:admin@alfagestion.com",
  "PublicKey": "...",
  "PrivateKey": "..."
}
```

## Desarrollo

Usar `appsettings.Development.json` local o `.env`. Ambos están ignorados por git.

Ejemplo `src/AlfaCore/appsettings.Development.json`:

```json
{
  "PushNotifications": {
    "Subject": "mailto:admin@alfagestion.com",
    "PublicKey": "PEGAR_PUBLIC_KEY",
    "PrivateKey": "PEGAR_PRIVATE_KEY"
  }
}
```

Ejemplo `.env` en la raíz del repo o dentro de `src/AlfaCore`:

```powershell
PushNotifications__Subject=mailto:admin@alfagestion.com
PushNotifications__PublicKey=PEGAR_PUBLIC_KEY
PushNotifications__PrivateKey=PEGAR_PRIVATE_KEY
```

## Producción

En producción no hardcodear la clave privada en el repositorio. Configurarla en el servidor de `https://alfanetweb.ddns.net/` mediante variables de entorno del proceso, del servicio Windows/IIS, o en un `appsettings.Production.json` que viva solo en el servidor.

Variables equivalentes:

```text
PushNotifications__Subject=mailto:admin@alfagestion.com
PushNotifications__PublicKey=PEGAR_PUBLIC_KEY
PushNotifications__PrivateKey=PEGAR_PRIVATE_KEY
```

Windows PowerShell, para la sesión actual:

```powershell
$env:PushNotifications__Subject = "mailto:admin@alfagestion.com"
$env:PushNotifications__PublicKey = "PEGAR_PUBLIC_KEY"
$env:PushNotifications__PrivateKey = "PEGAR_PRIVATE_KEY"
```

Windows PowerShell, persistente a nivel máquina:

```powershell
[Environment]::SetEnvironmentVariable("PushNotifications__Subject", "mailto:admin@alfagestion.com", "Machine")
[Environment]::SetEnvironmentVariable("PushNotifications__PublicKey", "PEGAR_PUBLIC_KEY", "Machine")
[Environment]::SetEnvironmentVariable("PushNotifications__PrivateKey", "PEGAR_PRIVATE_KEY", "Machine")
```

Después reiniciar el servicio o el pool de IIS que ejecuta AlfaCore.

Linux/systemd:

```ini
Environment=PushNotifications__Subject=mailto:admin@alfagestion.com
Environment=PushNotifications__PublicKey=PEGAR_PUBLIC_KEY
Environment=PushNotifications__PrivateKey=PEGAR_PRIVATE_KEY
```

Después ejecutar `systemctl daemon-reload` y reiniciar el servicio.

La `PublicKey` puede enviarse al navegador para crear la suscripción. La `PrivateKey` nunca debe enviarse al cliente y solo la usa el servidor para firmar el envío Web Push.

## Generar claves

Herramienta incluida en el repo:

```powershell
powershell -ExecutionPolicy Bypass -File tools/push/generate-vapid-keys.ps1
```

Linux/macOS:

```bash
bash tools/push/generate-vapid-keys.sh
```

Comando directo equivalente:

```bash
npx web-push generate-vapid-keys
```

Copiar la clave pública en `PushNotifications:PublicKey` y la privada en `PushNotifications:PrivateKey`. Mantener el mismo par de claves mientras existan suscripciones activas; si se cambia el par, los dispositivos deben volver a suscribirse.

## Checklist PWA

- `manifest.webmanifest` debe responder sin 404.
- `start_url` y `scope` deben ser `/`.
- `display` debe ser `standalone`.
- Deben existir íconos `192x192` y `512x512`.
- `service-worker.js` debe registrarse correctamente.
- El sitio público debe servirse por HTTPS.
- El navegador debe soportar Push API.
- En iOS, AlfaCore debe estar instalada en pantalla de inicio y abierta desde ese ícono.

## Prueba Android

1. Abrir `https://alfanetweb.ddns.net/conversaciones` en Chrome o Edge.
2. Si aparece `Instalar app`, tocarlo y aceptar la instalación.
3. Abrir `Notificaciones`.
4. Activar notificaciones del dispositivo y aceptar el permiso del navegador.
5. Elegir alcance: `Solo conversaciones asignadas a mi usuario` o `Todas las conversaciones accesibles`.
6. Tocar `Enviar prueba`.

## Prueba iOS

1. Abrir `https://alfanetweb.ddns.net/conversaciones` en Safari.
2. Tocar `Instalar app` y seguir: `Compartir > Agregar a pantalla de inicio`.
3. Abrir AlfaCore desde el ícono instalado, no desde la pestaña de Safari.
4. Abrir `Notificaciones`, activar el permiso y tocar `Enviar prueba`.

En iOS las notificaciones web requieren que la PWA esté instalada en pantalla de inicio.
