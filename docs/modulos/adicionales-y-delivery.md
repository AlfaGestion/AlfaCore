# Adicionales y pedidos web para gastronomía

**Estado:** propuesta para aprobación · **Relevamiento:** 28 de noviembre de 2026

AlfaCore ya recibe pedidos manuales de delivery y retiro y envía comandas desde Salón. La propuesta agrega opciones por artículo, publica una carta para comprar sin mesa y conecta esos pedidos con la operación existente. El comprobante conserva su circuito fiscal y contable, con el importe de los adicionales incluido en el precio de cada artículo.

## 1. Relevamiento

Se hicieron consultas parametrizadas `SELECT` de metadatos y una muestra sin datos identificatorios sobre `ALFANET`, con `ApplicationIntent=ReadOnly`. La base relevada corre en SQL Server 2016, con collation `Latin1_General_CI_AI` y nivel de compatibilidad 100. Las confirmaciones SQL se limitan a esa base. La instalación de Loren Burgers, el circuito real de ambas sucursales y el ejecutable VB6 quedan pendientes para el piloto.

| Área | Base relevada y código actual | Consecuencia para el diseño |
| --- | --- | --- |
| Artículos y precios | `V_MA_ARTICULOS` es tabla, con PK `IDARTICULO nvarchar(25)`. `PuntoVentaService` resuelve catálogo y precio efectivo, con lista/clase por cliente o punto de venta. `PuntoVentaCartaService.GetCartaAsync` usa los rubros y la lista del punto, y consulta agotados en `POS_ARTICULO_AGOTADO`. | Usar el maestro y el cálculo comercial actual. El adicional es una opción comercial del artículo base. `V_MV_AdicionalesAFacturar`, `C_MV_CPTE_ADICIONALES` y `P_TA_FORMULAS` existen, pero sus estructuras corresponden a otros circuitos y carecen del contrato grupo/opción por línea de venta. |
| Pedidos manuales | `VentasDelivery.razor` (`/ventas/delivery`) usa `PuntoVentaPedidoService` y abre `VentasPuntoVenta.razor` para cerrar. `POS_PEDIDO` ya tiene `IDPUNTOVENTA`, `MODALIDAD` (`DELIVERY`/`TAKEAWAY` y `SALON` en el circuito de mesas), cliente, teléfono, dirección, `OBSERVACIONES nvarchar(500)`, `HORARIOPROGRAMADO`, estado, marcas de alta/modificación y vínculo `TC`/`IDCOMPROBANTE`. `POS_PEDIDO_DET` tiene PK `ID int`, precio `money`, IVA, cantidad y `OBSERVACIONES nvarchar(250)`. | Reutilizar las dos tablas. El editor manual actualmente reemplaza el detalle al guardar y el cambio de estado valida el código sin imponer toda la secuencia. Con comandas y opciones será necesario preservar identidades y restringir las transiciones de los pedidos web. |
| Salón y comandas | `VentasSalon.razor`, `PuntoVentaSalonService`, `VentasCocina.razor`, `PuntoVentaCocinaService` y `PuntoVentaSalonTickets` usan `POS_SECTOR`, `POS_MESA`, `POS_PEDIDO_DET` y `POS_COMANDA`. El detalle tiene `ESTADO`, `DESTINO`, `ORIGEN` e `IDCOMANDA`. El ticket ya imprime la observación de la línea. La grilla de mesas carga pedidos `SALON` con mesa y estado `ABIERTA`/`CUENTA`. `V_MV_Comanda` existe para el Salón VB6. | Compartir el envío por destinos y el render de detalles. Incorporar una vista por pedido para la plaza virtual. Mantener el circuito de mesas y su PIN. [Documentación de Salón](pos-salon.md) |
| Comprobantes | `PuntoVentaService.CreateSaleAsync` usa `sp_web_Alta_Comprobante` y `sp_web_CpteInsumos` para `V_MV_Cpte` y `V_MV_CpteInsumos`. El detalle tiene `ID int IDENTITY`, sin índice único propio en la base relevada, y PK compuesta `TC, IDCOMPROBANTE, IDCOMPLEMENTO, IDARTICULO, SECUENCIA`. Tiene `IMPORTE`, `IMPORTE_S_IVA`, `AlicIVA` y `TotalFinal`. `V_MV_CpteInsumos.OBSERVACIONES` está ausente en `ALFANET`; `AddReceiptItemAsync` solo actualiza el campo si existe. `V_MV_CPTE_OBSERV` existe y guarda conceptos u observaciones del comprobante. | Guardar la observación de línea fiscal en una extensión web. Revisar el SP, que abre transacción y devuelve `@pIdVMVCpteInsumosRES` mediante `@@IDENTITY`. En la muestra de seis cabeceras `CB`, `DATALENGTH(TC)` devolvió 4 bytes y el relleno final de `CUENTA` fue 0 bytes. |
| Carta QR y carrito | `CartaMesa.razor` usa `PublicLayout`, `PuntoVentaCartaService`, token `MESA` de `CentralPublicLinkService` y PIN al pedir. Hoy agrega por artículo y pasa al servicio una observación común al envío. `CatalogoCarrito.razor` guarda un `Dictionary<string, decimal>` por código en localStorage y `CarritoComprasService.ConfirmarPedidoPublicoAsync` agrupa por artículo y genera `NP` para el circuito comercial existente. `ALFACORE_CARRITOS_WEB` y `ALFACORE_CARRITOS_WEB_DET` son definiciones de carritos generales con detalle único por artículo. | Extraer catálogo/selección y componentes compartidos donde corresponda. La carta gastronómica usa `POS_PEDIDO`. El registro de enlaces `ALFA_CENTRAL.ALFA_PUBLIC_LINK` se verificó en el código de `CentralPublicLinkService`; la instancia auditada corresponde a la base operativa. |
| Sucursales | `POS_PUNTOVENTA` tiene `ID int`, `CODIGO nvarchar(4)`, `SUCURSAL nvarchar(4)`, `UNEGOCIO nvarchar(4)` e `IDCAJA`. `PuntoVentaConfigService` toma opciones de `TPV_SUCURSAL` y los puntos configurados. `V_TA_UnidadNegocio` tiene PK `Codigo`; `V_TA_TPV` contiene campos de sucursal y terminal de sincronización. | Publicar una sucursal de entrega por `POS_PUNTOVENTA.ID` en el piloto, con mapeo fiscal y operativo explícito. Los códigos de sucursal, unidad y caja tienen funciones distintas. |
| Repartos y tarifas legacy | `V_MV_Reparto`, `V_MV_RepartoDet`, `VT_MVREPARTO` y vistas de insumos pendientes existen. El reparto legacy está estructurado alrededor de planilla, chofer, vehículo y artículos. `V_TA_TarifaFlete` tiene código, descripción, importe y tipo, sin sucursal ni límites de distancia. | El repartidor gastronómico toma un pedido individual, con estados y permisos por sucursal. El piloto usa esa asignación en la extensión del pedido y tarifas hijas del punto con límites en metros. |
| Mercado Pago y WhatsApp | Existe `MercadoPagoPointPosService`, `MercadoPagoPointConfigService`, el cliente HTTP de Point y un webhook de Point en `Program.cs`. Checkout Pro y geocodificación no aparecieron en el código relevado. WhatsApp tiene webhooks Meta en `Program.cs`, `CONV_PLANTILLAS*`, `ConversacionesService.SendTemplateMessageAsync`, `WhatsAppRuntimeCredentialResolver` y `WhatsAppSecureVault`. El servicio de plantilla atiende el contexto de una conversación; también existe un envío interno a teléfono que conviene revisar para extraer la operación común. | Checkout Pro necesita una integración con su propia cuenta, ruta y reconciliación. Reutilizar transporte y gestión de plantillas Meta, con una operación de servidor acotada a notificaciones de pedidos. |
| Configuración y acceso | `TA_CONFIGURACION` tiene PK física `(GRUPO, CLAVE)`, `VALOR nvarchar(150)`, `ValorAux ntext` y `DESCRIPCION nvarchar(250)` en esta base. `ConfiguracionSql` ya busca por `CLAVE` y hoy solo maneja `VALOR`. Se encontraron claves `GOUR_*`, `TPV_SUCURSAL` y `MERCADOPAGO_*` de Point. `TA_USUARIOS` tiene `NOMBRE` y `SISTEMA`; `PermissionService` centraliza acceso web y `AppEventService` es el servicio de eventos y errores. | Extender la lectura centralizada a `ValorAux`, respetar el límite portable de 50 caracteres para descripciones y reutilizar permisos y `AUX_ERR`. |

