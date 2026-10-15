# Configuración de ventas y búsqueda de clientes

## Accesos y alcance

Configuración General → Ventas → Carga de comprobantes, o `/ALFANET/153/configuracion-general/ventas?tab=carga-comprobantes` en la base utilizada para pruebas. Se reutiliza la página y el menú existentes; no se crea otra clave de menú ni otra tabla de parámetros.

El editor reúne 35 claves confirmadas de `TA_CONFIGURACION`, agrupadas por precios, IVA, descuentos, proforma, orígenes/stock, contabilidad y electrónica. Cada campo indica alcance y limitaciones. Las credenciales, certificados, emisores por unidad y configuración específica de POS permanecen en sus pestañas existentes, accesibles desde este panel.

`USAPROFORMA` pertenece al POS y `UsaFacturaProforma` al circuito relevado de Carga/Alfa Gestión: no se sustituyen entre sí. Las equivalencias contables, período, motivo de stock, ambiente electrónico y proforma sin registración se muestran como referencia de solo lectura. El asiento de proforma sigue siendo obligatorio. Los controles aún no implementados en Carga se identifican explícitamente; cambiar su parámetro no agrega ese circuito.

El inventario VB6 se incorpora como recurso de la aplicación desde el CSV del relevamiento, sin duplicarlo. Su buscador muestra hasta 50 claves coincidentes; es documentación de presencia en las fuentes, no certificación funcional. No se consultan ni exponen los valores de todas las claves legacy.

## Grabación y contexto

El servicio exige sesión/base autorizada y revalida `TA_USUARIOS.AdminCentral` al guardar. Solo se envían claves modificadas; se rechazan claves desconocidas, de lectura, duplicadas o valores fuera del tipo admitido. Se preservan grupo y descripción de claves existentes. La representación anterior completa (`VALOR`/`ValorAux`/existencia) genera una versión SHA-256: una edición concurrente provoca conflicto, sin sobrescribirla.

La grabación es transaccional y auditada con `IAppEventService`; los errores técnicos usan el mismo servicio para `AUX_ERR`. Antes del commit se revalida sesión/usuario. El panel descarta datos si cambia la sesión y no admite guardar un snapshot de otra base. Guardar esta pestaña no ejecuta los guardados de Portal Cliente, ARCA, POS ni Mercado Pago. No se modificaron parámetros de la base para entregar la interfaz.

## Cliente y CUIT

`AlfaLookup` informa selección nula después de cada tecla. El handler anterior interpretaba ese evento como limpiar el texto y lo reemplazaba por vacío; el hijo volvía a ver menos de dos caracteres. Ahora conserva el texto enlazado y elimina solo la identidad seleccionada y la validación dependiente. Se aplica también al lookup de factura asociada.

La consulta utiliza la vista oficial `VT_CLIENTES`, excluye bajas/bloqueados, admite código, razón social y documento, normaliza CUIT con guiones/puntos/espacios y prioriza coincidencia exacta. Los comodines ingresados se tratan como texto literal. Mantiene el debounce del componente compartido.

Las acciones del header «Buscar cliente» y «Nuevo cliente / CUIT» reutilizan `QuickClienteModal`, `ICuentasComercialesService` e `IArcaPadronService` del POS, con consulta de padrón, validación de duplicados y alta mediante el servicio existente. El cliente seleccionado vuelve al borrador e invalida su liquidación. No se pasa una cuenta eventual: el circuito directo requiere un cliente real de cuenta corriente.

La pantalla de configuración declara `ModuleKey = "configuracion-general"` para evitar conservar un header de otro módulo. El nuevo panel permanece dentro de las rutas y el contexto actuales, incluido `?directo=1`.

## Verificación

Los resultados de compilación, pruebas SQL con rollback, regresión y comprobación visual de esta pasada se registran en [avances y pendientes](../../gestion/ventas-carga-comprobantes-avances-y-pendientes.md). La prueba del alta usa un padrón y servicio de clientes simulados: no crea clientes reales ni solicita CAE.
