# Continuidad Codex

## Objetivo de este archivo

Este archivo resume el estado actual del trabajo para poder continuarlo desde otra PC o en una nueva conversación sin tener que reconstruir todo el contexto.

Uso sugerido al retomar:

```text
Leé docs/CONTINUIDAD_CODEX.md y continuemos desde ahí.
```

---

## Estado general

Se trabajó sobre el repo `AlfaCore` en mejoras de:

- Auditoría de usuarios
- visor de comprobantes
- manejo de sesiones SQL
- documentación y manuales
- control centralizado de backups de clientes (sesión actual, ver sección propia más abajo)

No se rehizo la arquitectura general.  
Se trabajó sobre la base actual del proyecto.

---

## Decisiones importantes ya tomadas

### 1. Manuales

Se definió este criterio:

- `src/AlfaCore/Docs/` = manuales funcionales que consume o puede consumir la aplicación
- `docs/` = documentación técnica, catálogo, reglas y archivos puente

### 2. Manual principal

`src/AlfaCore/Docs/manual_usuario.md`

Ya no debe considerarse un manual de Compras.

Ahora debe ser y ya fue reescrito como:

- **Manual General de AlfaCore**

Su función es explicar:

- qué es AlfaCore
- base activa
- navegación
- menú
- filtros
- grillas
- exportaciones
- ayuda general

### 3. Manuales por módulo

Se acordó trabajar con un manual específico por módulo importante.

Ejemplo ya creado:

- `src/AlfaCore/Docs/manual_auditoria_usuarios.md`

### 4. Archivos puente en `docs/`

Se dejaron archivos puente para consulta rápida desde el repositorio:

- `docs/manual_usuario.md`
- `docs/manual_auditoria_usuarios.md`

Estos no son la fuente principal del contenido.

---

## Cambios funcionales realizados

### Auditoría de usuarios

Se hicieron mejoras importantes en el módulo.

#### Nuevo control agregado

Se agregó al combo `Tipo control`:

- `Posibles comprobantes duplicados`

Objetivo:

- detectar comprobantes de compras potencialmente duplicados

Fuente principal:

- `C_MV_Cpte`

Cruce informativo:

- `MV_ASIENTOS`

Archivos tocados:

- `src/AlfaCore/Services/AuditoriaService.cs`
- `src/AlfaCore/Models/AuditoriaModels.cs`
- `src/AlfaCore/Components/Pages/AuditoriaUsuarios.razor`
- `docs/CATALOGO_RUTINAS.md`

#### Control “Comprobantes iniciados y no grabados”

Se ajustó la vista para mostrar mejor datos de cancelación.

Cambios:

- se quitaron columnas genéricas que no aportaban
- se agregaron:
  - hora cancelación
  - minutos hasta cancelación
  - importe al cancelar
  - traza original del sistema

También se agregaron KPI específicos para ese control:

- comprobantes cancelados
- promedio min. cancelación
- máx. min. cancelación
- importe total cancelado
- usuarios involucrados

#### Exportación desde Auditoría

Se agregó:

- exportar a `PDF`
- exportar a `Excel`

Archivos tocados para esto:

- `src/AlfaCore/Services/AuditoriaExcelExporter.cs`
- `src/AlfaCore/Program.cs`
- `src/AlfaCore/Components/Pages/AuditoriaUsuarios.razor`

#### Compatibilidad SQL vieja

En `HIERROSUR` apareció error por `TRY_CONVERT`.

Se corrigió reemplazando funciones modernas por lógica compatible con SQL Server más viejo:

- sin `TRY_CONVERT`
- sin depender de `CONCAT` para ese parseo crítico

Archivo principal corregido:

- `src/AlfaCore/Services/AuditoriaService.cs`

---

## Cambios en visor de comprobantes

Se trabajó sobre:

- `src/AlfaCore/Components/Shared/ComprobanteViewer.razor`
- `src/AlfaCore/Components/Shared/ComprobanteViewer.razor.css`