## 2. Diferencias con la propuesta inicial

| Propuesta inicial | Decisión y motivo |
| --- | --- |
| Grupo con `MIN`, `MAX` y `OBLIGATORIO` | Conservar `TIPO` y `MODO`. `MINIMO > 0` representa obligatoriedad y evita inconsistencias entre un booleano y el mínimo. `UNICA` tiene máximo 1. |
| Hijas de pedido y factura; observación por línea | Usar `POS_PEDIDO_DET.OBSERVACIONES` y agregar su hija de opciones. En la factura, usar una extensión de línea con observación base y una hija de opciones. Las fotos históricas incluyen rótulo y precio, para reimpresión aun cuando cambie el catálogo. |
| Nuevo carrito y nuevo panel | Reusar la carta y los componentes de catálogo aplicables, el layout público y `/ventas/delivery` como panel operativo. Un borrador público ligero da continuidad y control de reenvíos. El panel y la plaza del Salón comparten consultas y comandos. |
| Sucursal virtual como sector/plaza | Una sección virtual «Pedidos web» en Salón, filtrable por Delivery/Retiro, proyecta los pedidos del alcance autorizado. Cada pedido puede avanzar de forma independiente; `IDMESA` queda nulo. La cocina usa el punto operativo asignado y sus destinos. |
| Mensajes de texto configurables | Configurar el disparador y la referencia a una plantilla Meta aprobada por número, idioma y evento, más las variables permitidas. Los cambios de redacción van por el catálogo de plantillas y su ciclo de aprobación. |
| Descuento de insumos vinculados | Guardar artículo y cantidad vinculada como datos de receta futura, también en la foto histórica. El primer alcance mantiene la imputación fiscal y de stock existente del artículo vendido. |

## 3. Diseño funcional y técnico

### Etapa 1 — Adicionales en POS, Salón y comanda

- ABM de grupos reutilizables con baja lógica, ítems ordenados y asignación desde la ficha de artículo. Grupos `AGREGAR` contabilizan opciones seleccionadas y aplican su recargo unitario. `QUITAR` comienza con todos los ítems activos incluidos, contabiliza los ítems retirados y registra cada acción `QUITAR` a precio cero. El valor inicial del grupo es cero retiros. Los predeterminados de `AGREGAR` respetan el máximo. La validación comprueba que los límites son alcanzables con las opciones activas.
- Un solo `SelectorAdicionales.razor` propuesto, con componentes AlfaDesign, devuelve IDs de opciones y observación de línea. Se monta en el alta/edición del Mostrador, Salón y pedido manual existente, y luego en la carta pública. `AdicionalesService` centraliza catálogo, validación, importe y foto histórica. Las rutinas puras de `Common` se ocupan de ordenar selecciones y construir la clave de comparación. El servidor valida pertenencia al artículo, actividad, obligatoriedad, min/max, duplicados, longitud y permisos.
- La agrupación exige misma identidad comercial, IVA, combinación de opciones, observación y precio. Unidades con distintas opciones o notas quedan en líneas distintas aunque coincida el precio. Cada línea tiene un identificador propio. Revisar las consolidaciones y reasignaciones de precio del Mostrador, incluida la carga de pedidos agrupada por artículo/precio/IVA. El ingreso rápido y el escáner abren el selector si el artículo requiere resolver opciones. `Ctrl+F`, `Ctrl+P`, `Ctrl+G`, `Ctrl+B` y `Esc` mantienen los handlers estándar. Con el selector abierto, `Esc` cierra sin guardar y el foco vuelve al disparador.
- Guardar opciones y detalle POS en la misma transacción. Para una línea pendiente, editar por ID o borrar explícitamente junto con sus hijas. Al enviar, congelar la preparación: cambios posteriores por anulación con motivo y nueva línea. Si se une o mueve una mesa, conservar IDs y detalle. La comanda usa el destino del artículo principal, imprime las opciones debajo del artículo y luego la nota libre. El mismo formateo alimenta cocina, barra y reimpresión. Ejemplo de salida: «– SIN KETCHUP».
- Usar el precio efectivo del artículo según el POS, sumar los recargos unitarios y aplicar el recorrido actual de IVA y redondeo. El DTO y `@pImporteUnitario` reciben el unitario final. Verificar en la prueba de venta `IMPORTE`, `IMPORTE_S_IVA`, `AlicIVA`, `TotalFinal`, totales de cabecera, ticket, CAE, asiento y cobranza contra un equivalente del VB6. Ejemplo de aceptación para una combinación gravada, con precios de pantalla de $8.000 y $500 de adicional: dos unidades iguales muestran $17.000 antes de otros ajustes admitidos. Una unidad de $8.500 y otra con una combinación distinta de $8.500 producen dos líneas.
- El alcance inicial trata los adicionales como parte del artículo principal, con su misma alícuota. Un ítem vinculado de otra alícuota requiere decisión fiscal previa a su habilitación. El vínculo de receta no agrega líneas cobrables. La fase futura definirá unidades, conversiones, insumos base, incrementos y removidos, depósito, reversión y merma con el circuito oficial de stock, sin anticipar sus movimientos en este DDL.

