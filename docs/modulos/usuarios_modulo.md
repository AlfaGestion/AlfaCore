# Modulo Usuarios

## Objetivo

Este documento define el alcance funcional y tecnico inicial del modulo `Usuarios` dentro de AlfaCore.

El modulo se implementa como una nueva aplicacion funcional del sistema y, al mismo tiempo, como piloto para la futura base comun de ABM / CRUD.

## Reglas aplicadas

- Se respetan `docs/CODEX_RULES.md`, `docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md` y `docs/CONFIGURACION_GLOBAL.md`.
- La tabla base oficial es `TA_USUARIOS`.
- Los errores tecnicos relevantes deben registrar en `AUX_ERR` mediante la capa centralizada actual.
- La implementacion debe trabajar sobre la estructura actual, sin rehacer el esquema general del sistema.

## Alcance de la primera version

La primera version del modulo debe resolver:

- listado de usuarios
- alta
- edicion
- baja logica
- reactivacion si luego se incorpora a la UI
- manejo basico de imagen de usuario
- manejo de contrasena compatible con el sistema actual

Quedan fuera de la primera etapa:

- permisos finos por menu o tarea
- autorizacion de tareas
- configuracion SMTP del usuario
- campos avanzados no necesarios para el piloto

## Tabla base

Objeto principal:

- `TA_USUARIOS`

Validacion de uso:

- esta documentada como tabla oficial esperada para seguridad / usuarios en `docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md`

## Clave funcional

La PK actual de la tabla es:

- `NOMBRE`
- `SISTEMA`

Para este modulo piloto:

- `SISTEMA` se trabajara fijo en `CN000PR`

Esto implica que la aplicacion opera, por ahora, sobre un unico sistema funcional.

## Campo de baja logica

Se incorpora:

- `Activo`

Uso esperado:

- nuevos usuarios se crean activos
- la baja desde UI debe marcar `Activo = 0`
- la grilla lista activos por defecto

## Campos iniciales del formulario

Campos visibles previstos para la primera version:

- `Nombre`
- `email_de`
- `EsGrupo`
- `CambiarProximoInicio`
- `Contrasena`
- `ConfirmacionContrasena`
- imagen de usuario opcional

## Patrón de validación adoptado

El modulo `Usuarios` pasa a ser tambien el piloto del patrón de validaciones para ABM.

La validacion queda separada en:

- **UI**
  - confirmacion de contrasena
  - marcado visual de campos invalidos
  - mensajes por campo
  - conservacion del estado cargado

- **Validador especifico**
  - `UsuariosValidator`
  - reglas funcionales del maestro
  - formato de email
  - reglas de grupo
  - unicidad de nombre
  - restricciones de imagen

- **Servicio**
  - `UsuariosService`
  - normaliza el request
  - invoca al validador
  - persiste solo si la validacion es correcta

- **SQL**
  - integridad estructural que ya exista en la tabla
  - PK actual
  - defaults
  - restricciones estables cuando se incorporen

## Objetos tecnicos del patrón

El piloto usa estos conceptos base:

- `ValidationIssue`
- `ValidationResult`
- `AppValidationException`
- `IEntitySaveValidator<T>`
- `IUsuariosValidator`
- `UsuariosValidator`

Este patrón debe servir como referencia para otros maestros.

## Reglas funcionales confirmadas

### 1. Sistema fijo

Para esta etapa:

- `SISTEMA = 'CN000PR'`

No se expone como campo editable al usuario.

### 2. Email funcional

El email del usuario debe usar:

- `email_de`

No debe confundirse con los campos tecnicos de configuracion de correo saliente.

### 3. Usuario grupo

Si:

- `EsGrupo = 1`

entonces:

- no lleva contrasena

Regla de validacion:

- si `EsGrupo = 1`, no debe guardar contrasena
- si `EsGrupo = 1`, no corresponde `CambiarProximoInicio = 1`

La UI y el servicio deben contemplar esta excepcion en validaciones y persistencia.

### 4. Defaults internos al crear

El servicio de usuarios debe aplicar por defecto:

- `IDCAJA = '1'`
- `UNEGOCIO = '   1'`
- `V_ModificaArtLuegoDeCargado = 1`
- `Activo = 1`

Observacion:

- `IDCAJA` y `UNEGOCIO` son campos de texto y deben conservar el criterio legacy esperado por el sistema actual.

## Contrasena

### Compatibilidad requerida

La contrasena debe mantenerse compatible con el esquema actual del sistema de escritorio.

Rutinas legacy confirmadas:

- codificacion reversible existente
- decodificacion reversible existente

### Regla de implementacion

La logica de codificacion / decodificacion no debe quedar dispersa por la UI.

Se recomienda encapsularla en un servicio especifico, por ejemplo:

- `UsuariosPasswordCodec`

### Regla de UX

- por defecto la contrasena debe mostrarse oculta
- puede existir accion explicita para mostrarla
- la confirmacion debe ser obligatoria al crear o cambiar contrasena
- al editar un usuario comun, la contrasena debe tratarse como campo sensible

### Regla de validacion

- la confirmacion de contrasena se controla en UI
- la obligatoriedad y compatibilidad de la contrasena se controlan en `UsuariosValidator`

## Imagenes de usuario

### Criterio acordado

Las imagenes no se persisten en SQL.

Se utiliza la configuracion existente:

- `RutaImagenes`

Esa ruta es relativa a donde se ejecuta la aplicacion.

### Estructura fisica acordada

La imagen del usuario debe resolverse en:

- `RutaImagenes\USUARIOS\<Nombre>.jpg`

Ejemplo:

- `IMAGENES\USUARIOS\albert.jpg`

### Regla funcional

- si existe la imagen, se muestra
- si no existe, se usa avatar por defecto
- no se agrega `SISTEMA` en la ruta en esta etapa

## Grilla inicial sugerida

Columnas sugeridas para la primera version:

- `Nombre`
- `Email`
- `EsGrupo`
- `CambiarProximoInicio`
- `Activo`
- `FechaHora_Grabacion`
- `FechaHora_Modificacion`

## Acciones iniciales sugeridas

- `Nuevo`
- `Editar`
- `Dar de baja`
- `Cambiar contrasena`

## Filtros iniciales sugeridos

- busqueda por nombre o email
- activos / inactivos
- grupos / usuarios comunes

## Campos postergados para una segunda etapa

Aunque existen en `TA_USUARIOS`, no forman parte de la primera version:

- permisos comerciales
- permisos de stock
- configuracion de mail saliente
- `Administrador`
- `Grupo`
- deposito
- vendedor
- otros flags operativos avanzados

Estos campos podran incorporarse despues de estabilizar la base del ABM.

## Estructura tecnica sugerida

Archivos esperados cuando se implemente:

- `Components/Pages/Usuarios.razor`
- `Models/UsuariosModels.cs`
- `Services/IUsuariosService.cs`
- `Services/UsuariosService.cs`

Opcional si se separa la logica sensible:

- `Services/UsuariosPasswordCodec.cs`

## Objetivo del piloto

El modulo `Usuarios` debe servir para validar:

- el patron de grilla base
- el patron de formulario
- el patron de validacion por entidad
- el manejo de baja logica
- el tratamiento de campos sensibles
- la integracion de imagenes por filesystem

Si ese piloto queda estable, la misma base podra repetirse luego en otros maestros del sistema.