Cambios hechos:

- ocultar importes en cero
- priorizar solapa inicial útil
- renombrar `Observaciones` a `Otros conceptos`
- mejorar layout de totales

Tema pendiente:

- revisar si en algunos casos CSS global sigue forzando visual vertical en ciertos navegadores o anchos específicos

---

## Cambios en sesiones SQL

Se detectó un problema probable con desaparición de conexiones en `sessions.json`.

Causa probable identificada:

- `SessionService` registrado como `Scoped`
- cada instancia carga `sessions.json` una vez
- luego guarda todo el archivo con su copia en memoria
- eso puede pisar sesiones agregadas por otra instancia o circuito

También existe fallback a regenerar desde config si el JSON falla.

Importante:

- este diagnóstico quedó identificado
- no necesariamente quedó completamente resuelto en esta etapa

Archivo a revisar si se retoma ese tema:

- `src/AlfaCore/Services/SessionService.cs`

---

## Control centralizado de backups de clientes

### Por qué

`EM_Backup.vbp` (proyecto VB6, repo `NMC_CONT_DEV`) generaba un `.SQL` que registraba
un **linked server** (`sp_addlinkedserver` a `alfanet.ddns.net`) con usuario y clave en
texto plano, para que el SQL Server de **cada cliente** escribiera directo en la base de
control central. Riesgo: credencial compartida en texto plano en disco de cada cliente,
y un linked server permanente de cada SQL Server de cliente hacia un host público.

Además, la subida del `.BAK` por FTP (`alfaftp.ddns.net`) viene fallando.

### Lo hecho hasta ahora

1. **`AlfaArchivos`** (repo aparte, `C:\Dev\AlfaArchivos`): es la alternativa HTTP al FTP
   que falla (mismo storage `E:\FTP`, mismo usuario/clave que el FTP). Se hizo hardening:
   límite de tamaño de subida (para `.BAK` grandes), fix de un bug de path traversal en
   `SafePath`, CSRF en los formularios, rate-limit en `/Login`. Compilado y probado
   (login, subida, borrado, traversal bloqueado). Commiteado en ese repo.

2. **`AlfaCore`** (este repo): se agregó el reemplazo del linked server.
   - Tabla nueva `dbo.ALFACORE_BACKUPS_CONTROL` en `ALFA_CENTRAL`
     (`docs/base-datos/sql-referencia/backups_control_modelo_inicial.sql`), vinculada
     lógicamente a `dbo.bases` (sin FK dura, tipo de `bases.id` no confirmado).
   - `ICentralBackupControlService` / `CentralBackupControlService.cs` (Dapper).
   - Endpoint `POST /api/vb6/backup-status` en `Program.cs`, protegido con header
     `X-Api-Key` contra `BackupStatus:ApiKey`.
   - De paso: la connection string de `ConnectionStrings:AlfaCentral` estaba
     hardcodeada en texto plano en `appsettings.json` (commiteada al repo). Se movió a
     `.env` (gitignored), seguido el mismo mecanismo que ya usaba el proyecto para
     `OPENAI_API_KEY` / `PushNotifications`. Se generó una `BackupStatus__ApiKey` nueva
     (random, 256 bits) también en `.env`.
   - Todo compiló limpio y pasó `python tools/catalogo/check_catalogo.py`.
   - Commit local: `621a839` — no se hizo push.

### Lo que falta (en orden)

1. **Urgente, fuera de mi alcance**: rotar la password del login `ALFA_CENTRAL` en el
   SQL Server real (`149.46.4.90`). Quedó expuesta en el historial de git (el repo tiene
   remoto en GitHub), así que sacarla de `appsettings.json` no alcanza.
