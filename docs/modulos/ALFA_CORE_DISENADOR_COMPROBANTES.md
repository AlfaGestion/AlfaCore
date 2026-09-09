# Alfa Core - Diseñador de Comprobantes

## Documento de análisis y arquitectura

**Proyecto:** Alfa Core  
**Módulo propuesto:** Utilidades -> Diseñador de comprobantes  
**Estado:** Diseño funcional / arquitectura inicial  
**Objetivo:** reemplazar progresivamente la generación rígida de documentos por código por un motor de plantillas configurable, reutilizable y preparado para incorporar diseño visual e inteligencia artificial.

---

# 1. Contexto

Alfa Gestión históricamente utiliza tecnologías legacy, incluyendo VB6 y Crystal Reports, para impresión de comprobantes e informes.

En Alfa Core, desarrollado sobre tecnologías .NET / Blazor, algunos documentos PDF se generan actualmente mediante código, por ejemplo con QuestPDF. Este enfoque es adecuado para documentos controlados por desarrollo, pero presenta una limitación importante: cualquier cambio de diseño requiere modificar código, recompilar y desplegar.

El objetivo de este proyecto es construir una nueva infraestructura de documentos que permita:

- definir distintos formatos de comprobantes;
- personalizar su presentación por Unidad de Negocio;
- modificar campos, columnas, encabezados, logos, márgenes y formatos;
- visualizar el resultado antes de guardar;
- generar PDF desde la misma definición utilizada en la vista previa;
- mantener historial de versiones;
- incorporar posteriormente un diseñador visual por bloques;
- incorporar posteriormente IA para modificar diseños mediante lenguaje natural.

El proyecto debe convivir inicialmente con los reportes y generadores actuales. No se plantea una migración masiva en una única etapa.

---

# 2. Decisión principal de arquitectura

La definición del documento NO debe depender directamente de QuestPDF, Playwright, HTML ni de un motor comercial de reportes.

La fuente de verdad será una definición estructurada y versionable en JSON.

Flujo conceptual:

```text
Datos del comprobante
        |
        v
ViewModel / DocumentData
        |
        v
TemplateDefinition JSON
        |
        v
DocumentRenderer
        |
        +-------> HTML
                     |
                     +-------> Vista previa Blazor
                     |
                     +-------> Playwright / Chromium
                                      |
                                      v
                                     PDF
```

La decisión de utilizar JSON como modelo intermedio permite cambiar el motor de renderización en el futuro sin perder las plantillas guardadas.

---

# 3. Ubicación funcional

La funcionalidad se incorporará inicialmente en:

```text
Utilidades
    -> Diseñador de comprobantes
```

El nombre visible al usuario será **Diseñador de comprobantes**.

Internamente, sin embargo, la arquitectura deberá utilizar nombres genéricos como `DocumentTemplate`, porque en el futuro el mismo motor puede utilizarse para:

- Cotizaciones
- Facturas
- Remitos
- Recibos
- Pedidos
- Proformas
- Órdenes de compra
- Órdenes de trabajo
- Listas de precios
- Estados de cuenta
- Informes técnicos
- Otros documentos generados por Alfa Core

---

# 4. Unidad de Negocio

Alfa Core utiliza la Unidad de Negocio como discriminador de empresa o contexto operativo.

Para este proyecto se utilizará:

```sql
UNegocio NVARCHAR(4)
```

No se introducirá un `IdEmpresa` nuevo ni una relación alternativa si el sistema existente ya utiliza `UNegocio`.

Ejemplos:

```text
0001
0002
A001
CASA
```

Debe preservarse el criterio habitual de Alfa:

- valores numéricos: presentación alineada a la derecha;
- valores alfanuméricos: presentación alineada a la izquierda.

La implementación deberá revisar cómo Alfa Core obtiene y utiliza actualmente `UNegocio` y reutilizar ese mecanismo en lugar de crear una segunda fuente de contexto.

## 4.1 Plantillas globales

Se recomienda permitir:

```text
UNegocio = NULL
```

para representar una plantilla global o estándar del sistema.

Resolución recomendada:

