# Hoja de ruta de demostración: Conciliación y Cobranzas

## Preparación y alcance

Base para trabajar: **SERVER-ALFAVB6 / ALFANET_DEMO**. El lote se llama **DEMO-CONCILIACION-202610** y utiliza septiembre y octubre de 2026. Todos sus clientes, identificadores, movimientos e importes son ficticios. Los identificadores con prefijo 99 permiten probar coincidencias de CUIT, pero **no son documentos fiscales válidos**. Las ventas se representan mediante proformas FP X; no es necesario emitir facturas electrónicas.

**Estado al 26/10/2026:** se creó ALFANET_DEMO desde un respaldo de ALFANET, con archivos físicos propios, y se guardó un respaldo inicial independiente. Hay 60 clientes ficticios, 333 comprobantes, 679 renglones contables y 27 proformas pendientes del lote; los asientos balancean y TC/CUENTA se guardaron sin relleno. Los extractos DEMO se dejan sin importar para realizar ese paso durante la presentación. ALFANET mantiene sus otros usos y el restablecedor no la modifica.

La copia conserva información histórica heredada: **no es una base completamente anonimizada**. Para compartir la pantalla, utilizar las cuentas y clientes DEMO y evitar los listados históricos. Se retiraron de la copia las credenciales externas heredadas para pagos, correo y mensajería; no utilizarla para operaciones fiscales reales.

Los extractos nuevos conservan los formatos de los archivos de Santander, Provincia y Mercado Pago. Se generan libros nuevos, sin copiar filas, nombres, identificadores, propiedades, comentarios, hojas ocultas ni imágenes de los originales.

