# Barra superior: mensajes, avisos y configuración

Los tres íconos de la barra superior del shell AlfaDesign (`MainLayout.razor`). Se ocultan en modo `?directo=1` y solo se muestran con usuario autenticado.

## Burbuja: mensajes (`Components/Layout/TopbarMensajesMenu.razor`)

- Panel con dos pestañas: **Sin leer** (conversaciones con `MensajesNoLeidosUsuario > 0`, más recientes primero) y **Sin responder** (último mensaje del cliente, la que más espera primero). Cada fila abre `/conversaciones?id=...`.
- Datos: `GlobalConversationNotificationsSource.FetchBandejaAsync` → `IConversacionesService.GetInboxAsync` (modo `pendientes`, todos los canales). Respeta los mismos permisos por número/usuario que la bandeja y pasa por `TenantDataAccessGuard` (solo con sesión autorizada).
- Se actualiza cada 30 segundos y al abrir el panel. La clasificación está en `GlobalConversationNotificationsSource.Clasificar` (tests: `TopbarMensajesMenuTests`).

## Campana: avisos (`Components/Layout/TopbarAvisosMenu.razor`, `Services/CentroAvisosService.cs`)

Los avisos se **calculan al momento**; solo se guarda qué marcó leído cada usuario en `ALFACORE_AVISOS_LEIDOS` (script `App_Data/updates/2026-10-05-001__avisos_leidos.sql`). Cada usuario ve solo los suyos; el técnico del usuario se resuelve por `V_TA_Tecnicos.UsuarioAsociado`.

| Aviso | Origen | Regla |
|---|---|---|
| Guardia | `CAL_EVENTOS` tipo `GUARDIA` del técnico | El día anterior ("Mañana tenés guardia") y el mismo día ("Hoy tenés guardia"). |
| Reunión / capacitación / otro | `CAL_EVENTOS` + `CAL_RECORDATORIOS.MinutosAntes` | Desde la anticipación del recordatorio (24 h si no tiene) hasta que termina. |
| Nueva reserva | `CAL_RESERVAS_REUNION` (reservas públicas desde `/reuniones`) | Reservas de los últimos 7 días para eventos del técnico; si el tipo de reunión no tiene técnico, se avisa a todos. |
| Te agendaron | `CAL_EVENTOS.UsuarioAlta` / `FechaHora_Grabacion` | Evento del técnico cargado por otro usuario en los últimos 7 días. |
| Novedad | `ALFACORE_NOVEDADES` publicadas, vigentes, de los últimos 30 días, sin lectura en `ALFACORE_NOVEDADES_LECTURAS` | Se lee dentro del panel; al marcarla se registra con `INovedadesService.MarkReadAsync` (mismo registro que el popup). |

- Las reglas están en `CentroAvisosService.ConstruirAvisos` (tests: `CentroAvisosTests`). Errores de carga → `AUX_ERR` vía `IAppEventService`.
- Si alguna tabla no existe en la base, esa fuente se omite (sin error).
- El pie del panel abre la configuración de notificaciones push del dispositivo (antes la abría la campana directamente).
- Pendiente: avisos de tickets asignados; push/WhatsApp de estos avisos.

## Engranaje: configuración (`Components/Layout/TopbarConfiguracionMenu.razor`, `Services/ConfiguracionCatalogo.cs`, `Components/Pages/ConfiguracionHub.razor`)

- **Lista única** `ConfiguracionCatalogo.Entradas`: título, descripción, área, ruta (puede incluir `?seccion=` para abrir una sección puntual), módulos relacionados y palabras clave. **Para sumar una configuración nueva, agregarla ahí**; el test `ConfiguracionCatalogoTests` falla si una ruta no existe como página.
- Permisos: una entrada se muestra si su pantalla está entre las rutas del menú del usuario o su módulo entre sus módulos habilitados (`IMenuService`). Si el menú no responde, se muestran todas y cada pantalla aplica su propio control.
- Menú del engranaje: **Este módulo** (entradas relacionadas con el módulo actual), **Mis preferencias** (notificaciones push del dispositivo), **Empresa y sistema**, buscador y "Ver toda la configuración".
- `/configuracion`: todas las configuraciones agrupadas por área con buscador (sin acentos ni mayúsculas); acepta `?buscar=`. Módulo `configuracion` registrado en `AlfaDesignManagedModules`. No se agregó a `ALFACORE_MENU_WEB`: se entra desde el engranaje.