1. buscar plantilla específica para `UNegocio`;
2. si no existe, utilizar plantilla global;
3. si tampoco existe, utilizar una definición estándar embebida o sembrada por migración.

---

# 5. Convención de tablas nuevas

Las tablas de este proyecto deben distinguirse claramente de las tablas legacy de Alfa Gestión.

Se utilizará el prefijo:

```text
CORE_
```

Ejemplos:

```text
CORE_DocumentTemplate
CORE_DocumentTemplateVersion
CORE_DocumentTemplateResource
CORE_DocumentTemplateAssignment
CORE_DocumentTemplateAiLog
```

No utilizar para estas tablas prefijos históricos como `MA_`, `MV_`, etc.

---

# 6. Modelo de persistencia propuesto

## 6.1 CORE_DocumentTemplate

Tabla principal de plantillas.

Campos sugeridos:

```sql
CREATE TABLE CORE_DocumentTemplate
(
    IdTemplate INT IDENTITY(1,1) NOT NULL PRIMARY KEY,

    UNegocio NVARCHAR(4) NULL,

    TipoDocumento NVARCHAR(30) NOT NULL,
    Nombre NVARCHAR(100) NOT NULL,

    TemplateJson NVARCHAR(MAX) NOT NULL,
    CssCustom NVARCHAR(MAX) NULL,

    EsSistema BIT NOT NULL CONSTRAINT DF_CORE_DocumentTemplate_EsSistema DEFAULT 0,
    EsPredeterminado BIT NOT NULL CONSTRAINT DF_CORE_DocumentTemplate_EsPredeterminado DEFAULT 0,
    Activo BIT NOT NULL CONSTRAINT DF_CORE_DocumentTemplate_Activo DEFAULT 1,

    FechaAlta DATETIME2 NOT NULL CONSTRAINT DF_CORE_DocumentTemplate_FechaAlta DEFAULT SYSDATETIME(),
    FechaModificacion DATETIME2 NULL,
    UsuarioModificacion NVARCHAR(50) NULL
);
```

Índice recomendado:

```sql
CREATE INDEX IX_CORE_DocumentTemplate_UNegocio_Tipo
ON CORE_DocumentTemplate
(
    UNegocio,
    TipoDocumento,
    Activo
);
```

Debe evaluarse agregar posteriormente restricciones para impedir más de una plantilla predeterminada activa para la misma combinación de `UNegocio` + `TipoDocumento`.

---

## 6.2 CORE_DocumentTemplateVersion

Mantendrá snapshots de la plantilla antes de cada modificación.

```sql
CREATE TABLE CORE_DocumentTemplateVersion
(
    IdVersion BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IdTemplate INT NOT NULL,
    Version INT NOT NULL,

    TemplateJson NVARCHAR(MAX) NOT NULL,
    CssCustom NVARCHAR(MAX) NULL,

    Fecha DATETIME2 NOT NULL CONSTRAINT DF_CORE_DocumentTemplateVersion_Fecha DEFAULT SYSDATETIME(),
    Usuario NVARCHAR(50) NULL,

    CONSTRAINT FK_CORE_DocumentTemplateVersion_Template
        FOREIGN KEY (IdTemplate)
        REFERENCES CORE_DocumentTemplate(IdTemplate)
);
```

Antes de modificar una plantilla existente debe guardarse su estado actual como versión histórica.

Funciones futuras:

- consultar historial;
- comparar versiones;
- restaurar versión;
- identificar usuario y fecha del cambio.

---

## 6.3 CORE_DocumentTemplateResource

Almacena recursos asociados a documentos.

Ejemplos:

- logo;
- firma;
- sello;
- imagen de fondo;
- QR estático;
- imagen institucional.

Propuesta:

```sql
CREATE TABLE CORE_DocumentTemplateResource
(
    IdResource INT IDENTITY(1,1) NOT NULL PRIMARY KEY,

    UNegocio NVARCHAR(4) NULL,

    Nombre NVARCHAR(100) NOT NULL,
    Tipo NVARCHAR(30) NULL,
    MimeType NVARCHAR(100) NULL,

    Archivo VARBINARY(MAX) NULL,
    RutaArchivo NVARCHAR(500) NULL,

    Activo BIT NOT NULL CONSTRAINT DF_CORE_DocumentTemplateResource_Activo DEFAULT 1,
    FechaAlta DATETIME2 NOT NULL CONSTRAINT DF_CORE_DocumentTemplateResource_FechaAlta DEFAULT SYSDATETIME()
);
```

