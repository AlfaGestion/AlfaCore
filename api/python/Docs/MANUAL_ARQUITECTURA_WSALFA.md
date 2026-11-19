# Manual de arquitectura interna de `wsAlfa`

## 1. Objetivo

Este manual define cómo decidir dónde crear endpoints nuevos dentro de `wsAlfa`, para que el proyecto pueda crecer sin mezclar:

- compatibilidad histórica
- app móvil
- web actual
- módulos nuevos
- integraciones específicas

La regla principal es esta:

**No usar solo el número de versión para organizar el sistema.**  
Primero se define el producto o canal, y después la versión del contrato.

## 2. Criterio principal

Antes de crear un endpoint nuevo, responder estas 4 preguntas:

1. ¿Qué producto o canal lo consume?
2. ¿Es parte de una API ya existente o de un módulo nuevo?
3. ¿Necesita mantener compatibilidad con clientes actuales?
4. ¿Es una mejora menor o un dominio funcional nuevo?

Con esas respuestas se decide el namespace correcto.

## 3. Qué significa cada bloque actual

## `routes/v1`
Usar solo para:

- compatibilidad vieja
- mantenimiento
- fixes puntuales

No conviene agregar funcionalidades nuevas grandes acá.

## `routes/v2`
Hoy funciona como API legacy principal compartida por varias partes del sistema.

Usar solo para:

- endpoints ya consumidos por móvil o web actual
- ampliaciones pequeñas de contratos existentes
- correcciones o mantenimiento

No conviene seguir agregando módulos grandes nuevos en `v2`.

## `routes/v3`
Usar solo si realmente representa una nueva versión del mismo contrato.

No usar `v3` como “cajón para cosas nuevas”.

Si una funcionalidad nueva no es evolución natural de `v2`, no debería entrar en `v3`.

## `routes/AGW/V1`
Este debería ser el espacio natural para:

- Alfa Gestión Web
- módulos nuevos
- pantallas nuevas de la web
- flujos nuevos
- integraciones nuevas de la web
- procesos asistidos por IA
- importadores
- conciliaciones
- dashboards nuevos

Este namespace es el recomendado para crecimiento ordenado.

## 4. Regla práctica para decidir dónde crear algo

## Crear en `v2` cuando:
- ya existe un endpoint parecido en `v2`
- ya hay clientes consumiéndolo
- el contrato debe mantenerse compatible
- el cambio es pequeño o incremental

## Crear en `v3` cuando:
- querés reemplazar formalmente un contrato de `v2`
- hay una nueva versión real del mismo recurso
- existe un plan claro de convivencia o migración

## Crear en `AGW/V1` cuando:
- es una funcionalidad nueva
- pertenece claramente a Alfa Gestión Web
- no querés arrastrar deuda histórica
- el flujo está pensado para web nueva
- el dominio funcional es nuevo

## 5. Regla especial para módulos nuevos

Si aparece un módulo nuevo completo, por ejemplo:

- conciliación de tarjetas
- importación bancaria
- lectura de PDFs
- workflows administrativos nuevos
- reportes operativos nuevos

la regla recomendada es:

**crear ese módulo en `AGW/V1`**, aunque parte de la web vieja lo consuma.

Eso evita contaminar `v2` con dominios nuevos.

## 6. Estructura recomendada para lo nuevo

Para cada módulo nuevo, separar:

- rutas
- servicios
- acceso a datos
- parsers o adaptadores

Ejemplo recomendado:

```text
routes/
  AGW/
    V1/
      card_reconciliation.py
      imports.py
      dashboard.py

services/
  card_reconciliation_service.py
  pdf_liquidation_service.py
  bank_rules_service.py

repositories/
  card_reconciliation_repository.py
  mv_asientos_repository.py

parsers/
  bank_pdf/
    base.py
    visa.py
    mastercard.py
```

## 7. Convención de nombres

Usar nombres por dominio, no por pantalla.

Conviene:

- `card_reconciliation`
- `cashbox`
- `session`
- `auth`
- `configuration`
- `reports`

Evitar nombres ambiguos como:

- `utils2`
- `new_module`
- `extra`
- `test_api`

## 8. Contratos y compatibilidad

Cada namespace debe asumir su responsabilidad:

## `v1/v2/v3`
- foco en compatibilidad
- cambios conservadores
- menor riesgo de ruptura

## `AGW/V1`
- foco en diseño limpio
- nuevos contratos
- evolución más libre
- pensado para web moderna

## 9. Reglas de crecimiento

## Sí hacer
- agregar módulos nuevos en `AGW/V1`
- mantener `v2` solo donde ya hay consumo real
- agrupar por dominio
- extraer lógica a `services` y `repositories`

## No hacer
- meter todo lo nuevo en `v2`
- usar `v3` solo porque “es más nuevo”
- dejar lógica pesada dentro de la ruta
- mezclar lógica web, móvil y admin sin separar contexto

## 10. Aplicación concreta a tu caso

## Conciliación de tarjetas
Recomendación:

- endpoint final en `AGW/V1`
- aunque ahora hayas hecho pruebas en `v2`, el destino correcto de crecimiento debería ser `AGW/V1`

Porque:

- es un dominio nuevo
- es propio de la web
- no es una evolución natural de ALFA Go
- necesita arquitectura limpia

## 11. Política sugerida de acá en adelante

1. `v1`, `v2`, `v3` quedan como capas legacy/evolutivas.
2. Todo módulo nuevo grande va a `AGW/V1`.
3. Si algo es reutilizable, la lógica se comparte en `services` o `repositories`, no duplicando rutas.
4. Las rutas deben ser finas; la lógica de negocio debe salir de los controllers/views.
5. Cada endpoint nuevo debe nacer con consumidor identificado:
   - web
   - móvil
   - admin
   - integración externa

## 12. Resumen ejecutivo

La regla simple es esta:

- legacy y compatibilidad: `v1/v2/v3`
- crecimiento nuevo de Alfa Gestión Web: `AGW/V1`

Si algo nuevo no necesita mantener compatibilidad con contratos viejos, no debería nacer en `v2`.