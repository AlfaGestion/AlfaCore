# AGENTS.md

Este proyecto utiliza reglas obligatorias definidas en:

## Lectura obligatoria (siempre)


- /docs/CODEX_RULES.md
- /docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md
- /docs/CONFIGURACION_GLOBAL.md
Estas definen:
- cómo trabajar
- qué objetos usar
- reglas críticas del sistema

---

## Lectura opcional (solo si es necesario)

- /docs/DATABASE_TABLES_SUMMARY.md

Usar únicamente cuando:
- se necesite entender una tabla específica
- haya dudas sobre la estructura de datos
- no alcance con DATABASE_OBJETOS_SQL_PRIORITARIOS.md

No cargar este archivo completo si no es necesario.

---

## Reglas de trabajo

- Trabajar siempre sobre la base actual
- No rehacer desde cero
- No asumir estructuras no confirmadas
- Priorizar objetos definidos como “oficiales”
- Todo error relevante debe registrarse en `AUX_ERR`, usando un servicio centralizado de logging.
---

## Regla clave

Antes de usar una tabla:

1. Revisar /docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md
2. Si no alcanza → consultar /docs/DATABASE_TABLES_SUMMARY.md


## Verificación asistida del catálogo

Antes de finalizar una tarea, ejecutar:

```bash
python tools/catalogo/check_catalogo.py