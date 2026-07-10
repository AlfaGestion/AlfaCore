Generá un MVP en Blazor Server para “Dashboard de Compras - Alfa Gestión”.

Usá SQL Server y consumí solo estas vistas:
- vw_compras_cabecera_dashboard
- vw_compras_detalle_dashboard

En esta primera etapa quiero únicamente:
- estructura del proyecto
- layout general
- menú lateral
- conexión a base de datos
- servicio de acceso a datos
- página Inicio con KPIs
- gráficos de evolución mensual, top proveedores y top rubros
- página Comprobantes con grilla filtrable

La interfaz debe estar en español y tener diseño moderno tipo dashboard BI.

Entregá el código completo archivo por archivo, indicando dónde va cada archivo.


# Prompt de continuación - Dashboard de Compras Alfa Gestión

## Contexto
Ya existe una primera iteración del proyecto generada a partir de un prompt inicial corto.

La app es un **Dashboard de Compras - Alfa Gestión**, construido sobre:

- SQL Server
- Blazor Server
- vistas SQL:
  - `vw_compras_cabecera_dashboard`
  - `vw_compras_detalle_dashboard`

El objetivo ahora es **continuar y mejorar** lo ya generado, sin rehacer innecesariamente la base existente.

---

## Instrucción principal para Codex / GPT

Quiero que continúes sobre el proyecto ya existente.

**No rehagas la solución desde cero.**
Primero analizá la estructura actual del proyecto, entendé qué partes ya están implementadas y luego avanzá sobre esa base.

## Objetivo
Completar y profesionalizar la app web **Dashboard de Compras - Alfa Gestión**, manteniendo coherencia con lo ya construido.

---

## Reglas de trabajo

1. **No reemplazar innecesariamente** lo que ya funciona.
2. **Reutilizar** componentes, servicios y estructura existentes siempre que tenga sentido.
3. Si detectás problemas de diseño, podés refactorizar, pero:
   - explicá qué cambiás
   - justificá por qué
   - no rompas funcionalidad existente
4. Toda la interfaz debe estar en **español**.
5. El diseño debe ser **moderno, limpio y tipo dashboard BI**.
6. Priorizá un resultado **ejecutable, mantenible y escalable**.
7. Si falta algún detalle menor, asumí una solución razonable y seguí avanzando.
8. Mostrá los cambios de forma concreta, archivo por archivo.

---

## Base de datos
La app debe consumir exclusivamente estas vistas:

- `vw_compras_cabecera_dashboard`
- `vw_compras_detalle_dashboard`

No generar lógica de negocio duplicada si ya está resuelta en las vistas.

---

## Funcionalidad ya prevista del sistema

### Tipos de comprobantes que suman
- `FCC`
- `NDC`
- `LIQC`
- `FPC`

### Tipos de comprobantes que restan
- `NCC`
- `NCPC`

El dato de si pasa o no por Libro IVA Compras es solo informativo en esta etapa.

---

## Lo que necesito que hagas ahora

### Etapa 1 - Revisión de lo existente
Primero:

- inspeccioná la estructura actual del proyecto
- indicá qué ya está implementado
- detectá faltantes
- detectá posibles mejoras de arquitectura

Luego continuá con la implementación.

---

## Etapa 2 - Completar módulos principales

### 1. Página Inicio / Resumen General
Verificar y completar si falta algo.

Debe incluir:
- Total comprado
- Cantidad de comprobantes
- Cantidad de proveedores
- Ticket promedio
- Neto
- IVA
- Evolución mensual
- Top proveedores
- Top rubros
- Top artículos

También debe permitir drill down hacia otras pantallas.

---

### 2. Página Proveedores
Implementar o completar:

- listado de proveedores
- total comprado
- cantidad de comprobantes
- ticket promedio
- última compra
- filtros
- acceso a detalle
- drill down a comprobantes y artículos

---

### 3. Página Comprobantes
Implementar o completar:

- grilla filtrable
- columnas:
  - fecha
  - TC
  - número
  - proveedor
  - neto
  - IVA
  - total
  - usuario
  - sucursal
  - depósito
  - estado
- vista de detalle del comprobante
- detalle de artículos asociados

---

### 4. Página Rubros
Agregar:

- total por rubro
- participación porcentual
- evolución
- top artículos del rubro
- navegación al detalle

---

### 5. Página Familias
Agregar:

- total por familia
- participación
- artículos asociados
- proveedores relacionados

---

### 6. Página Artículos
Agregar:

- código de artículo
- descripción
- cantidad comprada
- total comprado
- costo promedio
- evolución del costo
- proveedores asociados
- historial

---

### 7. Página Recepción / Estado
Agregar:

- comprobantes pendientes
- parciales
- aprobados
- finalizados
- cerrados
- indicadores del estado del proceso

---

## Componentes reutilizables esperados

Verificar si existen y, si no, crearlos:

- componente de filtros globales
- componente de tarjetas KPI
- componente de tabla reutilizable
- componente de gráficos reutilizable
- layout general con menú lateral
- barra superior de filtros rápidos

---

## Filtros globales
La app debe soportar filtros por:

- fecha desde
- fecha hasta
- proveedor
- artículo
- rubro
- familia
- usuario
- sucursal
- depósito
- estado
- TC

Agregar también accesos rápidos:
- hoy
- esta semana
- este mes
- mes anterior
- últimos 3 meses
- año actual

---

## Requisitos técnicos
Quiero que el proyecto quede bien organizado en capas o secciones equivalentes.

Debe incluir, si todavía no existe:
- modelos
- servicios
- repositorios o acceso a datos
- componentes
- páginas
- layout
- configuración de conexión a SQL Server

Usar:
- `async/await`
- nombres claros
- separación de responsabilidades
- código mantenible

---

## Diseño visual
Quiero un estilo:

- profesional
- claro
- simple
- moderno
- orientado a BI

Estructura visual:
- menú lateral izquierdo
- barra superior
- KPIs arriba
- gráficos al centro
- tablas debajo

---

## Forma de entrega
Quiero que trabajes en este orden:

1. revisar lo existente
2. resumir el estado actual
3. proponer ajustes si hacen falta
4. implementar los faltantes
5. mostrar archivos nuevos o modificados
6. explicar brevemente cada cambio importante

---

## Restricción importante
No me des solo recomendaciones teóricas.
Quiero implementación concreta sobre el proyecto actual.

---

## Resultado esperado
Quiero terminar con una app funcional, bien organizada, conectada a SQL Server, que use las vistas ya definidas y permita analizar compras de forma visual, rápida y con drill down.