Para archivos pequeños como logos o firmas es aceptable `VARBINARY(MAX)`.

Para archivos pesados o una futura arquitectura distribuida puede utilizarse almacenamiento externo y persistir únicamente una referencia (`RutaArchivo`, Blob Storage, S3, MinIO, etc.).

No guardar imágenes Base64 directamente dentro de `TemplateJson`.

---

## 6.4 CORE_DocumentTemplateAssignment

No es obligatorio para la primera etapa.

Su objetivo futuro será permitir reglas de selección de plantilla por:

- Unidad de Negocio;
- tipo de documento;
- sucursal;
- punto de venta;
- circuito;
- cliente;
- usuario;
- u otros criterios.

Ejemplo conceptual:

```text
UNegocio 0001
TipoDocumento COTIZACION
Sucursal MAYORISTA
Template Cotización Mayorista
```

---

## 6.5 CORE_DocumentTemplateAiLog

No corresponde implementar en la primera etapa.

Permitirá auditar modificaciones realizadas mediante IA.

Información sugerida:

- IdTemplate;
- prompt solicitado por el usuario;
- JSON anterior;
- JSON propuesto;
- usuario;
- fecha;
- resultado/aprobación.

---

# 7. Plantillas de sistema y personalizadas

Debe existir una diferencia entre:

```text
Plantilla del sistema
Plantilla personalizada
```

Una plantilla de sistema tendrá:

```text
EsSistema = 1
```

Recomendación:

- no modificar directamente plantillas de sistema desde el diseñador;
- permitir duplicarlas;
- modificar la copia;
- preservar la original para futuras actualizaciones de Alfa Core.

Ejemplo:

```text
[Sistema] Cotización estándar
[Personalizada] Cotización Alfa NET
[Personalizada] Cotización Mayorista
```

Esto evita que una actualización de Alfa Core destruya personalizaciones realizadas por un cliente.

---

# 8. Modelo JSON del documento

El JSON será la fuente de verdad del diseño.

Ejemplo conceptual:

```json
{
  "schemaVersion": 1,
  "paper": {
    "size": "A4",
    "orientation": "Portrait",
    "marginTopMm": 10,
    "marginBottomMm": 10,
    "marginLeftMm": 10,
    "marginRightMm": 10
  },
  "blocks": [
    {
      "id": "header-logo",
      "type": "logo",
      "visible": true,
      "resourceId": 1,
      "align": "right",
      "width": 150
    },
    {
      "id": "customer",
      "type": "customer",
      "visible": true,
      "fields": [
        "Cliente.RazonSocial",
        "Cliente.CUIT",
        "Cliente.Domicilio"
      ]
    },
    {
      "id": "items",
      "type": "items",
      "columns": [
        {
          "field": "Codigo",
          "title": "Código",
          "visible": true,
          "widthPercent": 15,
          "align": "left"
        },
        {
          "field": "Descripcion",
          "title": "Descripción",
          "visible": true,
          "widthPercent": 45,
          "align": "left"
        },
        {
          "field": "Cantidad",
          "title": "Cantidad",
          "visible": true,
          "widthPercent": 10,
          "align": "right"
        },
        {
          "field": "Precio",
          "title": "Precio",
          "visible": true,
          "widthPercent": 15,
          "align": "right"
        },
        {
          "field": "Total",
          "title": "Total",
          "visible": true,
          "widthPercent": 15,
          "align": "right"
        }
      ]
    },
    {
      "id": "totals",
      "type": "totals",
      "visible": true
    }
  ]
}
```

## 8.1 Versionar el esquema

Incluir desde el comienzo:

```json
"schemaVersion": 1
```

Esto permitirá migrar plantillas si el formato JSON evoluciona en el futuro.

---

# 9. Principio de diseño: bloques antes que coordenadas

