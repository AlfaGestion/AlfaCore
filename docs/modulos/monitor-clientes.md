# Monitor de Clientes

Administrar → Monitor de Clientes (`/admin/monitor-clientes`, solo superadmin). Muestra el estado del
sistema de escritorio (Alfa Gestión VB6) de cada cliente: envíos y recepciones de puntos de venta,
backup y tareas automáticas. Creado el 28/10/2026 (fase 1: tablero; fase 2: seguimiento y avisos al chat de soporte).

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
| `CERT_ARCA` | Vencimiento del certificado de factura electrónica de cada CUIT | 30 días antes | 7 días antes o vencido |

- Medida `EDAD`: el estado sale de la antigüedad de `UltimoOk`. Si nunca hubo un OK es crítico. Si el
  OK está dentro del límite pero el último intento dio `ERROR`, queda en aviso.
- Medida `RESULTADO`: el estado sale de `Resultado`.
- Medida `VENCE` (28/10/2026): `UltimoOk` trae la **fecha de vencimiento** y los límites son minutos *antes*
  de esa fecha. Vencido es crítico. Si el sistema no pudo leer el archivo (`Resultado = ERROR`) queda en aviso.
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
| `CERT_ARCA` | Archivos de `TA_CONFIGURACION` `RUTA_CRT` y `RUTA_CRT_2`, y `RUTA_CRT` de cada `V_TA_UNIDADNEGOCIO` con `USAEFC`. Lee la fecha `NotAfter` con crypt32 (PEM o DER). Destino `CUIT / archivo.crt`; el mensaje trae la ruta |

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
- `MON_SEGUIMIENTO` (fase 2): quién tomó una fila, hasta cuándo está silenciada y una nota. Misma clave
  que `MON_ESTADO` (también sirve para la fila calculada `REPORTE`, con destino vacío).
- `MON_CONFIG` (fase 2): configuración y estado de los avisos (`AVISO_*`, ver abajo).

## Seguimiento y avisos al chat de soporte (fase 2, 28/10/2026)

Cada fila con problema tiene un menú: **Lo tomo** / **Soltar**, **Silenciar** (1 h, 4 h, hasta las 8,
24 h, 7 días), **Quitar silencio** y **Nota**. Cuando el control vuelve a OK, la fila queda libre sola (se
borran el tomado y la nota); el silencio sigue hasta que vence. El filtro "Críticos y avisos sin tomar"
muestra lo que nadie está mirando.

`MonitorClientesAvisosHostedService` (cada 5 minutos; al minuto si se tocó **Enviar aviso ahora**):

1. Evalúa el tablero y libera el seguimiento de lo que se normalizó.
2. Pendientes = filas en **crítico**, de clientes con prioridad `AVISO_PRIORIDADES` (por defecto `1,2`),
   sin tomar y sin silenciar. Los avisos (amarillo) no van al chat.
3. Publica **un solo mensaje** con todas las pendientes en el hilo interno **Monitor de Clientes** de
   Conversaciones de ALFANET2007 (lo crea solo; su id queda en `AVISO_ID_HILO`), cuando:
   - aparece una pendiente nueva (se marca "(nueva)");
   - siguen las mismas y pasaron `AVISO_REPETIR_MINUTOS` (por defecto 120; 0 = no repetir);
   - se vaciaron las que se habían avisado (mensaje "se normalizaron").
4. Si hay usuarios en `AVISO_USUARIOS_PUSH`, les manda además un push que abre el tablero (el mensaje en un
   hilo interno no genera push por sí mismo).

Se configura desde el tablero (sección "Avisos al chat de soporte"). `AVISO_ACTIVO = NO` apaga los
avisos automáticos (el botón "Enviar aviso ahora" igual funciona). Corre sin sesión, contra la conexión
`AlfaGestion`; se apaga con `ProcesosSegundoPlano:Deshabilitados` y en SaaS solo corre en el servidor que
tiene los workers (`IWorkerAssignmentService`). Las reglas están en `MonitorClientesAvisos` (sin base de
datos, probadas en `MonitorClientesAvisosTests`).

## Código

- `Services/MonitorClientesService.cs` (+ interfaz), `Services/MonitorClientesSchema.cs`,
  `Models/MonitorClientesModels.cs`.
- `Services/MonitorClientesAvisos.cs` (reglas del aviso) y `Services/MonitorClientesAvisosHostedService.cs`.
- `Components/Pages/AdminMonitorClientes.razor` (+ `.css`); entrada en `ModuleTopNavPresets.BuildAdministrar`.
- Endpoint en `Program.cs`, junto a `/api/vb6/backup-status`.

## Pendiente (fases siguientes)

- Pantalla para editar límites por cliente (hoy, directo en `MON_CONTROL_CLIENTE`).
- Más controles: comprobantes sin CAE, disco, tamaño de base, versión del sistema.