Material de la demostración: `C:\dev\Demos\Conciliacion\`. **Extractos** contiene los archivos bancarios; **Guia/Guion_demostracion.xlsx** reúne checklist, casos, referencias, comprobantes y resultados esperados. **Scripts** contiene el restablecedor y **Versiones** conserva cada entrega del material.

## Cuentas y archivos

| Banco | Cuenta del lote | Archivo inicial | Segunda descarga |
|---|---|---|---|
| Santander | 111020901 | Santander_DEMO_01_mes.xlsx | Santander_DEMO_02_semana_superpuesta.csv |
| Provincia | 111020902 | Provincia_DEMO_01_mes.xlsx | Provincia_DEMO_02_semana_superpuesta.csv |
| Mercado Pago | 111010904 | MercadoPago_DEMO_01_mes.xlsx | MercadoPago_DEMO_02_semana_superpuesta.csv |

Cada archivo inicial contiene **61 movimientos**. Cada segunda descarga contiene **44 filas: 42 repetidas y 2 nuevas**. Después de las dos importaciones deben quedar **63 movimientos únicos por cuenta**, 189 entre los tres bancos. Los importes, fechas, descripciones y referencias de las filas repetidas se conservan exactamente entre los archivos del lote.

Se prepararon **60 clientes, 333 comprobantes y 679 renglones contables**, utilizando cuentas propias del lote. Las contrapartidas auxiliares son 43991 (ingresos por servicios), 53991 (gastos bancarios), 53992 (impuestos bancarios), 43992 (intereses ganados) y 11991 (retenciones IIBB). No se reemplazan cuentas existentes: si un código está ocupado, el sembrador se detiene.

## Antes de compartir la pantalla

1. Confirmar que el selector muestra **ALFANET_DEMO en SERVER-ALFAVB6**. Si no aparece, registrar una conexión a esa base sin reemplazar la conexión existente de ALFANET.
2. Si se eligió conservar información anterior, usar únicamente las cuentas y clientes del lote; no mostrar búsquedas generales, importaciones anteriores ni otros módulos. Para una presentación pública sin restricciones se necesita una base exclusiva de demostración.
3. Verificar ejercicio abierto para septiembre/octubre de 2026, talonario CB X disponible y permisos del usuario que presentará.
4. Abrir Conciliación bancaria y limpiar filtros antiguos. En **Conciliar cuenta**, usar período **01/10/2026 a 31/10/2026**, pendientes y la cuenta DEMO elegida.
5. Revisar criterios: tolerancia **1,00**, margen **3 días**, comparación de fecha activada y referencia opcional. Si la base conserva datos anteriores, anotar la configuración previa antes de cambiarla y restaurarla al terminar.
6. Revisar **Cuentas para asientos**. Se agregaron doce reglas específicas para los bancos DEMO, conservando las existentes. Elegir las cuentas DEMO por sus códigos para comisiones, intereses e IIBB; no aceptar una contrapartida de otro conjunto de datos. La clasificación sugerida puede requerir corrección antes de generar el asiento.
7. Verificar que la IA esté configurada si se va a demostrar. Se envían al proveedor las filas analizadas: seleccionar exclusivamente una cuenta del lote.

## Recorrido breve: 15 a 20 minutos

### 1. Importar y buscar — 3 minutos

Importar los tres archivos `01_mes.xlsx`, asignando a cada uno su cuenta del lote. Verificar banco detectado, cantidad y total. Mostrar revisión de una importación y luego todas las importaciones. Buscar un importe sin conocer el banco; filtrar por cliente, clasificación, registración y conciliación. Ordenar pulsando un encabezado y pasar a la segunda página de una lista de 61 filas.

### 2. Comparar lo registrado — 4 minutos

En **Conciliar cuenta**, comparar una cuenta DEMO. Mostrar las dos listas y explicar que **Cobrado** indica que existe el recibo y **Conciliado** indica que se confirmó su vínculo con el banco.

- **E01, ALMACEN LUNA:** importe y fecha coinciden. Revisar la propuesta y confirmarla.
- **E02, PANADERIA NUBE:** el recibo supera al banco en **0,22**. Aceptar la diferencia dentro de tolerancia, escribir un motivo y confirmar. Los importes originales deben mantenerse.
- **E03, TALLER HORIZONTE:** el recibo tiene fecha dos días posterior. Debe poder proponerse dentro del margen.
- **E04, FARMACIA ESTRELLA:** la diferencia es de siete días. Con margen de tres días no debe tratarse como una coincidencia automática; revisar antes de ampliar el criterio o vincular manualmente.

Abrir **Historial** y comprobar los vínculos, usuario y motivo. Desconciliar uno de prueba si se quiere repetir ese paso.

### 3. Cuenta corriente y cobranzas — 5 minutos

- **E10, SERVICIOS CEDRO:** el pago ya fue registrado seis días antes con diferencia de **0,81**. Abrir cuenta corriente, localizar el recibo, consultar el comprobante y su asiento completo. Revisarlo antes de usar **Marcar como cobrado**; no crear un segundo recibo.
- **E05, DISTRIBUIDORA RIO:** un depósito paga dos proformas, distribuidas 40 % y 60 %. En **Cobrar**, revisar la selección múltiple y confirmar un único recibo. Las dos proformas deben quedar canceladas.
- **E06, TRANSPORTES COMETA:** la proforma supera el depósito en **500,00**. Existe una cobranza anterior del mismo cliente con retención IIBB. Revisar la cuenta sugerida, completar/corregir el importe restante y confirmar solo después de verificar el saldo.
- **E07, HOTEL BRISA:** el depósito cubre la mitad de la proforma. Mostrar el saldo restante; la diferencia no representa necesariamente una retención.

### 4. Gastos e inteligencia asistida — 4 minutos

- **E12:** la comisión ya tiene asiento. Conciliar ese registro existente.
- **E13:** la comisión no está contabilizada. Seleccionar el movimiento, **Generar asiento**, revisar banco, contrapartida 53991, fecha y balance, y confirmar. Volver al historial para mostrar el vínculo.
- **E14/E15:** repetir la revisión con IIBB o intereses si hay tiempo; comprobar la clasificación y la cuenta adecuada.
- **Asistir con IA:** analizar pendientes de una cuenta DEMO, leer la evidencia de las propuestas y confirmar solo las seleccionadas. El resultado del modelo puede variar; la condición de aprobación es que respete cuenta, sentido, tolerancia, fechas y revisión del usuario.
- Abrir **? → Preguntar a IA** y consultar «¿Cómo verifico si una cobranza ya está registrada?». Abrir la fuente del manual y volver al chat.

### 5. Importación superpuesta — 2 minutos

Importar los tres archivos `02_semana_superpuesta.csv`. Por banco deben reconocerse **42 repetidos y 2 nuevos**. No deben duplicarse los movimientos anteriores, sus recibos ni sus conciliaciones. Reimportar el mismo archivo como verificación adicional: no deben aparecer nuevos movimientos.

## Casos adicionales para una demostración extendida

| Caso | Qué mostrar | Resultado que se debe comprobar |
|---|---|---|
| E08 | Pagador ficticio con nombre distinto del cliente | Asignación manual y opción de recordar; luego reevaluar la propuesta. |
| E09 | Cliente identificado sin factura pendiente | Se muestra anticipo/sin pendientes; no confirmar una aplicación inexistente. |
| E11 | Dos empresas con igual CUIT, uno con guiones | Comparar CUIT normalizado y el importe pendiente; no elegir solo por nombre o CUIT. |
| E16 | Transferencias entre las tres cuentas | Santander −55.000 / Provincia +55.000 y Mercado Pago −33.000 / Santander +33.000. La entrada de 33.000 de Santander no está en el extracto: sirve para mostrar un movimiento de Alfa aún sin contraparte bancaria. |
| E17 | Dos depósitos contra un recibo | Seleccionar ambos movimientos bancarios y un renglón contable; verificar igualdad de totales antes de conciliar. |
| E18 | Dos recibos del mismo importe | Mantener la ambigüedad para revisión; no confirmar ambos contra un único depósito. |
| V01–V42 | Volumen de movimientos | Probar búsqueda, orden, paginación, filtros y propuestas a través de varias páginas. |
| N1/N2 | Nuevos ingresos de la segunda descarga | Quedan como movimientos nuevos; sus proformas ya existen para poder cobrarlos durante la demo. |

## Registrar el resultado y repetir

En la hoja **Checklist** del guion anotar fecha, presentador, cuenta, paso, resultado obtenido y observaciones. Distinguir una sugerencia del sistema de una operación confirmada. Registrar también cualquier caso que no se haya podido demostrar.

Para empezar otra demostración:

1. Terminar de anotar el resultado y cerrar las pantallas abiertas de ALFANET_DEMO.
2. Ejecutar `C:\dev\Demos\Conciliacion\Scripts\Restablecer_DEMO.cmd`.
3. Ingresar usuario y clave SQL autorizados. La clave no se guarda en el script.
4. Revisar que diga SERVER-ALFAVB6 / ALFANET_DEMO y confirmar escribiendo **ALFANET_DEMO**.
5. La herramienta verifica el respaldo `E:\MSSQL\BACKUP\ALFANET_DEMO.bak`, cierra las conexiones de esa base, restaura y vuelve a habilitar su acceso. Comprueba el lote y deja un registro en **Logs**.
6. Actualizar o volver a conectar AlfaCore. Las cobranzas, asientos, importaciones, conciliaciones y numeración vuelven al punto inicial. Los Excel y notas locales del presentador se conservan; el guion original queda en Versiones.

Para revisar el destino y el respaldo sin restaurar, ejecutar `python Scripts/restablecer_demo.py` desde la carpeta de la demostración. La restauración requiere la opción `--restaurar` y la confirmación indicada.

Los scripts de siembra no son un limpiador: si el lote ya existe se detienen. No borrar comprobantes, movimientos ni configuración para reiniciar una demo. El restablecedor solo admite ALFANET_DEMO; no tiene una opción para apuntar a ALFANET u otra base.

## Cuando haya actualizaciones o correcciones

Guardar los archivos corregidos en su carpeta y conservar una nueva entrega en **Versiones**, con fecha y nombre propios. Restaurar el estado inicial, aplicar la corrección en ALFANET_DEMO, probar el recorrido y recién después preparar un nuevo respaldo inicial. Archivar la versión anterior del `.bak` en el servidor antes de reemplazarlo; el script de creación se detiene si ya existe `ALFANET_DEMO.bak`. No guardar como punto inicial una demostración que quedó a mitad de su recorrido.

El respaldo previo a la preparación contiene los datos originales: es material administrativo del servidor y **no debe incluirse en el paquete público de la demostración**.