No se recomienda comenzar replicando el modelo de posicionamiento absoluto de Crystal Reports.

Evitar inicialmente depender de:

```text
X
Y
Width
Height
```

como estructura principal.

Para documentos de negocio es preferible una composición fluida por bloques:

```text
Encabezado
Datos empresa
Datos comprobante
Datos cliente
Detalle
Totales
Observaciones
Pie
```

Ventajas:

- descripciones que ocupan varias líneas;
- cantidades variables de ítems;
- documentos de varias páginas;
- cambio de A4 a A5;
- impresión horizontal;
- textos extensos;
- datos variables de clientes;
- menor riesgo de solapamiento.

Posteriormente puede incorporarse posicionamiento libre como característica avanzada si aparece una necesidad real.

---

# 10. Bloques previstos

No implementar todos en la primera etapa.

Catálogo futuro:

```text
Logo
Imagen
Texto
Campo
Datos de empresa
Datos del cliente
Datos del comprobante
Tabla de detalle
Totales
Observaciones
Firma
QR
Separador
Espacio
Columnas / contenedor
Pie de página
Encabezado de página
```

Cada bloque deberá tener un `id` estable para que pueda ser modificado visualmente o por IA.

---

# 11. Catálogo de campos

El usuario no deberá escribir nombres arbitrarios de propiedades.

El sistema expondrá un catálogo controlado por tipo de documento.

Ejemplo para Cotización:

```text
EMPRESA
Empresa.Nombre
Empresa.CUIT
Empresa.Domicilio
Empresa.Telefono
Empresa.Email
Empresa.Logo

COMPROBANTE
Comprobante.Numero
Comprobante.Fecha
Comprobante.FechaVencimiento
Comprobante.Vendedor
Comprobante.CondicionVenta

CLIENTE
Cliente.Codigo
Cliente.RazonSocial
Cliente.CUIT
Cliente.Domicilio
Cliente.Telefono
Cliente.Email

DETALLE
Item.Codigo
Item.Descripcion
Item.Cantidad
Item.Precio
Item.Descuento
Item.Total

TOTALES
Totales.Neto
Totales.IVA
Totales.Descuento
Totales.Total
```

Debe existir validación para impedir que una plantilla utilice campos inexistentes o no permitidos.

---

# 12. Alineación de valores

Como regla predeterminada:

- texto -> izquierda;
- números -> derecha;
- importes -> derecha;
- cantidades -> derecha;
- fechas -> configurable, normalmente izquierda/centro;
- títulos -> según definición visual.

En particular, `UNegocio NVARCHAR(4)` debe mostrarse de acuerdo con el criterio histórico de Alfa:

- si su contenido es completamente numérico, alinearlo visualmente a la derecha;
- si contiene caracteres alfabéticos, alinearlo a la izquierda.

---

# 13. Papel e impresión

Configuraciones mínimas previstas:

## Tamaño

```text
A4
A5
Carta
Ticket 80 mm
Ticket 58 mm
Personalizado
```

No es necesario implementar todos en la Etapa 1.

## Orientación

```text
Portrait
Landscape
```

## Márgenes

```text
Superior
Inferior
Izquierdo
Derecho
```

en milímetros.

El renderer HTML generará reglas equivalentes a:

```css
@page {
    size: A4 portrait;
    margin: 10mm;
}
```

---

# 14. HTML y CSS

La plantilla JSON debe generar HTML mediante un renderer controlado por Alfa Core.

No se recomienda guardar el HTML generado como fuente de verdad.

Flujo:

```text
TemplateJson
    +
DocumentData
    |
    v
DocumentRenderer
    |
    v
HTML temporal
```

De esta manera no existe riesgo de que:

```text
JSON != HTML guardado
```

El HTML puede ser generado en memoria cada vez que se requiere previsualizar o imprimir.

## CSS personalizado

`CssCustom` podrá incorporarse para ajustes avanzados.

Debe considerarse como nivel avanzado y no como mecanismo principal del diseñador.

Posteriormente podrá existir una interfaz:

```text
Diseñador | HTML | CSS | JSON
```

solo para usuarios técnicos o administradores.

