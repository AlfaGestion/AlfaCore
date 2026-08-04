# Integración: SMTP y correo transaccional

## Alcance en AlfaCore

AlfaCore usa SMTP para correos transaccionales en dos circuitos principales:

- verificación de cuentas del registro público;
- envío de comprobantes por email desde Punto de Venta.

Código relacionado:

- `src/AlfaCore/Services/CentralRegistrationService.cs`
- `src/AlfaCore/Services/PuntoVentaService.cs`
- `src/AlfaCore/Models/PuntoVentaModels.cs`
- `src/AlfaCore/appsettings.json`

Documento relacionado: `docs/arquitectura/INFRAESTRUCTURA_SERVIDORES.md`.

## Configuración

Para registro público, la configuración se lee desde `RegistroPublico` o variables legacy equivalentes.

| Clave | Uso |
|---|---|
| `RegistroPublico:EmailServer` / `EMAIL_SERVER` | Servidor SMTP |
| `RegistroPublico:EmailPort` / `EMAIL_PORT` | Puerto |
| `RegistroPublico:EmailAccount` / `EMAIL_CTA` | Cuenta remitente |
| `RegistroPublico:EmailPassword` / `EMAIL_PASS` | Password |
| `RegistroPublico:EmailSsl` / `EMAIL_SSL` | SSL/TLS |

Para Punto de Venta, la configuración se administra por `TA_CONFIGURACION`:

| Clave | Uso |
|---|---|
| `EMAIL_SERVER` | Servidor SMTP |
| `EMAIL_PORT` | Puerto |
| `EMAIL_CTA` | Cuenta remitente |
| `EMAIL_PASS` | Password |
| `EMAIL_SSL` | SSL/TLS |

## Flujo técnico

1. El servicio resuelve configuración.
2. Valida destinatario con `MailAddress`.
3. Construye `MailMessage` HTML.
4. Crea `SmtpClient` con servidor, puerto, credenciales y SSL.
5. Envía mediante `SendMailAsync`.
6. Si falla, el error debe registrarse por la capa centralizada y exponerse al usuario con un mensaje claro.

## Problemas frecuentes

- Puerto incorrecto o SSL mal configurado suele terminar en timeout o error de autenticación.
- Algunos proveedores exigen contraseña de aplicación, no la contraseña normal de la casilla.
- El remitente debe coincidir con la cuenta autenticada en muchos servidores.
- Firewalls del servidor pueden bloquear salida al puerto SMTP.
- Las credenciales no deben quedar en archivos versionados.
- Si la base cliente no tiene claves `EMAIL_*`, Punto de Venta no puede enviar comprobantes.

## Lecciones aplicadas en AlfaCore

- Registro público y Punto de Venta comparten concepto SMTP, pero resuelven configuración de lugares distintos por compatibilidad.
- Validar email antes de enviar mejora el mensaje al usuario y evita errores técnicos innecesarios.
- El HTML del correo debe ser simple y robusto; muchos clientes de correo ignoran CSS avanzado.
- Conviene registrar contexto del envío, no solo el stack trace: módulo, destinatario, comprobante o proceso.

## Fuentes oficiales

- [Microsoft .NET: SmtpClient](https://learn.microsoft.com/dotnet/api/system.net.mail.smtpclient)
- [Microsoft .NET: MailMessage](https://learn.microsoft.com/dotnet/api/system.net.mail.mailmessage)
