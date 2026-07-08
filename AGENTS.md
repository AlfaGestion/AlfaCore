# AGENTS.md

Este proyecto utiliza reglas obligatorias definidas en:

## Lectura obligatoria (siempre)

- /docs/CODEX_RULES.md
- /docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md
- /docs/CONFIGURACION_GLOBAL.md

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

## Regla obligatoria: script de actualización para módulos nuevos

Todo módulo nuevo que agregue una pantalla web accesible desde el menú **debe incluir un script SQL de actualización** en `src/AlfaCore/App_Data/updates/`.

### Convención de nombre

```text
AAAA-MM-DD-NNN__<area>_<modulo>_menu_web.sql
```

Ejemplo: `2026-06-08-001__compras_reporte_compras_menu_web.sql`

### Estructura obligatoria del script (6 pasos, en este orden)

1. **Guardia** — si no existe `ALFACORE_MENU_WEB`, hacer `RETURN`
2. **Columna NombreWeb** — agregar si no existe en bases antiguas
3. **INSERT en `ALFACORE_MENU_WEB`** — idempotente, solo si la clave no existe aún
4. **UPDATE en `ALFACORE_MENU_WEB`** — actualiza ruta, icono y nombre si la fila ya existía
5. **Descripción en `TA_MENU`** — actualizar solo si la columna existe y el campo está vacío
6. **Permisos en `TA_TAREAS`** — `INSERT` para usuarios con filas explícitas (usuarios con restricciones activas); los usuarios sin filas ya tienen acceso irrestricto por política del sistema

### Reglas del script

- Debe ser **idempotente**: puede ejecutarse varias veces sin romper datos
- **No tocar `TA_MENU`** si la clave ya existe en el árbol legacy (confirmar con el usuario primero)
- Si la clave **no existe** en `TA_MENU`, agregarla dentro del mismo script con guardia de existencia
- El paso 6 usa el patrón:

```sql
INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
SELECT DISTINCT t.USUARIO, t.SISTEMA, N'<CLAVE>'
FROM dbo.TA_TAREAS t
WHERE ISNULL(t.TAREA, N'') <> N''
  AND NOT EXISTS (
      SELECT 1 FROM dbo.TA_TAREAS x
      WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
        AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
        AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'<CLAVE>'
  );
```

### Referencia

Ver ejemplos reales en:
- `2026-05-26-002__tecnicos_menu_web_y_permisos.sql` (clave legacy existente, sin tocar `TA_MENU`)
- `2026-05-30-001__ventas_punto_venta_menu_web.sql` (clave nueva, incluye `INSERT` en `TA_MENU`)

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
