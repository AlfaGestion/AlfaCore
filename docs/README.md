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

### `ui/`

Contrato oficial de implementación de [AlfaDesign](./ui/README.md): norma visual, tokens, componentes, guía operativa, mapa Figma y checklist.

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

- [Conciliación de tarjetas](../src/AlfaCore/Docs/manual_conciliacion_tarjetas.md) · [Contrato técnico](modulos/conciliacion-tarjetas.md).

Viven en:

- [src/AlfaCore/Docs/manual_usuario.md](../src/AlfaCore/Docs/manual_usuario.md)
- [Demostración de Conciliación y Cobranzas](../src/AlfaCore/Docs/manual_demo_conciliacion_bancaria.md)
- [src/AlfaCore/Docs/manual_consultas.md](../src/AlfaCore/Docs/manual_consultas.md)
- [src/AlfaCore/Docs/manual_auditoria_usuarios.md](../src/AlfaCore/Docs/manual_auditoria_usuarios.md)

Puentes disponibles desde `docs/`:

- [manual_usuario.md](./manual_usuario.md)
- [manual_consultas.md](./manual_consultas.md)
- [manual_auditoria_usuarios.md](./manual_auditoria_usuarios.md)

## Dónde guardar documentación nueva

Proyecto en desarrollo: [Ventas — Carga de Comprobantes](./modulos/ventas-carga-comprobantes/README.md), con alcance por rubro, matriz de compatibilidad, inventario VB6 y relevamiento SQL preparado.

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

Reportes comerciales: [documentación técnica](./modulos/reportes-comerciales.md), [manual de Compras/Ventas y búsqueda de Configuración](../src/AlfaCore/Docs/manual_reportes_comerciales.md) y [avances de la pasada](./gestion/configuracion-reportes-comerciales-avances.md).

Cuenta corriente de Compras: [fuentes, comportamiento y validación](./modulos/reportes-compras-cuenta-corriente.md).

Conciliación bancaria inteligente: [integración y alcance](modulos/conciliacion-bancaria.md), [manual de uso](../src/AlfaCore/Docs/manual_conciliacion_bancaria.md).

Ayuda contextual: [modelo y mantenimiento](arquitectura/ayuda-contextual.md). Primeros manuales: [Conciliación](../src/AlfaCore/Docs/manual_conciliacion_bancaria.md) y [Cobranzas](../src/AlfaCore/Docs/manual_cobranzas.md).