---

# 15. Motor de plantillas

Puede evaluarse Scriban u otro motor .NET equivalente para expresiones controladas, condiciones y loops.

Ejemplo conceptual:

```html
{{ for item in items }}
  ...
{{ end }}
```

Sin embargo, el motor de plantillas NO debe transformarse en una superficie de scripting libre para el usuario común.

Principio de seguridad:

- exponer únicamente propiedades permitidas;
- evitar ejecución arbitraria;
- no permitir acceso libre al runtime, filesystem o servicios internos;
- validar placeholders/campos antes de renderizar.

---

# 16. Renderizado PDF

Recomendación para el nuevo motor:

```text
HTML/CSS
    |
    v
Microsoft Playwright
    |
    v
Chromium Headless
    |
    v
PDF
```

Beneficio principal:

la vista previa web y el PDF utilizan la misma representación HTML/CSS.

Esto disminuye diferencias entre:

```text
lo que el usuario ve
```

y

```text
lo que finalmente se imprime
```

QuestPDF no debe eliminarse inmediatamente. Debe convivir durante la transición.

---

# 17. Vista previa

El diseñador debe poder renderizar una vista previa con datos reales o de ejemplo.

Para la primera versión se recomienda utilizar datos de ejemplo controlados de una cotización.

Posteriormente:

- seleccionar una cotización existente;
- utilizar datos ficticios;
- cambiar entre varios casos de prueba;
- probar documentos con muchos ítems;
- probar textos largos.

---

# 18. Experiencia de usuario futura

Diseñador previsto:

```text
+----------------+--------------------------------+------------------+
| COMPONENTES    | DOCUMENTO                      | PROPIEDADES      |
|                |                                |                  |
| Logo           |       Vista hoja A4            | Tamaño           |
| Empresa        |                                | Alineación       |
| Cliente        |                                | Fuente           |
| Comprobante    |                                | Color            |
| Detalle        |                                | Márgenes         |
| Totales        |                                | Mostrar/Ocultar  |
| Texto          |                                |                  |
| Imagen         |                                |                  |
| Firma          |                                |                  |
| QR             |                                |                  |
+----------------+--------------------------------+------------------+
```

Barra superior futura:

```text
Unidad de negocio
Tipo de documento
Plantilla
Formato papel
Orientación
Vista previa
Guardar
Duplicar
Historial
Modificar con IA
```

---

# 19. Diseñador visual

No debe implementarse como primer paso.

La evolución recomendada es:

1. configuración mediante formularios;
2. renderer estable;
3. preview estable;
4. PDF estable;
5. bloques configurables;
6. reordenamiento drag & drop;
7. propiedades de bloque;
8. diseñador visual completo.

Esto evita construir primero una UI compleja sobre un motor todavía inestable.

---

# 20. Inteligencia Artificial

La IA se incorporará posteriormente.

## 20.1 Principio fundamental

La IA NO debería generar C# ni modificar directamente código de Alfa Core.

Tampoco se recomienda que el mecanismo principal sea pedir HTML/CSS completamente libre.

La IA debe modificar el modelo estructurado del documento.

Ejemplo:

Usuario:

```text
Poné el logo arriba a la derecha, sacá el código del artículo y hacé el total más grande.
```

La IA podría devolver una propuesta estructurada:

```json
{
  "changes": [
    {
      "blockId": "header-logo",
      "property": "align",
      "value": "right"
    },
    {
      "blockId": "items",
      "column": "Codigo",
      "property": "visible",
      "value": false
    },
    {
      "blockId": "totals",
      "property": "fontSize",
      "value": 22
    }
  ]
}
```

El servidor valida los cambios y genera la nueva vista previa.

## 20.2 Flujo de confirmación

```text
Usuario describe cambio
        |
        v
IA propone modificación
        |
        v
Validación servidor
        |
        v
Vista previa
        |
        v
Usuario confirma
        |
        v
Guardar nueva versión
```

Nunca aplicar automáticamente una modificación de IA sobre la plantilla activa sin preview y confirmación.

---

# 21. Seguridad

El sistema debe tratar los templates como datos potencialmente no confiables.

