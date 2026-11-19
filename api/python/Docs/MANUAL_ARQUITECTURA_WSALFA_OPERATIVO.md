# Manual operativo de arquitectura para `wsAlfa`

## 1. Objetivo

Este documento baja a tierra cÃ³mo organizar `wsAlfa` sin romper lo existente, tomando como criterio principal separar:

- lo legacy que ya estÃ¡ en producciÃ³n
- lo nuevo que pertenece a Alfa GestiÃ³n Web
- la lÃ³gica reusable fuera de las rutas

## 2. DefiniciÃ³n prÃ¡ctica de legacy

En este proyecto, `legacy` significa:

- rutas o contratos ya consumidos por clientes reales
- partes que usa ALFA Go o la web actual
- cÃ³digo que no conviene rediseÃ±ar agresivamente porque puede romper compatibilidad

No significa â€œmaloâ€. Significa â€œhay que tocarlo con cuidadoâ€.

## 3. Regla principal de decisiÃ³n

Antes de crear algo nuevo, responder:

1. Â¿Esto lo consume la app mÃ³vil, la web o ambos?
2. Â¿Es una extensiÃ³n chica de algo existente o un mÃ³dulo nuevo?
3. Â¿Necesita compatibilidad con contratos actuales?
4. Â¿Puede nacer con diseÃ±o limpio?

## 4. QuÃ© queda dÃ³nde

## `routes/v1`
- mantenimiento
- compatibilidad vieja
- sin mÃ³dulos nuevos grandes

## `routes/v2`
- contratos existentes usados por mÃ³vil o web actual
- fixes y ampliaciones chicas
- no usar como destino de mÃ³dulos nuevos grandes

## `routes/v3`
- solo si realmente es una nueva versiÃ³n del mismo contrato
- no usar como â€œcajÃ³nâ€ de funcionalidades nuevas

## `routes/AGW/V1`
- todo mÃ³dulo nuevo propio de Alfa GestiÃ³n Web
- conciliaciÃ³n de tarjetas
- importaciÃ³n bancaria
- dashboards nuevos de web
- flujos asistidos por IA
- integraciones de web que no necesitan heredar deuda de `v2`

## 5. Estructura recomendada de crecimiento

```text
wsAlfa/
  routes/
    v1/
    v2/
    v3/
    AGW/
      V1/
        router.py
        cliente_id_bp.py
        card_reconciliation.py

  services/
    card_reconciliation_service.py
    card_pdf_service.py
    bank_rules_service.py

  repositories/
    card_reconciliation_repository.py
    mv_asientos_repository.py
    conciliacion_aux_repository.py

  parsers/
    bank_liquidations/
      base_parser.py
      visa_parser.py
      mastercard_parser.py
      amex_parser.py

  functions/
    ... legado compartido actual ...
```

## 6. QuÃ© va en cada capa

## `routes`
Solo:
- request
- validaciÃ³n de parÃ¡metros
- llamada al servicio
- armado de response

No deberÃ­a contener:
- SQL complejo
- matching
- parsing de PDF grande
- lÃ³gica de conciliaciÃ³n

## `services`
AcÃ¡ vive la lÃ³gica de negocio.

Ejemplos:
- crear conciliaciÃ³n
- analizar PDFs
- inferir banco/adquirente
- decidir perÃ­odos
- correr matching
- preparar sugerencias

## `repositories`
AcÃ¡ vive el acceso a datos.

Ejemplos:
- leer `MV_ASIENTOS`
- grabar `MV_CONCILIACION`
- grabar `MV_CONCILIACION_AUX`
- confirmar `MV_CONCILIACION_Cpte_AUX`
- actualizar `MV_ASIENTOS`

## `parsers`
AcÃ¡ vive la lectura especÃ­fica de PDFs.

Ejemplos:
- parser Visa
- parser Mastercard
- parser Amex
- parser genÃ©rico

## 7. AplicaciÃ³n concreta a conciliaciÃ³n de tarjetas

## Ruta recomendada
- `routes/AGW/V1/card_reconciliation.py`

## Servicios sugeridos
- `services/card_reconciliation_service.py`
- `services/card_pdf_service.py`

## Repositorios sugeridos
- `repositories/card_reconciliation_repository.py`
- `repositories/card_reconciliation_aux_repository.py`
- `repositories/mv_asientos_repository.py`

## Parsers sugeridos
- `parsers/bank_liquidations/base_parser.py`
- `parsers/bank_liquidations/visa_parser.py`
- `parsers/bank_liquidations/mastercard_parser.py`

## 8. Endpoints sugeridos para conciliaciÃ³n

Bajo `AGW/V1`:

- `POST /AGW/V1/card-reconciliation/analyze-pdfs`
- `POST /AGW/V1/card-reconciliation/create`
- `POST /AGW/V1/card-reconciliation/{id}/load-system`
- `POST /AGW/V1/card-reconciliation/{id}/import-bank-movements`
- `POST /AGW/V1/card-reconciliation/{id}/match`
- `POST /AGW/V1/card-reconciliation/{id}/confirm`
- `GET /AGW/V1/card-reconciliation/{id}`

## 9. PolÃ­tica de migraciÃ³n sin romper nada

No mover masivamente lo legacy.

La estrategia recomendada es:

1. dejar `v1/v2/v3` como estÃ¡n para compatibilidad
2. todo mÃ³dulo nuevo nace en `AGW/V1`
3. cuando haga falta reutilizar lÃ³gica legacy, encapsularla en servicios o repositorios
4. evitar seguir metiendo dominios nuevos en `v2`

## 10. Regla prÃ¡ctica para el dÃ­a a dÃ­a

## Si el endpoint ya existe o es una mejora chica
- tocar `v2` o `v3`

## Si es un dominio nuevo de web
- crear en `AGW/V1`

## Si la lÃ³gica se puede reutilizar
- extraer a `services` o `repositories`

## Si el cambio es solo para compatibilidad
- dejarlo dentro del espacio legacy correspondiente

## 11. Ejemplo concreto de decisiÃ³n

## Caso: conciliaciÃ³n de tarjetas
- no pertenece a ALFA Go
- es un dominio nuevo
- es propio de web
- necesita crecer con parsers, matching y reglas por banco

ConclusiÃ³n:
- debe vivir en `AGW/V1`
- no conviene seguir desarrollÃ¡ndolo dentro de `v2`

## 12. Resumen ejecutivo

La regla de trabajo es esta:

- `v1/v2/v3` = compatibilidad y mantenimiento
- `AGW/V1` = crecimiento nuevo de Alfa GestiÃ³n Web
- `services/repositories/parsers` = base profesional para crecer sin mezclar responsabilidades