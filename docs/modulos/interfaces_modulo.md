# Modulo Interfaces

## Objetivo

Este documento define el diseno funcional y tecnico inicial del modulo `Interfaces`, enfocado primero en una bandeja de **recepcion documental**.

La prioridad de esta etapa es resolver:

- alta de comprobantes recibidos
- carga de uno o varios archivos por comprobante
- clasificacion por tipo documental
- seguimiento por estado
- trazabilidad completa de usuario, PC, fecha/hora y cambios

No se intenta en esta primera etapa vincular automaticamente estos registros con compras, stock, contabilidad o proveedores.

## Reglas aplicadas

- Se respetaron `docs/CODEX_RULES.md`, `docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md` y `docs/CONFIGURACION_GLOBAL.md`.
- Las configuraciones dinamicas se apoyan en `TA_CONFIGURACION`.
- Los errores tecnicos relevantes del modulo deben registrar en `AUX_ERR` mediante la capa centralizada actual.
- El modulo nace con tablas nuevas e independientes para reducir impacto sobre circuitos existentes.

## Concepto funcional

La unidad principal del modulo no es el archivo individual sino el **comprobante recibido**.

Ejemplo:

- una factura con tres imagenes o PDF distintos debe registrarse como un solo comprobante recibido
- cada archivo queda asociado como adjunto del mismo comprobante

Eso permite:

- observacion unica por caso
- estado operativo unico
- historial unico
- control de edicion segun estado
- mejor auditoria y trazabilidad

## Modelo de datos propuesto

### 1. `INT_ESTADO`

Catalogo de estados del flujo documental.

Campos clave:

- `Codigo`
- `Descripcion`
- `Orden`
- `Activo`
- `PermiteEdicion`
- `EsInicial`
- `EsFinal`

Estados iniciales sugeridos:

- `A_PROCESAR`
- `EN_PROCESO`
- `PROCESADO`
- `APROBADO`
- `RECHAZADO`
- `ANULADO`

### 2. `INT_TIPO_DOCUMENTO`

Catalogo de clasificacion documental.

Tipos iniciales sugeridos:

- `COMPROBANTE_COMPRA`
- `LIQUIDACION_BANCARIA`
- `LIQUIDACION_TARJETA`
- `LISTA_PRECIOS`
- `MOVIMIENTO_STOCK`
- `GASTO`
- `OTROS`

### 3. `INT_COMPROBANTE_RECIBIDO`

Cabecera del caso documental.

Guarda:

- tipo principal
- estado actual
- observacion
- usuario y PC de alta/modificacion/anulacion
- fecha/hora de alta, estado y anulacion
- cantidad de adjuntos
- ruta base utilizada

### 4. `INT_COMPROBANTE_RECIBIDO_ADJUNTO`

Archivos fisicos asociados al comprobante.

Guarda:

- nombre original
- nombre guardado
- ruta relativa
- extension
- mime type
- tamano
- hash opcional
- orden de visualizacion
- marca de principal

### 5. `INT_COMPROBANTE_RECIBIDO_HIST`

Historial funcional del comprobante.

Acciones iniciales previstas:

- `ALTA`
- `MODIFICACION`
- `CAMBIO_ESTADO`
- `ANULACION`
- `ADJUNTO_ALTA`
- `ADJUNTO_BAJA`

## Configuracion central

Estas claves quedan previstas en `TA_CONFIGURACION`:

- `InterfacesRecepcionTipo`
- `InterfacesRecepcionNombre`
- `InterfacesRecepcionRuta`
- `InterfacesFtpHost`
- `InterfacesFtpPuerto`
- `InterfacesFtpUsuario`
- `InterfacesFtpClave`
- `InterfacesFtpPasivo`
- `InterfacesEstadoInicial`
- `InterfacesTamanoMaximoMb`
- `InterfacesExtensionesPermitidas`

### Criterio de lectura

El sistema debe buscar por `CLAVE`.

Para valores:

- usar `VALOR` si tiene contenido
- si `VALOR` esta vacio y `ValorAux` tiene datos, usar `ValorAux`

## Reglas funcionales iniciales

### Alta

- no permitir guardar un comprobante sin adjuntos
- el estado inicial debe salir de configuracion
- el tipo documental es obligatorio
- la observacion es opcional pero recomendada

### Edicion

- solo se permite si el estado actual tiene `PermiteEdicion = 1`
- se puede editar observacion, tipo y adjuntos
- toda modificacion debe dejar rastro en historial

### Cambio de estado

- debe validar existencia del estado destino
- debe registrar usuario, PC y fecha/hora
- debe dejar rastro en historial

### Anulacion

- se recomienda baja logica, no borrado fisico
- al anular debe completarse fecha/hora, usuario y PC
- el estado final esperado es `ANULADO`

### Archivos

- se guardan fisicamente en la ruta configurada
- se recomienda estructura por anio/mes/id de comprobante
- se debe renombrar el archivo fisico para evitar colisiones
- el nombre original siempre debe conservarse en base

## Estructura fisica sugerida

Ruta base:

- `InterfacesRecepcionRuta`

Estructura recomendada:

- `YYYY_MM\IdComprobanteRecibido\`

Ejemplo:

- `C:\DocumentosCompras\2026_05\125\`

Ventajas:

- orden operativo
- menor riesgo de colisiones
- mejor soporte y backup

## Flujo de UI recomendado

### Pantalla 1 - Bandeja

Listado principal con filtros por:

- fecha desde / hasta
- estado
- tipo documental
- usuario de alta
- texto libre

Columnas sugeridas:

- numero interno
- fecha/hora
- tipo
- observacion resumida
- estado
- usuario
- cantidad de adjuntos

### Pantalla 2 - Nuevo comprobante

Formulario con:

- tipo documental
- observacion
- carga multiple de archivos
- lista previa de adjuntos

### Pantalla 3 - Detalle

Muestra:

- cabecera
- adjuntos
- historial
- acciones permitidas segun estado

## Diseno tecnico sugerido en AlfaCore

### Pages

- `src/AlfaCore/Components/Pages/Interfaces.razor`
- `src/AlfaCore/Components/Pages/InterfacesDetalle.razor`

### Models

- `src/AlfaCore/Models/InterfacesModels.cs`

### Services

- `src/AlfaCore/Services/IInterfacesService.cs`
- `src/AlfaCore/Services/InterfacesService.cs`
- `src/AlfaCore/Services/IInterfacesConfigService.cs`
- `src/AlfaCore/Services/InterfacesConfigService.cs`

### Repositories

- `src/AlfaCore/Repositories/IInterfacesRepository.cs`
- `src/AlfaCore/Repositories/InterfacesRepository.cs`

## Alcance recomendado para la primera implementacion

### Incluir

- grilla principal
- alta de comprobante recibido
- adjuntos multiples
- lectura de ruta desde `TA_CONFIGURACION`
- alta de historial
- cambio manual de estado
- anulacion logica
- descarga de adjuntos

### Dejar para segunda etapa

- OCR
- clasificacion automatica
- asociacion automatica a proveedor
- aprobaciones multinivel
- integracion con circuitos contables o de compras reales

## Script inicial

El script base propuesto para crear estas tablas y configuraciones iniciales queda en:

- `docs/interfaces_modelo_inicial.sql`
