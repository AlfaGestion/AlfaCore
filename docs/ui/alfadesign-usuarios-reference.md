# Usuarios: Referencia AlfaDesign v1

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Mapa Figma](./alfadesign-figma-map.md) · [Checklist](./alfadesign-checklist.md)

Usuarios es referencia para un ABM administrativo: `Browse -> Edit/New`. Conserva dominio y seguridad legacy; no convierte `EsGrupo` en rol ni incorpora permisos sin contrato backend.

## Estado

- AlfaDesign cerrado: 13/13.
- Smart Search: compact.
- Filter Popover: anclado al Smart Search real y viewport-safe.
- Compact shell: aprobado; conserva AlfaCore + nombre de módulo.
- Data View Footer: patrón compartido `.alfa-data-view-footer`, sin repetir "Usuarios".
- Column resize: no implementado; no es requisito de cierre.

## Arquitectura

- App Top Bar global: `MainLayout`.
- Context Toolbar compartida: `MainPageHeader`.
- Browse: Data View con tabla compacta, Smart Search compact, chips, filtros reales, selección individual/masiva, agrupación, Configurar vista, paginación y Action Menu.
- New/Edit: `UsuariosEditView`, página completa con tabs Información/Acceso, dirty state, validación inline y confirmación de descarte.
- Baja: lógica individual/masiva con `AlfaConfirmDialog`.

No existe Record independiente ni URL estable propia para New/Edit.

## Data View

Browse usa Data View Header para columnas, rows densas y Data View Footer compartido. La paginación principal vive en Context Toolbar; el selector 25/50/100 vive como control secundario del Footer en la implementación actual.

Footer esperado: `14 registros · Página 1 de 1` y, si aplica, selección. No usar `registro(s)` ni título de módulo en el Footer.

## Component-First

Usuarios reutiliza `AlfaButton`, `AlfaIconButton`, `AlfaInput`, `AlfaSelect`, `AlfaCheckbox`, `AlfaTag`, `AlfaTabs`, `AlfaActionMenu`, `AlfaDialog`, `AlfaConfirmDialog`, `AlfaNotification` y `AlfaEmptyState`. Smart Search y tabla son patrones compartidos/de módulo, no componentes Razor únicos.

## Diferencias Con Figma

Nodos revisados: Listado `107:1363`, Nuevo `108:1640`, Edición `108:1840`, Validación `108:2036`, Confirmación de baja `108:2235`.

Diferencias deliberadas:

- Producción usa `Nombre` como identificador de cuenta; no separa nombre completo.
- Rol y permisos de Figma no existen como contrato de este editor.
- `EsGrupo` no equivale a rol.
- New/Edit no copian la lista lateral de Figma para no duplicar Browse dentro del formulario.
- Baja real es baja lógica; no se inventa reactivación.

## Deudas Separadas

Funcionales/técnicas fuera del cierre visual: reset de contraseña dedicado, reactivación, URL estable para New/Edit, transaccionalidad de foto/filesystem, uniformidad histórica de auditoría y mejoras de seguridad legacy de contraseña.

## Archivos Productivos

- `Components/Pages/Usuarios.razor`.
- `Components/Pages/Usuarios.razor.css`.
- `Components/Pages/Usuarios/UsuariosEditView.razor`.
- Servicios/modelos/validador de Usuarios.
