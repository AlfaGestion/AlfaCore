# Proyecto Punto de Venta

## Objetivo

Desarrollar un nuevo módulo `Punto de Venta` dentro del menú `Ventas` de `AlfaCore`, con una experiencia visual similar al POS de Odoo y tomando como referencia funcional el POS existente en:

- `C:\dev\AlfaWeb-main\`
- `C:\dev\wsAlfa\`

La implementación nueva debe quedar **totalmente integrada en AlfaCore** y **no depender de wsAlfa en runtime**.

`AlfaWeb-main` y `wsAlfa` se usarán solo como referencia para:

- entender flujo funcional
- detectar reglas de negocio
- relevar tablas, vistas y stored procedures
- validar orden de grabación

---

## Decisión arquitectónica

El nuevo POS se implementará directamente en:

- `Blazor Server`
- `SQL Server`
- `Dapper`

No se consumirá `wsAlfa` para operar.

La lógica de negocio se portará a servicios de `AlfaCore` reutilizando los objetos SQL ya existentes en la base.

---

## Ubicación funcional

El módulo debe vivir dentro de `Ventas`.

Ruta sugerida:

- `/ventas/punto-venta`

Debe agregarse como opción del menú web bajo el módulo `Ventas`.

---

## Alcance de la v1

La primera versión debe resolver:

- búsqueda de artículos
- filtro por familia
- carrito de venta
- cliente mínimo
- consumidor final
- cobro con uno o varios medios de pago
- grabación de comprobante
- grabación del detalle
- creación de cobranza
- aplicación de cobranza al comprobante
- movimientos de caja
- visualización final del comprobante o ticket

---

## Fuera de alcance en v1

No incluir en la primera etapa:

- factura electrónica / AFIP
- promociones complejas
- descuentos avanzados por reglas comerciales
- funcionamiento offline
- devoluciones
- notas de crédito desde POS
- multi caja manual
- conciliaciones avanzadas

---

## Definiciones funcionales confirmadas

### Usuario actual

El POS usará el usuario actual de `AlfaCore`.

### Caja

La caja operativa del POS se toma desde:

- `TA_USUARIOS.IDCAJA`

Ese valor siempre debe existir y es el que se usa para registrar movimientos.

Como resguardo técnico, si alguna vez faltara, se podrá usar:

- caja default `1`

### Cliente

En v1 se usará una versión mínima:

- cliente existente
- consumidor final
- alta rápida mínima

### Tipo de comprobante por defecto

- `FC`

### Sucursal por defecto

Debe salir de:

- `V_TA_CPTE`

Si no existiera resolución específica:

- usar `0001`

### Caja por defecto global

- `1`

### Stock

Según el relevamiento:

- no controla stock negativo
- el stock impacta al grabar el comprobante

### Electrónica

Para v1:

- no se implementa

### TC de mostrador

Según definición actual:

- `FC`

Más adelante puede ampliarse a:

- `NP`
- `FP`

---

## Objetos SQL confirmados

### Grabación del comprobante

Objetos principales:

- `sp_web_Alta_Comprobante`
- `sp_web_CpteInsumos`
- `V_MV_CPTE`
- `V_TA_CPTE`

Archivo de referencia:

- [1  Grabación del comprobante.txt](C:/dev/AlfaCore/docs/1%20%20Grabación%20del%20comprobante.txt)

### Cobranza y aplicación

Objetos principales:

- `sp_web_CreaCobPorFactura`
- `sp_web_creaLineaAsiento`
- `sp_web_CreaAplicacionCobranzaFactura`
- `MV_ASIENTOS`
- `MV_APLICACION`

Archivo de referencia:

- [2 Cobranza y aplicación.txt](C:/dev/AlfaCore/docs/2%20Cobranza%20y%20aplicación.txt)

### Artículos y precios

Objetos principales:

- `sp_web_getFamiliasArticulos`
- `sp_web_getListaDePrecios_query`
- `V_MA_ARTICULOS`
- `V_MA_PRECIOS`
- `V_MA_PRECIOSCAB`
- `V_TA_FAMILIAS`
- `V_TA_Unidad`

Archivo de referencia:

- [3 Artículos y precios.txt](C:/dev/AlfaCore/docs/3%20Artículos%20y%20precios.txt)

### ImÃ¡genes del catÃ¡logo

La resoluciÃ³n actual de imÃ¡genes del POS en `AlfaCore` se apoya en:

- `V_MA_ARTICULOS.RutaImagen`
- `TA_CONFIGURACION.CLAVE = 'VERIFICADOR_RUTAIMAGENES'`

Orden de resoluciÃ³n:

1. Leer `RutaImagen` desde `V_MA_ARTICULOS`.
2. Leer `TA_CONFIGURACION.CLAVE = 'VERIFICADOR_RUTAIMAGENES'`.
3. Si `RutaImagen` es absoluta, usarla directamente.
4. Si `RutaImagen` es relativa, probar primero:
   - `{VERIFICADOR_RUTAIMAGENES}\\{RutaImagen}`
5. Si no alcanza, probarla como ruta relativa a la carpeta donde estÃ¡ instalado `AlfaCore`:
   - `{ContentRootPath}\\{RutaImagen}`
6. No se usa `TA_CONFIGURACION.CLAVE = 'RUTAIMAGENES'` para buscar fÃ­sicamente la imagen.

Regla importante:

- `VERIFICADOR_RUTAIMAGENES` se usa como primera carpeta base de bÃºsqueda
- `RUTAIMAGENES` pertenece a la lÃ³gica histÃ³rica de gestiÃ³n para construir el valor que luego se graba en `V_MA_ARTICULOS.RutaImagen`
- pero en `AlfaCore` no debe reutilizarse como carpeta base de bÃºsqueda
- la fuente real de resoluciÃ³n es el contenido ya grabado en `V_MA_ARTICULOS.RutaImagen`

ImplementaciÃ³n actual:

- el frontend no consume rutas de disco directamente
- las imÃ¡genes se sirven mediante el endpoint:
  - `/api/punto-venta/articulos/{idArticulo}/imagen`

ConclusiÃ³n prÃ¡ctica:

- la referencia principal sigue siendo `V_MA_ARTICULOS.RutaImagen`
- la primera base de bÃºsqueda es:
  - `TA_CONFIGURACION.CLAVE = 'VERIFICADOR_RUTAIMAGENES'`
- si la ruta es relativa, el siguiente fallback es:
  - `{ContentRootPath}\\{RutaImagen}`
- no hay fallback a `wsAlfa`
- no hay fallback a una API externa de imÃ¡genes

### Clientes

Objetos principales:

- `VT_CLIENTES`
- `MA_CUENTAS`
- `MA_CUENTASADIC`

Archivo de referencia:

- [4 Clientes.txt](C:/dev/AlfaCore/docs/4%20Clientes.txt)

### Configuración comercial

Objetos principales:

- `TA_CONFIGURACION`
- `V_TA_CPTE`

Archivo de referencia:

- [5 Configuración comercial.txt](C:/dev/AlfaCore/docs/5%20Configuración%20comercial.txt)

### Stock

Objeto principal:

- `V_MV_STOCK`

Archivo de referencia:

- [8 STOCK.txt](C:/dev/AlfaCore/docs/8%20STOCK.txt)

### Electrónica / AFIP

Objeto identificado:

- `V_MV_CPTE_ELECTRONICOS`

Archivo de referencia:

- [9 Electrónica  AFIP.txt](C:/dev/AlfaCore/docs/9%20Electrónica%20%20AFIP.txt)

### Seguridad y sesión

Archivo de referencia:

- [10 Seguridad y sesión.txt](C:/dev/AlfaCore/docs/10%20Seguridad%20y%20sesión.txt)

Nota:

- la definición funcional cerrada es usar `TA_USUARIOS.IDCAJA`

---

## Reglas de negocio detectadas

### Alta de comprobante

`sp_web_Alta_Comprobante`:

- crea la cabecera en `V_MV_CPTE`
- toma cliente desde `VT_CLIENTES`
- resuelve lista, clase de precio y vendedor
- genera número si no se informa
- arma `IDCOMPROBANTE = SUCURSAL + NUMERO + LETRA`
- registra `FechaHora_Grabacion`

### Detalle del comprobante

`sp_web_CpteInsumos`:

- toma el encabezado desde `V_MV_CPTE`
- busca datos de artículo en `V_MA_ARTICULOS`
- resuelve IVA, exento, unidad y costo
- inserta los ítems del comprobante

### Cobranza

`sp_web_CreaCobPorFactura`:

- crea un comprobante de cobranza asociado a la factura
- usa `CBFP` si la factura es `FP`
- usa `CBCT` para el resto

### Líneas de medios de pago

`sp_web_creaLineaAsiento`:

- registra líneas contables de la cobranza
- se usa una línea inicial y luego una por medio de pago

### Aplicación

`sp_web_CreaAplicacionCobranzaFactura`:

- inserta en `MV_APLICACION`
- vincula la cobranza con la factura original

---

## Fuentes funcionales del sistema actual

### Referencia PHP

- `C:\dev\AlfaWeb-main\app\Controllers\User\Pos.php`
- `C:\dev\AlfaWeb-main\app\Views\sales\pos\index.php`
- `C:\dev\AlfaWeb-main\public\assets\js\dist\Pos\pos.main.js`
- `C:\dev\AlfaWeb-main\public\assets\js\dist\Receipt\Receipt.js`
- `C:\dev\AlfaWeb-main\public\assets\js\dist\Cash\CashBox.js`

### Referencia wsAlfa

- `C:\dev\wsAlfa\routes\v2\sales.py`
- `C:\dev\wsAlfa\routes\v2\products.py`
- `C:\dev\wsAlfa\functions\Document.py`

Estas fuentes sirven como especificación viva del comportamiento actual del POS.

---

## Diseño técnico propuesto en AlfaCore

### Página principal

- `Components/Pages/VentasPuntoVenta.razor`

### Componentes sugeridos

- `VentasPosCart.razor`
- `VentasPosProducts.razor`
- `VentasPosCustomer.razor`
- `VentasPosPayment.razor`
- `VentasPosCashDialog.razor`
- `VentasPosReceiptDialog.razor`

### Modelos

- `Models/PuntoVentaModels.cs`

### Servicios

- `Services/IPuntoVentaService.cs`
- `Services/PuntoVentaService.cs`

Si el volumen crece, dividir:

- `PuntoVentaService.Products.cs`
- `PuntoVentaService.Customers.cs`
- `PuntoVentaService.Sales.cs`
- `PuntoVentaService.Cash.cs`

---

## Estado de pantalla esperado

La pantalla debe manejar:

- usuario actual
- caja actual
- `TC` actual
- sucursal actual
- cliente actual
- búsqueda de artículos
- familia seleccionada
- carrito
- total
- medios de pago aplicados
- observaciones
- resultado de grabación

---

## Flujo operativo esperado

1. Abrir POS.
2. Cargar contexto:
   - usuario
   - caja
   - `TC`
   - sucursal
3. Buscar artículos.
4. Agregar artículos al carrito.
5. Seleccionar cliente o consumidor final.
6. Confirmar total.
7. Registrar medios de pago.
8. Grabar comprobante.
9. Grabar cobranza.
10. Aplicar cobranza al comprobante.
11. Mostrar ticket / comprobante.
12. Limpiar carrito y quedar listo para otra venta.

---

## Interfaz visual esperada

El diseño debe tomar como referencia el POS actual estilo Odoo:

- panel lateral con carrito
- grilla principal de productos
- buscador rápido
- total destacado
- botón de cobrar dominante
- acceso rápido a cliente
- acceso rápido a caja
- operación veloz táctil / mouse

Debe priorizar:

- lectura rápida
- poco clic
- foco comercial
- operación continua

---

## Riesgos técnicos a controlar

- sucursal mal resuelta para el `TC`
- caja incorrecta respecto del usuario
- inconsistencia entre total vendido y total cobrado
- error parcial entre cabecera, detalle y cobranza
- artículos con IVA/exento mal calculados
- mensajes SQL poco claros al operador
- reglas escondidas en configuración no relevada

---

## Estrategia recomendada de implementación

### Fase 1

Construir una v1 operativa con:

- `FC`
- cliente mínimo
- sin electrónica
- caja por usuario
- grabación directa con SP existentes

### Fase 2

Agregar:

- más TCs
- mejoras visuales
- impresión avanzada
- controles operativos extra

### Fase 3

Evaluar:

- electrónica / AFIP
- más reglas comerciales
- extensiones de caja

---

## Orden sugerido de construcción

1. Servicio de contexto POS.
2. Consulta de artículos y familias.
3. Carrito.
4. Cliente mínimo.
5. Grabación de comprobante.
6. Cobro y aplicación.
7. Caja.
8. Ticket / comprobante final.
9. Pulido visual.

---

## Estado del análisis

Con la documentación relevada y las definiciones funcionales cerradas:

- el módulo es viable
- no requiere `wsAlfa` en runtime
- la base ya tiene los objetos necesarios para una v1
- el riesgo principal ya no es de datos sino de implementación ordenada del flujo

---

## Conclusión

`Punto de Venta` puede implementarse como módulo nuevo dentro de `Ventas` en `AlfaCore`, con arquitectura propia y sin depender del POS PHP actual ni de `wsAlfa` para operar.

El sistema actual se toma únicamente como referencia funcional y técnica.  
La nueva implementación debe apoyarse en los objetos SQL oficiales ya relevados y seguir la arquitectura existente de `AlfaCore`.
