# Tokens AlfaDesign

[Índice](./README.md) · [Norma v1](./alfadesign-v1.md) · [Componentes](./alfadesign-components.md)

La fuente productiva actual de tokens es `src/AlfaCore/wwwroot/css/alfacore-design.css`. Los componentes y patrones consumen variables `--alfa-*`; un literal solo se admite como implementación puntual o deuda semántica documentada.

## Tokens Productivos

| Categoría | Tokens reales |
|---|---|
| Fondos | canvas `#0e1014`; surface `#16181d`; surface-raised/table-header `#1d2026`; input `#252830`; row-alt `#1a1d23`; row-hover `#22262e` |
| Bordes | default `#333640`; strong `#4d525e`; subtle = 46% de default |
| Texto | primary `#f5f5f7`; secondary `#a6abb5`; disabled `#666b75` |
| Acción | accent `#4f9efa`; hover `#6bb2ff`; active `#3880e0` |
| Estados | selected `#29476b`; success `#27ae60`; warning `#f2c94c`; danger `#eb5759` |

## Tipografía

- Body: `--alfa-font-body` = 13 px.
- Small: `--alfa-font-small` = 12 px.
- Caption: `--alfa-font-caption` = 11.5 px.
- Inputs y botones parten de Body; labels y tabla compacta usan Small/Caption según jerarquía.

## Tamaños Y Defaults

| Concepto | Tipo | Valor actual |
|---|---|---:|
| Control sm | token | 34 px |
| Control md | token | 36 px |
| Icon button | token | 36 px |
| App Top Bar desktop | implementation default | ~44 px |
| Context Toolbar desktop | implementation default | ~44 px |
| Shell compacto | implementation default | breakpoint alrededor de 1100 px |
| App Top Bar compacta | implementation default | ~40 px |
| Context Toolbar compacta | implementation default | ~40 px |
| Smart Search compact | implementation default | 408 px preferred |
| Smart Search standard | implementation default | 520 px preferred |
| Smart Search wide | implementation default | 760 px preferred |
| Data View Footer | implementation default | 34-36 px |
| Popover gutter seguro | implementation default | responsabilidad de JS/CSS compartidos |

No convertir todos estos defaults en tokens. Son contrato operativo actual, no necesariamente variables globales permanentes.

## Spacing, Radios Y Capas

- Spacing productivo: 4, 8, 12, 16 y 24 px.
- Radius productivo: 4, 8 y full (999 px).
- Action Menu: backdrop 1998, panel 1999.
- Dialog: `--alfa-z-modal` = 2100.
- Notification: `--alfa-z-notification` = 2200.

## Comportamientos Que No Son Tokens

No son tokens: cálculo viewport-aware de Smart Search, preview/commit de column resize, persistencia `WidthPx`, sticky Actions, clamp min/max de columnas y elección compact/standard/wide. Se documentan como patrones.

## Diferencias Con Figma

Figma usa variables `--alfacore-*`; producción usa `--alfa-*`. Figma registra alturas 28/36/44, spacing 32/40/48 y radius lg 12 que CSS aún no expone. Hasta una decisión conjunta, CSS manda para implementación productiva y Figma para intención visual.
