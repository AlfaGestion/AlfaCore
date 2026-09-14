# Diseñador de comprobantes - Etapa 1

## Alcance

El MVP incorpora plantillas JSON para `COTIZACION`, resolución por `UNegocio`, vista previa HTML y PDF beta con Playwright. El generador histórico `CotizacionPdfService` basado en QuestPDF continúa disponible y no se reemplaza.

Flujo: `COT_VERSION` -> `CotizacionDocumentData` -> `CORE_DocumentTemplate` -> `DocumentRenderer` -> HTML -> preview/PDF beta.

## Base de datos

Aplicar `src/AlfaCore/App_Data/updates/2026-09-08-003__utilidades_disenador_comprobantes.sql` sobre cada base Alfa Gestión. Crea de manera idempotente `CORE_DocumentTemplate`, `CORE_DocumentTemplateVersion`, su índice y la plantilla global de sistema para Cotización.

Las cotizaciones existentes no almacenan `UNegocio`; en esta etapa se elige la unidad en el diseñador, tomando las opciones de `V_TA_UnidadNegocio`. La plantilla específica tiene prioridad sobre la global.

## Playwright

El paquete `Microsoft.Playwright` se restaura junto con el proyecto, pero Chromium se instala por ambiente. Después de publicar, ejecutar una vez desde la carpeta publicada:

```powershell
pwsh .\playwright.ps1 install chromium
```

En servidores sin PowerShell 7, usar el ejecutable `playwright.cmd install chromium` generado por el paquete, o ejecutar el comando equivalente desde el directorio de salida. La cuenta que ejecuta el servicio web debe poder leer el directorio de navegadores de Playwright. No se guardan binarios de Chromium dentro del repositorio.

## Prueba manual

1. Aplicar el script SQL y abrir `Utilidades -> Diseñador de comprobantes`.
2. Elegir una unidad o dejar `Plantilla global` y duplicar `Cotización estándar`.
3. Cambiar márgenes, logo o columnas y guardar; volver a guardar una edición para comprobar el snapshot en `CORE_DocumentTemplateVersion`.
4. Elegir una cotización real y usar `Actualizar vista previa`.
5. Usar `PDF beta` y verificar el archivo generado por Chromium.
6. Abrir la cotización normal y comprobar que `Descargar PDF` sigue usando QuestPDF; `Probar PDF beta` abre el nuevo diseñador sin modificar el documento.

## Revisión del prompt y continuación fiscal (2026-09-13)

El prompt de Etapa 1 describe una base que ya existe. La implementación actual usa .NET 8,
Blazor Server, Dapper, servicios registrados por DI y `IAppEventService` para errores en
`AUX_ERR`. Ya están `DocumentTemplateService`, `DocumentRenderer`, `DocumentPdfService`,
`CotizacionDocumentService`, las tablas de plantillas/versiones y el diseñador en Utilidades.
También existe `FacturaDocumentService` para facturas A/B/C.
No corresponde crear otro motor ni repetir las tablas o instalar Playwright nuevamente.

Esta continuación conserva el bloque JSON `QrAfip`, pero genera su PNG en memoria
con `IArcaQrService`/`ArcaQrService` y QRCoder (dependencia ya instalada).
No consulta ni modifica `Aux_MV_CpteQR`, no utiliza COM y no requiere conexión a ARCA.
Permite configurar su lado entre 20 y 60 mm (límite de diseño de la aplicación, no una
certificación normativa). Sin ancho explícito usa 30 mm. El renderer conserva proporción,
alineación y evita dividir el bloque entre páginas. Si hay CAE pero faltan bytes de QR,
incluye un aviso visible tanto en HTML como en PDF; no genera un QR ficticio.
El contenido fiscal sigue fuera del editor.

La resolución respeta `Tipo_Cpte`: 1/6/11 para facturas A/B/C, 2/7/12 para notas de
débito y 3/8/13 para notas de crédito. Cada tipo conserva su denominación, sin confundirse
con factura por compartir letra. El fallback histórico por letra se conserva únicamente sin tipo electrónico informado.

### Verificación de esta continuación

- Pruebas automatizadas: `dotnet test tests/AlfaCore.Tests/AlfaCore.Tests.csproj --filter "FullyQualifiedName~DocumentosFiscalTests|FullyQualifiedName~ArcaQrTests"`.
- En el diseñador, elegir Factura A/B/C, duplicar una plantilla, marcarla predeterminada,
  ajustar el QR (por ejemplo 35 mm y derecha) y guardar. Elegir una factura real y actualizar
  vista previa/PDF. En el diseñador se utiliza el borrador seleccionado, incluso sin guardar;
  las impresiones externas al diseñador conservan la resolución central de plantilla.
