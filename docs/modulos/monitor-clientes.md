# Monitor de Clientes

Administrar → Monitor de Clientes (`/admin/monitor-clientes`, solo superadmin). Muestra el estado del
sistema de escritorio (Alfa Gestión VB6) de cada cliente: envíos y recepciones de puntos de venta,
backup y tareas automáticas. Creado el 28/10/2026 (fase 1: solo tablero, sin avisos por chat).

## Cómo funciona

1. El TimerLiquidacion de cada cliente manda cada tanto un reporte a `POST /api/vb6/monitor-estado`.
2. AlfaCore identifica al cliente por la licencia (`ALFA_CENTRAL.dbo.Clientes.LicenciaPrincipal`) y guarda
   el último dato de cada control en **ALFANET2007** (conexión `AlfaGestion`).
3. El tablero evalúa cada control contra los límites y lo marca OK, Aviso, Crítico o Sin datos.
4. La prioridad P1 a P4 es la clasificación del cliente en `VT_CLIENTES.Clasificacion` → `TA_CLASIFICACIONES`
   (la misma que muestra Conversaciones).

Si un sistema deja de reportar, su fila **Reporte del sistema** pasa a aviso o crítico y el resto de
sus controles quedan como **Sin datos**: así se detecta también un servidor apagado o un Timer cerrado.

## Endpoint

`POST /api/vb6/monitor-estado`, `application/x-www-form-urlencoded`, header `X-Api-Key` = `BackupStatus:ApiKey`
(la misma clave que `backup-status`; en el VB6, `ALFACORE_BACKUP_APIKEY`).

| Campo | Contenido |
|---|---|
| `licenciaPrincipal` | Licencia del sistema (obligatorio) |
| `dbServidor`, `dbNombre` | Servidor SQL y base que reporta (obligatorios). Un cliente con varias bases tiene un origen por base |
| `hostCliente` | Equipo que manda el reporte |
| `versionSistema` | Versión del exe que reporta |
| `fechaHoraCliente` | Hora del equipo, `yyyy-MM-dd HH:mm:ss`. Se usa para corregir el desfase de reloj |
| `items` | Una línea por control: `TIPO\|DESTINO\|ULTIMO_OK\|ULTIMO_INTENTO\|RESULTADO\|MENSAJE` |

- Fechas en `yyyy-MM-dd HH:mm:ss`, vacías si no hay. El destino no puede tener `|` (para un PDV usar
  `SERVIDOR\INSTANCIA/BASE`). El mensaje va último y puede tener `|`.
- Cada reporte es la foto completa de los tipos que trae: si un tipo viene, los destinos de ese tipo que
  no vinieron se borran (PDV dado de baja, tarea borrada). Los tipos que no vienen quedan como estaban.
- Tipos desconocidos y `REPORTE` (lo calcula el servidor) se ignoran.
- Respuestas: `200 OK <grabados> <ignorados>`, `401` clave incorrecta, `404 licencia-inexistente`,
  `400` faltan datos.

## Tipos de control (`MON_CONTROL_TIPO`)

| Código | Medida | Aviso | Crítico |
|---|---|---|---|
| `REPORTE` | Hace cuánto que el sistema no reporta | 1 h | 3 h |
| `ENVIO_PDV` | Último envío correcto de artículos y precios a cada PDV | 2 h | 3 h |
| `RECEPCION_PDV` | Última recepción correcta de cada PDV | 1 h | 2 h |
| `BACKUP` | Último backup correcto | 26 h | 36 h |
| `TAREA` | Resultado de la última ejecución de cada tarea del Timer | ADVERTENCIA | ERROR |

- Medida `EDAD`: el estado sale de la antigüedad de `UltimoOk`. Si nunca hubo un OK es crítico. Si el
  OK está dentro del límite pero el último intento dio `ERROR`, queda en aviso.
- Medida `RESULTADO`: el estado sale de `Resultado`.
- Límites propios de un cliente, o un control desactivado: `MON_CONTROL_CLIENTE` (NULL = el del tipo).
- Para agregar un control nuevo (por ejemplo, vencimiento del certificado ARCA) alcanza con una fila
  nueva en `MON_CONTROL_TIPO` y que el VB6 mande ese tipo.

### De dónde saca el VB6 cada dato (`ModMonitorAlfaCore.bas` en TimerLiquidacion)

| Tipo | Origen en la base central del cliente |
|---|---|
| `BACKUP` | `msdb.dbo.backupset`; si no se puede leer, `TA_CONFIGURACION` `ULTIMO_BCK` |
| `ENVIO_PDV` | `MA_NOVEDADES_ENVIO_PDV`: la `FECHA_LECTURA` más vieja de cada destino |
| `RECEPCION_PDV` | `MA_RECEPCION_PDV_ESTADO`: NETCPV la crea y graba el resultado de cada PDV al recibir (automático o manual; no los PDV por FTP ni las franquicias). Se omiten los PDV dados de baja en `V_TA_TPV` |
| `TAREA` | `TA_TAREAS_AUTOMATICAS` habilitadas que no son manuales |

Un PDV que queda desconectado no se intenta recibir, así que su último OK envejece y pasa a crítico. Si un
cliente apaga los PDV de noche, conviene subirle el límite de `RECEPCION_PDV` en `MON_CONTROL_CLIENTE`.

## Tablas (ALFANET2007)

Script: `docs/base-datos/sql-referencia/2026-10-28-001__alfanet2007_monitor_clientes.sql`, idéntico a
`MonitorClientesSchema.Script` (lo verifica `MonitorClientesTests`). `MonitorClientesService` lo aplica
solo la primera vez que se usa. No va en `App_Data/updates` porque esos scripts corren en todas las
bases de clientes.

- `MON_CONTROL_TIPO`, `MON_CONTROL_CLIENTE`: qué se controla y con qué límites.
- `MON_ORIGEN`: cada sistema que reporta (cliente + servidor + base), con su último reporte.
  `Desactivado = 1` lo saca del tablero.
- `MON_ESTADO`: último dato de cada control (origen + tipo + destino).

## Código

- `Services/MonitorClientesService.cs` (+ interfaz), `Services/MonitorClientesSchema.cs`,
  `Models/MonitorClientesModels.cs`.
- `Components/Pages/AdminMonitorClientes.razor` (+ `.css`); entrada en `ModuleTopNavPresets.BuildAdministrar`.
- Endpoint en `Program.cs`, junto a `/api/vb6/backup-status`.

## Pendiente (fases siguientes)

- Incidentes ("lo tomo", silenciar hasta) y aviso por chat o push solo para clientes P1/P2 en crítico,
  agrupado para no llenar el soporte.
- Pantalla para editar límites por cliente.
- Más controles: certificado ARCA, comprobantes sin CAE, disco, tamaño de base, versión del sistema.
