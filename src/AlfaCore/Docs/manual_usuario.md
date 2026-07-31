# Manual General de AlfaCore
### Alfa Gestión · Guía principal de uso

---

## Índice

1. [¿Qué es AlfaCore?](#1-qué-es-alfacore)
2. [¿Para qué sirve?](#2-para-qué-sirve)
3. [Ingreso al sistema](#3-ingreso-al-sistema)
4. [Selección de base de datos](#4-selección-de-base-de-datos)
5. [Pantalla principal y navegación](#5-pantalla-principal-y-navegación)
6. [Cómo usar el menú](#6-cómo-usar-el-menú)
7. [Cómo leer una pantalla de AlfaCore](#7-cómo-leer-una-pantalla-de-alfacore)
8. [Filtros, búsquedas y listados](#8-filtros-búsquedas-y-listados)
9. [Detalle, acciones y exportaciones](#9-detalle-acciones-y-exportaciones)
10. [Ayuda y manuales por módulo](#10-ayuda-y-manuales-por-módulo)
11. [Buenas prácticas de uso](#11-buenas-prácticas-de-uso)
12. [Preguntas frecuentes](#12-preguntas-frecuentes)

---

## 1. ¿Qué es AlfaCore?

**AlfaCore** es la plataforma web de Alfa Gestión.

Su objetivo es concentrar en una sola aplicación distintas herramientas de gestión, análisis, auditoría, consultas, seguimiento operativo y soporte a decisiones.

No es un único módulo.  
Es una **base común** desde la que se accede a distintas áreas del negocio.

Por ejemplo, según la instalación y los permisos, AlfaCore puede incluir:

- dashboards de gestión
- consultas SQL
- auditoría
- tareas
- conversaciones
- interfaces
- costos
- seguridad

Cada módulo conserva su propia lógica, pero todos comparten una misma forma de navegación.

---

## 2. ¿Para qué sirve?

AlfaCore sirve para:

- consultar información del sistema desde el navegador
- analizar indicadores del negocio
- revisar operaciones y alertas
- administrar configuraciones y procesos
- acceder a herramientas de apoyo para usuarios, supervisores y dueños

En términos simples:

> AlfaCore permite trabajar con información de Alfa Gestión de forma moderna, visual y centralizada.

---

## 3. Ingreso al sistema

La aplicación se abre desde un navegador web.

Según cómo esté instalada en tu empresa, podés ingresar con una dirección como:

```text
http://localhost:5055
```

o bien con la dirección del servidor publicada por el área de sistemas.

Al ingresar, el sistema puede pedirte:

- seleccionar una base activa
- iniciar sesión con usuario del sistema

Si no podés ingresar:

- verificá que la base correcta esté activa
- revisá usuario y contraseña
- si persiste, contactá a soporte

---

## 4. Selección de base de datos

AlfaCore trabaja sobre una **base activa**.

Eso significa que la información que ves depende de la sesión SQL seleccionada.

### ¿Dónde se ve?

En la parte superior de la aplicación aparece un chip o indicador con:

- servidor
- base de datos activa

### ¿Qué pasa si cambiás de base?

Al cambiar la base:

- la sesión funcional actual puede cerrarse
- tenés que volver a ingresar
- cambiás el contexto completo de trabajo

### Recomendación

Antes de revisar datos o hacer análisis, confirmá siempre:

- qué base está activa
- si corresponde al cliente o empresa con la que querés trabajar

---

## 5. Pantalla principal y navegación

La interfaz de AlfaCore tiene una estructura general común:

### Encabezado

En la parte superior vas a encontrar normalmente:

- nombre del entorno
- módulo actual
- base activa
- usuario logueado
- acceso a ayuda

### Menú lateral

Desde el menú lateral accedés a los módulos.

### Área de trabajo

Es la zona central donde se muestra la pantalla activa:

- resumen
- grilla
- filtros
- detalle
- gráficos
- formularios

### Acciones rápidas

Algunas pantallas también muestran:

- recargar
- exportar
- abrir detalle
- configurar

---

## 6. Cómo usar el menú

El menú lateral es la entrada principal a los módulos.

### Qué podés hacer

- abrir un módulo
- volver a la opción anterior
- buscar una opción por nombre
- acceder a Ayuda
- ingresar a Tareas

### Búsqueda de menú

Si no sabés dónde está una opción, usá el buscador del menú.

Podés escribir, por ejemplo:

- auditoría
- consultas
- clientes
- interfaces
- costos

### Recomendación

Si estás empezando, usá primero:

- menú lateral
- título del módulo activo
- botón de ayuda

Eso te ubica rápido dentro del sistema.

---

## 7. Cómo leer una pantalla de AlfaCore

Aunque cada módulo tiene sus particularidades, la mayoría de las pantallas siguen un patrón similar.

### 7.1 Título y descripción

Arriba de todo se informa:

- qué módulo estás viendo
- qué analiza o administra

### 7.2 Filtros

Sirven para acotar la información.

Lo más habitual es filtrar por:

- fechas
- usuario
- cuenta
- tipo
- estado
- riesgo

### 7.3 KPI

Las tarjetas KPI resumen lo más importante de la pantalla.

Ejemplos:

- cantidades
- importes
- alertas
- tiempos
- usuarios involucrados

### 7.4 Grilla o tabla

Es el listado detallado del resultado.

### 7.5 Detalle

Al abrir un registro, podés ver información ampliada para analizar mejor el caso.

---

## 8. Filtros, búsquedas y listados

### Filtros

Los filtros ayudan a responder preguntas concretas.

Ejemplos:

- “mostrame solo este usuario”
- “quiero revisar solo abril”
- “quiero ver solo riesgo alto”

### Búsqueda por texto

Muchas pantallas tienen una búsqueda libre para encontrar por coincidencia:

- nombres
- cuentas
- comprobantes
- motivos
- equipos

### Ordenamiento

En muchas tablas podés ordenar por columnas como:

- fecha
- importe
- usuario
- riesgo

### Paginación

Cuando hay muchos resultados, la tabla se divide en páginas.

Podés:

- cambiar de página
- ajustar tamaño de página

---

## 9. Detalle, acciones y exportaciones

### Ver detalle

Cuando una fila tiene detalle, conviene usarlo antes de sacar conclusiones.

El detalle suele mostrar:

- información ampliada
- motivo
- observaciones
- relaciones con otros registros
- datos técnicos o funcionales

### Abrir comprobante o registro relacionado

En algunos módulos podés ir del listado a la ficha o visor del comprobante.

### Exportar

Según el módulo, puede haber exportación a:

- `PDF`
- `Excel`

### Cuándo usar cada formato

Usá `PDF` para:

- compartir
- imprimir
- presentar

Usá `Excel` para:

- analizar
- ordenar
- comentar
- trabajar con más detalle

---

## 10. Ayuda y manuales por módulo

AlfaCore tiene dos niveles de documentación:

### Manual general

Este archivo.

Sirve para entender:

- qué es AlfaCore
- cómo se navega
- cómo funciona la base activa
- cómo leer las pantallas

### Manuales por módulo

Cada módulo importante puede tener su propio manual.

Ejemplos:

- Auditoría de usuarios
- Consultas
- [AlfaKnowledge en Conversaciones](manual_alfaknowledge_conversaciones.md)
- otros módulos específicos

### Cuándo usar cada uno

Usá el **manual general** cuando quieras entender el sistema.

Usá el **manual del módulo** cuando necesites trabajar en profundidad una pantalla concreta.

---

## 11. Buenas prácticas de uso

### Confirmá siempre la base activa

Es la validación más importante antes de interpretar cualquier dato.

### Filtrá antes de analizar

No tomes decisiones con resultados demasiado amplios si podés acotar:

- período
- usuario
- cuenta
- módulo

### Leé primero el resumen y después el detalle

El orden recomendado es:

1. mirar KPI
2. revisar listado
3. abrir detalle
4. exportar si hace falta

### No confundas alerta con error confirmado

Muchos módulos muestran hallazgos, no sentencias definitivas.

La regla práctica es:

> primero revisar, después concluir

### Usá Ayuda dentro del contexto correcto

Si estás en un módulo específico, buscá primero su manual.

---

## 12. Preguntas frecuentes

### ¿AlfaCore reemplaza a Alfa Gestión?

No.  
Es la capa web y analítica que complementa y moderniza el acceso a información y procesos.

### ¿Todo lo que veo depende de la base activa?

Sí.  
La base seleccionada define el contexto de datos.

### ¿Puedo trabajar en más de una base?

Sí, pero no al mismo tiempo en la misma sesión funcional sin cambiar el contexto.

### ¿Todos los usuarios ven los mismos módulos?

No necesariamente.  
Depende de:

- permisos
- configuración
- base activa
- opciones habilitadas

### ¿Qué hago si una pantalla no carga?

Primero revisá:

- base activa
- conexión
- permisos

Si el problema sigue, compartí el código de incidente con soporte.

### ¿Qué hago si no encuentro una opción?

Probá:

- el buscador del menú
- el botón de ayuda
- el manual del módulo correspondiente

---

## Cierre

AlfaCore está pensado para que el trabajo diario sea más claro, más visible y más controlable.

La mejor manera de aprovecharlo es usarlo con este criterio:

- primero ubicar el módulo correcto
- después confirmar la base activa
- luego filtrar
- y recién ahí analizar o decidir

Este manual general es el punto de partida.  
Para el uso profundo de cada área, apoyate en los manuales específicos por módulo.
