# Prompt para Codex - Nueva opción InformesIA

## Contexto
Estoy trabajando en una app llamada **Dashboard de Compras - Alfa Gestión**, hecha en:

- ASP.NET Core
- Blazor Server
- SQL Server

La app ya tiene estas secciones:
- Inicio
- Proveedores
- Comprobantes
- Rubros
- Familias
- Artículos
- Actividad

Quiero agregar una nueva opción al menú llamada:

👉 **InformesIA**

---

## Objetivo
Quiero una pantalla **minimalista** que permita al usuario pedir informes en lenguaje natural, usando únicamente la información disponible en las vistas ya creadas.

La idea es que el usuario escriba una consulta libre, por ejemplo:
- "mostrame los proveedores que más crecieron este mes"
- "compará rubros contra el período anterior"
- "qué artículos aumentaron más"
- "qué familias concentran el gasto"
- "qué usuarios cargaron más comprobantes"

El sistema debe:
1. interpretar la consulta
2. generar una consulta segura
3. ejecutar solo lectura
4. mostrar el resultado en una tabla
5. opcionalmente mostrar un gráfico si corresponde
6. mostrar sugerencias de preguntas útiles

---

## Restricciones muy importantes
Esta funcionalidad debe estar **estrictamente limitada**.

### Solo se puede consultar estas vistas:
- `vw_compras_cabecera_dashboard`
- `vw_compras_detalle_dashboard`
- `vw_estadisticas_ingresos_diarias`
- `vw_familias_jerarquia`

### No se puede usar nada fuera de eso
- no otras tablas
- no otras vistas
- no stored procedures
- no funciones peligrosas
- no SQL dinámico sin validación

### Solo lectura
La funcionalidad debe permitir únicamente:
- `SELECT`

Debe bloquear:
- `INSERT`
- `UPDATE`
- `DELETE`
- `DROP`
- `ALTER`
- `TRUNCATE`
- `EXEC`
- `MERGE`
- `CREATE`
- `sp_`
- comentarios maliciosos
- múltiples sentencias
- cualquier comando peligroso o no permitido

### Seguridad adicional
- limitar cantidad máxima de filas devueltas
- usar timeout razonable
- validar SQL antes de ejecutar
- si la consulta no es segura, rechazarla con mensaje claro
- si la IA propone algo fuera de las vistas permitidas, rechazarlo

---

## Enfoque funcional
No quiero un chat complejo.

Quiero una pantalla simple, minimalista y útil.

### Debe incluir:
1. un cuadro de texto para consulta libre
2. un botón "Generar informe"
3. un bloque de sugerencias de preguntas
4. una tabla con resultados
5. opcionalmente un gráfico si el resultado lo amerita
6. un resumen corto en lenguaje natural, si se puede
7. mensaje claro si no hay datos o si la consulta no es válida

---

## UX esperada
La pantalla debe verse moderna pero simple.

### Estructura sugerida:
- título: Informes IA
- subtítulo explicando que trabaja solo sobre información del dashboard
- caja de consulta libre
- botones de sugerencias rápidas
- botón de ejecutar
- resultado abajo en tabla
- gráfico opcional
- mensajes de validación y seguridad claros

---

## Comportamiento esperado
### Cuando el usuario pregunta algo:
El sistema debe intentar clasificar la intención.

### Casos ideales:
- ranking
- comparaciones
- variaciones
- evolución temporal
- concentraciones
- top/bottom
- resúmenes por proveedor, rubro, familia, artículo, usuario, comprobante

### Si la consulta puede resolverse:
- generar consulta segura
- ejecutarla
- mostrar tabla
- si corresponde, sugerir o generar gráfico

### Si no puede resolverse:
- devolver mensaje claro
- ofrecer reformular
- mostrar sugerencias relacionadas

---

## Recomendación de implementación
Prefiero una solución en dos capas:

### Capa 1 - Interpretación
Interpretar la intención del usuario y traducirla a una consulta segura o a una plantilla SQL controlada.

### Capa 2 - Validación y ejecución
Antes de ejecutar:
- validar que solo haya SELECT
- validar que solo use las vistas permitidas
- bloquear palabras peligrosas
- limitar rows
- ejecutar con seguridad

---

## Alcance técnico esperado
Quiero que implementes esto dentro del proyecto actual.

### Espero que agregues:
- nueva página o componente `InformesIA`
- modelos / DTOs necesarios
- servicio específico para Informes IA
- lógica de validación SQL
- lógica de sugerencias
- tabla de resultados reusable o genérica
- gráfico opcional reusable si es posible
- integración con el layout/menu existente

---

## Sugerencias de preguntas
Quiero que la pantalla tenga sugerencias listas para tocar/clickear, por ejemplo:
- Proveedores con mayor crecimiento en el período
- Artículos con mayor aumento
- Rubros con más participación
- Familias que más crecieron
- Comprobantes con mayor importe
- Usuarios con más actividad
- Concentración del gasto por proveedor
- Evolución de compras por mes

