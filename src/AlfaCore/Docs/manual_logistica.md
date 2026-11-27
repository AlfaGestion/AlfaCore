# Logística: carga de viajes

El módulo **Logística** registra los viajes que se hacen para los clientes, con lo que se le cobra al cliente y lo que se le paga al chofer o fletero. Sus pestañas son **Viajes**, **Tarifas**, **Choferes**, **Destinos**, **Tipo vehículo**, **Reportes**, **Liquidaciones** y **Configuración**.

## Cargar un viaje

1. En **Viajes**, pulsá [[bi:plus-circle]] **Nuevo viaje**.
2. Completá la **Fecha**, el **Cliente**, el **Destino**, el **Chofer** y el **Tipo vehículo**.
3. El sistema busca la tarifa que corresponde y calcula el **Total cliente** y el **Total fletero**. Revisalos.
4. Pulsá [[bi:floppy]] **Guardar**.

El listado se filtra con el buscador; [[bi:sliders]] **Configurar vista** elige las columnas y [[bi:floppy]] **Guardar vista** la deja así. [[bi:file-earmark-excel]] **Exportar Excel** descarga los viajes filtrados.

## Tarifas

En **Tarifas** se cargan los precios por destino y tipo de vehículo, generales o por cliente. [[bi:plus-circle]] **Nueva tarifa** agrega una. [[bi:percent]] **Aumento de tarifa** sube varias tarifas a la vez por porcentaje. [[bi:stars]] **Actualizar con IA** propone tarifas nuevas a partir de una lista o un archivo; revisalas antes de grabar.

## Choferes, destinos y tipos de vehículo

Son los maestros del módulo. En cada pestaña usá [[bi:plus-circle]] **Nuevo chofer**, [[bi:plus-circle]] **Nuevo destino** o [[bi:plus-circle]] **Nuevo tipo de vehículo**, completá **Código**, **Descripción** y si está **Activo**, y [[bi:floppy]] **Guardar**.

## Reportes de viajes

En **Reportes** elegí el período y los filtros (cliente, chofer, destino) y pulsá [[bi:search]] **Buscar**. [[bi:printer]] **Imprimir** y [[bi:file-earmark-excel]] **Exportar Excel** sacan el reporte.

## Liquidaciones a choferes y fleteros

En **Liquidaciones** elegí el chofer o fletero y el período para ver los viajes a pagar.

1. Seleccioná los viajes a liquidar.
2. Revisá el total y la vista previa ([[bi:printer]] **Imprimir** genera la liquidación).
3. Pulsá [[bi:cash-coin]] **Marcar pagados** y [[bi:check2-circle]] **Confirmar pago**. Los viajes ya pagados no se vuelven a liquidar; si se pagó una parte, se informa como pago parcial.

## Configuración

En **Configuración** se definen los parámetros del módulo (por ejemplo, cómo se calculan los totales). Pulsá [[bi:pencil-square]] **Editar configuración**, hacé los cambios y [[bi:floppy]] **Guardar**.
