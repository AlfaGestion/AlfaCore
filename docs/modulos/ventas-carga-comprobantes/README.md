# Ventas — Carga de Comprobantes

Fecha de inicio: 2026-10-14. Actualización: 2026-10-15. Estado: consulta, simulación y grabación comercial acotada en homologación, con ensayo transaccional previo y recuperación de operación. El circuito completo incluye factura, detalle, stock aplicable, asiento/IVA y aplicación al origen. No está habilitada la emisión en producción ni certificada la equivalencia general de todas las configuraciones legacy.

## Objetivo y alcance acordado

Migrar progresivamente `V_MV_VENTAS.vbp` a AlfaCore conservando los efectos comerciales, contables, fiscales y de stock de los circuitos incluidos. Primera prioridad: **papeleras, mayoristas y distribuidoras que facturan pedidos o remitos**, individualmente o consolidados, con cantidades parciales.

Ampliación del 15/10: factura directa de artículos, notas de crédito y débito electrónicas asociadas, y proforma con asiento obligatorio, fuera del libro de IVA y con IVA según configuración. Los circuitos están conectados a pantalla, grabación, recuperación y PDF; ver [alcance y validaciones](carga-directa-notas-proformas.md).

Fuente revisada: `\\server-alfavb6\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT_DEV\V_MV_VENTAS.vbp`. Hay que confirmar que el ejecutable y las bibliotecas usados por los clientes corresponden a estas fuentes antes de certificar equivalencia.

En esta etapa se excluyen por pedido del usuario:

- Lectura de balanzas y comunicación con equipos mediante OCX.
- Centros de costo.
- Generación, traslado y liberación de reservas.
- Circuito de autorización para emitir comprobantes.

La exclusión de autorización de emisión no elimina los permisos de acceso del usuario, los controles fiscales ni las validaciones comerciales. No se portan la solicitud de PIN ni sus registros como condición de emisión en esta etapa. Cualquier otra política que mezcle una validación con la autorización requiere separar ambas antes de implementarla.

No se alteran globalmente configuraciones legacy para desactivar estas capacidades. Las exclusiones pertenecen al alcance del módulo nuevo. Los documentos de origen que requieran reservas, preparación o distribución de centros de costo para mantener consistencia no se habilitan hasta establecer un circuito compatible. No se borran reservas existentes ni se omite silenciosamente un efecto requerido.

## Evidencia y documentos

- [Avances, pendientes priorizados y registro por pasada](../../gestion/ventas-carga-comprobantes-avances-y-pendientes.md). Por pedido del usuario, actualizar ese registro al cerrar cada pasada del módulo, con cambios, pruebas efectivas, pendientes y siguiente paso.
- [Matriz de compatibilidad y pruebas](matriz-compatibilidad.md).
- [Herramientas y procedimiento de comparación de grabaciones](comparacion-grabaciones.md).
- [Configuraciones: llamadas estáticas y dinámicas](configuracion-vb6.csv).
- [Inventario de fuentes y huellas SHA-256](inventario-fuentes.json).
- [Ejecutable instalado, menú y contexto de arranque](arranque-ejecutable-menu.md).
- [Consulta de metadatos SQL utilizada en el relevamiento](../../base-datos/sql-referencia/ventas_carga_comprobantes_relevamiento.sql).
- [Hallazgos SQL de DISTRIWALTERP y contrato del próximo incremento](sql-distriwalterp-hallazgos.md).
- [Incremento implementado: consulta y simulación](incremento-consulta-simulacion.md).
- [Incremento de cálculo y contrato de grabación](incremento-calculo-grabacion.md).
- [Corrección de pendientes y filtros operativos](filtros-pendientes.md).
- [Primera autorización electrónica en homologación](prueba-electronica-homologacion.md).
- [Grabación comercial, recuperación y recorrido de pruebas](grabacion-comercial-homologacion.md).
- [Carga directa, notas electrónicas y proformas: alcance implementado y pruebas](carga-directa-notas-proformas.md).
- [Edición de precios y descuentos: permisos, límites y pruebas](edicion-precios-descuentos.md).
- [Manual de usuario](../../../src/AlfaCore/Docs/manual_carga_comprobantes.md).

El CSV registra llamadas de `Cfg`, `Cfg$`, `Cfg2`, `Cfg2$` y `sp_Cfg`, con archivo, línea, rutina y si la clave es dinámica. No contiene valores de configuración ni credenciales. Es evidencia estática: no prueba que cada rama sea alcanzable, ni que las claves existan en una base. No incluye todos los lectores posibles o la lógica interna de DLL compiladas. Una línea corresponde al inicio de la sentencia VB6, que puede continuar en varias líneas.

Reproducción del inventario desde la raíz del repositorio:

```powershell
python tools/ventas/relevar_ventas_vb6.py '\\server-alfavb6\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT_DEV\V_MV_VENTAS.vbp'
```

El generador lee las referencias del VBP en CP1252 y escribe los dos inventarios en UTF-8 sin BOM. No ejecuta el programa ni accede a SQL. Exporta nombres de claves y referencias; no copia el VBP, líneas completas o argumentos de conexión.

