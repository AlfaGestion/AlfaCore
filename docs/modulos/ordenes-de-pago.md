# Órdenes de Pago (OPG / PGFP)

## Estado

v1 implementada el 06/11/2026 (fases 0-3 y 5). Pendiente: impresión PDF (fase 4) y recorrido de la pantalla en navegador con sesión real.
Verificación de base: ALFANET_DEMO en SERVER-ALFAVB6 (solo lectura + ensayos con rollback).

Origen legacy: `Z:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT\PAGOS.vbp`, formulario de arranque `FrmMvPagos.frm`. Líneas citadas de esa fuente a esta fecha.

## Decisiones (Alberto, 06/11/2026)

1. Una sola opción de menú, **Orden de Pago**, con interruptor **Proforma** (visible solo si la base usa proformas y el usuario tiene `VerProforma`). Con el interruptor, el comprobante es `PGFP`.
2. Medios de pago de la v1: efectivo, banco/transferencia y cheque propio. Cheques de terceros: v1.1.
3. Se respeta `MezclarProformas` (por defecto no mezclar).
4. La OPG **no tiene vencimiento**: solo fecha de emisión. (El legacy graba `FechaEstInicio`; no se pide ni se muestra.)
5. Retenciones (Ganancias, IIBB, IVA, SUSS) quedan para la v2.

## Cómo funciona el legacy

`FrmMvPagos` cambia de modo por parámetro: sin parámetro `PG`; `OPGConMedios` y `OPGSinMedios` generan `OPG`; `OPGLiquidacion` liquida una OPG existente generando un `PG`; `@PGFP` arranca con el tilde Proforma.

- La OPG con medios es la "orden de pago y liquidación": si se cargan medios (`Parcial > 0`, líneas 6055-6062) se graba finalizada y con asiento; sin medios queda pendiente.
- `FNTC` (línea 6899): con proforma el tipo es `PGFP` (salvo OPG sin medios); si no, `OPG` en modo orden de pago.
- Listas (líneas 7061-7275): con el tilde solo se eligen `FPC`/`NCCP`/`PGFP`; sin tilde, el resto. `MezclarProformas=SI` quita la restricción; el tipo grabado sigue dependiendo del tilde.
- `PGFP` no graba retenciones (líneas 3079, 4154). Las vistas contables excluyen `PGFP`.
- Cabecera `V_MV_CPTE`: `TC`, `IdComprobante`, `Finalizada=1`, `TCOrigen`/`ComprobanteOrigen` = sí mismo, `ImpDescuento1`, `Comentarios`, `UNegocio`, datos del proveedor (`VT_PROVEEDORES`).
- Asiento `MV_ASIENTOS`: Debe proveedor por el total pagado; Haber por cada medio (cuenta = `MA_CUENTAS.CodigoOpcional` del medio) y por descuentos/retenciones. Debe = Haber o se aborta.
- Aplicaciones `MV_APLICACION`: una por factura (`FCC`/`NDC`/`FPC`) con `TC_PRINT` e `IdComprobante_PRINT`. Anticipos y NC usados como medio se aplican a las facturas sin línea de asiento.
- Si los medios superan las facturas: anticipo previa confirmación. Si los débitos superan lo pagado: se ajusta o se aborta (siempre aborta si es agente de retención).
- Numeración: talonario `COMPRAS`, equivalencia `NUM_<TC>`; contable por `EQU_OPG`/`EQU_PG` (`PGFP` usa su código).
- Pendientes: vistas `CO_Cptes_Impagos` y `CO_Cptes_Creditos_Pendientes`; `chequesxunegocio` limita por unidad; `PagosAplicacionAuto` tilda todo.

## Lo que ya existe en AlfaCore

- Patrón a seguir: `CobranzasService` (transacción Serializable, journal de idempotencia, asiento balanceado, aplicaciones, contador). Numeración: `NumeracionAsiento.cs`, `CompraCargaService.Numeracion.cs`.
- `CO_CPTES_IMPAGOS_2026` ya incluye `FCC`, `FPC`, `NDC`.
- No existe módulo de pagos. La carga de compras **no soporta `FPC`** (pendiente), por lo que hoy las proformas de compra vienen solo del VB6.

## Plan

0. **Verificar base** con `docs/base-datos/sql-referencia/2026-11-06__verificar_ordenes_de_pago.sql` (solo lectura): configs, vistas de pendientes, tablas de talonarios, muestra de OPG reales.
1. **Servicio** `OrdenPagoService` (Preparar / Validar / Grabar) + modelos. Journal `ALFACORE_PAGO_OPERACION`. Todo configurable en `TA_CONFIGURACION`.
2. **Pantalla** Blazor en shell AlfaDesign (`AlfaDesignManagedModules` + `ModuleKey`): proveedor, comprobantes, medios, totales.
3. **Menú/permisos**: script idempotente en `App_Data/updates/` sobre `ALFACORE_MENU_WEB`/`ALFACORE_TAREAS_WEB` (nunca `TA_MENU`/`TA_TAREAS`).
4. **Impresión** PDF de la OPG (QuestPDF).
5. **Pruebas** con rollback en `ALFANET2007_PRUEBAS`: pago exacto, parcial, anticipo, proforma, mezcla rechazada, reintento idempotente. Luego `python tools/catalogo/check_catalogo.py`.

