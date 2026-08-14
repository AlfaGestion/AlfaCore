# Checklist AlfaDesign

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Guía](./alfadesign-module-guide.md)

Este checklist forma parte de Definition of Done. Una entrega debe informar `Cumple: X/13`, excepciones, deuda visual, regresiones y legacy restante. Presencia en markup no equivale a cumplimiento: validar render, comportamiento, integración, estados y responsive.

## 1. Shell

- [ ] App Top Bar compartida.
- [ ] Context Toolbar compartida.
- [ ] Acciones, navegación, búsqueda, vistas y paginación corresponden al estado real.
- [ ] Compact mode usable alrededor de 1024/1100 px.
- [ ] La bajada AlfaCore + módulo se conserva en compacto.
- [ ] No hay barras locales duplicadas, `fixed` superpuestos ni scroll horizontal global.

## 2. Smart Search

- [ ] Aparece solo si hay colección consultable.
- [ ] Popover anclado al trigger real.
- [ ] Viewport-safe, con gutters y max-height adecuados.
- [ ] Sizing semántico elegido por contenido: compact, standard o wide.
- [ ] Grid de filtros explícito; no depende de auto-placement frágil.
- [ ] `Acciones` + `Aplicar/Limpiar` funcionan como unidad.
- [ ] No hay clipping ni separación artificial de acciones.

## 3. Data View

- [ ] Data View Header correcto para List/Table/Grid.
- [ ] Rows/cards densos y legibles.
- [ ] Scroll interno; no overflow horizontal global.
- [ ] Data View Footer presente cuando aplica.
- [ ] Footer usa `.alfa-data-view-footer`.
- [ ] Footer no repite nombre de módulo.
- [ ] Resumen usa separadores `·` y pluralización natural cuando sea razonable.
- [ ] Page-size, si existe, queda como control secundario; no duplica paginación principal.

## 4. Column Sizing, Si Aplica

- [ ] Justificado por densidad/heterogeneidad de columnas.
- [ ] Metadata min/default/max/resizable.
- [ ] Columnas estructurales fijas.
- [ ] Ellipsis y legibilidad al reducir.
- [ ] Horizontal scroll interno como fallback.
- [ ] Preview durante drag.
- [ ] Persistencia al commit, no por pixel.
- [ ] `WidthPx` opcional compatible con configuración anterior.
- [ ] Reset widths conserva visibilidad, orden y agrupación.

No convertir column resize en requisito obligatorio de 13/13 cuando no aporta valor.

## 5. Actions

- [ ] Jerarquía Primary/Secondary/Ghost/Danger consistente.
- [ ] Icon actions tienen label accesible.
- [ ] Sticky Actions si la tabla lo necesita.
- [ ] Sticky Actions no se corta contra scrollbar y respeta zebra/hover/selected.

## 6. Componentes

- [ ] Se reutilizó el catálogo AlfaDesign antes de crear UI.
- [ ] No quedan botones Bootstrap, `.form-control`, dropdowns o modales legacy sin excepción.
- [ ] Componentes se validan visual y funcionalmente, no solo por presencia.

## 7. Tokens Y Escala

- [ ] Colores, spacing, radios, tamaños y estados usan tokens existentes.
- [ ] Literales inevitables están documentados.
- [ ] Tipografía, densidad y touch targets son legibles.

## 8. Estados

- [ ] Loading, empty, no results, error, validación, disabled y processing están resueltos.
- [ ] Un error parcial no destruye shell ni estado ingresado.

## 9. Overlays Y Feedback

- [ ] `AlfaDialog`/`AlfaConfirmDialog` renderizan overlay real.
- [ ] Backdrop, z-index, foco, Escape, scroll interno y footer funcionan.
- [ ] `AlfaLookup` no queda cortado por dialog.
- [ ] `AlfaNotification` usa severidad semántica, texto y cierre; no depende solo de color.

## 10. Formularios

- [ ] Labels, binding, dirty state, errores inline, cancelar/descartar y doble submit son correctos.

## 11. Responsive

- [ ] Validado a 2048.
- [ ] Validado a 1440.
- [ ] Validado a 1024.
- [ ] Desktop amplio no se degrada por fixes compactos.
- [ ] Sin overflow horizontal global.

## 12. Funcionalidad

- [ ] Se preservaron rutas, permisos, consultas, persistencia, auditoría y callbacks.
- [ ] No hay acciones, columnas o datos ficticios.

## 13. Validación Técnica

- [ ] `dotnet build AlfaCore.sln`
- [ ] `dotnet test AlfaCore.sln`
- [ ] `python tools/catalogo/check_catalogo.py`
- [ ] `git diff --check`
- [ ] Cache-bust y assets vigentes después de CSS/JS.
- [ ] Textos visibles sin mojibake ni caracteres de reemplazo.
- [ ] Sin conflictos, debug temporal, secretos, capturas o temporales versionados.

## Resultado Obligatorio

```text
AlfaDesign checklist
Cumple: X/13
Excepciones:
Deuda visual:
Regresiones:
Legacy restante:
```

## Estado De Referencias Cerradas

| Módulo | Resultado | Nota |
|---|---|---|
| Contactos | 13/13 | Cerrado visualmente; column resize no aplica/no implementado. |
| Usuarios | 13/13 | Cerrado visualmente; column resize no implementado. |
| Técnicos | 13/13 | Cerrado visualmente; column resize no implementado. |

## Estado En Curso

| Módulo | Estado | Nota |
|---|---|---|
| Clientes | migración en curso | Browse AlfaDesign con Smart Search standard, Data View Footer, column resize y sticky Actions; Editor pendiente. |
| Proveedores | legacy | Comparte componente con Clientes; no declarar AlfaDesign. |
