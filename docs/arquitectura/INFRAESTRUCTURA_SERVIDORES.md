# Infraestructura de servidores (Alfa Gestión / AlfaCore)

Referencia interna de qué corre en cada servidor. Útil para saber desde dónde hay que tener
alcance de red al implementar features que tocan más de un servidor (ej. aprovisionamiento de
bases nuevas, jobs administrativos, backups).

| Servidor | IP | Rol |
|---|---|---|
| SERVER-ALFAWEB | 10.8.0.32 | App web pública — https://alfanetweb.ddns.net/ |
| SERVER-ALFACENTRAL | 10.8.0.53 | App web pública — https://alfacentral.ddns.net/ |
| SERVER-ALFACORE | 10.8.0.31 | Motor SQL Server — hospeda `ALFA_CENTRAL` y las bases de cada cliente (`ALFANET2007`, `AW_<codigo>`, etc.) |

## Puntos importantes

- **Las apps web (ALFAWEB/ALFACENTRAL) NO están co-ubicadas con el motor de base de datos.**
  Cualquier operación que necesite `RESTORE DATABASE`/`CREATE DATABASE`/lectura de archivos `.bak`
  debe correr con una ruta que el **motor SQL Server en SERVER-ALFACORE** pueda leer directamente
  (disco local de ese servidor, o un share de red al que el servicio de SQL Server tenga acceso) —
  no alcanza con que el archivo esté en la máquina donde corre la app web.
- `ConnectionStrings:AlfaGestion` y `ConnectionStrings:AlfaCentral` en `appsettings.json` ya apuntan
  a `10.8.0.31` (`ALFANET2007` y `ALFA_CENTRAL` respectivamente).
- `wsAlfa` (el web service Python/Flask del sistema anterior) corre bajo IIS en distintas copias/
  entornos — la que se confirmó como la real activa fue `\\server-vpn2022\C\inetpub\wwwroot\wsAlfa\`.
  Su `.env` tiene, entre otras, las credenciales de `DB_SERVER_ALFA`/`DB_NAME_ALFA` (apuntan a
  `10.8.0.31` / `ALFANET2007` — la misma base que `ConnectionStrings:AlfaGestion`) y del SMTP usado
  para el mail de verificación de cuentas (`mail.alfagestion.com.ar:587`, cuenta
  `envios@alfagestion.com.ar`).

## Contexto: registro público AlfaNet Web (implementación 2026-07-27)

Se portó a AlfaCore (C#) la rutina de auto-registro que hoy vive en `AlfaWeb-main`
(CodeIgniter 4 PHP) + `wsAlfa-main` (Python/Flask), manteniendo compatibilidad con el circuito
legacy. Decisiones confirmadas durante la implementación:

- Mantener la numeración de cliente vía `sp_web_altaClienteAlfa` (ya existe en `ALFANET2007` /
  SERVER-ALFACORE — no hace falta agregar una conexión nueva).
- El alta pública se hace desde `https://alfanetweb.ddns.net/registrarme`.
- El usuario recibe un correo de verificación y **recién después** de confirmar se aprovisiona la
  base nueva.
- Aprovisionar la base nueva por `RESTORE` de una plantilla `.bak`, y luego ejecutar los scripts de
  `src/AlfaCore/App_Data/updates/` para dejar la estructura al día.
- Nombre de la base nueva: `AW_<codigo>` (AW = "Alta Web").
- Login SQL propio por cliente nuevo. **Por ahora** se usa el nombre de la base tanto como usuario
  SQL como password (`AW_<codigo>` / `AW_<codigo>`), pero esa decisión quedó encapsulada en
  `CentralProvisioningService.ResolveDatabaseCredentials(...)` para endurecerla después sin rehacer
  el flujo.
- reCAPTCHA v2 checkbox, validado del lado del servidor contra Google (`siteverify`).
- El usuario de acceso y el email del registro se guardan en `TA_USUARIOS` de la base nueva, para
  mantener el login legacy.
- El bloqueo por prueba de 30 días **queda solo en el legacy**. No se implementó un bloqueo nuevo en
  AlfaCore dentro de esta etapa.
- `dbo.clientes.password`/`dbo.users.password` se guardan en texto plano hoy (`PlainTextPasswordVerifier`
  en AlfaCore, igual que el sistema viejo) — se mantiene esa convención al portar para no romper el
  login existente; mejorarlo a hash es una tarea aparte, no incluida acá.

## Configuración operativa confirmada

- URL pública: `https://alfanetweb.ddns.net`
- SQL Server objetivo del aprovisionamiento: `10.8.0.31`
- Ruta recomendada para la plantilla de restore:
  `C:\AlfaCore\Backups\Plantillas\ALFAWEB_PLANTILLA.bak`
- SMTP reutilizado del entorno legacy:
  - servidor: `mail.alfagestion.com.ar`
  - puerto: `587`
  - cuenta: `envios@alfagestion.com.ar`
- Las claves de reCAPTCHA y del SMTP se leen desde `.env` / `appsettings` bajo la sección
  `RegistroPublico`.

## Nota técnica sobre `.env`

- AlfaCore carga `.env` al iniciar.
- Desde 2026-07-27, `DotEnvLoader` prioriza el `.env` más alto en la jerarquía de carpetas, para
  evitar que un `src/AlfaCore/.env` pise silenciosamente al `.env` principal del repositorio.
- Recomendación operativa: mantener un solo `.env` efectivo en la raíz del proyecto
  (`C:\dev\AlfaCore\.env` en desarrollo, y el equivalente en la carpeta publicada del servidor).
