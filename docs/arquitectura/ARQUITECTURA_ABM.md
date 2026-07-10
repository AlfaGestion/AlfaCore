# Arquitectura ABM Base

## Objetivo

Este documento define el criterio recomendado para construir modulos de **ABM / CRUD** dentro de AlfaCore.

La idea no es crear una rutina universal que apunte directo a cualquier tabla, sino una base comun reutilizable con comportamiento controlado por entidad.

## Reglas aplicadas

- Se respetan `docs/CODEX_RULES.md`, `docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md` y `docs/CONFIGURACION_GLOBAL.md`.
- El acceso a datos debe seguir usando SQL Server + Dapper.
- Los errores tecnicos relevantes deben registrar en `AUX_ERR` mediante la capa centralizada actual.
- La configuracion dinamica del sistema debe seguir usando `TA_CONFIGURACION` cuando corresponda.

## Criterio general

Para nuevos maestros o catalogos:

- no hacer una pantalla totalmente aislada por cada tabla
- no hacer una rutina generica ciega que intente persistir cualquier tabla sin reglas propias
- si conviene construir una **base ABM reutilizable**

El enfoque recomendado es:

- infraestructura comun reutilizable
- definicion especifica por entidad
- servicios especificos cuando existan reglas propias

## Objetivos funcionales de la base ABM

La base comun deberia resolver, cuando aplique:

- grilla inicial con busqueda
- orden de columnas
- columnas visibles / ocultas
- filtros basicos
- agrupacion si el caso lo justifica
- alta
- edicion
- baja logica
- acciones complementarias por registro

## Limites deliberados

La base ABM no debe asumir por si sola:

- validaciones de negocio complejas
- reglas sensibles como contrasenas
- relaciones dependientes no triviales
- permisos finos
- procesamiento de archivos o imagenes

Esas reglas deben quedar en la definicion o servicio especifico de cada maestro.

## Estructura recomendada

Cuando se implemente un nuevo maestro, el patron esperado es:

### 1. Pagina del modulo

Responsable de:

- layout de la aplicacion
- toolbar
- grilla
- detalle o formulario
- mensajes al usuario

No debe contener SQL directo ni logica de persistencia pesada.

### 2. Modelos / DTOs

Responsables de:

- filtros
- filas de la grilla
- formularios
- requests de alta / edicion
- respuestas de detalle

Deben ser simples y sin logica de negocio relevante.

### 3. Servicio del modulo

Responsable de:

- orquestar casos de uso
- aplicar reglas del maestro
- invocar validadores especificos de la entidad
- coordinar alta, edicion y baja logica
- traducir errores funcionales para UI

### 4. Acceso a datos

Responsable de:

- consultas SQL
- uso de Dapper
- mapeo de datos
- inserts / updates / bajas logicas

## Modelo recomendado de construccion

Cada maestro deberia definirse con dos niveles:

### A. Infraestructura comun

Piezas reutilizables como:

- componente de grilla
- acciones estandar
- toolbar comun
- helpers de filtros
- manejo comun de feedback y errores

### B. Definicion por entidad

Cada modulo debe declarar:

- columnas visibles por defecto
- orden inicial
- filtros permitidos
- acciones habilitadas
- campos editables
- reglas de validacion
- si usa baja logica
- si requiere acciones especiales

## Baja logica

Cuando el objeto lo permita, la estrategia por defecto deberia ser:

- campo `Activo` o equivalente
- listar solo activos por defecto
- permitir ver inactivos cuando haga falta
- desactivar en lugar de borrar fisicamente

El borrado fisico solo deberia usarse si existe una razon clara y validada.

## Relaciones y lookups

Si un maestro usa tablas relacionadas, la base ABM deberia soportar:

- listas simples
- combos / lookups
- autocompletado cuando el volumen lo requiera
- validacion de claves relacionadas

Pero la definicion de cada relacion debe quedar en el modulo especifico, no embebida como regla magica global.

## Campos sensibles

Campos o acciones sensibles no deben tratarse como texto generico.

Ejemplos:

- contrasenas
- reseteo de acceso
- permisos
- acciones administrativas

Regla:

- manejar estos casos desde acciones o servicios especificos
- evitar mostrarlos o persistirlos de manera ingenua

## Arquitectura recomendada de validaciones

Para nuevos ABM, la validacion no debe quedar resuelta solo con:

- `required` en la UI
- `if` dispersos dentro del servicio
- triggers como regla principal

El criterio recomendado es por capas:

### 1. UI

Responsable de:

- validaciones inmediatas
- confirmaciones visuales
- feedback por campo
- no perder los datos ya cargados por el usuario

Ejemplos:

- confirmacion de contrasena
- longitud visible en inputs
- formato basico antes de enviar

### 2. Validador especifico de entidad

Responsable de:

- obligatorios reales
- combinaciones invalidas
- unicidad funcional
- reglas condicionales
- restricciones del maestro que no deben vivir en la pantalla

Patron esperado:

- `ValidationIssue`
- `ValidationResult`
- una excepcion especifica de validacion para transportar errores funcionales
- un validador por entidad

Ejemplos:

- `UsuariosValidator`
- `ClientesValidator`
- `ProveedoresValidator`

### 3. Servicio del modulo

Responsable de:

- normalizar el request
- llamar al validador
- abortar la persistencia si la validacion falla
- guardar solo cuando la validacion es correcta

### 4. SQL Server

Responsable de:

- PK
- FK
- `NOT NULL`
- `UNIQUE`
- `CHECK` estables

Regla:

- las restricciones estructurales deben vivir en SQL cuando aplique
- las reglas funcionales deben quedar visibles en la aplicacion

## Resultado esperado en UI

Cuando una validacion falle:

- mostrar un mensaje general claro
- marcar los campos afectados
- devolver errores por campo cuando sea posible
- conservar el estado del formulario para corregir y reintentar

No dejar como unico feedback:

- una excepcion general
- un mensaje tecnico crudo
- un reinicio completo del formulario

## Imagenes y archivos

Si un maestro usa imagenes o adjuntos:

- definir claramente si vive en SQL o filesystem
- usar configuracion central cuando el path sea configurable
- no mezclar logica de archivos con la grilla base

La base ABM puede ayudar con UI, pero la regla fisica debe definirse por modulo.

## Preferencias de usuario

Si mas adelante se persisten preferencias de grilla o filtros por usuario:

- usar `TA_CONFIGURACION`
- seguir la convencion general del proyecto
- no crear tablas paralelas de configuracion sin necesidad clara

## Criterio de implementacion

El objetivo es que cada nuevo maestro reutilice la base sin quedar preso de una abstraccion excesiva.

En terminos practicos:

- si la base comun ayuda, reutilizar
- si una regla es propia del maestro, resolverla en el maestro
- si una abstraccion complica mas de lo que simplifica, no forzarla

## Orden recomendado de trabajo

Para implementar un nuevo ABM:

1. validar reglas funcionales del maestro
2. revisar objetos SQL oficiales
3. definir grilla minima
4. definir formulario minimo
5. definir validaciones
6. definir alta / edicion / baja
7. recien despues extraer infraestructura reutilizable si el patron ya quedo claro

## Modulo piloto

El primer modulo propuesto para validar esta arquitectura es:

- `Usuarios`

Ese modulo debe servir como caso real de negocio y como piloto de la base comun reutilizable.
