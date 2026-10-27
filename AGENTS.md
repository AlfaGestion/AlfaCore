# AGENTS.md

Este proyecto utiliza reglas obligatorias definidas en:

## Lectura obligatoria (siempre)

- /docs/CODEX_RULES.md
- /docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md
- /docs/CONFIGURACION_GLOBAL.md
- /docs/ui/alfadesign-module-guide.md (toda pantalla web nueva o rediseñada)

Estas definen:
- cómo trabajar
- qué objetos usar
- reglas críticas del sistema

---

## Lectura opcional (solo si es necesario)

- /docs/DATABASE_TABLES_SUMMARY.md

Usar únicamente cuando:
- se necesite entender una tabla específica
- haya dudas sobre la estructura de datos
- no alcance con DATABASE_OBJETOS_SQL_PRIORITARIOS.md

No cargar este archivo completo si no es necesario.

### Para ubicar o crear documentación

- /docs/README.md

Usar cuando:
- haya que buscar documentación existente
- haya que decidir dónde guardar documentación nueva
- se quiera distinguir documentación técnica, manuales de usuario y material legacy

---

## Reglas de trabajo

- Trabajar siempre sobre la base actual
- No rehacer desde cero
- No asumir estructuras no confirmadas
- Priorizar objetos definidos como “oficiales”
- Todo error relevante debe registrarse en `AUX_ERR`, usando un servicio centralizado de logging.
- Si una URL entra con `?directo=1`, AlfaCore debe quedar encerrado en ese módulo: no debe mostrar `Aplicaciones` ni accesos a otros módulos. Esta regla aplica a todos los módulos actuales y a cualquier módulo nuevo.
- **Códigos-PK de texto (ancho fijo):** al grabar un PK de texto de un maestro/tabla de referencia (TA_/V_TA_/C_TA_, artículos, vendedores, etc.) se formatea al ancho del campo con la lógica estándar del sistema: **numérico → alineado a la derecha (blancos a la izquierda); alfanumérico → a la izquierda**. Usar `AlfaCore.Common.CodigoPk.Format(codigo, ancho)`. Para comparar/leer, hacerlo siempre con `LTRIM(RTRIM(...))`. Detalle y excepción (plan de cuentas `MA_CUENTAS`, siempre a la izquierda) en `docs/DATABASE_TABLES_SUMMARY.md`.

- **Excepción obligatoria en movimientos: `TC` y `CUENTA` se graban sin relleno.** Usar `.Trim()`, nunca `CodigoPk.Format`, `PadRight` ni `PadLeft`, para el tipo de comprobante y la cuenta de cliente/proveedor/contabilidad en `V_MV_CPTE`, `MV_ASIENTOS`, `MV_APLICACION` y demás movimientos. Incluye variantes como `TC_ORIGEN`, `TCO_ORIGEN`, `TC_PRINT` y `TCORIGEN`. La regla de ancho fijo anterior corresponde a códigos maestros (unidad, moneda, vendedor, depósito, etc.), no a estos campos de movimiento. Conservar la representación de Alfa Gestión: `TC='CB'`, `CUENTA='112010681'`; verificar con `DATALENGTH` contra un registro equivalente legacy. Las comparaciones con `=` y `LEN` no sirven para detectar espacios finales; las concatenaciones sí pueden resultar distintas.

## Atajos estándar del punto de venta

En el módulo web de Punto de Venta se deben conservar estos atajos y vincularlos a la misma acción del botón correspondiente:

- `Ctrl+F`: abrir **Nuevo cliente / consulta ARCA** y enfocar CUIT/DNI.
- `Ctrl+P`: abrir **Reimprimir comprobantes**.
- `Ctrl+G`: abrir **Cobrar** desde la pantalla principal o grabar/aceptar la cobranza activa, sin imprimir.
- `Ctrl+B`: abrir la confirmación de **Vaciar carrito**.
- `Esc`: cerrar el modal o menú activo, sin guardar cambios; si no hay modal/menú y el carrito tiene datos, abrir la confirmación de **Vaciar carrito**; si el carrito está vacío, preguntar antes de cerrar el módulo.

Los atajos de consulta y grabación deben reutilizar los handlers existentes de los botones; no duplicar la lógica de negocio en JavaScript.

---

## Regla clave

Antes de usar una tabla:

1. Revisar /docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md
2. Si no alcanza → consultar /docs/DATABASE_TABLES_SUMMARY.md

---

## Regla obligatoria: ubicación de documentación

### Manuales de usuario

Los manuales funcionales o de usuario deben guardarse en:

- `src/AlfaCore/Docs/`

Ejemplos:

- `src/AlfaCore/Docs/manual_usuario.md`
- `src/AlfaCore/Docs/manual_consultas.md`
- `src/AlfaCore/Docs/manual_auditoria_usuarios.md`

En `docs/` solo pueden quedar archivos puente cortos hacia esos manuales cuando convenga tener acceso directo desde el repositorio.

### Documentación técnica

