# Checklist AlfaDesign

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Guía](./alfadesign-module-guide.md)

Este checklist forma parte de Definition of Done. Cada sección vale un punto; una entrega debe informar `Cumple: X/13` y detallar excepciones, deuda visual, regresiones y legacy restante.

> **Presencia del componente != cumplimiento AlfaDesign.** No alcanza con encontrar un componente en Razor o en el DOM. La validación debe comprobar: 1) componente correcto; 2) render visual correcto; 3) comportamiento correcto; 4) integración correcta con el layout; y 5) estados correctos. Este criterio se aplica a todos los componentes compartidos.

## 1. Estructura de pantalla

- [ ] App Top Bar presente mediante el componente/shell global.
- [ ] Context Toolbar presente mediante infraestructura compartida y con acciones del estado real.
- [ ] Smart Search y filtros aparecen solo cuando existe funcionalidad real.
- [ ] Paginación corresponde al tipo de pantalla y no se simula.
- [ ] View Switcher muestra solamente vistas reales.
- [ ] Data View Header aparece solo en List/Table/Grid; Record/Edit/New no arrastran Table Header.
- [ ] No hay barras locales duplicando App Top Bar o Context Toolbar.
- [ ] No se recrearon componentes globales dentro del módulo.
- [ ] No hay sidebar global legacy, `fixed` superpuestos ni compensaciones artificiales.
- [ ] El shell usa flex/min-height/scroll interno correctamente y no genera scroll horizontal global.

## 2. Componentes

- [ ] Se buscó y reutilizó el catálogo AlfaDesign antes de crear UI; no quedan botones Bootstrap, inputs/selects/checkboxes/tabs/menús/confirmaciones/empty states/modales legacy sin excepción.
- [ ] Lo nuevo reutilizable es compartido, parametrizable y está documentado.
- [ ] Cada componente se renderiza visualmente según su contrato; no se valida solo por estar presente en markup.
- [ ] Variantes, tamaños, jerarquía, integración y estados default/hover/focus/disabled/loading/empty/error/selected se comprobaron cuando corresponden.

## 3. Acciones

- [ ] Jerarquía Primary/Secondary/Ghost/Danger consistente; una primary por grupo.
- [ ] Icon actions tienen label accesible y los handlers reales se conservaron.

## 4. Tokens

- [ ] Colores, spacing, radios, tamaños y estados usan tokens existentes.
- [ ] Literales inevitables y diferencias Figma/CSS están documentados.

## 5. Escala

- [ ] Tipografía, densidad, touch targets y jerarquía son legibles y coherentes.

## 6. Estados

- [ ] Carga, vacío, sin resultados, error, validación, disabled y processing están resueltos cuando corresponden.
- [ ] Un error parcial no destruye el shell ni el estado ingresado.

## 7. Overlays y feedback

- [ ] El Dialog se renderiza realmente como overlay y no inline.
- [ ] Backdrop visible y correcto.
- [ ] Surface contenida y sólida.
- [ ] Z-index correcto.
- [ ] Header/body/footer mantienen estructura.
- [ ] Scroll ocurre dentro del dialog cuando corresponde.
- [ ] No expande el documento.
- [ ] No deja interactuar visualmente con contenido trasero cuando no corresponde.
- [ ] Escape/cerrar/cancelar funcionan.
- [ ] Focus se mantiene correctamente.
- [ ] Feedback es claro, accionable y no expone detalles técnicos.

Para `AlfaLookup` comprobar input, panel de resultados, selección, hover, loading, empty, error, teclado, Escape y overflow. Para `AlfaButton`, comprobar variante, tamaño, disabled, hover, focus y jerarquía semántica. La misma validación contractual aplica al resto del catálogo.

- [ ] Lookup abierto dispone de un área visible razonable.
- [ ] Results panel no queda cortado por Dialog.
- [ ] Dialog no activa scroll innecesario con contenido corto.
- [ ] Footer permanece visible.
- [ ] En viewport bajo el overflow degrada correctamente.
- [ ] Success utiliza señal semántica success.
- [ ] Error utiliza señal danger.
- [ ] Warning utiliza la señal de atención definida.
- [ ] Info utiliza accent.
- [ ] La notificación no depende únicamente del color: mantiene título y texto.
- [ ] Success permanece aproximadamente 4 s.
- [ ] Warning/Error permanecen aproximadamente 8 s.
- [ ] Hover pausa el auto-dismiss y al salir continúa.
- [ ] Close manual funciona.
- [ ] Un click normal fuera no descarta accidentalmente la notificación.
- [ ] La surface de Notification sigue siendo sólida AlfaDesign.
- [ ] Notification queda por encima de Dialog cuando corresponde.
- [ ] No existe feedback legacy paralelo en el alcance migrado.

