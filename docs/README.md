# Documentación de AlfaCore

Este índice define dónde vive cada tipo de documentación del proyecto.

## Regla rápida

- `src/AlfaCore/Docs/` → manuales de usuario y ayuda funcional de la aplicación
- `docs/` → documentación técnica, arquitectura, operación, backlog, continuidad y material histórico

No duplicar manuales completos en `docs/`. Si hace falta acceso rápido desde el repositorio, crear un archivo puente corto.

## Archivos raíz que no deben moverse sin revisar referencias

- [CODEX_RULES.md](./CODEX_RULES.md)
- [DATABASE_OBJETOS_SQL_PRIORITARIOS.md](./DATABASE_OBJETOS_SQL_PRIORITARIOS.md)
- [CONFIGURACION_GLOBAL.md](./CONFIGURACION_GLOBAL.md)
- [DATABASE_TABLES_SUMMARY.md](./DATABASE_TABLES_SUMMARY.md)
- [CATALOGO_RUTINAS.md](./CATALOGO_RUTINAS.md)
- Este [README.md](./README.md)

## Carpetas dentro de `docs/`

### `arquitectura/`

Documentación transversal del sistema:

- arquitectura general
- estándares de UI
- lineamientos técnicos comunes

### `base-datos/`

Documentación técnica de base de datos.

Subcarpeta:

- `base-datos/sql-referencia/` → scripts, vistas, consultas de apoyo, modelos iniciales y SQL histórico útil

### `modulos/`

Documentación técnica por módulo. Acá van documentos como:

- diseño técnico de un módulo
- decisiones de implementación
- integración de un módulo con otros circuitos
- notas técnicas que no son manuales de usuario

### `gestion/`

Documentación de trabajo y continuidad:

- backlog
- changelog
- continuidad de conversaciones
- notas de tareas
- issues o decisiones operativas

### `legacy/`

Material histórico o en bruto que no conviene mezclar con documentación activa:

- relevamientos funcionales viejos
- archivos `.txt` heredados
- dumps SQL grandes
- documentos todavía no normalizados

## Manuales de usuario

Viven en:

- [src/AlfaCore/Docs/manual_usuario.md](../src/AlfaCore/Docs/manual_usuario.md)
- [src/AlfaCore/Docs/manual_consultas.md](../src/AlfaCore/Docs/manual_consultas.md)
- [src/AlfaCore/Docs/manual_auditoria_usuarios.md](../src/AlfaCore/Docs/manual_auditoria_usuarios.md)

Puentes disponibles desde `docs/`:

- [manual_usuario.md](./manual_usuario.md)
- [manual_consultas.md](./manual_consultas.md)
- [manual_auditoria_usuarios.md](./manual_auditoria_usuarios.md)

## Dónde guardar documentación nueva

- Si explica uso para usuario final: `src/AlfaCore/Docs/`
- Si explica implementación, arquitectura o decisiones técnicas: `docs/arquitectura/` o `docs/modulos/`
- Si es SQL o soporte de base de datos: `docs/base-datos/` o `docs/base-datos/sql-referencia/`
- Si es continuidad, backlog o nota de trabajo: `docs/gestion/`
- Si es material histórico o relevamiento viejo: `docs/legacy/`

## Convención práctica

Antes de crear un documento nuevo:

1. Buscar si ya existe algo relacionado.
2. Elegir la carpeta por tipo de documento, no por costumbre.
3. Mantener nombres claros y consistentes.
4. Evitar dejar archivos sueltos en la raíz de `docs/`, salvo los considerados base.