Requisitos:

- validar JSON al guardar;
- validar `schemaVersion`;
- validar tipos de bloques;
- validar campos permitidos;
- sanitizar contenido textual cuando corresponda;
- evitar JavaScript arbitrario dentro de plantillas;
- no permitir URLs externas sin una política explícita;
- restringir CSS avanzado si puede afectar la aplicación;
- no exponer secretos, tokens ni propiedades internas a la plantilla;
- aplicar autorización por usuario/rol para diseñar o modificar plantillas.

---

# 22. Versionado

Cada edición de una plantilla debe preservar el estado anterior.

Flujo:

```text
Guardar
   |
   +--> snapshot de versión actual
   |
   +--> actualizar CORE_DocumentTemplate
```

Posteriormente se implementará:

```text
Historial
Comparar
Restaurar
```

El número de versión puede comenzar en 1 y aumentar secuencialmente por plantilla.

---

# 23. Duplicación

Debe existir la función **Duplicar plantilla**.

Casos principales:

- copiar plantilla estándar a una personalizada;
- crear una variante mayorista;
- crear una versión sin precios;
- crear una variante para otra Unidad de Negocio.

La duplicación debe generar un nuevo `IdTemplate`.

---

# 24. Plantilla predeterminada

Por tipo de documento y Unidad de Negocio debe poder determinarse una plantilla predeterminada.

Ejemplo:

```text
UNegocio: 0001
TipoDocumento: COTIZACION
Predeterminada: Cotización Alfa NET
```

Si no existe predeterminada específica:

1. buscar global;
2. utilizar plantilla estándar del sistema.

La política exacta debe centralizarse en un servicio y no replicarse en múltiples pantallas.

---

# 25. Arquitectura de carpetas sugerida

La estructura exacta debe adaptarse a la arquitectura actual encontrada por Codex en Alfa Core.

Conceptualmente:

```text
Domain/
    Documents/
        DocumentTemplate.cs
        DocumentTemplateDefinition.cs
        DocumentBlock.cs
        PaperDefinition.cs

Application/
    Documents/
        Models/
        Services/
            IDocumentTemplateService.cs
            DocumentTemplateService.cs
            IDocumentRenderer.cs
            DocumentRenderer.cs
            IDocumentPdfService.cs
            DocumentPdfService.cs

Infrastructure/
    Persistence/
        Entities/
        Configurations/
        Migrations/

Web/
    Components/
        DocumentDesigner/
            DocumentDesigner.razor
            DocumentTemplateList.razor
            DocumentSettings.razor
            DocumentPreview.razor
```

No crear esta estructura mecánicamente si Alfa Core ya utiliza otra convención. Primero inspeccionar el repositorio y respetar sus patrones existentes.

---

# 26. Integración con Cotizaciones

Cotización será el primer documento utilizado como laboratorio.

La integración deberá localizar el modelo y los servicios actuales de cotizaciones y construir un ViewModel específico para impresión.

Ejemplo conceptual:

```text
Cotizacion existente
       |
       v
CotizacionDocumentData
       |
       v
DocumentRenderer
```

El ViewModel de impresión debe estar desacoplado de entidades de persistencia cuando sea posible.

Esto evita que el template dependa directamente de estructuras internas de EF o de tablas legacy.

---

# 27. Contrato de datos inicial para Cotización

Modelo conceptual, sujeto a adaptación a las clases existentes de Alfa Core:

```text
Empresa
  Nombre
  CUIT
  Domicilio
  Telefono
  Email
  Logo

Comprobante
  Numero
  Fecha
  FechaVencimiento
  Vendedor
  CondicionVenta

Cliente
  Codigo
  RazonSocial
  CUIT
  Domicilio
  Telefono
  Email

Items[]
  Codigo
  Descripcion
  Cantidad
  Precio
  Descuento
  Total

Totales
  Neto
  Descuento
  Impuestos
  Total

Observaciones
```

Codex deberá mapear esto contra los nombres reales del proyecto.

---

# 28. Primera pantalla

Primera versión de:

```text
Utilidades -> Diseñador de comprobantes
```

No será todavía un canvas de drag & drop.