- Elegir una factura con CAE aprobado que nunca se haya impreso en VB6: debe generar el QR
  sin necesitar una fila en `Aux_MV_CpteQR`. Probar pesos y dólares con cotización histórica.
- Escanear un PDF real y contrastarlo con la impresión legacy antes de usarlo en producción.

### Referencia VB6 y datos fiscales

Fuente leída: `C:\dev\AlfaVb6_migrar\ModGlobal.bas`, etiquetas `CreaQr` (7749 y 8539),
llamadas desde la impresión de `V_MV_VENTAS.vbp`. VB6 crea `PyQR`, genera un archivo PNG
y lo copia a `Aux_MV_CpteQR` para Crystal. Esa tabla es auxiliar de impresión, no evidencia
de que el QR se genere durante la emisión. Los proyectos VB6, AlfaWeb y wsAlfa son solo lectura.

AlfaCore toma fecha, punto de venta, tipo/número, importe, tipo/número de documento receptor
y CAE de `V_MV_CPTE_ELECTRONICOS` (requiere `Resultado = A`). No vuelve a mapear el receptor
desde el maestro actual. Para emisor adopta la consulta fiscal del reporte VB6:
`V_TA_UnidadNegocio.CUIT` cuando `USAEFC = 1`, o `TA_CONFIGURACION.WSFE_CUIT`.
Usa la unidad del comprobante, no la unidad elegida para personalizar la plantilla.

Moneda desde `V_MV_Cpte.Moneda`: vacío/0/1/PES → PES, 2/DOL → DOL y 3/EUR → EUR.
La equivalencia de euros está documentada también en el SQL legacy de valorización.
Pesos usa cotización 1; dólares/euros usan `TA_COTIZACION.MONEDA2/MONEDA3` del último ID
del día del comprobante, siguiendo la búsqueda histórica de VB6. Una moneda desconocida,
cotización faltante o datos fiscales inválidos detienen el reporte y se registran mediante
`IAppEventService` en `AUX_ERR`; no se inventa una cotización ni se recupera una imagen vieja.

Se corrige el literal de VB6 `TipoCodAutorizacionQR = "A"`: para CAE corresponde `E`.
El servicio puro distingue CAE (`E`) y CAEA (`A`); el adaptador actual solo utiliza CAE,
porque esa es la autorización confirmada en la fuente actual.
Referencia: https://arca.gob.ar/fe/qr/documentos/QRespecificaciones.pdf .
La URL base sigue la sección técnica actual del documento (`https://www.arca.gob.ar/fe/qr/`).

### Pendientes explícitos

Validar contra una base real y escanear el PDF impreso: las pruebas automatizadas no
verifican el esquema SQL instalado ni los datos de cada cliente. La regla histórica de
cotización y la configuración actual de CUIT pueden no reconstruir cambios posteriores a
la emisión; se mantienen las fuentes confirmadas en legacy y requieren esa comparación.
Quedan para continuaciones: lectura de CAEA, comprobantes asociados de NC/ND, exportación,
leyendas especiales y M histórico. Esta entrega no modifica SQL, menús ni dependencias,
y no cambia el camino QuestPDF de cotizaciones.


## Crédito/débito y totales al pie (2026-09-14)

El diseñador ofrece Nota de crédito A/B/C y Nota de débito A/B/C. Comparten la estructura
base de factura (empresa, receptor, ítems, IVA, CAE y QR), con denominación y código ARCA
propios. No se aplica signo contable al importe impreso: se conservan los importes del
comprobante. La búsqueda del combo filtra por el tipo fiscal completo y la resolución
elige una plantilla independiente para cada tipo, no solo para cada letra.

Ejecutar `src/AlfaCore/App_Data/updates/2026-09-14-001__documentos_credito_debito.sql`
mediante el actualizador existente. Agrega seis plantillas globales de sistema. Copia la
plantilla de factura de la misma letra si existe una de sistema activa; si no, utiliza
la definición base incluida. Es idempotente y conserva predeterminadas y diseños ya
existentes. No agrega pantallas de menú ni modifica tablas legacy.

`DocumentTemplateDefinition.TotalesAlPiePagina` se persiste dentro del JSON; su default
es `false`, compatible con plantillas anteriores. La opción aparece junto a Totales.

- Desmarcado: el HTML conserva el flujo previo, con totales debajo del detalle.
- Marcado: `DocumentRenderer` agrupa desde Totales hasta el final (firma en cotizaciones,
  CAE/QR en fiscales). `DocumentPagination` usa las medidas de papel, orientación y márgenes
  para colocar ese cierre una sola vez al fondo de la última página de contenido.
- Las tablas se dividen por filas, repiten encabezado y continúan textos de filas que
  superan una hoja sin duplicar cantidades/importes. Si el cierre no entra, pasa a una
  nueva última página. No se utiliza un footer fijo repetido para los totales.
