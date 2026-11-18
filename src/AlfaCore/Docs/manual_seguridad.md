# Seguridad

Pantallas para administrar quién puede usar cada opción de AlfaCore. Los cambios se aplican sobre la base activa. Está en **Archivos → Seguridad**, como en Alfa Gestión, y sus pestañas son Usuarios, Autorización de tareas, Perfiles y Accesos a Conversaciones. El **Reporte de auditoría** también está en Archivos → Seguridad y tiene su propio manual (Auditoría).

## Usuarios

Alta, edición y baja de los usuarios que entran al sistema.

**Crear un usuario**

1. Pulsá **Nuevo usuario**.
2. Completá **Nombre** (con ese nombre ingresa al sistema) y **Email**.
3. Definí la contraseña inicial. Si marcás **Clave próximo inicio**, el usuario tiene que cambiarla la primera vez que entra.
4. Pulsá **Guardar**.

Después, en **Autorización de tareas**, definí qué puede usar (o asignale un perfil).

**Editar o dar de baja**

Hacé clic en el usuario para abrir su ficha y editarla. **Dar de baja** lo deja inactivo: no puede ingresar, pero su historial se conserva. Para varios usuarios, marcalos y usá **Dar de baja** desde la selección.

**Buscar y filtrar**

El buscador filtra por nombre o email. El embudo permite ver activos o inactivos; el filtro **Activos** viene aplicado.

## Autorización de tareas

Define qué opciones del menú web de AlfaCore puede usar cada usuario y sus asignaciones (unidad de negocio, caja y depósito). Estos permisos son propios de AlfaCore: no cambian los permisos del Alfa Gestión de escritorio.

**Elegir el usuario**

1. Abrí el embudo de la barra superior.
2. En **Usuario** elegí a quién querés revisar.
3. Pulsá **Aplicar**. El nombre del usuario queda en el chip del buscador y debajo se carga su árbol de permisos.

Si se agregó un usuario mientras tenías la pantalla abierta, pulsá **Recargar usuarios**.

**Buscar una opción del menú**

Escribí en la barra superior parte del nombre de la opción, por ejemplo «cobranzas». El árbol se filtra mientras escribís. Borrá el texto para volver a ver todo.

**Marcar permisos**

- Marcá o desmarcá cada opción. Una carpeta con la etiqueta **PARCIAL** tiene algunas opciones habilitadas y otras no.
- **Seleccionar visible** y **Deseleccionar visible** actúan solo sobre lo que se ve en el árbol. Combinado con la búsqueda, sirve para habilitar de una vez todas las opciones que coinciden.
- **Expandir todo** y **Contraer todo** abren o cierran las carpetas.
- **Aplicar mismos permisos que…** copia los permisos de otro usuario: elegí el usuario de origen y pulsá **Copiar permisos**. Revisá el resultado antes de grabar.

**Asignaciones del usuario**

- **Administrador del sistema** marca al usuario como administrador. Asignalo solo a quien administra el sistema.
- **Puede ver y realizar proformas** habilita los comprobantes proforma y su inclusión en los reportes.
- **Unidad de negocio** es obligatoria; si no se elige, se usa la unidad 1.
- **Caja asignada** y **Depósito asignado** son opcionales. Las cajas se filtran según la unidad elegida.

**Grabar o cancelar**

- Con el primer cambio se habilitan **Grabar** y **Cancelar** en la barra superior.
- **Grabar** guarda los cambios del usuario.
- **Cancelar** descarta todo lo que no grabaste y vuelve a mostrar lo que estaba guardado.
- Mientras haya cambios sin grabar no podés consultar otro usuario. Primero grabá o cancelá.

**Quién ve qué (la regla)**

- Un usuario **sin ninguna opción tildada ve todo** el menú web. Apenas le tildás una y grabás, pasa a ver **solo lo tildado**. La pantalla te avisa antes de grabar cuando eso cambia.
- Algunas opciones dicen **Solo si se tilda** (por ejemplo **Configurar Conversaciones** y **Ver todos los números**). Esas las tiene únicamente quien las tenga tildadas: no se dan por "ver todo" y tampoco cuentan para la regla anterior, así que tildarlas no le esconde el resto del menú. Al tildar un módulo entero tampoco se tildan solas: se marcan una por una.
- Conversaciones tiene tres partes que se dan por separado: **Plantillas de WhatsApp**, **Configurar Conversaciones** (canales, números, asistente IA y reglas; también la tiene el administrador del sistema) y **Ver todos los números** (ver y responder los chats de todos los números).
- Si alguien entra a una pantalla para la que no tiene permiso, ve un aviso que le dice qué permiso le falta.

**Perfil del usuario**

En el panel de la derecha, **Perfil** asigna un perfil (ver *Perfiles de permisos*). Las opciones del perfil se suman a las del usuario y aparecen tildadas con la marca **Perfil**: no se pueden destildar desde acá; se cambian en el perfil. Elegí **Sin perfil** para sacárselo y grabá.

## Perfiles de permisos

Un perfil es un grupo de permisos con nombre (por ejemplo **Vendedor** o **Administración**) que se asigna a los usuarios. Así no hace falta tildar opción por opción en cada uno: cuando cambiás el perfil, el cambio les llega a todos los que lo tienen.

**Crear un perfil**

1. Entrá a **Seguridad → Perfiles** y pulsá **Nuevo perfil**.
2. Poné el **Nombre** y, si querés, una **Descripción**.
3. Tildá las opciones del árbol. Podés buscar una opción escribiendo parte de su nombre.
4. Pulsá **Grabar**.

**Asignarlo a un usuario**

En **Seguridad → Autorización de tareas**, consultá el usuario, elegí el perfil en **Perfil** y grabá. Cada usuario tiene un perfil como máximo; además puede tener opciones propias.

**Cambiar o eliminar un perfil**

- Elegí el perfil en la lista, cambiá lo que necesites y pulsá **Grabar**: se aplica en el momento a todos sus usuarios (la pantalla dice quiénes lo tienen).
- **Eliminar** borra el perfil. Sus usuarios se quedan sin perfil y conservan solo sus opciones propias.

## Accesos a Conversaciones

Define quién ve las conversaciones de WhatsApp. Antes estaba en Conversaciones → Configuración → Operación y accesos.

**Ven todos los números**

Marcá a quienes coordinan la atención (supervisores, encargados). Ven y responden las conversaciones de cualquier número. No les da permiso para configurar Conversaciones. Es lo mismo que el permiso **Ver todos los números** de Autorización de tareas.

**Quién atiende cada número**

Para cada número de WhatsApp, marcá qué usuarios lo atienden y pulsá **Guardar**. Un número sin ningún usuario marcado lo ven todos.

Para cambiar estos accesos hace falta el permiso **Configurar Conversaciones** o ser administrador del sistema.
