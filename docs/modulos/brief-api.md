# API de resumen diario (`/api/brief/*`)

Tres endpoints **GET de solo lectura** para que un asistente externo (Claude, etc.) arme el resumen diario.

## Configuración (no va en el repo)

| Dónde | Clave | Para qué |
|---|---|---|
| Config del servidor (variable de entorno `Brief__Token`) | `Brief:Token` | Token Bearer fijo. Sin él, las rutas devuelven 404. |
| Config del servidor (`Brief__ConnectionString`) | `Brief:ConnectionString` | Cadena a ALFANET2007 con un **usuario SQL de solo lectura** (`db_datareader`). Sin ella, 404. |
| Config del servidor (`Brief__IdTecnico`) | `Brief:IdTecnico` | `IdTecnico` de `V_TA_Tecnicos` dueño del resumen. Si falta, `/actividad` y `/crm/oportunidades?soloMias=true` responden 503. No usa `TA_CONFIGURACION`, así el usuario de solo lectura no necesita acceso a esa tabla. |

Generar el token: `[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`.
No usa la conexión de la sesión ni la de `ConexionClienteService`: es independiente del modo SaaS.

## Autenticación

`Authorization: Bearer <token>`. Comparación en tiempo constante. 401 si falta o no coincide. Respuestas con `Cache-Control: no-store`.

## `GET /api/brief/soporte/pendientes`

Conversaciones abiertas (estado no cerrado en `CONV_ESTADOS`, no archivadas), máx. 50, ordenadas por `horasSinRespuesta` desc.

Campos: `id`, `cliente`, `contacto`, `canal`, `asunto`, `fechaApertura`, `fechaUltimoMensaje`, `ultimoEscribio` (`cliente` | `nosotros`), `tecnico`, `estado`, `horasSinRespuesta`, `ultimoMensajeCliente` (≤300 caracteres).

- `asunto`: título del ticket vinculado a la conversación; si no hay, el resumen del último mensaje.
- `ultimoEscribio` ignora las notas internas.
- `horasSinRespuesta`: horas desde el último mensaje del cliente **si el cliente escribió último**; `0` si ya respondimos nosotros.
- El teléfono no se expone; `contacto` es el nombre.

## `GET /api/brief/actividad?desde=AAAA-MM-DD`

`desde` es opcional (por defecto, hace 7 días); formato inválido → 400. Trabajo pendiente y hecho en la mesa de ayuda (`/tickets`) del técnico `Brief:IdTecnico`:

- `partesHoras`: partes de horas con `Fecha >= desde` (`ALFACORE_PARTES_HORAS`), con `minutos`.
- `ticketsAbiertosAsignados`: tickets abiertos asignados al técnico, por prioridad desc y antigüedad.
- `ticketsSinAsignar`: tickets abiertos sin técnico. Los crea el bot ("Consulta sin resolver: ...") y son la mayoría, por eso van en lista aparte.
- `ticketsCerradosDesde`: tickets cerrados desde `desde`.

Cada ítem: `tipo` (`PARTE_HORAS` | `TICKET`), `id`, `titulo`, `cliente`, `estado`, `fecha`, `vencimiento`, `prioridad` (tickets, 0-3) y `minutos` (partes). Los tickets **no tienen fecha de vencimiento** en el modelo: `vencimiento` viene siempre nulo. Máx. 50 por lista.

## `GET /api/brief/crm/oportunidades?soloMias=false`

Seguimiento de oportunidades abiertas del CRM (etapa que no es Ganada ni Perdida), máx. 50. `soloMias=true` filtra por `Brief:IdTecnico` (el "Mi flujo" de la pantalla).

Orden: primero las de próxima acción **vencida**, luego las **sin acción** cargada, al final las programadas.

Campos: `id`, `numero`, `titulo`, `cliente`, `etapa`, `importeEstimado`, `probabilidad`, `prioridad`, `tecnico`, `fechaAlta`, `fechaCierreEstimada`, `ultimoMovimiento`, `diasSinMovimiento`, `proximaAccion`, `proximaAccionTipo`, `proximaAccionVencimiento`, `proximaAccionEstado` (`VENCIDA` | `PROGRAMADA` | `SIN_ACCION`).

`ultimoMovimiento` es el más reciente entre alta, modificación y `CRM_ACTIVIDAD`.

## Garantías y límites

- Fechas ISO 8601 con offset `-03:00` (la base guarda hora local sin zona).
- No se leen ni devuelven datos de pago ni contraseñas. Como los textos son libres, `BriefTextSanitizer` oculta de forma **best effort** lo que parece contraseña/clave/PIN/token, tarjeta o CBU (14–22 dígitos) antes de truncar. No es una garantía absoluta: el usuario SQL de solo lectura sigue siendo la defensa de fondo.
- Errores: 500 con `{ error, incidente }`; el detalle queda en el log.
- Compatible con compat level 100 (sin `OFFSET`, `STRING_SPLIT`, `OPENJSON`).

## Código

`Services/BriefEndpoints.cs`, `Services/BriefService.cs`, `Models/BriefModels.cs`, `tests/AlfaCore.Tests/BriefApiTests.cs`.
