# Integración: ARCA / AFIP

## Alcance en AlfaCore

En AlfaCore, ARCA/AFIP aparece en dos niveles:

- soporte documental dentro de Interfaces mediante el tipo `CONTROLES_ARCA`;
- referencias legacy y de Punto de Venta para factura electrónica, libros IVA y códigos AFIP.

No se detectó en el código web actual una integración SOAP completa nueva de facturación electrónica implementada de punta a punta en C#. Por eso esta ficha documenta el alcance confirmado y los cuidados para evoluciones futuras.

Código y documentación relacionada:

- `src/AlfaCore/App_Data/updates/2026-05-14-001__interfaces_tipos_documento_controles_arca.sql`
- `src/AlfaCore/Models/InterfacesModels.cs`
- `src/AlfaCore/Services/InterfacesService.cs`
- `docs/modulos/interfaces_modulo.md`
- `docs/modulos/PUNTO_VENTA_PROYECTO.md`
- `docs/legacy/analisis-funcional/9-electronica-afip.txt`

## Alcance confirmado

El script `2026-05-14-001__interfaces_tipos_documento_controles_arca.sql` agrega o actualiza en `INT_TIPO_DOCUMENTO` el código:

```text
CONTROLES_ARCA
```

Esto permite clasificar documentación recibida como controles ARCA dentro del módulo Interfaces.

## Conceptos oficiales relevantes

ARCA mantiene documentación técnica para Web Services SOAP. En factura electrónica se usan servicios como:

- `wsfev1` para comprobantes electrónicos sin detalle de ítem;
- `wsmtxca` para comprobantes con detalle de ítems;
- `wsfexv1` para exportación;
- `wsbfev1` para bonos fiscales electrónicos;
- WSAA para autenticación y autorización con certificado.

## Problemas frecuentes

- ARCA/AFIP requiere certificados digitales y asociación del certificado al CUIT y servicio correspondiente.
- Los ambientes de homologación y producción tienen endpoints y credenciales operativas distintas.
- Los tokens/autorizaciones de WSAA tienen vencimiento; no deben tratarse como credenciales permanentes.
- Los códigos de comprobante, punto de venta, letra, concepto, moneda e impuestos deben mapearse contra reglas oficiales y contra la configuración contable de Alfa Gestión.
- En Alfa Gestión no se debe asumir que una vista `V_` es una vista SQL real ni que una tabla legacy ya sirve como fuente oficial sin verificar.
- La facturación electrónica debe registrar CAE, vencimiento y resultado de autorización de forma trazable.

## Lecciones aplicadas en AlfaCore

- Separar recepción documental de integración tributaria evita confundir un control adjunto con una autorización fiscal real.
- Si se implementa WSFE/WSAA en AlfaCore, debe tener servicio dedicado, logging en `AUX_ERR`, configuración centralizada y pruebas por ambiente.
- Los mapeos AFIP/ARCA deben documentarse con scripts SQL o tablas de referencia, no hardcodearse en C#.
- Para comprobantes que afectan contabilidad, la fuente principal de análisis sigue siendo `MV_ASIENTOS`; las tablas operativas de ventas/compras no reemplazan esa fuente.

## Fuentes oficiales

- [ARCA: Web Services SOAP](https://www.afip.gob.ar/ws/)
- [ARCA: documentación de webservices](https://www.afip.gob.ar/ws/documentacion/)
- [ARCA: certificados](https://www.afip.gob.ar/ws/documentacion/certificados.asp)
- [ARCA: Webservices de factura electrónica](https://www.afip.gob.ar/ws/documentacion/ws-factura-electronica.asp)