### Comprobante y recuperación

El alta normal de `CreateSaleAsync` ejecuta las líneas por separado y, al recuperar un comprobante existente, el hecho de que haya artículos basta para omitir esa etapa. Para el circuito nuevo se propone registrar una operación estable antes de comenzar y guardar su vínculo con la cabecera al crearla. Persistir por línea el resultado del alta y la extensión dentro de una transacción local. El índice por operación/posición permite devolver el resultado ya grabado y detectar qué línea falta si hubo un corte. El hash de solicitud evita aceptar otra venta con la misma clave. Reutilizar el patrón de los journals `ALFACORE_COBRANZA_OPERACION` y `ALFACORE_VENTAS_CARGA_OPERACION`, adaptado a las etapas del POS.

Verificar en homologación que el SP y sus triggers admiten ese límite transaccional. Para identificar la línea exacta, corregir en una actualización compatible la obtención del ID en el SP web, conservando su firma y capturando el `INSERTED.ID` de la inserción propia mediante `OUTPUT … INTO` o mecanismo equivalente validado. Resolver la clave compuesta después de los triggers, dentro de esa transacción. La extensión de la factura guarda como vínculo lógico ID técnico, cabecera y clave de detalle; el lector coteja esos valores al consultar. Evitar una FK sobre los movimientos legacy que pueda bloquear borrados o reconstrucciones de VB6. Definir antes del piloto el tratamiento de ediciones, anulaciones y purgas legacy que dejen un vínculo histórico sin padre.

Para un pedido convertido en venta, usar una clave estable por `POS_PEDIDO.ID` y persistir qué etapas quedaron confirmadas. Si falla CAE, asiento, cobranza o vínculo al pedido, consultar la etapa correspondiente antes de reintentar. Vincular la factura existente y recuperar sus faltantes, evitando otra emisión u otro cobro. La emisión externa se concilia por los identificadores del circuito actual, con pausas para revisión si el resultado queda incierto. `MarcarFacturadoAsync` hoy pone `ENTREGADO`; en pedidos web debe vincular el comprobante y preservar el estado de entrega hasta que el operador o repartidor complete la entrega. Respetar `.Trim()` para `TC` y `CUENTA` y validar `DATALENGTH` contra movimientos equivalentes al probar. Usar `CodigoPk.Format` en códigos de maestros y comparar códigos con `LTRIM(RTRIM(...))`. Mantener numeración, comprobantes, impuestos, caja y asientos a cargo del motor vigente.

### Etapa 2 — Carta QR y carrito web

1. En `CartaMesa.razor`, compartir el selector y el modelo de líneas; enviar opciones y observaciones por línea con el flujo actual de PIN, autorización y autoaceptación. Mantener la observación general del envío en el contexto del pedido según acuerdo operativo.
2. Crear el canal público gastronómico bajo `PublicLayout` con marca del comercio, link expirable o revocable del alcance autorizado y un tipo nuevo en `CentralPublicLinkService`. El token resuelve la base; las sucursales seleccionables las deriva el servidor de los puntos publicados. La pantalla comparte catálogo, rubros, precios, disponibilidad y selector con Salón. Aplicar límites por enlace/sesión/origen y validar cada envío del carrito.
3. Elegir sucursal, armar y editar el resumen, capturar nombre, celular normalizado y observación general. Cambiar de punto obliga a recalcular disponibilidad y precio y aceptar las diferencias. Conservar un borrador versionado con un identificador aleatorio generado una vez, más un secreto de sesión independiente guardado como hash. Almacenar el JSON en SQL y validarlo en C# para compatibilidad 100. El navegador solo conserva la referencia segura, sin teléfono ni dirección en localStorage. Al volver atrás se restaura el mismo borrador. La etapa 2 deja operativo el envío del QR y permite revisar el carrito externo de punta a punta hasta el resumen; el cierre público se habilita con la etapa 3.

### Etapa 3 — Checkout, recepción y repartidor

**Cotización.** En retiro se omiten dirección y envío. En delivery, ingresar dirección y confirmar el punto geocodificado o usar ubicación actual con permiso y confirmación visual; guardar una dirección o referencia operativa suficiente para que el repartidor pueda entregar. Mostrar instrucciones de la sucursal, aclaración libre y demora estimada. Calcular el radio y el tramo en el servidor, con distancia geodésica en metros como criterio inicial y señalización explícita al cliente. Tramos contiguos `[desde, hasta)` con último límite inclusivo, sin solapamientos ni huecos hasta el radio. Caso de aceptación: 1.000 m pasa al segundo tramo cuando el primero termina en 1.000; el radio exacto se acepta; un metro más se rechaza. Cotizar con artículo de envío configurado, monto pactado y alícuota propia; proponer línea de $0 para el tramo gratis si el motor y el comprobante la admiten, decisión pendiente de homologación. El artículo de envío queda fuera de cocina y de la carta.