- Portada conserva su página y no participa en la paginación del detalle.
- Un bloque indivisible o cierre mayor que una página genera un error controlado; no se
  recorta silenciosamente. Revisar el tamaño de imágenes/textos o desmarcar la opción.

El pequeño script propio `Services/DocumentPagination.js` es un recurso embebido del
ensamblado, no contenido editable. Se usa porque C# no conoce la altura final de las
fuentes y celdas en Chromium. `DocumentPdfService.PrepareHtmlAsync` ejecuta la medición en
el servidor y entrega HTML estático paginado al preview; la exportación imprime ese mismo
HTML. Los errores se registran centralmente en `AUX_ERR`. Chromium es necesario también
para la vista previa cuando se activa esta opción. El tamaño de pantalla no cambia la
cantidad de hojas del documento preparado.

Los servicios de cotización y factura aceptan opcionalmente una plantilla de preview,
validan tipo y definición y no la guardan. Esto permite probar el checkbox y cualquier
otro cambio del borrador antes de guardar, sin imprimir accidentalmente otra plantilla.
Fuera del diseñador, los llamados existentes conservan el comportamiento de resolución.

### Archivos de esta ampliación

- `Models/DocumentosModels.cs`: catálogo fiscal y opción del JSON.
- `Services/FacturaDocumentService.cs`, `IFacturaDocumentService.cs`: identificación,
  búsqueda por tipo y plantilla de prueba.
- `Services/CotizacionDocumentService.cs`, `ICotizacionDocumentService.cs`: plantilla de
  prueba y preparación de páginas.
- `Services/DocumentTemplateService.cs`: validación y fallback de los nuevos tipos.
- `Services/DocumentRenderer.cs`: denominación del documento y agrupación del cierre.
- `Services/DocumentPagination.cs`, `DocumentPagination.js`: medidas y paginación.
- `Services/DocumentPdfService.cs`, `IDocumentPdfService.cs`: preparación compartida con
  logging y espera de la paginación antes de exportar.
- `Components/Pages/DisenadorComprobantes.razor`: tipos, checkbox y prueba del borrador.
- `AlfaCore.csproj`: inclusión del recurso de paginación.
- Script SQL indicado arriba: seed de seis plantillas.
- `tests/AlfaCore.Tests/DocumentosFiscalTests.cs`: códigos y nombres de crédito/débito.
- `tests/AlfaCore.Tests/DocumentPaginationTests.cs`: Chromium real, varias hojas,
  A4/A5/horizontal, fila extensa, portada/firma y compatibilidad desmarcada.

Prueba manual: abrir el diseñador, elegir un tipo, seleccionar un comprobante del mismo
tipo, marcar Totales al pie y actualizar la vista previa. Probar PDF antes de guardar,
revisar la última hoja y luego guardar la plantilla. Repetir desmarcado. Para comparar
con datos reales faltan la ejecución del script y pruebas contra la base del cliente;
los documentos de las pruebas automatizadas utilizan datos sintéticos.


## Formato habitual A4 vertical y ticket de 80 mm

A4 vertical continúa como default de las plantillas nuevas. El selector de papel agrega
`Ticket80` (etiqueta «Ticket de 80 mm»); A5/horizontal se conservan para diseños existentes.
No se requieren scripts ni columnas SQL: el tamaño se guarda en el JSON de la plantilla.

Al elegir Ticket80 en el diseñador se usa orientación vertical y márgenes iniciales de
3 mm, editables. Los márgenes laterales no pueden sumar más de 20 mm. Al volver a hoja
en la misma edición se recuperan los márgenes previos al cambio. El ticket es continuo:
la opción de totales al pie de una hoja queda deshabilitada y se conserva su valor para
volver a A4; en ticket los totales siempre siguen al detalle. La portada no se imprime.

`DocumentTicketLayout` adapta el HTML a 80 mm, apila el encabezado y muestra el detalle en
bloques por artículo, conservando los campos visibles y sus títulos. Los anchos porcentuales
de columnas solo se aplican a hojas. Los importes se alinean a la derecha. CAE, QR y firma
permanecen en el cierre. El pie configurado aparece una vez dentro del ticket, sin usar
el footer nativo de páginas A4.

`DocumentPdfService` mide el contenido con fuentes e imágenes cargadas y define el tamaño
físico del PDF mediante `@page ticket`: 80 mm de ancho y alto calculado. La vista previa
usa ese mismo HTML preparado. El límite técnico es 5 metros por ticket; si lo supera,
se detiene la exportación con logging centralizado (usar A4 para esos documentos).