## Fuera de la v1

Retenciones, OPG sin medios y su liquidación posterior, descuentos, centros de costo, otras monedas, cheques de terceros.

## Implementación (06/11/2026)

Archivos: `Models/OrdenPagoModels.cs`, `Services/OrdenPagoService.cs` (reglas puras, consultas) y `OrdenPagoService.Grabar.cs` (grabación), `Components/Pages/OrdenPago.razor` (+ `.css`), update `2026-11-06-003__compras_orden_pago_menu_web.sql` (journal `ALFACORE_PAGO_OPERACION` + menú `D509012`), DI en `Program.cs`, pruebas `tests/AlfaCore.Tests/OrdenPagoTests.cs`. Ruta `/compras/pagos/orden-de-pago`, permiso `D509012`.

Hallazgos de la base (ALFANET_DEMO):
- `EQU_OPG=OPG`, `EQU_PG=PG`; no hay `NUM_OPG`. `MEZCLARPROFORMAS=NO`, `USAFACTURAPROFORMA=SI`, `CHEQUESXUNEGOCIO=NO`. `TIPOREGFACTURACION` no existe: el legacy graba `TIPO_REG` con 4 espacios (se respeta).
- Contador: `V_TA_CPTE.X_ULTIMO_NRO` guarda el **próximo** número (OPG 41, PGFP 220, PG 224); talonario 0, sucursal 1, letra X, sistema Compras.
- Pendientes: `CO_CPTES_IMPAGOS` (haber en cuentas `TipoVista='PR'`, `Estado=1` = bloqueado) y `CO_CPTES_CREDITOS_PENDIENTES` (debe en PR/PE: NC, anticipos, OPG sobrantes). `CO_CPTES_IMPAGOS_2026` no trae `MONEDA`.
- Medios: `MA_CUENTAS.MedioDePago` EF / DB / CH; cheque propio = `CH` con `TipoVista='BC'` y chequera en `V_TA_CTRL_CHEQUERAS` (`CuentaBancoDiferido` = cuenta del medio). `MV_ASIENTOS.ChPropio` no admite NULL.
- Las OPG históricas de la demo son sin medios (sin asiento); no hay ejemplo de OPG con asiento ni FPC cargadas para pagar.

Reglas implementadas:
- OPG (`TC=OPG`) o, con el interruptor Proforma, `PGFP`. El interruptor aparece solo con `USAFACTURAPROFORMA=SI` y `TA_USUARIOS.VerProforma`. Sin mezcla (salvo `MEZCLARPROFORMAS=SI`): proforma paga solo `FPC` y aplica solo `NCCP`/`PGFP`; el pago normal excluye esos tipos. La lista ya viene filtrada; la grabación lo revalida.
- Asiento: Debe proveedor por el dinero (suma de medios), Haber por cada medio; primera línea con CAB* del proveedor y `LIVA_Ret_*` en 0 (como el legacy). Los créditos no tienen línea: se aplican a las facturas por orden (`MV_APLICACION` con el crédito como pagador) y la orden aplica el resto. Lo que sobra es anticipo (queda como crédito pendiente de la orden), con confirmación explícita.
- Se exige al menos un medio con importe; moneda 1; comprobantes bloqueados o en otra moneda no se seleccionan.
- Numeración bajo bloqueo: `max(contador, máximo existente + 1)`; actualiza `X_ULTIMO_NRO`. Idempotencia por `Operacion` + hash (journal). Una transacción Serializable.
- Sin vencimiento en la OPG (decisión de Alberto).

Pruebas: 23 (20 puras: reparto, anticipo, créditos, reglas de proforma; 3 contra ALFANET_DEMO con rollback: pago exacto con asiento/aplicación/saldo/contador/idempotencia, crédito + anticipo + rechazo de mezcla, cheque propio con chequera). Requieren `ORDENPAGO_TEST_SQL` (ALFANET_DEMO o ALFANET2007_PRUEBAS); sin ella se omiten. Tras correrlas no quedó nada en la demo.

No cubierto todavía: pago real de una `FPC` (no hay proformas de compra cargadas; solo se probó el rechazo de la mezcla), impresión, y la pantalla en un navegador autenticado.

## Pendiente
- Fase 4: PDF de la orden de pago (reporte legacy `OPAGO`, `OPAGO-X.rpt`).
- Verificar la pantalla de punta a punta con sesión (AlfaDesign, teclado, 390/1024/1440 px).
- v2: retenciones (GAN, IIBB, IVA, SUSS; `PGFP` nunca retiene), OPG sin medios y su liquidación, descuentos, centros de costo, otras monedas, cheques de terceros, tarjetas.
