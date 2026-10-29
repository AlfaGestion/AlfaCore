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

### Clientes que todavía no reportaron

La acción **Clientes sin reporte** abre una consulta independiente para superadmin. Consulta en la
conexión central `AlfaGestion` el padrón oficial `VT_CLIENTES` (sin bajas, títulos ni códigos vacíos),
con clasificación de `TA_CLASIFICACIONES`, y excluye cualquier código que ya exista en `MON_ORIGEN`.
También excluye orígenes desactivados del monitor: haberlos desactivado no significa que nunca
reportaron. No modifica clientes, orígenes ni versiones. Los clientes sin licencia asociada también
pueden aparecer; es un listado para revisar instalación/configuración, no una prueba de versión vieja.

El diálogo permite buscar por código o razón social, filtrar por clasificación individual, **P1 y P2**
o **Sin clasificación**, y muestra 50 clientes por página. Cada apertura y **Actualizar lista** vuelve
a consultar la base, para retirar a los clientes que ya comenzaron a reportar. Un error se muestra
como error, sin presentar una lista vacía como si no hubiera pendientes. Los sistemas que reportaron
y dejaron de hacerlo siguen en el tablero de alertas, no en este listado.

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

Los destinatarios push se eligen desde un combo de usuarios activos de `TA_USUARIOS` en la conexión
central `AlfaGestion`, filtrados por el sistema del operador y excluyendo grupos. Se pueden agregar
varios y quitar individualmente; se conserva `AVISO_USUARIOS_PUSH` con nombres separados por coma.
Una selección antigua que ya no esté disponible permanece visible hasta que se quite expresamente.
Son los nombres de inicio de sesión de AlfaCore, compartidos con Conversaciones; no son correos ni
contactos externos. La entrega requiere una suscripción push activa y notificaciones habilitadas por
el usuario en su navegador. No se envía ningún aviso al seleccionar o guardar destinatarios.

## Actualizaciones: AlfaActualizacion_Monitor (29/10/2026)

Setup (Inno Setup) que actualiza en el cliente TimerLiquidacion, EM_Backup, NETCPV y ControlBase.
Fuentes en `NMC_CONT_DEV\Instalador\AlfaActualizacion_Monitor.iss`.

1. **Publicar** (PC de desarrollo): `python NMC_CONT_DEV\Herramientas\ArmarActualizacion.py --notas "..."`.
   Junta los exes de DEV, compila el setup con versión `AAAAMMDD-NN` y lo sube a AlfaArchivos en
   `ACTUALIZACIONES/<versión>/`; después lo vuelve a bajar para comparar el SHA-256 y lo registra con
   `POST /api/vb6/actualizacion-publicar` (misma clave que backup-status). Queda **sin liberar** (`NADIE`).
2. **Liberar** en este tablero (sección "Actualizaciones"): `PRUEBA` (solo los códigos de cliente indicados),
   `P1P2`, `TODOS` o `NADIE` (pausa). Los clientes de prueba la reciben en cualquier liberación salvo `NADIE`.
3. **En el cliente**, la tarea `ACTUALIZACION_ALFA` del Timer (cada 60 min, la crea el Timer solo) llama a
   `GET /api/vb6/actualizacion-pendiente?licenciaPrincipal=&version=`, que responde `NADA` o
   `ACTUALIZAR|versión|archivo|sha256|tamaño`. Dentro de `ACTUALIZACION_HORARIO` (por defecto 21 a 7 h) y sin
   tareas en curso, la baja, verifica tamaño y SHA-256 y corre el setup con `/VERYSILENT /AUTO`.
   El setup no corta un backup en curso (queda "postergada"); cierra los programas, guarda los exes anteriores en
   `_respaldo_actualizacion\<versión>\`, copia y vuelve a abrir cada Timer con su misma línea de comandos.
4. **Resultado**: el Timer informa el tipo `ACTUALIZACION` (destino = versión instalada; ERROR = crítico) y la
   versión dentro de `VersionSistema`. La columna "Instalada en" cuenta los sistemas con esa versión.

Tabla `MON_ACTUALIZACION` (ALFANET2007). La elección de versión está en
`MonitorClientesService.ElegirActualizacion` (probada en `MonitorActualizacionTests`).

## Código

- `Services/MonitorClientesService.cs` (+ interfaz), `Services/MonitorClientesSchema.cs`,
  `Models/MonitorClientesModels.cs`.
- `Services/MonitorClientesAvisos.cs` (reglas del aviso) y `Services/MonitorClientesAvisosHostedService.cs`.
- `Components/Pages/AdminMonitorClientes.razor` (+ `.css`); entrada en `ModuleTopNavPresets.BuildAdministrar`.
- Endpoint en `Program.cs`, junto a `/api/vb6/backup-status`.

## Pendiente (fases siguientes)

- Pantalla para editar límites por cliente (hoy, directo en `MON_CONTROL_CLIENTE`).
- Más controles: comprobantes sin CAE, disco, tamaño de base, versión del sistema.