**Horarios y geocodificación.** Interpretar calendario por zona horaria de sucursal, con intervalos semanales, cruce de medianoche y excepciones de fecha. Revalidar apertura, agotados y vigencia del presupuesto al confirmar. Una aprobación posterior al cierre o vencimiento entra en revisión operativa, con resolución y eventual devolución auditada. Usar proveedor configurable comercial o Nominatim propio, con timeout, límite y caché por proveedor/consulta normalizada según licencia y política de retención. El caché excluye nombre, teléfono y referencias privadas y puede empezar en memoria con TTL. Ante dirección ambigua o proveedor caído, ofrecer corregir/elegir ubicación válida o retiro. El Nominatim público limita a 1 solicitud por segundo, excluye autocompletado y pide no enviar datos personales. [Política de Nominatim](https://operations.osmfoundation.org/policies/nominatim/)

**Confirmación e idempotencia.** La clave de borrador se convierte en `external_reference` de pago y queda como vínculo único con un `POS_PEDIDO`. Una transacción reclama el borrador por versión, revalida, congela importes y escribe cabecera, líneas, opciones y extensión web. Dos pestañas o reenvíos con la misma clave y misma intención devuelven el pedido existente; una carga distinta tras el cierre exige resolución explícita del pedido anterior. El retiro o delivery en efectivo pasa a `PENDIENTE` / «Recibido», con pago contra entrega. Mercado Pago queda `ESPERA_PAGO` y visible como pendiente de pago en recepción, fuera de la cola de cocina, impresión y avisos de recibido.

**Mercado Pago.** Proponer Checkout Pro vía Preferences API para el piloto. Reservar el intento local antes de solicitar preferencia reutilizable y crearla con montos del servidor, expiración, URLs de retorno y referencia externa. Ante timeout con resultado desconocido, reconciliar antes de crear una segunda preferencia. El retorno del navegador solo consulta estado. El webhook identifica la configuración por un identificador público de integración asignado en servidor, verifica `x-signature`, persiste la recepción deduplicada y consulta el pago a la API del comercio. Validar moneda, importe completo, titular/cuenta cobradora, entorno, referencia y estado `approved`. Fijar el pago aceptado una sola vez dentro de la transacción que libera el pedido y registra el evento. Reintentar consultas y reconciliar intents abiertos mediante un proceso programado; los eventos viejos no regresan estados y los pagos múltiples, tardíos, reembolsos o contracargos entran en excepción operativa. Confirmar qué cuenta cobra y qué código imputa la cobranza antes de habilitar el piloto. [Preferences API](https://www.mercadopago.com.ar/developers/es/reference/online-payments/checkout-pro-preferences/overview) · [Notificaciones de pago](https://www.mercadopago.com.ar/developers/es/docs/checkout-pro-preferences/payment-notifications)

**Operación.** El panel recibe el pedido en `PENDIENTE`, acepta y emite comandas una sola vez a cocina/barra, y pasa a `EN_PREPARACION`. Al quedar listas todas las líneas de preparación se pasa a `LISTO`; para `TAKEAWAY` se muestra «Listo para retirar». Para delivery, tras asignar un repartidor se avanza de `LISTO` a `EN_CAMINO` y luego a `ENTREGADO`. En retiro se completa por mostrador desde `LISTO`. Anulación o expiración exige motivo y separa devolución de pago de cancelación operativa. Los cambios de estado, asignaciones, liberaciones, avisos de llegada y excepciones registran hora UTC, actor y motivo cuando corresponde. Cada comando verifica estado y versión y escribe historial en la misma transacción. Compartir en `PuntoVentaSalonService` la operación que agrupa por destino y revalidar el modo y punto permitido. Revisar qué pasa con una cocina que también recibe mesas y ventas manuales.

**Sucursal fiscal.** `CreateSaleAsync` acepta sucursal para el comprobante, pero el código de POS usa `PosUNegocio = "   1"` para disponibilidad, numeración y CAE, y documenta el mismo literal en el alta web. Si el piloto necesita emisores por unidades distintas en una misma base, homologar la resolución de unidad por punto a lo largo de alta, CAE, asiento y caja antes de publicar la segunda sucursal. El mapeo debe venir del punto y de la configuración fiscal vigente.

**Repartidor.** Vista móvil autenticada con permisos explícitos, alcance por punto, listado de pedidos disponibles y propios, toma atómica con `UPDATE` condicional y reasignación auditada para despacho. Revelar dirección y teléfono solo a despacho y al repartidor asignado. `Abrir Google Maps` usa el destino confirmado, con parámetros codificados. «Llegué» genera un evento auditable con plantilla configurada y deduplicación. La entrega en efectivo registra la confirmación del cobro y la conciliación con caja según el circuito a aprobar. [Google Maps URLs](https://developers.google.com/maps/documentation/urls/get-started)

**WhatsApp.** Según configuración, encolar `RECIBIDO`, `LISTO_PARA_RETIRAR`, `EN_CAMINO` y, para la acción del repartidor, `LLEGUE`. Registrar cuándo se obtuvo consentimiento y qué texto aceptó el comprador. Usar exclusivamente Meta, resolver número, idioma y plantilla aprobada de utilidad con la integración existente y sus credenciales protegidas. Separar el envío externo de la transacción del pedido. Reclamar la cola con vencimiento, guardar el ID de Meta, reintentar solo fallos seguros y derivar confirmaciones ambiguas a revisión para evitar dobles envíos. Si el aviso falla, el pedido sigue visible en el panel. [Política de WhatsApp Business](https://business.whatsapp.com/policy) · [Plantillas de Meta](https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/overview)

### Configuración, componentes y permisos

Claves nuevas propuestas en grupo `VENTAS`, leídas por `CLAVE` con `ConfiguracionSql`. Usar `VALOR` para valores cortos y `ValorAux` cuando corresponda, con fallback si `VALOR` está vacío. Las familias por punto usan el ID numérico y cada clave respeta los 50 caracteres. Confirmar colisiones y usar las claves funcionales vigentes donde ya cubran la misma semántica.

| Clave | Uso |
| --- | --- |
| `POS-ADICIONALES-HABILITADO` | Habilitación gradual de la edición y selección. El histórico se sigue mostrando. |
| `POS-PEDIDOS-WEB-HABILITADO`, `POS-PEDIDOS-WEB-BORRADOR-MIN` | Publicación general y vencimiento del borrador. |
| `POS-PEDIDOS-WEB-PUNTO-{ID}` | JSON por punto con publicación, etiqueta pública, modalidades, medios habilitados, zona, coordenadas, radio, horarios, excepciones, demora, referencia de integración y textos al comprador. Incluye los textos de entrega/retirada y la versión de consentimiento. |
| `POS-PEDIDOS-WEB-ARTICULO-ENVIO` | Código del artículo maestro de envío. Usar por base, con posibilidad de valor en el JSON de punto si se necesita distinta imputación. |
| `POS-PEDIDOS-WEB-GEOCODIFICACION` | Proveedor y perfil no secreto, TTL, timeout y política de límites. |
| `POS-PEDIDOS-WEB-CHECKOUT` | Caducidad de cotización/preferencia y política de espera/reconciliación. Perfil por punto con referencias a credenciales seguras y código de medio de pago contable. |
| `POS-PEDIDOS-WEB-WHATSAPP` | Mapeo de eventos habilitados, número, plantilla, idioma y variables por alcance. |

Los tramos se editan en la misma pantalla de configuración y se guardan como filas hijas del punto. Las reglas entre tramos y el radio de `TA_CONFIGURACION` se validan juntas y se actualizan en una transacción, bloqueando el punto durante el cambio. Guardar credenciales de Mercado Pago y geocodificación en almacenamiento seguro o variables de entorno, por comercio/entorno. Reutilizar el vault Meta existente. De las claves `MERCADOPAGO_*` relevadas solo se consultaron nombres; Checkout Pro configura sus propias credenciales y evalúa reutilizar un medio contable únicamente si Tesorería lo aprueba.

`PuntoVentaPedidoWebService` propuesto coordina cotización, confirmación, despacho y eventos, con repositorios Dapper parametrizados. `PuntoVentaPedidoService`, Salón, cocina y Mostrador siguen dando sus capacidades de dominio. Clientes de geocodificación y Checkout Pro contienen su transporte, como servicios específicos; usar `IAppEventService` para errores relevantes en `AUX_ERR`, con correlación técnica y sin datos personales, tokens ni payload de pago en logs. Usar autorización en servidor para cada operación, incluida la restricción por base y punto. Para acceso de repartidor, aplicar permiso web explícito y perfil reducido, sin extender el acceso general del usuario.

En las pantallas internas nuevas usar barra superior y Context Toolbar AlfaDesign, `PageHeaderShellMode.AlfaDesignPilot`, `IPageHeaderService`, `ModuleKey = "ventas"` en cada publicación y registro de `ventas` en `AlfaDesignManagedModules`. Mantener `?directo=1` durante la navegación interna y ocultar Aplicaciones y accesos externos al módulo. Ubicar la parametrización en Ventas > Configuración y publicar panel/repartidor bajo Ventas / Punto de Venta. Revisar `D6055-DELIVERY`, desactivado como acceso separado cuando se introdujo el selector POS. Toda nueva entrada interna tendrá su actualización idempotente de `ALFACORE_MENU_WEB` y `ALFACORE_TAREAS_WEB`, permiso explícito cuando aplique y manual/ruta contextual en `AyudaManuales.Pantallas`. La carta invitada sigue el `PublicLayout` actual con los componentes compartidos. [Guía AlfaDesign](../ui/alfadesign-module-guide.md) · [Configuración global](../CONFIGURACION_GLOBAL.md)

El listado del ABM de grupos usa búsqueda y paginación en servidor (`OFFSET/FETCH` se comprobó en `ALFANET`) y «Configurar vista» por usuario, persistido en `TA_CONFIGURACION` con el patrón `USUVIEW`. Confirmar el motor mínimo de las bases del piloto antes de escribir las consultas definitivas. [Reglas de desarrollo](../CODEX_RULES.md)

### DDL propuesto para revisión

El siguiente DDL es un borrador para la base operativa, dividido por etapas y limitado a estructuras nuevas. Está pensado para SQL Server con compatibilidad 100, usando el collation de la base para códigos de maestros. Las relaciones con tablas web propias llevan FK. Los códigos del maestro y la referencia al comprobante se validan en aplicación para evitar restricciones nuevas sobre escrituras legacy. La futura actualización deberá comprobar precondiciones y tipos, consolidar transaccionalmente los lotes y validar idempotencia en copia. La retención y depuración se definen por entidad y por obligación contable.

#### Etapa 1

```sql
IF OBJECT_ID(N'dbo.POS_OPC_GRUPO', N'U') IS NULL
CREATE TABLE dbo.POS_OPC_GRUPO (
    ID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    NOMBRE nvarchar(100) NOT NULL,
    TIPO nvarchar(8) NOT NULL,
    MODO nvarchar(8) NOT NULL,
    MINIMO smallint NOT NULL,
    MAXIMO smallint NOT NULL,
    ACTIVO bit NOT NULL DEFAULT (1),
    CHECK (TIPO IN (N'UNICA', N'MULTIPLE')),
    CHECK (MODO IN (N'AGREGAR', N'QUITAR')),
    CHECK (MINIMO >= 0 AND MAXIMO >= MINIMO AND MAXIMO > 0
           AND (TIPO <> N'UNICA' OR MAXIMO = 1))
);

IF OBJECT_ID(N'dbo.POS_OPC_ITEM', N'U') IS NULL
CREATE TABLE dbo.POS_OPC_ITEM (
    ID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IDGRUPO int NOT NULL REFERENCES dbo.POS_OPC_GRUPO(ID),
    DESCRIPCION nvarchar(120) NOT NULL,
    PRECIO_ADICIONAL decimal(19,4) NOT NULL DEFAULT (0),
    PREDETERMINADO bit NOT NULL DEFAULT (0),
    IDARTICULO_VINCULADO nvarchar(25) NULL,
    CANTIDAD_VINCULADA decimal(18,4) NULL,
    ORDEN smallint NOT NULL DEFAULT (0),
    ACTIVO bit NOT NULL DEFAULT (1),
    CHECK (PRECIO_ADICIONAL >= 0),
    CHECK ((IDARTICULO_VINCULADO IS NULL AND CANTIDAD_VINCULADA IS NULL)
        OR (IDARTICULO_VINCULADO IS NOT NULL AND CANTIDAD_VINCULADA IS NOT NULL
            AND CANTIDAD_VINCULADA > 0))
);

IF OBJECT_ID(N'dbo.POS_ARTICULO_OPC_GRUPO', N'U') IS NULL
CREATE TABLE dbo.POS_ARTICULO_OPC_GRUPO (
    IDARTICULO nvarchar(25) NOT NULL,
    IDGRUPO int NOT NULL REFERENCES dbo.POS_OPC_GRUPO(ID),
    ORDEN smallint NOT NULL DEFAULT (0),
    PRIMARY KEY (IDARTICULO, IDGRUPO)
);

IF OBJECT_ID(N'dbo.POS_PEDIDO_DET_OPC', N'U') IS NULL
CREATE TABLE dbo.POS_PEDIDO_DET_OPC (
    IDLINEA int NOT NULL REFERENCES dbo.POS_PEDIDO_DET(ID),
    IDITEM int NOT NULL REFERENCES dbo.POS_OPC_ITEM(ID),
    GRUPO nvarchar(100) NOT NULL,
    DESCRIPCION nvarchar(120) NOT NULL,
    ACCION nvarchar(8) NOT NULL,
    PRECIO_APLICADO decimal(19,4) NOT NULL,
    IDARTICULO_VINCULADO nvarchar(25) NULL,
    CANTIDAD_VINCULADA decimal(18,4) NULL,
    ORDEN smallint NOT NULL,
    PRIMARY KEY (IDLINEA, IDITEM),
    CHECK ((ACCION = N'AGREGAR' AND PRECIO_APLICADO >= 0)
        OR (ACCION = N'QUITAR' AND PRECIO_APLICADO = 0))
);

IF OBJECT_ID(N'dbo.POS_VENTA_OPERACION', N'U') IS NULL
CREATE TABLE dbo.POS_VENTA_OPERACION (
    IDOPERACION uniqueidentifier NOT NULL PRIMARY KEY,
    IDPEDIDO int NULL REFERENCES dbo.POS_PEDIDO(ID),
    HASH_SOLICITUD binary(32) NOT NULL,
    IDCPTE int NULL,
    IDCOBRANZA int NULL,
    ETAPA nvarchar(24) NOT NULL,
    FECHAHORA_UTC datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
    VERSION rowversion NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.POS_VENTA_OPERACION')
                 AND name = N'UX_POS_VENTA_OPERACION_PEDIDO')
CREATE UNIQUE INDEX UX_POS_VENTA_OPERACION_PEDIDO ON dbo.POS_VENTA_OPERACION(IDPEDIDO)
    WHERE IDPEDIDO IS NOT NULL;

IF OBJECT_ID(N'dbo.POS_CPTE_LINEA', N'U') IS NULL
CREATE TABLE dbo.POS_CPTE_LINEA (
    IDLINEA_CPTE int NOT NULL PRIMARY KEY,
    IDOPERACION uniqueidentifier NOT NULL REFERENCES dbo.POS_VENTA_OPERACION(IDOPERACION),
    POSICION int NOT NULL,
    IDCPTE int NOT NULL,
    TC nvarchar(4) NOT NULL,
    IDCOMPROBANTE nvarchar(13) NOT NULL,
    IDCOMPLEMENTO int NOT NULL,
    IDARTICULO nvarchar(25) NOT NULL,
    SECUENCIA int NOT NULL,
    IDLINEA_PEDIDO int NULL,
    OBSERVACIONES nvarchar(250) NULL,
    UNIQUE (IDOPERACION, POSICION)
);

IF OBJECT_ID(N'dbo.POS_CPTE_LINEA_OPC', N'U') IS NULL
CREATE TABLE dbo.POS_CPTE_LINEA_OPC (
    IDLINEA_CPTE int NOT NULL REFERENCES dbo.POS_CPTE_LINEA(IDLINEA_CPTE),
    IDITEM int NOT NULL REFERENCES dbo.POS_OPC_ITEM(ID),
    GRUPO nvarchar(100) NOT NULL,
    DESCRIPCION nvarchar(120) NOT NULL,
    ACCION nvarchar(8) NOT NULL,
    PRECIO_APLICADO decimal(19,4) NOT NULL,
    IDARTICULO_VINCULADO nvarchar(25) NULL,
    CANTIDAD_VINCULADA decimal(18,4) NULL,
    ORDEN smallint NOT NULL,
    PRIMARY KEY (IDLINEA_CPTE, IDITEM),
    CHECK ((ACCION = N'AGREGAR' AND PRECIO_APLICADO >= 0)
        OR (ACCION = N'QUITAR' AND PRECIO_APLICADO = 0))
);
```

La posición de `POS_CPTE_LINEA` identifica la línea en la solicitud congelada. La secuencia refleja la clave que quedó en el movimiento después de los triggers. Las operaciones de venta desde pedido aplican la unicidad cuando se emite el comprobante final. Casos de reemisión autorizada, notas de crédito y anulaciones se deben diseñar con su propio vínculo y nunca deben sustituir silenciosamente la operación original.

#### Etapa 2

```sql
IF OBJECT_ID(N'dbo.POS_PEDIDO_WEB_BORRADOR', N'U') IS NULL
CREATE TABLE dbo.POS_PEDIDO_WEB_BORRADOR (
    ID uniqueidentifier NOT NULL PRIMARY KEY,
    IDENLACE_PUBLICO int NOT NULL,
    HASH_SESION binary(32) NOT NULL,
    IDPUNTOVENTA int NOT NULL REFERENCES dbo.POS_PUNTOVENTA(ID),
    CONTENIDO nvarchar(max) NOT NULL,
    VENCE_UTC datetime2(0) NOT NULL,
    VERSION rowversion NOT NULL
);
```

`IDENLACE_PUBLICO` corresponde a `PublicLinkDto.IdPublicLink` (`int`), sin FK entre bases. La actualización central futura ampliará `CK_ALFA_PUBLIC_LINK_Tipo` a partir del catálogo vigente de `PublicLinkTipos`, conservando los tipos existentes. Para un enlace por comercio y base, acordar el valor de `IdReferencia` y el alcance de puntos publicados. `CONTENIDO` trae una versión de formato y se puede depurar al vencer el borrador.

#### Etapa 3

```sql
IF OBJECT_ID(N'dbo.POS_ENVIO_TARIFA', N'U') IS NULL
CREATE TABLE dbo.POS_ENVIO_TARIFA (
    IDPUNTOVENTA int NOT NULL REFERENCES dbo.POS_PUNTOVENTA(ID),
    DESDE_METROS int NOT NULL,
    HASTA_METROS int NOT NULL,
    IMPORTE decimal(19,4) NOT NULL,
    PRIMARY KEY (IDPUNTOVENTA, DESDE_METROS),
    CHECK (DESDE_METROS >= 0 AND HASTA_METROS > DESDE_METROS AND IMPORTE >= 0)
);

IF OBJECT_ID(N'dbo.POS_PEDIDO_WEB', N'U') IS NULL
CREATE TABLE dbo.POS_PEDIDO_WEB (
    IDPEDIDO int NOT NULL PRIMARY KEY REFERENCES dbo.POS_PEDIDO(ID),
    IDBORRADOR uniqueidentifier NOT NULL UNIQUE,
    IDENLACE_PUBLICO int NOT NULL,
    HASH_SESION binary(32) NULL,
    SEGUIMIENTO_VENCE_UTC datetime2(0) NULL,
    HASH_CONFIRMACION binary(32) NOT NULL,
    MEDIO_PAGO nvarchar(16) NOT NULL,
    ESTADO_COBRO nvarchar(24) NOT NULL,
    IDCOBRO_ACEPTADO bigint NULL,
    MONEDA char(3) NOT NULL,
    TOTAL decimal(19,4) NOT NULL,
    VENCE_PAGO_UTC datetime2(0) NULL,
    LATITUD decimal(9,6) NULL,
    LONGITUD decimal(9,6) NULL,
    DISTANCIA_METROS int NULL,
    REFERENCIA_ENTREGA nvarchar(250) NULL,
    IDLINEA_ENVIO int NULL REFERENCES dbo.POS_PEDIDO_DET(ID),
    DEMORA_MIN smallint NOT NULL,
    CONSENTIMIENTO_UTC datetime2(0) NULL,
    CONSENTIMIENTO_VERSION nvarchar(30) NULL,
    REPARTIDOR nvarchar(50) NULL,
    SISTEMA_REPARTIDOR nvarchar(50) NULL,
    VERSION rowversion NOT NULL,
    CHECK (MEDIO_PAGO IN (N'EFECTIVO', N'MERCADOPAGO')),
    CHECK (TOTAL >= 0 AND DEMORA_MIN >= 0),
    CHECK ((LATITUD IS NULL AND LONGITUD IS NULL AND DISTANCIA_METROS IS NULL)
        OR (LATITUD IS NOT NULL AND LONGITUD IS NOT NULL AND DISTANCIA_METROS IS NOT NULL
            AND LATITUD BETWEEN -90 AND 90 AND LONGITUD BETWEEN -180 AND 180
            AND DISTANCIA_METROS >= 0)),
    CHECK ((REPARTIDOR IS NULL AND SISTEMA_REPARTIDOR IS NULL)
        OR (REPARTIDOR IS NOT NULL AND SISTEMA_REPARTIDOR IS NOT NULL))
);

IF OBJECT_ID(N'dbo.POS_PEDIDO_EVENTO', N'U') IS NULL
CREATE TABLE dbo.POS_PEDIDO_EVENTO (
    ID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IDPEDIDO int NOT NULL REFERENCES dbo.POS_PEDIDO(ID),
    IDCOMANDO uniqueidentifier NOT NULL,
    TIPO nvarchar(24) NOT NULL,
    ESTADO_ANTERIOR nvarchar(20) NULL,
    ESTADO_NUEVO nvarchar(20) NULL,
    ACTOR nvarchar(50) NOT NULL,
    SISTEMA nvarchar(50) NULL,
    DETALLE nvarchar(250) NULL,
    FECHAHORA_UTC datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
    UNIQUE (IDPEDIDO, IDCOMANDO)
);

IF OBJECT_ID(N'dbo.POS_PEDIDO_CHECKOUT', N'U') IS NULL
CREATE TABLE dbo.POS_PEDIDO_CHECKOUT (
    ID uniqueidentifier NOT NULL PRIMARY KEY,
    IDPEDIDO int NOT NULL REFERENCES dbo.POS_PEDIDO_WEB(IDPEDIDO),
    INTEGRACION varchar(64) COLLATE Latin1_General_BIN2 NOT NULL,
    PREFERENCIA varchar(120) COLLATE Latin1_General_BIN2 NULL,
    ESTADO nvarchar(24) NOT NULL,
    FECHAHORA_UTC datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
    VENCE_UTC datetime2(0) NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.POS_PEDIDO_CHECKOUT')
                 AND name = N'UX_POS_PEDIDO_CHECKOUT_PREFERENCIA')
CREATE UNIQUE INDEX UX_POS_PEDIDO_CHECKOUT_PREFERENCIA
    ON dbo.POS_PEDIDO_CHECKOUT(INTEGRACION, PREFERENCIA) WHERE PREFERENCIA IS NOT NULL;

IF OBJECT_ID(N'dbo.POS_PEDIDO_COBRO', N'U') IS NULL
CREATE TABLE dbo.POS_PEDIDO_COBRO (
    ID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IDPEDIDO int NOT NULL REFERENCES dbo.POS_PEDIDO_WEB(IDPEDIDO),
    INTEGRACION varchar(64) COLLATE Latin1_General_BIN2 NOT NULL,
    PAGO_PROVEEDOR varchar(80) COLLATE Latin1_General_BIN2 NOT NULL,
    ESTADO_PROVEEDOR nvarchar(24) NOT NULL,
    IMPORTE decimal(19,4) NOT NULL,
    MONEDA char(3) NOT NULL,
    FECHAHORA_PROVEEDOR_UTC datetime2(0) NOT NULL,
    FECHAHORA_VERIFICACION_UTC datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
    UNIQUE (INTEGRACION, PAGO_PROVEEDOR),
    UNIQUE (IDPEDIDO, ID)
);

IF OBJECT_ID(N'dbo.FK_POS_WEB_COBRO_ACEPTADO', N'F') IS NULL
ALTER TABLE dbo.POS_PEDIDO_WEB ADD CONSTRAINT FK_POS_WEB_COBRO_ACEPTADO
    FOREIGN KEY (IDPEDIDO, IDCOBRO_ACEPTADO) REFERENCES dbo.POS_PEDIDO_COBRO(IDPEDIDO, ID);

IF OBJECT_ID(N'dbo.POS_CHECKOUT_WEBHOOK', N'U') IS NULL
CREATE TABLE dbo.POS_CHECKOUT_WEBHOOK (
    ID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    INTEGRACION varchar(64) COLLATE Latin1_General_BIN2 NOT NULL,
    DEDUPLICACION binary(32) NOT NULL,
    RECURSO varchar(80) COLLATE Latin1_General_BIN2 NOT NULL,
    ESTADO nvarchar(20) NOT NULL,
    INTENTOS smallint NOT NULL DEFAULT (0),
    RECIBIDO_UTC datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
    PROXIMO_UTC datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
    RESERVADO_HASTA_UTC datetime2(0) NULL,
    UNIQUE (INTEGRACION, DEDUPLICACION)
);

IF OBJECT_ID(N'dbo.POS_PEDIDO_AVISO', N'U') IS NULL
CREATE TABLE dbo.POS_PEDIDO_AVISO (
    IDEVENTO bigint NOT NULL PRIMARY KEY REFERENCES dbo.POS_PEDIDO_EVENTO(ID),
    IDNUMERO_WHATSAPP int NOT NULL,
    IDPLANTILLA bigint NOT NULL,
    IDIOMA nvarchar(12) NOT NULL,
    ESTADO nvarchar(20) NOT NULL,
    INTENTOS smallint NOT NULL DEFAULT (0),
    PROXIMO_UTC datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
    RESERVADO_HASTA_UTC datetime2(0) NULL,
    IDMENSAJE_META varchar(255) COLLATE Latin1_General_BIN2 NULL
);
```

El borrador y la extensión web se validan por el mismo enlace y punto. El `IDBORRADOR` único conserva su valor operativo tras la purga del borrador. La extensión retiene el hash de la sesión hasta el vencimiento de seguimiento para permitir volver al pago o consultar el estado del pedido sin el borrador. `IDLINEA_ENVIO` se valida contra el mismo pedido. El panel usa el índice existente de `POS_PEDIDO(IDPUNTOVENTA, ESTADO)` y el histórico consulta el índice que empieza por `IDPEDIDO`. Al preparar la actualización agregar índices para reclamo de colas por estado/hora y revisar sus planes con volumen representativo. Un intento de checkout reclama el pedido antes de llamar a la API y su estado gobierna la creación o reconciliación. El webhook solo responde éxito después de confirmar el registro duradero o reconocer una entrega ya persistida. Usar la identidad de notificación estable cuando el proveedor la entregue, con deduplicación de negocio por pago y pedido aun cuando lleguen solicitudes con IDs distintos. `IDCOBRO_ACEPTADO` vincula un único pago confirmado al pedido, mientras la tabla de cobros conserva otros resultados verificados. Definir una eliminación coordinada para la referencia circular entre pedido y cobro aceptado.

## 4. Plan y estimación

Horas de desarrollo, pruebas y ajuste técnico. El tiempo de respuesta de proveedores y la disponibilidad del comercio para homologar se estiman aparte.

| Etapa | Horas | Alcance y criterio de salida |
| --- | ---: | --- |
| 1. Adicionales en POS, Salón y comanda | 72–100 | ABM y asignación, selector compartido, validación, agrupación, observaciones, comandas, trazabilidad fiscal y recuperación de venta. Caja y VB6 verifican total, IVA, ticket y lectura con una venta testigo. Probar cobro/impresión, combinaciones con el mismo precio y atajos. |
| 2. Carta QR y carrito web | 42–60 | QR con adicionales por línea, carta externa por punto, invitado, resumen editable y borrador recuperable. Validar cambio de punto, agotados, PIN y aislamiento entre bases. |
| 3. Delivery, Checkout Pro, panel y repartidor | 116–168 | Tarifas y horarios, geocodificación, cierre y pago, recepción y comandas, estados y asignación, Meta, móvil y conciliación. Probar fronteras de radio, cierre nocturno, webhooks duplicados y fuera de orden, aprobación tardía, toma simultánea y fallos de facturación/comunicación. |
| **Total** | **230–328** | Piloto con dos sucursales, efectivo y un esquema inicial de cuenta Checkout Pro. |

Primero validar grabación, impresoras y fiscalización sobre copia de una base representativa. Luego activar Salón y Mostrador por configuración, publicar carta en una sucursal piloto y sumar la segunda después de verificar precios, enrutamiento, cobros, reintentos y visibilidad por punto. Deshabilitar la entrada pública desde configuración durante una incidencia y conservar las operaciones en curso para conciliarlas.

## 5. Riesgos y decisiones antes de implementar

| Tema | Decisión pendiente |
| --- | --- |
| Sucursales y cocina | Confirmar si Pilar y Mariló comparten base, cuál es el `POS_PUNTOVENTA.ID` de cada canal, caja, unidad, numeración, depósitos, listas aprobadas y destinos de cocina/barra. Si operan en bases distintas, decidir si habrá dos enlaces o una selección central de bases ya autorizadas. Si comparten una cocina, definir el mapeo entre punto publicado y punto operador. |
| Fiscal y cobranza | Confirmar momento de emisión para retiro/delivery y pagos prepagos, cuenta contable y medio de Mercado Pago, conciliación de comisiones, efectivo al repartidor, reembolsos, contracargos y documentos de reversión. Acordar imputación e IVA del artículo envío, tramo gratis, tratamiento de adicionales con otra alícuota y alcance de descuentos/promos. |
| Compatibilidad real | Verificar la firma e implementación del SP, triggers, edición/reemisión y visualización de VB6 en la copia del piloto. Acordar qué espera el negocio que se vea en el ticket fiscal, la reimpresión web y la comanda. Confirmar el resultado de recuperación cuando haya cortes antes y después de CAE, pago o vínculo al pedido. |
| Catálogo y preparación | Confirmar que un grupo reutilizado comparte precios y predeterminados en todos sus artículos/sucursales en la primera versión. Decidir el manejo de agotados por opción, cambios después de la aceptación y visibilidad de opciones para artículos vendidos mediante combos o ingreso rápido. |
| Cobertura y operación | Validar línea recta frente a distancia por recorrido, barrios con acceso especial, tolerancia de ubicación, excepciones, horario de corte, demora y qué hacer ante un pedido pagado fuera de hora. Acordar quién acepta en efectivo y quién concilia un cobro pendiente o duplicado. |
| Accesos y datos | Aprobar alcance por usuario/sucursal, retención de dirección y celular, proveedor de geocodificación y condiciones de caché, texto de consentimiento y plantillas por número e idioma. Validar dispositivos móviles e impresoras reales. |
| Capacidad de operación | Validar alertas de pagos/reintentos, estados trabados, proveedor caído, cocina desconectada y recuperación de mensajes cuyo resultado sea incierto. Definir responsable y plazo de actuación en el piloto. |
