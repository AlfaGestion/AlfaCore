# Módulo Conversaciones

## Etapa 1

Este documento cubre la primera entrega pedida para el módulo `Conversaciones`:

- relevamiento de tablas existentes
- validación de objetos oficiales o candidatos
- propuesta de modelo de datos
- criterios para los scripts SQL iniciales

## Reglas aplicadas

- Se respetó `docs/CODEX_RULES.md`.
- Se validó primero `docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md`.
- Se consultó `docs/DATABASE_TABLES_SUMMARY.md` solo para resolver tablas no cubiertas por el archivo prioritario.
- No se propone rehacer módulos existentes.
- Todo error relevante del futuro módulo debe registrarse en `AUX_ERR` mediante el servicio centralizado actual.

## Relevamiento de tablas existentes

### 1. Clientes

Fuente oficial confirmada:

- `VT_CLIENTES`

Criterio:

- Es la fuente funcional oficial para leer clientes.
- No conviene leer clientes directamente desde `MA_CUENTAS` para lógica funcional.

Campos relevantes detectados:

- clave funcional: `CODIGO`
- descripción principal: `RAZON_SOCIAL`
- campos útiles para conversaciones: `CONTACTO`, `TELEFONO`, `MAIL`, `LOCALIDAD`, `DADA_DE_BAJA`

Observación:

- Como `VT_CLIENTES` es una vista, la integridad física de una FK del nuevo módulo no puede apuntar a la vista.
- Para integridad física en SQL Server, el modelo nuevo guarda `ClienteCodigo` y puede relacionarse por FK con `MA_CUENTAS.CODIGO`.
- Para lecturas funcionales del módulo, debe usarse `VT_CLIENTES`.

### 2. Técnicos / agentes

Objeto confirmado por definición funcional del proyecto:

- `V_TA_Tecnicos`

Objeto relacionado de soporte:

- `TA_USUARIOS`

Criterio confirmado:

- La asignación operativa del inbox debe apoyarse en `V_TA_Tecnicos`.
- `TA_USUARIOS` sigue siendo importante porque `V_TA_Tecnicos` ya relaciona `UsuarioAsociado` y `SistemaAsociado` con usuarios del sistema.

Campos relevantes detectados en `V_TA_Tecnicos`:

- clave: `IdTecnico`
- descripción principal: `Nombre`
- otros campos útiles: `Cargo`, `Telefono`, `UsuarioAsociado`, `SistemaAsociado`, `Baja`

Campos relacionados en `TA_USUARIOS`:

- clave: `NOMBRE` + `SISTEMA`
- uso esperado: auditoría, permisos, acciones ejecutadas desde la app

Conclusión:

- El módulo `Conversaciones` debe guardar la asignación principal por `IdTecnico`.
- Cuando haga falta identificar el usuario del sistema vinculado al técnico, se resolverá a través de `V_TA_Tecnicos.UsuarioAsociado` y `V_TA_Tecnicos.SistemaAsociado`.

### 3. Contactos varios

Objeto confirmado por definición funcional del proyecto:

- `MA_CONTACTOS`

Criterio confirmado:

- Para contactos del módulo se usa solo `MA_CONTACTOS`.
- En esta tabla ya conviven clientes, proveedores y contactos varios.

Campos relevantes detectados:

- `MA_CONTACTOS.id`: PK física
- `MA_CONTACTOS.idContacto`: identificador legacy disponible en la misma tabla
- `MA_CONTACTOS.Nombre_y_Apellido`
- `MA_CONTACTOS.Telefono`
- `MA_CONTACTOS.Celular`
- `MA_CONTACTOS.email`
- `MA_CONTACTOS.Cargo`

Conclusión:

- El vínculo principal del módulo debe guardarse con `MA_CONTACTOS.id`.
- `idContacto` queda como dato legacy disponible dentro de la misma tabla para integraciones futuras si hace falta, pero no como FK separada del módulo.