2. Correr `backups_control_modelo_inicial.sql` contra `ALFA_CENTRAL`.
3. Cargar el `BackupStatus__ApiKey` real en el `.env` del servidor donde corre `AlfaCore`
   en producción (el valor generado hoy solo existe en el `.env` local de esta PC).
4. Tocar `ModBackup.bas` (`NMC_CONT_DEV`) para:
   - sacar el bloque `sp_addlinkedserver` / `sp_addlinkedsrvlogin` del `.SQL` generado
   - reemplazar la subida por FTP (`cFTP`/`mFTP`) por HTTP contra `AlfaArchivos` (login +
     `POST /upload` con token CSRF)
   - agregar el `POST /api/vb6/backup-status` al final del proceso de backup, reusando
     el patrón `WinHttp.WinHttpRequest.5.1` que ya existe en `ModAlfaCore.bas`
5. (Fase 2, no arrancada) pantalla en AlfaCore para ver el estado de backups por cliente
   y alertas de espacio en disco.

### Archivos tocados en esta etapa (además de los ya listados abajo)

- `docs/base-datos/sql-referencia/backups_control_modelo_inicial.sql` (nuevo)
- `src/AlfaCore/Models/BackupsControlModels.cs` (nuevo)
- `src/AlfaCore/Services/ICentralBackupControlService.cs` (nuevo)
- `src/AlfaCore/Services/CentralBackupControlService.cs` (nuevo)
- `src/AlfaCore/Program.cs`
- `src/AlfaCore/appsettings.json`
- `.env.example`
- `.env` (no versionado)

---

## Documentación creada o ajustada

### Manual general

- `src/AlfaCore/Docs/manual_usuario.md`

### Manual específico

- `src/AlfaCore/Docs/manual_auditoria_usuarios.md`

### Puentes en docs

- `docs/manual_usuario.md`
- `docs/manual_auditoria_usuarios.md`

---

## Archivos importantes modificados recientemente

- `src/AlfaCore/Services/AuditoriaService.cs`
- `src/AlfaCore/Models/AuditoriaModels.cs`
- `src/AlfaCore/Components/Pages/AuditoriaUsuarios.razor`
- `src/AlfaCore/Services/AuditoriaExcelExporter.cs`
- `src/AlfaCore/Program.cs`
- `src/AlfaCore/Components/Shared/ComprobanteViewer.razor`
- `src/AlfaCore/Components/Shared/ComprobanteViewer.razor.css`
- `src/AlfaCore/Docs/manual_usuario.md`
- `src/AlfaCore/Docs/manual_auditoria_usuarios.md`
- `docs/manual_usuario.md`
- `docs/manual_auditoria_usuarios.md`

---

## Próximos pasos sugeridos

### Opción 1. Ayuda contextual por módulo

Preparar `Ayuda.razor` para abrir:

- manual general por defecto
- manual de Auditoría de usuarios por `topic` específico

Ejemplo deseado:

- `/ayuda`
- `/ayuda?topic=consultas`
- `/ayuda?topic=auditoria-usuarios`

### Opción 2. Crear más manuales por módulo

Seguir el mismo criterio para:

- tareas
- conversaciones
- interfaces
- costos
- seguridad

### Opción 3. Revisar SessionService

Arreglar de forma definitiva el riesgo de sobrescritura de `sessions.json`.

### Opción 4. Revisión visual final del visor de comprobantes

Confirmar en navegador:

- distribución horizontal de totales
- solapa inicial correcta

---

## Cómo retomar rápido

Si retomás desde otra conversación, usar algo así:

```text
Leé docs/CONTINUIDAD_CODEX.md.
Quiero continuar desde ese estado.
El próximo paso es: [describir la tarea].
```

---

## Verificación usada durante esta etapa

Antes de cerrar cambios, se estuvo ejecutando:

```text
dotnet build src/AlfaCore/AlfaCore.csproj
python tools/catalogo/check_catalogo.py
```

El chequeo de catálogo debe seguir haciéndose antes de finalizar nuevas tareas.