La documentación técnica del proyecto debe guardarse en `docs/`, usando estas carpetas:

- `docs/arquitectura/` → arquitectura, estándares, lineamientos de UI, notas transversales
- `docs/base-datos/` → documentación técnica de base de datos
- `docs/base-datos/sql-referencia/` → scripts SQL, vistas, modelos iniciales y consultas de referencia
- `docs/modulos/` → documentación técnica por módulo
- `docs/gestion/` → continuidad, backlog, changelog, notas de trabajo e issues
- `docs/legacy/` → material histórico, relevamientos viejos, dumps o documentos sin normalizar

### Archivos que deben permanecer en `docs/` raíz

No mover sin antes actualizar referencias y herramientas:

- `docs/CODEX_RULES.md`
- `docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md`
- `docs/CONFIGURACION_GLOBAL.md`
- `docs/DATABASE_TABLES_SUMMARY.md`
- `docs/CATALOGO_RUTINAS.md`
- `docs/README.md`

### Convención para documentación nueva

Antes de crear un documento nuevo:

1. Revisar `docs/README.md`
2. Elegir la carpeta temática correcta
3. Evitar duplicar contenido ya existente
4. Si es un manual de usuario, guardarlo en `src/AlfaCore/Docs/`
5. Si hace falta acceso rápido desde `docs/`, crear un archivo puente corto en lugar de duplicar el contenido

---

## Regla obligatoria: shell AlfaDesign para módulos nuevos (menú superior, sin sidebar)

Toda pantalla web nueva **debe** usar el shell AlfaDesign (barra superior global + Context Toolbar
propio del módulo) y **nunca** el sidebar lateral legacy. No es opcional ni "a criterio": es la
norma vigente para código nuevo.

### Cómo activarlo (2 pasos, los dos son obligatorios)

1. En la página/componente, inyectar `IPageHeaderService` e implementar `IDisposable`:
   ```csharp
   protected override void OnInitialized() => UpdatePageHeader();
   protected override void OnAfterRender(bool firstRender) { if (firstRender) UpdatePageHeader(); }
   public void Dispose() => PageHeader.Clear();

   private void UpdatePageHeader() => PageHeader.Set(new PageHeaderConfig
   {
       ShellMode = PageHeaderShellMode.AlfaDesignPilot,
       // Title, Breadcrumb, TopNavigationItems, Search/SearchContent, Actions, Pagination...
   });
   ```
2. **Paso que se olvida fácil y rompe todo en silencio**: agregar el primer segmento de la ruta
   (`_currentModule`, ej. `"articulos"` para `/articulos`) al `HashSet<string> AlfaDesignManagedModules`
   en `src/AlfaCore/Components/Layout/MainLayout.razor` (~línea 1511). Si falta este paso, el llamado a
   `PageHeader.Set(...)` no tira ningún error, pero `MainLayout` sigue mostrando el sidebar legacy
   genérico y **nunca** renderiza el topbar/toolbar/búsqueda del `PageHeaderConfig` — el síntoma es
   "la pantalla no tiene menú superior ni botones, y el sidebar no corresponde al módulo".
3. **Segundo paso, también obligatorio**: setear `ModuleKey = "<módulo>"` (mismo valor de arriba) en
   CADA llamada a `PageHeader.Set(...)` de la página. Sin esto, navegar desde OTRO módulo
   AlfaDesignPilot (ej. Clientes) hacia el nuevo puede dejar el topbar/las acciones (Guardar/Cancelar)
   pegadas a la página anterior -- `MainLayout` sólo limpia el header viejo entre dos módulos
   AlfaDesignPilot distintos si ambos declaran `ModuleKey` y no coincide. El síntoma es sutil: la
   pantalla nueva carga bien, pero el título/tab superior sigue diciendo el módulo anterior y los
   botones del header no hacen nada (llaman a una instancia ya dispuesta).

Ver `docs/ui/alfadesign-module-guide.md` para el resto del proceso (Smart Search, Data View,
column sizing, etc.) y páginas ya migradas (`Clientes.razor`/`CuentasComercialesPage.razor`,
`ConfiguracionGeneralEmail.razor` para el caso simple sin lista) como referencia de estructura.

---

## Regla obligatoria: script de actualización para módulos nuevos

Todo módulo nuevo que agregue una pantalla web accesible desde el menú **debe incluir un script SQL de actualización** en `src/AlfaCore/App_Data/updates/`.

### Convención de nombre

```text
AAAA-MM-DD-NNN__<area>_<modulo>_menu_web.sql
```

Ejemplo: `2026-06-08-001__compras_reporte_compras_menu_web.sql`

### Menú y permisos web independientes del legacy (obligatorio)

El menú y los permisos de AlfaCore viven en **sus propias tablas**: `ALFACORE_MENU_WEB` (opciones,
padre, nombre, ruta, orden) y `ALFACORE_TAREAS_WEB` (permisos). El menú y los permisos del sistema
legacy (Alfa Gestión VB6) son `TA_MENU` y `TA_TAREAS`, y **AlfaCore no los modifica nunca**: ni
INSERT, ni UPDATE (tampoco `DESCRIPCION`), ni DELETE.