### 4. Estados

Objeto detectado:

- `TA_ESTADOS`

Resultado de validación:

- `TA_ESTADOS` no representa estados de conversación.
- En la documentación del proyecto aparece como catálogo de provincias.

Conclusión:

- No debe reutilizarse `TA_ESTADOS` para el estado del inbox.
- Hace falta una tabla nueva específica para el dominio de conversaciones.

Propuesta:

- crear `CONV_ESTADOS`

Estados iniciales sugeridos:

- `ABIERTA`
- `PENDIENTE`
- `EN_GESTION`
- `CERRADA`
- `ARCHIVADA`

## Modelo de datos propuesto

### Objetos nuevos

- `CONV_ESTADOS`
- `CONV_CONVERSACIONES`
- `CONV_MENSAJES`
- `CONV_ASIGNACIONES`
- `CONV_ETIQUETAS`
- `CONV_CONVERSACION_ETIQUETAS`
- `CONV_ADJUNTOS`
- `CONV_WEBHOOK_LOG`

### Decisiones de diseño

#### Conversación

`CONV_CONVERSACIONES` concentra:

- número WhatsApp normalizado
- vínculo opcional con cliente
- vínculo opcional con contacto
- estado actual
- asignación actual
- resumen de último mensaje
- timestamps operativos

#### Mensajes

`CONV_MENSAJES` guarda:

- mensajes entrantes
- mensajes salientes
- notas internas
- identificador oficial de Meta cuando exista
- payload original cuando haga falta trazabilidad

#### Asignaciones

Se separa `CONV_ASIGNACIONES` para conservar historial de cambios de agente/técnico sin perder la asignación vigente de la conversación.

#### Estados

Se crea `CONV_ESTADOS` porque el catálogo encontrado en la base (`TA_ESTADOS`) no corresponde al dominio.

#### Contactos

La referencia principal queda simplificada:

- `IdContacto` para `MA_CONTACTOS.id`

#### Integración WhatsApp

Se deja preparado:

- log de webhook recibido
- payload original
- identificador oficial del mensaje
- estado de envío
- tipo de mensaje

## Relaciones propuestas con objetos existentes

### Cliente

- lectura oficial: `VT_CLIENTES`
- FK física sugerida: `MA_CUENTAS(CODIGO)`
- campo nuevo: `CONV_CONVERSACIONES.ClienteCodigo`

### Agente asignado

- referencia principal: `V_TA_Tecnicos(IdTecnico)`
- campo nuevo: `IdTecnico`
- resolución de usuario asociado: `V_TA_Tecnicos.UsuarioAsociado` + `V_TA_Tecnicos.SistemaAsociado`

### Contacto

- referencia física: `MA_CONTACTOS(id)`
- campo nuevo: `IdContacto`

### Estado

- referencia nueva: `CONV_ESTADOS(CodigoEstado)`

## Notas de arquitectura para etapas siguientes

### Backend

Servicios a crear en próximas etapas:

- listado de conversaciones
- lectura de mensajes por conversación
- creación de mensaje saliente
- registro de mensaje entrante
- asignación de conversación
- cambio de estado
- adaptación del logging central usando `AUX_ERR`

### Tiempo real

No se detectó configuración actual de SignalR en el código relevado.

Conclusión:

- no conviene asumir tiempo real ya activo
- sí conviene diseñar servicios y DTOs dejando preparada una futura capa de notificación

### Webhook de Meta

Se deja previsto:

- endpoint de entrada
- tabla de log técnico
- payload bruto
- marca de procesamiento correcto/error

## Riesgos y dudas abiertas

### 1. Cliente vs contacto

Una conversación puede existir:

- solo con teléfono y contacto
- con cliente identificado
- o con contacto ligado a una cuenta

Por eso el modelo propuesto mantiene los vínculos opcionales y no obliga a que toda conversación tenga cliente desde el inicio.