---

## Gráficos opcionales
Si la consulta devuelve información apta para gráfico, mostrar uno simple.

Por ejemplo:
- barras para rankings
- líneas para evolución temporal
- torta o barras para concentración

Si no corresponde gráfico, mostrar solo tabla.

---

## Mensajes importantes para el usuario
La pantalla debe dejar claro:
- que solo consulta información existente
- que no modifica datos
- que trabaja sobre las vistas del dashboard
- que algunas preguntas pueden no resolverse si no encajan en las vistas disponibles

---

## Fuente de datos autorizada
Solo estas vistas:

### 1. `vw_compras_cabecera_dashboard`
Útil para:
- comprobantes
- proveedores
- usuario
- importes
- tipo de registro
- estado
- cabecera general

### 2. `vw_compras_detalle_dashboard`
Útil para:
- artículos
- rubros
- familias
- cantidades
- costos
- detalle
- análisis de variaciones

### 3. `vw_familias_jerarquia`
Útil para:
- jerarquía de familias
- padre/hijo
- nivel jerárquico
- composición de ramas

---

## Seguridad: requisito obligatorio
Quiero que implementes un validador fuerte antes de ejecutar cualquier SQL.

El validador debe rechazar cualquier consulta que:
- no sea SELECT
- tenga ';'
- tenga comentarios '--' o '/*'
- mencione tablas o vistas no autorizadas
- tenga palabras peligrosas
- intente usar EXEC, DROP, INSERT, UPDATE, DELETE, ALTER, CREATE, MERGE, TRUNCATE, sp_, xp_

Si la consulta se rechaza:
- mostrar mensaje claro y amigable
- no ejecutar nada

---

## Forma de trabajo
No rehagas la app desde cero.

Trabajá sobre el proyecto existente.

Primero:
1. revisá el proyecto actual
2. agregá la opción de menú InformesIA
3. creá la pantalla
4. implementá el backend y validación
5. dejá una versión funcional mínima pero usable

---

## Lo que espero como entregable
Quiero que me entregues:

1. propuesta breve de implementación
2. archivos nuevos o modificados
3. página InformesIA funcional
4. servicio o lógica de backend
5. validador de consultas
6. sugerencias iniciales cargadas
7. explicación breve de cada cambio importante

---

## Prioridad funcional
Si tenés que priorizar, hacelo en este orden:

1. nueva pantalla InformesIA
2. cuadro de texto + sugerencias
3. generación de tabla
4. validación de seguridad
5. soporte de gráfico opcional
6. resumen corto en lenguaje natural

---

## Resultado esperado
Quiero terminar con una nueva opción **InformesIA** que:
- sea minimalista
- permita preguntas libres
- muestre sugerencias
- genere una tabla
- opcionalmente muestre gráfico
- use solo las vistas autorizadas
- sea estrictamente de solo lectura
- bloquee comandos peligrosos

No quiero solo ideas generales. Quiero implementación concreta sobre el proyecto actual.

## Funcionalidades adicionales obligatorias

### 1. Historial de últimas consultas
Quiero que InformesIA guarde las últimas **20 consultas** para poder reutilizarlas fácilmente.

Debe permitir:
- ver historial reciente
- volver a ejecutar una consulta anterior
- cargar una consulta previa en el cuadro de texto
- opcionalmente borrar una consulta del historial

Cada registro debe guardar al menos:
- texto de la consulta
- fecha y hora
- si fue exitosa
- tipo de resultado si aplica

Si existe usuario actual, asociar el historial al usuario.
Si no existe esa infraestructura todavía, implementar una persistencia simple razonable.

---

### 2. Exportación a PDF
Quiero que el resultado pueda exportarse a **PDF**.

El PDF debe incluir, si están disponibles:
- título del informe
- texto de la consulta
- fecha y hora
- filtros aplicados
- tabla de resultados
- gráfico generado
- resumen corto en lenguaje natural

La exportación debe ser clara y profesional.

---

### 3. Manejo seguro de API Key
La API key de OpenAI no debe estar hardcodeada.

Quiero que la implementación:
- lea la API key desde variable de entorno
- soporte `.env` en desarrollo si corresponde
- nunca exponga la API key en el frontend
- ejecute llamadas a OpenAI solo desde backend

No dejar secretos en código fuente.

---

### 4. Micrófono para dictado de consultas
Quiero agregar una función opcional de micrófono para que el usuario pueda dictar la consulta.

Objetivo:
- botón de micrófono junto al cuadro de texto
- capturar voz
- convertirla en texto
- cargar el texto en el cuadro de consulta
- luego permitir que el usuario edite y ejecute

Prioridad:
- implementar primero una versión simple y funcional
- si la transcripción no está disponible, mostrar mensaje claro

La función de micrófono no debe reemplazar la entrada manual; debe ser complementaria.

Si la implementación requiere elegir entre:
- dictado simple desde navegador
- o transcripción backend

priorizar la alternativa más simple y segura para una primera versión.