Debe permitir como mínimo:

```text
Unidad de Negocio
Tipo de documento
Plantilla

Nueva
Duplicar
Editar
Guardar

Formato A4/A5
Orientación vertical/horizontal
Márgenes

Logo visible
Alineación del logo
Tamaño del logo

Campos visibles de empresa
Campos visibles de cliente
Campos visibles del comprobante

Columnas visibles del detalle
Orden de columnas
Ancho de columnas
Alineación

Totales visibles
Texto de observaciones/pie

Vista previa
Generar PDF
```

La primera implementación puede reducir este conjunto si es necesario, siempre conservando la arquitectura preparada para ampliarlo.

---

# 29. Etapas de implementación

## Etapa 1 - Motor mínimo viable

Objetivo: demostrar de punta a punta que una Cotización puede generarse mediante una plantilla persistida.

Incluye:

- tablas base nuevas `CORE_`;
- modelos C# de template;
- persistencia JSON;
- plantilla estándar de Cotización;
- datos de Cotización -> DocumentData;
- renderer JSON -> HTML;
- preview HTML;
- generación HTML -> PDF con Playwright;
- pantalla inicial dentro de Utilidades;
- selección por `UNegocio`;
- configuración básica de papel/logo/columnas;
- coexistencia con QuestPDF.

No incluye:

- drag & drop;
- IA;
- scripting libre;
- diseñador absoluto por coordenadas;
- reglas complejas de asignación;
- editor HTML completo;
- todos los tipos de comprobantes.

---

## Etapa 2 - Personalización completa de Cotización

- mostrar/ocultar campos;
- reorganizar columnas;
- estilos configurables;
- tipografías;
- colores;
- encabezados;
- pie;
- recursos gráficos;
- varias plantillas por `UNegocio`;
- plantilla predeterminada;
- duplicación;
- historial/versiones;
- restauración.

---

## Etapa 3 - Diseñador por bloques

- toolbox de componentes;
- drag & drop;
- contenedores/columnas;
- panel de propiedades;
- reordenamiento;
- editor de bloques;
- preview en tiempo real.

---

## Etapa 4 - Nuevos tipos de documentos

Reutilizar el motor para:

- Factura
- Remito
- Recibo
- Pedido
- Proforma
- Orden de compra
- etc.

Cada tipo debe aportar:

- contrato de datos;
- catálogo de campos;
- plantilla estándar;
- resolver de datos.

---

## Etapa 5 - IA

- prompt en lenguaje natural;
- contexto del JSON actual;
- catálogo de campos permitidos;
- salida estructurada;
- validación;
- preview;
- confirmar/rechazar;
- auditoría en `CORE_DocumentTemplateAiLog`.

---

# 30. Estrategia de migración desde QuestPDF

No realizar reemplazo total inicial.

Propuesta:

```text
Cotización
  -> nuevo motor

Otros PDFs
  -> continúan con QuestPDF
```

Una vez validado el nuevo motor:

```text
Remito
Factura
Recibo
...
```

se migran individualmente.

Debe existir una forma sencilla de volver temporalmente al generador actual si aparece una regresión durante la implantación inicial.

---

# 31. Manejo de errores y logs

Agregar logs claros en las etapas críticas:

```text
Carga de template
Deserialización JSON
Validación
Obtención de DocumentData
Render HTML
Render PDF
Carga de recursos
Resolución de plantilla predeterminada
```

Los logs deben incluir cuando corresponda:

```text
IdTemplate
UNegocio
TipoDocumento
Id del comprobante
Usuario
Excepción
```

No registrar datos sensibles innecesarios.

---

# 32. Pruebas mínimas

## Persistencia

- crear plantilla;
- editar plantilla;
- leer plantilla;
- duplicar plantilla;
- mantener independencia por `UNegocio`.

## JSON

- JSON válido;
- JSON corrupto;
- versión de esquema no soportada;
- bloque desconocido;
- campo desconocido.

## Cotización

Probar:

- sin ítems;
- un ítem;
- muchos ítems;
- descripción larga;
- cliente con razón social larga;
- importes grandes;
- logo presente;
- logo ausente;
- observación extensa;
- varias páginas.

