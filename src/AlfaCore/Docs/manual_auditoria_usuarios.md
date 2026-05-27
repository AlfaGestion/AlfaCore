# Manual de Usuario — Auditoría de usuarios
### Alfa Gestión · Módulo Auditoría

---

## Índice

1. [¿Qué es Auditoría de usuarios?](#1-qué-es-auditoría-de-usuarios)
2. [¿Para qué sirve en el negocio?](#2-para-qué-sirve-en-el-negocio)
3. [Qué analiza y qué no analiza](#3-qué-analiza-y-qué-no-analiza)
4. [Cómo usar la pantalla](#4-cómo-usar-la-pantalla)
5. [Filtros disponibles](#5-filtros-disponibles)
6. [Cómo leer los KPI](#6-cómo-leer-los-kpi)
7. [Controles disponibles](#7-controles-disponibles)
8. [Cómo revisar el detalle de una alerta](#8-cómo-revisar-el-detalle-de-una-alerta)
9. [Exportación a PDF y Excel](#9-exportación-a-pdf-y-excel)
10. [Recomendaciones de uso para dueño de negocio](#10-recomendaciones-de-uso-para-dueño-de-negocio)
11. [Preguntas frecuentes](#11-preguntas-frecuentes)

---

## 1. ¿Qué es Auditoría de usuarios?

**Auditoría de usuarios** es una herramienta de control interno que ayuda a detectar operaciones que merecen revisión.

No está pensada para acusar automáticamente a una persona ni para modificar datos del sistema.  
Su objetivo es mostrar:

- operaciones incompletas
- remitos con diferencias de facturación
- bajas o anulaciones
- actividad fuera del patrón normal
- posibles comprobantes de compras duplicados

En resumen: es una pantalla para **detectar situaciones a revisar** antes de que se transformen en pérdidas, errores administrativos o riesgos operativos.

---

## 2. ¿Para qué sirve en el negocio?

Este módulo ayuda al dueño, gerente o responsable administrativo a responder preguntas como:

- ¿Qué usuarios generan más situaciones anómalas?
- ¿Qué comprobantes se iniciaron y luego se cancelaron?
- ¿Hay remitos que todavía no se facturaron?
- ¿Se está facturando de más o de menos?
- ¿Se están haciendo bajas de comprobantes sensibles?
- ¿Hay actividad en horarios o equipos poco habituales?
- ¿Puede haber compras duplicadas cargadas dos veces?

Su valor principal es **preventivo**:

- reduce pérdidas por error
- mejora disciplina operativa
- ayuda a detectar fallas de circuito
- permite hablar con evidencia y no con sospechas

---

## 3. Qué analiza y qué no analiza

### Qué analiza

El módulo toma información de auditoría funcional del sistema y cruza acciones, remitos, comprobantes, accesos y compras para identificar patrones.

### Qué no analiza

- No decide culpabilidad.
- No borra comprobantes.
- No corrige datos automáticamente.
- No reemplaza el criterio del responsable del negocio.

Una alerta significa:

> “Esto merece revisión”.

No significa:

> “Esto está mal con certeza”.

---

## 4. Cómo usar la pantalla

La pantalla se compone de cuatro sectores:

### 4.1 Buscador superior

Permite buscar por texto libre:

- usuario
- equipo
- número de comprobante
- cliente o cuenta
- motivo

### 4.2 Panel de filtros

Desde el desplegable de filtros podés acotar el análisis por:

- fechas
- usuario
- PC
- tipo de control
- riesgo
- tipo de comprobante
- cliente / cuenta
- umbrales de análisis

### 4.3 KPI superiores

Muestran un resumen rápido del período y del control seleccionado.

### 4.4 Grilla de resultados

Muestra las alertas detectadas.  
Desde cada fila podés:

- ver el detalle
- abrir el comprobante relacionado
- exportar resultados

---

## 5. Filtros disponibles

### Período

Define desde qué fecha hasta qué fecha querés auditar.

### Usuario

Sirve para revisar una persona puntual o un sector si cada equipo tiene usuarios distintos.

### PC

Útil para detectar si un equipo específico concentra anomalías.

### Tipo de control

Permite enfocarse en una sola clase de alerta.

### Riesgo

Filtra por:

- `ALTO`
- `MEDIO`
- `BAJO`

### TC

Filtra por tipo de comprobante.

### Cliente / cuenta

Sirve para revisar casos ligados a una cuenta específica.

### Umbrales

Según el control elegido, la pantalla usa distintos parámetros:

- días mínimos sin factura
- umbral de modificaciones
- días de tolerancia para duplicados
- solo diferencias de sucursal

Estos valores permiten que la auditoría sea más estricta o más flexible.

---

## 6. Cómo leer los KPI

Los KPI cambian según el control elegido.

### Cuando no hay un control específico de compras duplicadas o cancelaciones

Los KPI habituales muestran:

- total de alertas
- riesgo alto
- riesgo medio
- cantidad de controles activos detectados

### Cuando el control es “Posibles comprobantes duplicados”

Los KPI se enfocan en:

- grupos sospechosos
- riesgo alto
- riesgo medio
- comprobantes involucrados
- contables duplicados

### Cuando el control es “Comprobantes iniciados y no grabados”

Los KPI se enfocan en:

- comprobantes cancelados
- promedio de minutos hasta cancelación
- máximo de minutos hasta cancelación
- importe total cancelado
- usuarios involucrados

Esto permite distinguir si el patrón parece:

- normal y operativo
- desordenado
- riesgoso

---

## 7. Controles disponibles

Esta es la parte más importante del módulo.

### 7.1 Comprobantes iniciados y no grabados

**Qué detecta**

Detecta comprobantes comerciales sensibles que fueron iniciados pero no tienen un cierre final equivalente.

**Qué puede significar**

- trabajo interrumpido
- errores de carga
- operaciones canceladas
- pruebas repetidas
- posible desorden operativo

**Qué mirar**

- hora de inicio
- hora de cancelación
- minutos hasta cancelación
- importe al cancelar
- traza original del sistema
- usuario y equipo

**Cómo interpretarlo**

No siempre es un problema.  
Puede ser normal si:

- el usuario corrige datos antes de grabar
- el comprobante se descartó rápidamente

Merece más revisión si:

- hay muchas cancelaciones
- los tiempos son largos
- los importes son altos
- se repite en el mismo usuario

**Pregunta de negocio que responde**

> “¿Quién está iniciando comprobantes que no terminan grabados y cuánto tiempo trabaja antes de cancelarlos?”

---

### 7.2 Remitos facturados parcialmente

**Qué detecta**

Detecta remitos cuyo importe fue facturado solo en parte.

**Qué puede significar**

- facturación pendiente
- facturación por etapas
- diferencia entre lo entregado y lo facturado
- posible pérdida de facturación si nunca se completa

**Qué mirar**

- importe original del remito
- importe aplicado
- diferencia pendiente
- facturas relacionadas

**Pregunta de negocio que responde**

> “¿Hay remitos entregados que todavía no se facturaron completos?”

---

### 7.3 Remitos sin factura

**Qué detecta**

Detecta remitos que no tienen aplicación posterior de facturación y que además ya superaron el umbral mínimo de días configurado.

**Qué puede significar**

- entrega no facturada
- retraso administrativo
- pérdida de ingreso
- falla en el circuito entre entrega y facturación

**Qué mirar**

- cantidad de días pendientes
- usuario
- cliente
- importe del remito

**Pregunta de negocio que responde**

> “¿Qué entregas siguen sin facturarse después de un plazo razonable?”

---

### 7.4 Remitos facturados de más

**Qué detecta**

Detecta remitos cuyo importe aplicado supera al importe original.

**Qué puede significar**

- error de aplicación
- duplicación parcial de facturación
- desajuste entre remito y comprobante final

**Qué mirar**

- importe del remito
- importe aplicado
- diferencia
- facturas relacionadas

**Pregunta de negocio que responde**

> “¿Se está facturando por encima de lo entregado?”

---

### 7.5 Posibles comprobantes duplicados

**Qué detecta**

Detecta grupos sospechosos de comprobantes de compras que coinciden en:

- proveedor
- número normalizado
- importe
- tipo de comprobante exacto

y aparecen más de una vez.

El control prioriza especialmente los casos con:

- distinta sucursal o punto de venta
- distintos usuarios de carga
- fechas muy cercanas

**Por qué se llama “posibles”**

Porque requiere revisión humana.  
Puede haber casos válidos, pero también puede tratarse de una carga duplicada real.

**Qué mirar**

- riesgo
- proveedor / cuenta
- número normalizado
- importe
- sucursales detectadas
- usuarios detectados
- impacto contable
- detalle del grupo

**Pregunta de negocio que responde**

> “¿Hay facturas de compra que parecen haberse cargado más de una vez?”

---

### 7.6 Modificaciones excesivas

**Qué detecta**

Detecta comprobantes comerciales que fueron modificados muchas veces por el mismo usuario.

El nivel de riesgo depende del umbral configurado.

**Qué puede significar**

- errores de carga reiterados
- falta de capacitación
- proceso inestable
- trabajo poco controlado

**Qué mirar**

- cantidad de ocurrencias
- usuario
- equipo
- comprobante

**Pregunta de negocio que responde**

> “¿Qué operaciones se corrigen demasiadas veces antes de quedar cerradas?”

---

### 7.7 Bajas de comprobantes

**Qué detecta**

Detecta bajas o anulaciones de comprobantes comerciales.

**Qué puede significar**

- corrección válida
- error previo
- anulación legítima
- posible abuso si ocurre con demasiada frecuencia

**Qué mirar**

- usuario
- equipo
- tipo de comprobante
- cantidad de bajas
- contexto operativo

**Pregunta de negocio que responde**

> “¿Quién está anulando comprobantes y con qué frecuencia?”

---

### 7.8 Actividad fuera de horario

**Qué detecta**

Detecta acciones realizadas:

- fuera del horario laboral definido
- en días no laborales
- o desde una PC distinta a la habitual del usuario

**Qué puede significar**

- horas extra legítimas
- urgencias operativas
- trabajo remoto no previsto
- uso poco habitual del sistema

**Qué mirar**

- fecha y hora
- día de la semana
- PC
- usuario
- formulario o tarea vinculada

**Pregunta de negocio que responde**

> “¿Hay actividad sensible fuera del patrón normal de trabajo?”

---

### 7.9 Ingreso sensible sin operación final

**Qué detecta**

Detecta ingresos a opciones sensibles del sistema donde hubo inicio de operación, pero no aparece un cierre o finalización coherente.

**Qué puede significar**

- operación interrumpida
- salida prematura
- proceso abandonado
- intento sin completar

**Qué mirar**

- formulario
- tarea
- hora de ingreso
- hora de egreso
- cantidad de inicios
- total de acciones registradas

**Pregunta de negocio que responde**

> “¿Se está entrando a opciones sensibles sin completar realmente la operación?”

---

## 8. Cómo revisar el detalle de una alerta

Al hacer clic en una fila se abre el detalle de la alerta.

Ese detalle sirve para pasar de una señal general a una revisión concreta.

Según el control, vas a ver información como:

- usuario
- PC
- comprobante
- cuenta / cliente / proveedor
- importe
- diferencia
- días pendientes
- motivo
- observaciones
- trazas del sistema
- vínculos con facturas o asientos

Recomendación práctica:

1. Mirá el motivo.
2. Revisá los datos duros.
3. Abrí el comprobante.
4. Confirmá el contexto con el responsable del área.

---

## 9. Exportación a PDF y Excel

La pantalla permite exportar resultados.

### PDF

Conviene para:

- reuniones
- revisión gerencial
- compartir un resumen visual

### Excel

Conviene para:

- análisis detallado
- ordenar por usuario o importe
- agregar comentarios
- enviar a administración o auditoría externa

Recomendación:

- usar PDF para presentar
- usar Excel para trabajar

---

## 10. Recomendaciones de uso para dueño de negocio

### Revisión diaria

Conviene mirar:

- comprobantes iniciados y no grabados
- remitos sin factura
- bajas de comprobantes

### Revisión semanal

Conviene mirar:

- modificaciones excesivas
- actividad fuera de horario
- ingreso sensible sin operación final

### Revisión mensual

Conviene mirar:

- posibles comprobantes duplicados
- usuarios con más alertas
- importes más altos involucrados

### Regla práctica

No revises solo cantidad de alertas.  
Priorizá por:

- importe
- repetición
- usuario
- impacto contable
- antigüedad del caso

---

## 11. Preguntas frecuentes

### ¿Una alerta significa fraude?

No.  
Significa que hay una situación que conviene revisar.

### ¿Puedo usar esto para controlar productividad?

Sí, pero con criterio.  
Sirve mejor para detectar desorden, riesgo o necesidad de capacitación que para medir desempeño aislado.

### ¿Qué control conviene mirar primero?

Para una revisión rápida:

- remitos sin factura
- bajas de comprobantes
- posibles comprobantes duplicados

### ¿Qué control conviene usar para revisar hábitos de carga?

- comprobantes iniciados y no grabados
- modificaciones excesivas
- actividad fuera de horario

### ¿La auditoría cambia datos del sistema?

No.  
Solo analiza y muestra información.

---

## Cierre

La mejor forma de aprovechar este módulo no es buscar “culpables”, sino usarlo para mejorar:

- el circuito de facturación
- la calidad de carga
- la disciplina operativa
- el control sobre excepciones

Bien usado, este módulo ayuda a convertir hechos aislados en patrones visibles, y patrones visibles en mejores decisiones.