## 8. Formularios

- [ ] Labels, binding, dirty state, errores inline, cancelar/descartar y doble envío son correctos.

## 9. Responsive

- [ ] Validado a 2048, 1440 y 1024 px.
- [ ] No hay scroll horizontal; paneles y overlays siguen utilizables.

## 10. Funcionalidad

- [ ] Se preservaron rutas, permisos, consultas, persistencia, auditoría y callbacks.
- [ ] No hay acciones o datos ficticios.

## 11. Auditoría legacy

- [ ] Se buscaron `.btn`/`btn-*`, `.form-control`, `.dropdown-menu`, Bootstrap modal, clases legacy, estilos inline, hex sospechosos y componentes antiguos.
- [ ] Cada hallazgo se migró o tiene excepción y motivo.

## 12. Accesibilidad

- [ ] Navegación por teclado, foco visible, nombres accesibles, roles/ARIA y contraste son suficientes.

## 13. Validación técnica

- [ ] `dotnet build AlfaCore.sln`
- [ ] `dotnet test AlfaCore.sln`
- [ ] `python tools/catalogo/check_catalogo.py`
- [ ] `git diff --check`
- [ ] La compilación se ejecutó después del último cambio de CSS aislado y el asset CSS contiene los selectores actualizados.
- [ ] La validación visual usa el proceso localhost reiniciado y el cache-bust vigente, no un proceso/asset anterior.
- [ ] Los textos visibles no contienen mojibake.
- [ ] Los caracteres UTF-8 en español se renderizan correctamente.
- [ ] No existen caracteres de reemplazo inesperados.
- [ ] El encoding no se corrige mediante `Replace` ad hoc.
- [ ] Los mensajes de Notification fueron revisados con caracteres acentuados.
- [ ] Sin conflictos, debug temporal, secretos, capturas o temporales versionados.

## Resultado obligatorio

```text
AlfaDesign checklist
Cumple: X/13
Excepciones:
Deuda visual:
Regresiones:
Legacy restante:
```

No declarar “AlfaDesign completo” con excepciones visuales legacy sin documentar.

## Resultado Fase 7.5 — Contactos (2026-08-13)

```text
AlfaDesign checklist
Cumple: 13/13
Excepciones: ninguna dentro del alcance aprobado.
Deuda visual: ninguna dentro de Contactos.
Deuda técnica separada: ConversacionesService.cs contiene 124 líneas históricas con mojibake fuera del alcance de Fase 7.5; no se corrigen en este checkpoint.
Regresiones: ninguna detectada en la validación funcional, visual aprobada y verificaciones técnicas.
Legacy restante: ninguno visual dentro de Contactos salvo excepciones documentadas/no visuales.
```

Responsive quedó validado en sesión autenticada a 2048, 1440 y 1024 px: Context panel lateral en 2048/1440, debajo del contenido en 1024, sin solapamientos ni scroll horizontal global. `AlfaDialog`, `AlfaLookup` y `AlfaNotification` permanecen contenidos y el scroll vertical funciona correctamente.

## Resultado Fase 8.4 — Usuarios (2026-08-13)

```text
AlfaDesign checklist
Cumple: 13/13
Excepciones: ninguna dentro del alcance aprobado.
Deuda visual: ninguna.
Regresiones: ninguna detectada.
Legacy restante: ninguno visual dentro de Usuarios; tabla/Smart Search y selector de archivo son patrones nativos justificados.
```

Los 13 puntos están respaldados por la validación manual aprobada de Browse/New/Edit/dirty state/acciones, la auditoría técnica de Fase 8.4 y la comprobación autenticada responsive a 2048, 1440 y 1024 px. En 1024 el editor pasa a una columna, la tabla conserva overflow exclusivamente interno, dialogs y notifications quedan contenidos y no aparece scroll horizontal global.
