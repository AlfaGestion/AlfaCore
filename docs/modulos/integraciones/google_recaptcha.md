# Integración: Google reCAPTCHA

## Alcance en AlfaCore

AlfaCore usa reCAPTCHA v2 checkbox en el registro público para reducir altas automatizadas. La validación se hace del lado servidor contra Google antes de continuar el flujo de registro.

Código relacionado:

- `src/AlfaCore/Services/RecaptchaValidationService.cs`
- `src/AlfaCore/Services/CentralRegistrationService.cs`
- `src/AlfaCore/Models/PublicRegistrationModels.cs`
- `src/AlfaCore/appsettings.json`

Documento relacionado: `docs/arquitectura/INFRAESTRUCTURA_SERVIDORES.md`.

## Configuración

La configuración vive en `appsettings`, variables de entorno o `.env`, sección `RegistroPublico`.

| Clave | Uso |
|---|---|
| `RegistroPublico:RecaptchaSiteKey` | Clave pública usada por el widget en frontend |
| `RegistroPublico:RecaptchaSecret` | Secreto usado por backend para verificar el token |

## Flujo técnico

1. El usuario completa el widget reCAPTCHA.
2. El frontend envía el token junto con la solicitud de registro.
3. `RecaptchaValidationService` valida que exista secreto y token.
4. AlfaCore llama por `POST` a `https://www.google.com/recaptcha/api/siteverify`.
5. Envía `secret`, `response` y `remoteip`.
6. Si Google responde `success = true`, el registro puede continuar.
7. Si falla, se muestra un mensaje funcional en español.

## Problemas frecuentes

- La Site Key y el Secret no son intercambiables.
- El token de usuario es de vida corta y de un solo uso; no debe almacenarse para reintentos futuros.
- Si falta el secreto en servidor, el registro debe bloquearse con mensaje claro.
- Un dominio no autorizado en la configuración de Google puede hacer que el widget o la verificación fallen.
- El backend debe verificar siempre; validar solo en navegador no protege contra automatización.

## Lecciones aplicadas en AlfaCore

- Mantener reCAPTCHA dentro del flujo de registro público evita exponer controles en módulos internos.
- La configuración por `.env` facilita desarrollo y producción sin subir secretos al repo.
- Conviene devolver mensajes de usuario simples y registrar detalles técnicos en la capa común si aparece un error relevante.
- La validación de reCAPTCHA es un filtro antiabuso, no una autorización ni una prueba de identidad.

## Fuentes oficiales

- [Google reCAPTCHA: verificar respuesta del usuario](https://developers.google.com/recaptcha/docs/verify)
- [Google reCAPTCHA v2: mostrar widget](https://developers.google.com/recaptcha/docs/display)
- [Google reCAPTCHA Help](https://support.google.com/recaptcha/)