## Decisiones de arquitectura

### Un motor común, configurado por capacidades

Mantener Blazor Server, C#, SQL Server y Dapper. Crear un servicio de aplicación específico de Carga de Comprobantes que coordine cálculo, validación y persistencia. No copiar la pantalla monolítica de VB6 ni usar `PuntoVentaService.CreateSaleAsync` como grabador universal.

El relevamiento organizó el motor en las responsabilidades siguientes. Los incrementos vinculados arriba distinguen las implementadas de los caminos todavía pendientes:

| Responsabilidad | Contrato esperado |
|---|---|
| Contexto de carga | Base activa, usuario, unidad de negocio, caja cuando aplique, depósito, TC y configuraciones efectivas |
| Configuración | Lectura centralizada desde `TA_CONFIGURACION`, con procedencia y defaults documentados por clave |
| Documentos de origen | Consultar pendientes y obtener cantidades y relaciones por línea, conservando documento y secuencia originales |
| Precios y totales | Cálculo determinista de precio, descuentos, impuestos, redondeos y moneda |
| Validador | Reutilizar `ValidationResult`/`ValidationIssue`; errores por campo y controles reales en servidor |
| Plan de grabación | Resolver explícitamente cabecera, detalles, stock, asientos, aplicaciones y estados antes de persistir |
| Persistencia | Dapper y objetos oficiales confirmados; una transacción para los efectos locales indivisibles |
| Integraciones | Electrónica y acciones posteriores con estados recuperables, sin duplicar operaciones externas |
| Estado del editor | Aislamiento por sesión/base/usuario, borrador y conservación de datos ante fallos |

Evitar variables globales compartidas entre usuarios. En Blazor, un cambio de base invalida contexto, búsquedas y borradores del contexto anterior; no permite grabar una carga de una base en otra accidentalmente.

### Configuración por rubro

El rubro ofrece un **perfil inicial de capacidades y presentación**. No cambia por sí mismo la semántica de IVA, numeración, stock o contabilidad. Una papelera puede ser también mayorista o distribuidora: no introducir tres motores distintos ni condiciones por nombre comercial del cliente.

| Capacidad | Primer uso previsto |
|---|---|
| Venta por unidad/bulto y equivalencias | Papeleras y mayoristas, si el maestro y sus unidades lo confirman |
| Lista y clase de precio, escalas/ofertas y descuentos | Todos los rubros prioritarios |
| Facturar uno o varios pedidos/remitos | Mayoristas y distribuidoras |
| Cantidades parciales y trazabilidad de origen | Todos los circuitos con documentos previos |
| Lotes/series y medidas | Opcional, según artículos y configuración real; no implica leer balanzas |
| Artículos, servicios y otros conceptos | Núcleo comercial compartido, con secciones visibles según uso |

Para configuraciones existentes, mantener nombres de claves, formatos y comportamiento observado. No aplicar un parser booleano uniforme: `= "SI"`, `<> "NO"`, ausencia y valor vacío tienen semánticas distintas. El adaptador debe resolver la regla de cada clave. `VALOR`/`ValorAux` se leen según la norma central; si difiere del lector VB6 de una clave, registrar y probar la diferencia.

Los perfiles nuevos se persistirán en `TA_CONFIGURACION` cuando se confirme su esquema y se definan las claves; no crear una tabla paralela. Aplicar un perfil será una operación explícita con vista previa de las claves afectadas, sin sobrescribir automáticamente configuración de clientes existentes. Preferencias de columnas por usuario se separan de reglas de negocio.

Claves por nombre de PC (por ejemplo `ModoTpv_...`) necesitan una identidad de terminal explícita para el navegador. No reemplazar el equipo del operador por el nombre del servidor web. La precedencia de usuario, unidad de negocio y terminal se releva por regla; no inventar una precedencia universal.

### Interfaz

Ruta implementada: `/ventas/carga-comprobantes`, también bajo el contexto SaaS. Shell AlfaDesign, `ModuleKey = "ventas"`, registro del segmento en `AlfaDesignManagedModules`, toolbar contextual, componentes compartidos y comportamiento encerrado con `?directo=1`.

Usar la experiencia del facturador de Odoo como referencia futura de productividad, sin cambiar el backend de Alfa Gestión. La primera pantalla combinará:

- Encabezado compacto: cliente, TC, fecha, vencimiento, lista, vendedor y depósito.
- Acción «Traer pedidos/remitos», selección con búsqueda y pendientes server-side.
- Grilla editable por teclado con unidades, bultos, precio y descuento según capacidad.
- Documento y secuencia de origen por línea, incluso si la presentación consolida artículos.
- Totales visibles y panel de detalle fiscal/comercial.
- Secciones progresivas para servicios, otros conceptos, lotes y observaciones.
- Vista de efectos antes de grabar y mensajes que conserven la carga.