Archivos de la ampliación: `Services/DocumentTicketLayout.cs` (nuevo), `DocumentRenderer.cs`,
`DocumentPdfService.cs`, `DocumentTemplateService.cs`, servicios de factura/cotización y
`Components/Pages/DisenadorComprobantes.razor`. Pruebas nuevas en
`tests/AlfaCore.Tests/DocumentTicketTests.cs`.

Prueba funcional: duplicar la plantilla A4 para conservar ambas variantes, elegir Ticket
80 mm, actualizar vista previa, exportar PDF y guardar. En el controlador de la impresora
seleccionar papel de 80 mm y escala real (100%). Ajustar los márgenes según el área imprimible
del equipo. La prueba con impresora física queda pendiente; las pruebas automatizadas
verifican Chromium y PDF, no el driver, el corte de papel ni comunicación ESC/POS.


## Emisor del comprobante por unidad de negocio (USAEFC)

La unidad se toma de `V_MV_Cpte.UNEGOCIO`, no del selector de plantilla del diseñador.
`FacturaDocumentService.BuildEmpresaAsync` consulta una vez `V_TA_UnidadNegocio` y resuelve:

- `USAEFC = 1`: nombre impreso desde `RAZON_SOCIAL`, CUIT impreso y CUIT del QR desde `CUIT`.
- Unidad sin la marca, inexistente o comprobante sin unidad: datos de configuración general
  (`NOMBRE` y `CUIT` para el encabezado; `WSFE_CUIT` para el QR, como en el circuito existente).

La decisión está centralizada en `ResolveEmisor`; `GenerateQrAsync` recibe el CUIT ya
resuelto y deja de consultar la unidad de nuevo. Encabezado, pie de empresa, A4 y Ticket80
usan la razón social resuelta. Aplica a facturas, notas de crédito y débito A/B/C.
Si una unidad marcada no tiene razón social o CUIT, se registra el error por la capa común
y se detiene el documento, sin sustituir silenciosamente el emisor por la empresa general.

Referencia leída: `C:/dev/AlfaVb6_migrar/ModGlobal.bas`, `DatosUnegocio` y la consulta fiscal
del reporte que utiliza `USAEFC`, `CUIT` y `WSFE_CUIT`. Se aplica la condición explícita
solicitada para AlfaCore: los datos de la unidad solo reemplazan a los generales con la marca.

Archivos: `Services/FacturaDocumentService.cs` (resolución compartida) y
`tests/AlfaCore.Tests/DocumentEmisorTests.cs` (nuevo: marca activa/inactiva, unidad ausente,
datos incompletos y render de factura/NC/ND en A4 y ticket). No cambia SQL ni datos guardados.
Prueba manual: imprimir comprobantes de dos unidades con razón social diferente y comparar
uno con `USAEFC = 1` y otro sin la marca; cambiar la unidad de diseño no debe cambiar el emisor.

### Portal Cliente: vista previa compartida

El detalle de cuenta corriente muestra el HTML de `FacturaDocumentService.RenderParaClienteAsync`
en un iframe aislado, utilizando el mismo renderer que el diseñador. Se resuelve la plantilla
guardada por tipo fiscal y unidad del comprobante, con la selección global habitual como respaldo.
El PDF del portal pasa por esta misma resolución. Incluye detalle, impuestos, emisor, CAE, QR,
Ticket80 y totales al pie solo en la última página según la plantilla.

Antes de renderizar o descargar se valida la cuenta del comprobante contra el código obtenido
de la sesión del cliente. Un ID inexistente o ajeno devuelve null. Los errores de configuración
ya no se confunden con un tipo no soportado; la UI los procesa mediante `UiOps` y el logging común.
Recibos y otros documentos sin renderer fiscal conservan el componente anterior como respaldo.
El botón Imprimir apunta al iframe completo; el sandbox impide ejecutar scripts del documento.

Archivos: `PortalClienteComprobanteDetalle.razor` y sus archivos CSS/JS, `IFacturaDocumentService.cs`
y `FacturaDocumentService.cs`. No requiere cambios SQL. Validación manual pendiente con sesión real:
comparar un comprobante en portal y diseñador con la misma plantilla guardada, descargar su PDF,
imprimir y comprobar que un ID de otra cuenta no muestre contenido.

### Integración con pedidos, remitos y cobranzas

La sincronización del 14/09/2026 conserva las opciones de pedidos, remitos y cobranzas incorporadas en el remoto. Las identificaciones CREDITO_/DEBITO_ y NOTA_CREDITO_/NOTA_DEBITO_ se consideran equivalentes al listar, resolver y marcar plantillas predeterminadas; no se eliminan plantillas existentes. La vista previa fiscal del portal conserva la validación de pertenencia y la generación de QR al consultar. Los servicios de documentos toman la conexión de la sesión activa.
