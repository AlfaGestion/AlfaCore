# Manual — Diseñador de Consultas
### Alfa Gestión · Dashboard de Compras

---

## Índice

1. [¿Qué es el Diseñador de Consultas?](#1-qu-es-el-diseador-de-consultas)
2. [Árbol de consultas](#2-rbol-de-consultas)
3. [Ejecutar una consulta](#3-ejecutar-una-consulta)
4. [Trabajar con el resultado](#4-trabajar-con-el-resultado)
5. [Crear una nueva consulta](#5-crear-una-nueva-consulta)
6. [Constructor visual](#6-constructor-visual)
7. [Editar y eliminar consultas](#7-editar-y-eliminar-consultas)
8. [Duplicar una consulta](#8-duplicar-una-consulta)
9. [Gestión de sesiones de base de datos](#9-gestin-de-sesiones-de-base-de-datos)
10. [Exportar resultados](#10-exportar-resultados)
11. [Gráficos de resultados](#11-grficos-de-resultados)
12. [Referencia técnica — campo CLAVE](#12-referencia-tcnica--campo-clave)
13. [Preguntas frecuentes](#13-preguntas-frecuentes)

---

## 1. ¿Qué es el Diseñador de Consultas?

El **Diseñador de Consultas** permite crear, organizar, ejecutar y exportar consultas SQL sobre la base de datos de Alfa Gestión directamente desde el navegador, sin necesidad de herramientas externas ni del módulo VB6.

Las consultas se almacenan en la tabla `V_TA_SCRIPT` con la marca `CL` y son accesibles para todos los usuarios del dashboard.

**Casos de uso típicos:**
- Consultar stock, precios o movimientos con filtros personalizados.
- Generar listados para exportar a Excel.
- Crear informes ad-hoc con parámetros ingresados por el usuario en tiempo de ejecución.
- Organizar consultas en una jerarquía de carpetas por área o temática.

---

## 2. Árbol de consultas

Al ingresar a **Mis consultas** verás un árbol jerárquico en el panel izquierdo.

### Estructura jerárquica

Las consultas se organizan por el campo **CLAVE**, que funciona como un código de posición: cada 2 dígitos representan un nivel.

| CLAVE | Nivel | Descripción |
|-------|-------|-------------|
| `10` | Raíz | Categoría principal |
| `1001` | 2° nivel | Subcategoría dentro de `10` |
| `100101` | 3° nivel | Ítem dentro de `1001` |

Los nodos que tienen hijos se muestran con un ícono **›** para colapsar/expandir. Los nodos hoja (consultas ejecutables) muestran su CLAVE y nombre.

### Íconos de referencia

- **›** / **˅** — Grupo expandible / colapsado. Hacer clic para alternar.
- **⊞** — Consulta con parámetros requeridos (el usuario debe completar valores antes de ejecutar).

### Buscador del árbol

El campo de búsqueda superior filtra en todos los niveles simultáneamente por **nombre** o **código CLAVE**. Al borrar la búsqueda se restaura el árbol completo.

---

## 3. Ejecutar una consulta

1. Hacer clic en el nombre de la consulta en el árbol → se abre la página de detalle.
2. Si la consulta **no tiene parámetros**, se ejecuta automáticamente al abrir.
3. Si tiene **parámetros**, aparece un formulario con los campos a completar. Los tipos se detectan automáticamente:
   - Campos con "fecha", "desde", "hasta" → selector de fecha.
   - Campos con "nro", "importe", "cantidad" → campo numérico.
   - Resto → texto libre.
4. Hacer clic en **Ejecutar**.

### Información del resultado

Una vez ejecutada, se muestran:
- Cantidad de filas totales leídas.
- Tiempo de ejecución en segundos.
- Hora de ejecución.
- Aviso si se superaron las 500 filas (en ese caso, usar Excel para ver el resultado completo).

---

## 4. Trabajar con el resultado

### Buscador en el resultado

El campo **"Buscar en el resultado…"** permite filtrar las filas visibles buscando texto en **todas las columnas** a la vez.

- Escribir el texto y presionar **Enter** para aplicar el filtro.
- Presionar **Escape** o hacer clic en **×** para limpiar.
- El contador muestra cuántas filas quedaron tras el filtro.
- La paginación trabaja sobre las filas filtradas.

### Ordenar por columna

Hacer clic en el encabezado de cualquier columna para ordenar:
- **1er clic** → orden ascendente (A→Z, menor→mayor).
- **2do clic** → orden descendente.
- **3er clic** → vuelve al orden original.

El ícono ↕ / ↑ / ↓ en el encabezado indica el estado activo.

### Paginación

Cuando hay más filas que las visibles por página, aparecen controles de paginación:
- **«** / **»** — Primera / última página.
- **‹** / **›** — Página anterior / siguiente.
- Números de página con ventana deslizante.
- Selector de filas por página: 25 / 50 / 100 / 200.

---

## 5. Crear una nueva consulta

Hacer clic en **+ Nueva consulta** (botón en la cabecera o en el sidebar).

### Datos básicos

| Campo | Descripción |
|-------|-------------|
| **Ubicación en el árbol** | Selector del nodo padre. Define la jerarquía. Al elegir, la CLAVE se sugiere automáticamente. |
| **Clave** | Código de posición en el árbol (ej: `1001`). Se autocomputa al elegir la ubicación. Editable. |
| **Nombre** | Texto que aparece en el árbol como etiqueta del nodo. Campo obligatorio. |
| **Descripción / Ayuda** | Texto de ayuda visible al usuario al ejecutar la consulta. Opcional. |

### Clave auto-sugerida

Al seleccionar la **ubicación** (nodo padre), el sistema sugiere automáticamente la próxima CLAVE disponible dentro de ese nivel. Por ejemplo, si el padre es `10` y ya existen `1001` y `1002`, sugiere `1003`.

Se puede cambiar manualmente si se necesita una clave específica.

### SQL

La consulta SQL se escribe en el área de texto. Solo se permiten bloques de lectura. Podés usar un `SELECT` directo o preparar variables locales con `DECLARE` / `SET` antes del `SELECT` final. Las instrucciones de escritura (`INSERT`, `UPDATE`, `DELETE`, `DROP`, etc.) son rechazadas por el sistema.

### Parámetros en el SQL

Usá el token `<P>` dentro del SQL para indicar que el usuario debe completar ese valor al ejecutar:

```sql
SELECT *
FROM V_CM_COMPRAS
WHERE FECHA_COMPROBANTE >= <P>
  AND FECHA_COMPROBANTE <= <P>
  AND PROVEEDOR = <P>
```

El sistema detecta automáticamente cuántos `<P>` hay y muestra un campo de etiqueta para cada uno. Esa etiqueta es el texto que verá el usuario como label del campo.

### Metadatos adicionales

En la sección colapsable **Metadatos adicionales** podés completar:

| Campo | Uso |
|-------|-----|
| **Tabla principal** | Nombre de la vista o tabla base de la consulta. |
| **Campos a totalizar** | Nombres de columnas numéricas separados por coma. Se usan para preseleccionar el eje Y en el gráfico automáticamente. |
| **Orden por defecto** | Cláusula ORDER BY preferida para esta consulta. |

---

## 6. Constructor visual

El **Constructor visual** es una herramienta opcional que genera el SQL automáticamente a partir de una selección visual de tabla, columnas, filtros y orden.

### Paso a paso

1. **Buscar tabla o vista**: escribí el nombre (o parte) en el campo de búsqueda y presioná Enter o el botón **Buscar**. El sistema consulta `INFORMATION_SCHEMA` de la base activa y muestra hasta 60 resultados.
2. **Seleccionar fuente**: elegí la tabla o vista del dropdown.
3. **Columnas**: se muestran todas las columnas disponibles. Tildá las que querés incluir en el SELECT. El buscador de columnas (esquina superior derecha de la grilla) resalta las que coinciden con el texto ingresado sin ocultar las demás.
4. **Filtros (WHERE)**: hacé clic en [[bi:plus-circle]] **Agregar filtro** para cada condición. Podés marcar **Parámetro `<P>`** para que el valor lo ingrese el usuario al ejecutar.
5. **Ordenar por**: agregá columnas para el ORDER BY con dirección ASC o DESC.
6. [[bi:code-slash]] **Generar SQL**: el sistema arma el SELECT completo y lo pega en el área de SQL.

> El SQL generado es editable. Podés ajustarlo manualmente después de generarlo.

---

## 7. Editar y eliminar consultas

Desde la página de detalle de cualquier consulta, hacé clic en [[bi:pencil]] **Editar** (botón en la cabecera).

En el editor podés modificar todos los campos. Al guardar, los cambios se aplican inmediatamente.

### Eliminar

En la parte inferior del editor aparece el botón **Eliminar**. Requiere confirmación explícita. Esta acción **no tiene marcha atrás**: elimina la consulta y todos sus parámetros asociados de la base de datos.

---

## 8. Duplicar una consulta

Desde la página de detalle, hacé clic en [[bi:copy]] **Duplicar**. Se abre el editor de nueva consulta con todos los campos pre-completados (nombre con prefijo "Copia de…", descripción, SQL y metadatos).

Solo necesitás:
1. Elegir la **ubicación** en el árbol (nodo padre).
2. Confirmar o ajustar la **CLAVE** sugerida.
3. Cambiar el **nombre** si es necesario.
4. Hacer clic en **Guardar consulta**.

---

## 9. Gestión de sesiones de base de datos

El **chip de sesión** en la cabecera muestra el servidor y base de datos activos (ej: `AGSERVER · ALFANET`).

Hacer clic en el chip abre el **panel lateral de sesiones**:

### Cambiar de sesión

Hacé clic en **Conectar** junto a la sesión deseada. El sistema cambia la conexión activa inmediatamente y recarga el árbol de consultas con los datos de la nueva base.

### Agregar una sesión nueva

En la parte inferior del panel, expandí el formulario "Nueva sesión" y completá:
- **Nombre**: etiqueta identificatoria (ej: "Producción", "Prueba").
- **Servidor**: nombre o IP del servidor SQL.
- **Base de datos**: nombre de la base.
- **Usuario** y **Contraseña**: credenciales de acceso.

Las sesiones se guardan en el archivo `App_Data/sessions.json` de la aplicación.

### Eliminar una sesión

Hacé clic en el ícono de papelera junto a la sesión que querés borrar. No se puede eliminar la sesión activa.

---

## 10. Exportar resultados

### Excel

El botón [[bi:file-earmark-excel]] **Excel** descarga el resultado completo (sin límite de filas) en formato `.xlsx` con formato de tabla.

> Si el resultado fue filtrado con el buscador, Excel descarga **todas las filas** de la consulta, no solo las filtradas.

### PDF

El botón [[bi:printer]] **PDF** abre el diálogo de impresión del navegador. Recomendamos:
- Orientación **horizontal** para tablas anchas.
- Escala al 80% si las columnas no entran en la página.

---

## 11. Gráficos de resultados

Después de ejecutar una consulta, aparece el botón **Ver gráfico** junto a Excel y PDF.

### Tipos de gráfico

| Tipo | Cuándo usar |
|------|-------------|
| **Barras** | Comparar categorías (ej: ventas por proveedor). |
| **Líneas** | Ver evolución en el tiempo (ej: importes por mes). |

### Configuración

- **Eje X**: elegí la columna que actúa como etiqueta (generalmente texto: nombre, código, fecha).
- **Eje Y**: tildá una o más columnas numéricas a graficar.

Si el campo **Campos a totalizar** está configurado en la consulta, las columnas correspondientes se preseleccionan automáticamente en el eje Y.

### Límite de puntos

El gráfico muestra hasta **60 puntos**. Si hay más filas, se informa y se grafican las primeras 60. Para conjuntos grandes, filtrá el resultado con el buscador antes de activar el gráfico.

---

## 12. Referencia técnica — campo CLAVE

La CLAVE define la posición de cada nodo en la jerarquía del árbol. Reglas:

- Máximo **6 niveles**, **2 dígitos por nivel**.
- La CLAVE de un nodo debe **comenzar** con la CLAVE de su padre.
- Ejemplo válido: raíz `10` → hijo `1001` → nieto `100101`.
- El sistema sugiere automáticamente la siguiente CLAVE disponible al crear un nodo.

### Estructura recomendada

```
10          ← Categoría: COMPRAS
  1001      ← Subcategoría: Proveedores
    100101  ← Consulta: Ranking de proveedores
    100102  ← Consulta: Proveedores sin compras
  1002      ← Subcategoría: Artículos
    100201  ← Consulta: Stock actual
20          ← Categoría: VENTAS
  2001      ← Consulta: Ventas del mes
```

---

## 13. Preguntas frecuentes

### ¿Qué tipos de SQL están permitidos?

Se aceptan consultas de lectura que devuelvan resultados con `SELECT`. También podés usar `WITH` para CTEs y preparar variables locales con `DECLARE` y `SET` antes del `SELECT`. El sistema rechaza `INSERT`, `UPDATE`, `DELETE`, `DROP`, `EXEC`, `TRUNCATE` y variantes.

### ¿La consulta tarda mucho y no termina?

El tiempo máximo de ejecución es de **60 segundos**. Si se supera, aparece un mensaje de error. Soluciones:
- Agregar filtros de fecha o de proveedor para reducir el volumen.
- Verificar que las columnas usadas en WHERE estén indexadas.
- Consultar al administrador de base de datos.

### ¿Puedo usar JOINs y subconsultas en el SQL?

Sí. El validador permite consultas de solo lectura para SQL Server, incluyendo `JOIN`, subconsultas, CTEs y variables locales (`DECLARE` / `SET`) siempre que el bloque termine resolviendo un `SELECT`.

### ¿Los cambios en las consultas afectan a otros usuarios?

Sí. Las consultas se guardan en la base de datos compartida. Cualquier cambio (editar, eliminar, crear) es inmediatamente visible para todos los usuarios del dashboard.

### ¿Cómo accedo a datos de otro período o empresa?

Desde el chip de sesión en la cabecera, podés cambiar a una sesión conectada a otra base de datos. El árbol se recarga automáticamente con las consultas de esa base.

### ¿Qué pasa si dos consultas tienen la misma CLAVE?

La base de datos no tiene restricción de unicidad en CLAVE. Si ocurre, ambas aparecen en el árbol en la misma posición. Se recomienda usar siempre la sugerencia automática para evitar duplicados.