No duplicar botones del Context Toolbar. El menú y permisos de la pantalla se incorporaron mediante el script idempotente `2026-10-14-002__ventas_carga_comprobantes_menu_web.sql`, clave D60CCW. Las ampliaciones deben conservar las reglas de `TA_MENU` y los scripts de actualización.

## Orden de implementación y criterios de salida

1. **Contrato y relevamiento**: cerrar matriz por circuito, confirmar fuentes ejecutadas, lectores de configuración y metadatos SQL. Salida: ningún objeto, campo o efecto del primer circuito depende de una suposición.
2. **Consulta de orígenes y simulación**: cargar pedidos/remitos, calcular cantidades pendientes y precios; elaborar efectos sin escribir movimientos. Salida: casos directos, parciales y consolidados comparados con VB6.
3. **Núcleo de grabación local**: cabecera, artículos, tareas/otros incluidos, stock, aplicaciones, contabilidad y auditoría. Salida: equivalencia en dos copias de la misma base y rollback completo ante fallo local.
4. **Electrónica y recuperación**: adaptar servicios existentes cuando su contrato alcance. Salida: aprobación, rechazo, respuesta incierta y reintento sin duplicación, mediante homologación o simulación.
5. **Editor operativo y menú**: conectar la interfaz a los servicios certificados, configurar capacidades, búsqueda, borradores y documentos. Salida: prueba funcional por operador del rubro y validación de teclado, foco y resoluciones AlfaDesign.
6. **Piloto**: habilitar solo circuitos certificados para clientes representativos; medir rendimiento y completar brechas. Extender TC, especializaciones y capacidades por el mismo proceso.

Cada entrega identifica qué circuitos permite. La certificación de una factura directa no certifica una factura de remitos ni un pedido preparado.

## Grabación, concurrencia y recuperabilidad

- Mantener identidad legacy (`TC`, `IdComprobante`, complemento y secuencia) y significado de campos. `CantidadUD` guarda coeficiente en el camino revisado.
- Usar `CodigoPk.Format` para PK de texto y lecturas/comparaciones con trim; respetar la excepción de cuentas contables.
- Revalidar pendientes, stock y numeración al confirmar, con exclusión mutua adecuada. Una consulta previa de pendientes no reserva cantidades.
- Mantener trazabilidad de cada origen; agrupar la UI no autoriza a agrupar líneas contables, fiscales o relaciones sin evidencia.
- Registrar un identificador de operación estable para impedir doble grabación por reconexión/reintento. El circuito de homologación utiliza el journal operativo documentado en el incremento comercial; el ID del comprobante por sí solo no sustituye esa identidad.
- En SQL utilizar parámetros y tipos explícitos para fechas/importes. No copiar SQL concatenado ni depender del idioma de la conexión.
- Auditar acciones funcionales y mantener equivalencia con la bitácora legacy cuando corresponda. Errores técnicos mediante el servicio central hacia `AUX_ERR`.
- Separar grabación local de impresión/correo; un fallo de entrega no permite facturar otra vez.
- No mantener bloqueos SQL durante interacciones del operador. La secuencia de electrónica y commit requiere un diseño específico: ARCA no participa del rollback SQL.

## Límites y pendientes actuales

La búsqueda de orígenes utiliza Smart Search en el Context Toolbar, siguiendo el contrato visual de Clientes: embudo, chips de filtros aplicados y panel wide anclado/clamped mediante `alfa-smart-search-popover.js`. Se mantienen búsqueda SQL, filtros y paginación existentes. `VentasCargaComprobantes.razor` publica `SearchContent`; su CSS elimina el formulario anterior. Las acciones del header refrescan explícitamente la página para mostrar simulación, verificación y confirmación sin esperar otro clic. `CargaComprobantesUiTests.cs` verifica estos callbacks sin forzar el render, la consulta aplicada al paginar/quitar chips y el panel en 2048/1440/1024. El uso funcional está en `src/AlfaCore/Docs/manual_carga_comprobantes.md`.

El relevamiento original de DISTRIWALTERP fue de lectura; los incrementos posteriores agregaron ensayos con rollback, una autorización fiscal aislada y el camino comercial acotado de homologación. Ver la evidencia de cada incremento. No se ejecutó el programa VB6 para una comparación campo por campo. El SQL de relevamiento sigue siendo una consulta de diagnóstico, independiente de los scripts de actualización.

Se ejecutaron pruebas de consulta/simulación, grabación transaccional, recuperación e interfaz, además de emisiones comerciales reales en homologación desde origen y por carga directa, incluyendo NC/ND y FP; ver la evidencia y los límites de cada incremento. No hay aún comparación de grabación VB6/web, equivalencia general certificada ni mediciones comparativas de velocidad. Los perfiles por rubro siguen siendo un diseño propuesto, sujeto al relevamiento comercial. Los pendientes y el resultado de cada pasada se mantienen en el registro de avances enlazado arriba. No se porta automáticamente ninguna especialización por cliente que cambie resultados sin definir antes su contrato.

- [Configuración de ventas y búsqueda/alta de clientes por CUIT](configuracion-y-clientes.md).