## PDF

- A4 vertical;
- A4 horizontal;
- márgenes;
- salto de página;
- encabezado y pie;
- coincidencia razonable entre preview y PDF.

## Unidad de Negocio

- plantilla específica;
- plantilla global;
- fallback correcto;
- aislamiento entre unidades de negocio.

---

# 33. Criterios de aceptación de la Etapa 1

La primera etapa se considera exitosa cuando:

1. existe `Utilidades -> Diseñador de comprobantes`;
2. se puede elegir la Unidad de Negocio usando el mecanismo existente de Alfa Core;
3. existe al menos una plantilla de Cotización persistida en `CORE_DocumentTemplate`;
4. su definición está guardada en JSON;
5. la definición se puede editar mediante un formulario básico;
6. existe una vista previa HTML;
7. la misma representación puede convertirse a PDF;
8. una Cotización real puede renderizarse usando esa plantilla;
9. el sistema actual basado en QuestPDF continúa funcionando durante la transición;
10. no se implementa todavía IA ni drag & drop.

---

# 34. Decisiones que Codex debe tomar inspeccionando el proyecto

Antes de modificar código, Codex debe verificar:

- solución/proyectos existentes;
- arquitectura actual;
- versión .NET;
- modalidad Blazor utilizada;
- ORM y DbContext existentes;
- estrategia de migraciones;
- mecanismo actual de `UNegocio`;
- autenticación y usuario logueado;
- menú de Utilidades;
- implementación actual de Cotizaciones;
- `CotizacionPdfService` o servicio equivalente;
- uso actual de QuestPDF;
- infraestructura para DI;
- librerías UI utilizadas;
- patrones de logging;
- políticas de autorización existentes.

Debe reutilizar patrones existentes y evitar introducir frameworks paralelos innecesarios.

---

# 35. Principios de implementación

1. **No romper funcionalidad existente.**
2. **No migrar todos los documentos de una vez.**
3. **JSON es la fuente de verdad.**
4. **HTML es una representación generada.**
5. **Preview y PDF deben compartir renderer.**
6. **La Unidad de Negocio se maneja mediante `UNegocio NVARCHAR(4)`.**
7. **Las tablas nuevas utilizan prefijo `CORE_`.**
8. **Cotización es el primer caso real.**
9. **Primero motor estable, luego diseñador visual.**
10. **Primero diseñador visual, luego IA.**
11. **IA modifica estructura validada, no código C#.**
12. **Las plantillas de sistema no se pisan con personalizaciones.**
13. **Guardar versiones antes de cambios importantes.**
14. **Evitar posicionamiento absoluto mientras no sea necesario.**
15. **No duplicar la lógica para resolver plantillas.**

---

# 36. Resultado esperado a largo plazo

El usuario debería poder realizar acciones como:

```text
- mover el logo;
- aumentar su tamaño;
- ocultar un campo;
- agregar el teléfono del vendedor;
- cambiar A4 por A5;
- modificar márgenes;
- quitar la columna Código;
- cambiar el orden de las columnas;
- resaltar el Total;
- agregar una firma;
- guardar variantes de una Cotización;
```

sin modificar C#, recompilar ni desplegar una nueva versión de Alfa Core.

En una etapa posterior podrá escribir:

```text
"Poné el logo arriba a la derecha, sacá el código del artículo y hacé el total más grande."
```

El sistema utilizará IA para proponer cambios sobre la definición estructurada, mostrará una vista previa y solamente guardará el resultado cuando el usuario lo confirme.

---

# 37. Conclusión

La solución recomendada no intenta replicar Crystal Reports completo desde el primer día.

Se construye primero una infraestructura de documentos moderna y desacoplada:

```text
SQL Server
    -> Template JSON
        -> Renderer C#
            -> HTML/CSS
                -> Preview Blazor
                -> Playwright PDF
```

Con esta base, Alfa Core podrá evolucionar progresivamente hacia un diseñador visual y luego hacia un diseñador asistido por IA, preservando compatibilidad con la arquitectura existente y permitiendo personalización por Unidad de Negocio.
