# Issue — Navegación y Pantallas Derivadas para Ventas, Stock, Caja y Bancos, Contabilidad

Fecha: 2026-04-24  
Estado: Pendiente  
Prioridad: Alta

---

## Objetivo

Llevar los nuevos módulos `Ventas`, `Stock`, `Caja y Bancos` y `Contabilidad` a un nivel similar al módulo `Compras`, incorporando:

- navegación lateral propia por módulo
- pantallas derivadas con drill-down
- filtros globales adaptados al dominio
- métricas ejecutivas y tablas de detalle
- consistencia visual y funcional entre módulos

La premisa no es copiar `Compras` literalmente, sino replicar el patrón de navegación y análisis respetando la lógica de cada área.

---

## Alcance esperado por módulo

### 1. Ventas

Debe quedar enfocado en circuito comercial, no en cobranzas.

#### Inicio

- KPIs comerciales:
  - facturado período
  - ticket promedio
  - comprobantes
  - clientes activos
- evolución mensual de últimos 12 meses
- top clientes
- top artículos vendidos
- últimos comprobantes

#### Pantallas derivadas sugeridas

- `Clientes`
- `Comprobantes`
- `Rubros`
- `Familias`
- `Artículos`
- `Actividad`

#### Filtros importantes

- fecha desde / hasta
- cliente
- usuario
- sucursal
- depósito
- tipo comprobante

#### Reglas de negocio

- excluir lógica de cobranzas de la pantalla principal
- restringir comprobantes a `FC`, `NC`, `ND`, `FP`
- los drill-down deben mantener filtros activos del módulo

---

### 2. Stock

Debe cubrir situación actual y análisis operativo.

#### Inicio

- KPIs:
  - stock valorizado
  - artículos con stock
  - bajo punto de pedido
  - sin stock
- evolución mensual del stock valorizado al cierre
- artículos más movidos
- artículos críticos

#### Pantallas derivadas sugeridas

- `Artículos`
- `Rubros`
- `Familias`
- `Depósitos`
- `Movimientos`
- `Análisis`

#### Filtros importantes

- fecha desde / hasta
- código artículo
- descripción artículo
- rubro
- familia
- depósito
- sucursal
- estado

#### Reglas de negocio

- el stock negativo no debe restar a la valorización
- la evolución debe reflejar saldo valorizado de cierre, no simple movimiento
- conviene evaluar vista SQL mensual para performance

---

### 3. Caja y Bancos

Debe quedar orientado a liquidez y movimientos financieros inmediatos.

#### Inicio

- KPIs:
  - saldo cajas
  - saldo bancos
  - ingresos del período
  - egresos del período
  - pendiente cobro
  - pendiente pago
- evolución financiera
- top cajas
- top cuentas bancarias

#### Pantallas derivadas sugeridas

- `Cajas`
- `Bancos`
- `Movimientos`
- `Pendientes de cobro`
- `Pendientes de pago`
- `Resumen financiero`

#### Filtros importantes

- fecha desde / hasta
- caja
- cuenta bancaria
- texto / detalle

#### Reglas de negocio

- no pedir artículos, rubros ni familias
- separar bien caja física, bancos y pendientes
- definir si los pendientes siguen en este módulo o pasan luego a una vista financiera más amplia

---

### 4. Contabilidad

Debe empezar con perfil gerencial, no técnico-contable profundo.

#### Inicio

- KPIs:
  - debe período
  - haber período
  - saldo neto
  - cantidad de asientos
- evolución mensual
- cuentas con más movimiento
- últimos asientos

#### Pantallas derivadas sugeridas

- `Cuentas`
- `Asientos`
- `Sucursales / Unidad de negocio`
- `Usuarios`
- `Resumen`

#### Filtros importantes

- fecha desde / hasta
- cuenta contable
- detalle
- usuario
- sucursal
- tipo (`D` / `H`)

#### Reglas de negocio

- primera etapa con mirada gerencial
- no cargar de entrada subdiarios, balance técnico, mayores completos ni reportes fiscales
- preparar estructura para profundización contable posterior

---

## Trabajo transversal necesario

### A. Navegación lateral por módulo

Replicar la lógica de `Compras`, pero con menú contextual para cada módulo.

Cada módulo debe tener:

- `Inicio`
- secciones derivadas del dominio
- `Ayuda`

### B. Rutas

Definir rutas consistentes para cada módulo, por ejemplo:

- `/ventas`
- `/ventas/clientes`
- `/ventas/comprobantes`
- `/stock`
- `/stock/articulos`
- `/caja-bancos`
- `/caja-bancos/movimientos`
- `/contabilidad`
- `/contabilidad/asientos`

### C. Estado de filtros

Crear persistencia de filtros por módulo, igual que ya se hizo en compras, evitando mezclar estados entre dominios.

### D. Servicios

Extender `GestionDashboardService` o separar servicios por módulo cuando el volumen crezca:

- `IVentasDashboardService`
- `IStockDashboardService`
- `ICajaBancosDashboardService`
- `IContabilidadDashboardService`

Por ahora se puede seguir en un servicio común si la complejidad sigue controlada.

### E. SQL / performance

Evaluar creación de vistas o consultas mensuales resumidas para evitar recalcular sobre grandes volúmenes en cada carga.

Prioridades probables:

- ventas mensuales
- stock valorizado mensual
- saldos consolidados de caja/bancos
- resumen contable mensual

### F. Drill-down

Los gráficos y tablas del home deben abrir pantallas filtradas del mismo módulo.

Ejemplos:

- top clientes -> `/ventas/clientes?...`
- top artículos -> `/ventas/articulos?...`
- artículos críticos -> `/stock/articulos?...`
- top bancos -> `/caja-bancos/bancos?...`
- top cuentas -> `/contabilidad/cuentas?...`

### G. Exportación

Definir si cada módulo tendrá:

- exportación PDF del inicio
- exportación Excel en pantallas de detalle

### H. Ayuda contextual

Agregar ayuda por módulo una vez que existan pantallas derivadas.

---

## Orden recomendado de implementación

### Etapa 1

- Ventas:
  - Clientes
  - Comprobantes
  - Artículos
  - Actividad

### Etapa 2

- Stock:
  - Artículos
  - Depósitos
  - Movimientos
  - Análisis

### Etapa 3

- Caja y Bancos:
  - Movimientos
  - Cajas
  - Bancos
  - Pendientes

### Etapa 4

- Contabilidad:
  - Asientos
  - Cuentas
  - Resumen por sucursal / usuario

---

## Riesgos y decisiones a validar

- definir qué pantallas deben abrirse primero por valor real de uso
- evitar copiar navegación de compras sin criterio funcional
- controlar performance en evolución mensual y acumulados
- confirmar qué objetos SQL deben quedar como fuente oficial por módulo
- decidir cuándo conviene migrar de servicio común a servicios separados

---

## Criterio de terminado

Se considera resuelto cuando:

- cada módulo tenga home + navegación lateral propia
- cada home tenga al menos 3 a 6 pantallas derivadas útiles
- los filtros sean coherentes con el dominio
- los links desde KPIs, gráficos o tablas abran análisis reales
- la experiencia sea homogénea con `Compras`, pero no forzada

