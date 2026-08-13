# Tokens AlfaDesign

[Índice](./README.md) · [Norma v1](./alfadesign-v1.md) · [Componentes](./alfadesign-components.md)

La fuente productiva actual es `src/AlfaCore/wwwroot/css/alfacore-design.css`. Los componentes deben consumir variables `--alfa-*`; un valor literal solo se admite para un caso semántico todavía no tokenizado y debe quedar documentado.

## Colores productivos

| Categoría | Tokens reales |
|---|---|
| Fondos | canvas `#0e1014`; surface `#16181d`; surface-raised/table-header `#1d2026`; input `#252830`; row-alt `#1a1d23`; row-hover `#22262e` |
| Bordes | default `#333640`; strong `#4d525e`; subtle = 46% de default |
| Texto | primary `#f5f5f7`; secondary `#a6abb5`; disabled `#666b75` |
| Acción | accent `#4f9efa`; hover `#6bb2ff`; active `#3880e0` |
| Estados | selected `#29476b`; hover `#21242b`; success `#27ae60`; warning `#f2c94c`; danger `#eb5759` |

Cada valor se consume por su variable `--alfa-*` homónima: canvas para viewport, surface para panel base, surface-raised para overlays, input para controles, border por jerarquía, text por énfasis, accent para interacción y estados solo con su significado semántico.

## Tipografía

- Cuerpo: `--alfa-font-body` = 13 px.
- Small/labels: `--alfa-font-small` = 12 px.
- Caption: `--alfa-font-caption` = 11.5 px.
- Inputs y botones parten de Body; labels y tabla compacta usan Small/Caption según jerarquía. Los headings usan la escala documentada de Figma o la especialización aprobada del módulo.
- Figma Foundations además registra Display 28 Bold, H1 22 Semi Bold, H2 18 Semi Bold, H3 15 Semi Bold, Body 13, Body Small 12 y Caption 11 Medium. Los tamaños de RecordView aprobados pueden especializar esta escala sin alterar List/Kanban.

## Tamaños

- Controles productivos: `--alfa-control-sm` = 34 px, `--alfa-control-md` = 36 px.
- Icon button: `--alfa-icon-button-size` = 36 px.
- App Top Bar y Context Toolbar: 44 px.

## Espaciado, radios y capas

- Spacing productivo: 4, 8, 12, 16 y 24 px.
- Radius productivo: 4, 8 y full (999 px).
- Action Menu: backdrop 1998, panel 1999. Dialog: `--alfa-z-modal` = 2100. Notification: `--alfa-z-notification` = 2200.
- Sombras no tienen tokens globales todavía; dialogs usan la decisión aprobada `0 12px 32px rgb(0 0 0 / 35%)`.
- Notification reutiliza surface-raised, border-strong, la misma sombra flotante y una señal de 3 px con `--alfa-success`, `--alfa-accent` o `--alfa-danger`; no agrega colores nuevos.

## Diferencias verificadas con Figma

El archivo Figma usa variables con sintaxis web `--alfacore-*`, mientras producción usa `--alfa-*`. Figma registra alturas 28/36/44; producción 34/36 y shell 44. Figma tiene spacing 32/40/48 y radius lg 12 que CSS aún no expone. Son deudas de sincronización: no renombrar ni cambiar escala silenciosamente. Hasta una decisión conjunta, manda CSS para implementación existente y Figma para intención visual.