Motivo: los scripts viejos insertaban cada opción web también en `TA_MENU` y se la daban a todos en
`TA_TAREAS`; aparecían en el menú y en la Autorización de Tareas del legacy sin que nadie las pidiera.
Las opciones de AlfaCore que se quieran usar desde el legacy las agrega a mano el responsable del
legacy, con su propia clave y el sufijo " (web)".

Protección automática: `LegacyTablesUpdateGuard` (lo usan `ActualizacionesService` y
`CentralProvisioningService`) saca una foto de `TA_MENU` y `TA_TAREAS` antes de correr los scripts y,
al terminar la tanda (aunque falle), las deja como estaban. Los permisos que un script intente dar en `TA_TAREAS` los
pasa a `ALFACORE_TAREAS_WEB` (solo a usuarios que ya tienen permisos web explícitos) y lo deja anotado
en la auditoría (`Actualizaciones` / `ProtegerLegacy`). Esa protección es una red de seguridad: los scripts nuevos igual tienen
que respetar esta regla.

### Estructura obligatoria del script (5 pasos, en este orden)

1. **Guardia** — si no existe `ALFACORE_MENU_WEB`, hacer `RETURN`
2. **Columnas nuevas de `ALFACORE_MENU_WEB`** — agregar si no existen en bases antiguas (`NombreWeb`, etc.)
3. **INSERT en `ALFACORE_MENU_WEB`** — idempotente, solo si la clave no existe aún. Con `VALUES`
   (`Menu = N'ALFA'`, `PadreClave` del módulo web): **no** leer la fila de `TA_MENU` (`SELECT ... FROM dbo.TA_MENU`)
4. **UPDATE en `ALFACORE_MENU_WEB`** — actualiza ruta, icono y nombre si la fila ya existía
5. **Permisos en `ALFACORE_TAREAS_WEB`** — `INSERT` solo para usuarios que ya tienen filas explícitas
   ahí (usuarios con restricciones web); los usuarios sin filas ya ven todo el menú web

### Reglas del script

- Debe ser **idempotente**: puede ejecutarse varias veces sin romper datos
- **Nunca** escribir en `TA_MENU` ni en `TA_TAREAS` (ver arriba)
- Para una clave web nueva usar un prefijo propio que no exista en el legacy (por ejemplo
  `D75-CONCILIA-IA`, `D60RPTWEB`): las claves numéricas del legacy pueden estar usadas por otro
  programa en alguna base de cliente
- El paso 5 usa el patrón:

```sql
INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
SELECT DISTINCT LTRIM(RTRIM(t.Usuario)), LTRIM(RTRIM(t.Sistema)), N'<CLAVE>', GETDATE(), N'SYSTEM'
FROM dbo.ALFACORE_TAREAS_WEB t
WHERE ISNULL(t.Clave, N'') <> N''
  AND NOT EXISTS (
      SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
        AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'<CLAVE>'
  );
```

### Referencia

Los scripts anteriores al 27/10/2026 (por ejemplo `2026-05-30-001__ventas_punto_venta_menu_web.sql`)
todavía escriben en `TA_MENU` / `TA_TAREAS`: **no copiarlos como modelo** en esa parte. Sus cambios
sobre el legacy los revierte `LegacyTablesUpdateGuard`.

---

## Regla obligatoria: encoding de archivos fuente

Todos los archivos fuente (`.cs`, `.razor`, `.ts`, `.js`, `.json`, `.sql`, `.md`) deben guardarse en **UTF-8 sin BOM**.

### Caracteres especiales en literales de texto

- Usar siempre los caracteres Unicode correctos: `á é í ó ú ü ñ Á É Í Ó Ú Ü Ñ ¿ ¡`
- **Nunca** escribir las secuencias mojibake (UTF-8 interpretado como Latin-1), como:
  - `Ã¡` en lugar de `á`
  - `Ã©` en lugar de `é`
  - `Ã³` en lugar de `ó`
  - `Ã±` en lugar de `ñ`
  - `Ã¢â‚¬â„¢` en lugar de `'`, etc.
- Si al leer un archivo ya existente se detectan secuencias mojibake, **corregirlas** antes de continuar editando.

### Cómo verificar

Si un string en el código muestra `Ã` seguido de una letra, es mojibake. Tabla de sustitución frecuente:

| Mojibake | Correcto |
|----------|----------|
| `Ã¡` | `á` |
| `Ã©` | `é` |
| `Ã­` | `í` |
| `Ã³` | `ó` |
| `Ãº` | `ú` |
| `Ã±` | `ñ` |
| `Ã¿` | `ÿ` |

---

## Verificación asistida del catálogo

Antes de finalizar una tarea, ejecutar:

```bash
python tools/catalogo/check_catalogo.py
```
