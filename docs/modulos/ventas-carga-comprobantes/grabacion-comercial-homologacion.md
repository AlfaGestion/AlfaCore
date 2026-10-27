# Grabación comercial en homologación

Actualización: 2026-10-15. Continúa la autorización fiscal aislada de la factura A 1007-185. Esa prueba anterior permanece separada: no se convierte automáticamente en una factura comercial porque no conservaba un plan de efectos por cantidades.

## Circuito implementado

Un NP/RM individual con artículos simples, precios históricos finales, moneda local, una alícuota gravada y cuenta corriente sin recargos ni límite de crédito activo. El emisor debe ser responsable inscripto, estar en homologación y utilizar el punto global configurado. El talonario debe corresponder a esa unidad, punto y letra. A/B se resuelven con el mapeo fiscal compartido de AlfaCore, incluida la clase A de los receptores monotributistas contemplados por ese mapeo.

Se consultan cuentas por cliente, motivo/condición de IVA e imputación del origen; se rechazan artículos con cuentas particulares para no aplicarles silenciosamente el fallback general. Se mantienen las exclusiones de reservas/preparación, centros de costos y autorización de emisión. No se alteraron configuraciones ni triggers para hacer pasar el circuito.

Quedan fuera de esta primera grabación: servicios/otros conceptos, percepciones, contado/cobranza, crédito limitado y bloqueo por vencidos, reparto, exentos/internos/ITC, múltiples alícuotas, moneda extranjera, lotes/series, conversiones entre unidades, artículos repetidos y consolidación de orígenes. La pantalla puede consultarlos pero el servidor debe rechazar su grabación con un motivo visible. No se declara equivalencia general con los cientos de configuraciones de VB6.

## Flujo y recuperación

1. Simular conserva el cálculo y los avisos de la selección.
2. Verificar electrónica autentica y consulta numeración sin solicitar CAE.
3. Validar grabación prepara cuentas, período, fecha del servidor, vencimiento y talonario. Ensaya los efectos reales dentro de una transacción que siempre termina en rollback. Los triggers conservan sus controles. Las identidades SQL pueden avanzar aunque se revierta la operación; no se reseedan.
4. La confirmación envía la huella del plan revisado. El servidor lo reconstruye: cambios en origen, cliente, fecha, configuración o emisor impiden solicitar CAE hasta validar nuevamente.
5. Se toma un bloqueo de aplicación por origen y se guarda el plan operativo en `ALFACORE_VENTAS_CARGA_OPERACION`, antes de llamar a ARCA. El journal contiene datos comerciales y fiscales, sin certificados, claves privadas ni tickets. No es una tabla alternativa de configuración.
6. Se solicita CAE usando la identidad estable de operación. No hay una transacción SQL abierta durante HTTP. La solicitud/respuesta fiscal continúa en `ALFACORE_VENTAS_HOMO_PRUEBA`.
7. Una aprobación queda como `PENDIENTE_GRABACION`. Se bloquean y releen origen/configuración en una transacción serializable. Si coinciden con el snapshot, se graban todos los efectos locales y el estado `GRABADA` en el mismo commit.
8. Si cambia la sesión o falla la grabación después del CAE, se conserva el resultado fiscal y el plan. Recuperar reutiliza ese CAE; no lo vuelve a solicitar. Si hubo respuesta incierta, utiliza `FECompConsultar` y compara identificación, documento receptor, fecha, moneda, importes y alícuotas antes de atribuir la autorización. Una consulta inexistente o diferente no libera automáticamente el número.
9. Un origen con operación pendiente bloquea nuevas operaciones del módulo. La misma operación devuelve el resultado existente. Una factura parcial ya grabada permite iniciar otra operación sobre sus pendientes, después de volver a consultarlos.
10. Historial ofrece las últimas 25 operaciones autorizadas para el alcance del usuario. PDF reutiliza plantillas/renderizador/Chromium existentes y agrega una marca visible de homologación. Reintentar el PDF no emite ni graba otra factura.

La aplicación lock por origen coordina AlfaCore; VB6 no está obligado a tomarla. Por eso la transacción final bloquea cabecera/detalle y vuelve a comparar pendientes y snapshot. No se certifica todavía toda interacción concurrente con ejecutables de terceros. Un origen modificado después de aprobar fiscalmente puede requerir conciliación manual; no se reemplaza el plan autorizado por una nueva selección.

## Mapeo confirmado

| Efecto | Fuente / comprobación | Implementación |
|---|---|---|
| Cabecera y neto de artículos | `V_mv_vta.frm:12596`, `12778–12803` | `V_MV_CPTE`, importe de insumos neto, fecha/vencimiento y origen |
| Detalle | `V_mv_vta.frm:12388–12542` | Un renglón por artículo seleccionado, cantidad, precio, descuento, secuencia y coeficiente comercial |
| Stock | `V_mv_vta.frm:13581–13626`, `14790–14816`; registros históricos | NP descarga; RM respeta descarga previa/configuración y rechaza inconsistencias; cantidad y cantidad base negativas en el alcance de unidad base |
| Contadores NP/RM | `V_mv_vta.frm:12547–12590` | Actualizador existente dentro de la misma transacción |
| Aplicación | `V_mv_vta.frm:14926–14969` | `MV_APLICACION`, importe neto y origen; finalización por trigger existente |
| Asiento y libro IVA | `V_mv_vta.frm:14996–15208`, `16054–16445` | Cliente al debe, IVA/ventas al haber, libro IVA en primer renglón, balance exigido cero |
| Subdiario sin asociación | `cnStuffContable.bas:7259–7274`; FC históricas de esta base | El retorno legacy son cuatro blancos; se conserva ese caso sin crear asociaciones nuevas |
| Cuenta contable | Norma `CodigoPk` y excepción del plan de cuentas | Referencias contables alineadas a la izquierda; maestros restantes con formato estándar |
| Numeración | Llamadas legacy a `GrabaUltimoCpte`, valor observado del talonario | ARCA asigna el número; el contador legacy conserva número + 1, sin disminuirlo |

Los ensayos SQL verifican estructura, efectos y rollback. La comparación general de importes históricos mantiene las diferencias documentadas en el incremento de cálculo; no se eliminan ajustando fórmulas sin evidencia. Falta ejecutar los casos equivalentes en el programa instalado para declarar equivalencia campo por campo. El acceso a la carpeta compartida devolvió error de autenticación en la revisión adicional del 15/10; se conservó el relevamiento ya obtenido, sin modificar la carpeta ni credenciales de red.

## Archivos de este incremento

Nuevos:

- `Models/CargaComprobantesGrabacionModels.cs`: plan, huellas y resultado operativo.
- `Services/CargaComprobantesPlanificador.cs`: capacidades, cuentas, período, talonario y snapshot.
- `Services/CargaComprobantesGrabador.cs`: efectos locales indivisibles, sin commit propio.
- `Services/CargaComprobantesEmisionService.cs`: coordinación durable y recuperación.
- `App_Data/updates/2026-10-14-004__ventas_carga_operaciones.sql`: journal e índice de numeración que conserva rechazos sin consumir el número; aplicado dos veces en DISTRIWALTERP para comprobar idempotencia.
- `tests/AlfaCore.Tests/CargaComprobantesGrabacionTests.cs`: factura completa y rollback con triggers reales.
- `tests/AlfaCore.Tests/CargaComprobantesRecuperacionTests.cs`: SOAP, conciliación, respuesta aprobada con fallo local/sesión, bloqueo de otra operación y ausencia de reemisión.
- `tests/AlfaCore.Tests/CargaComprobantesArcaRuntime.cs`: infraestructura de pruebas con logging real y conexión solo por entorno.
- `tests/AlfaCore.Tests/CargaComprobantesComercialHomologacionTests.cs`: ensayo real de emisión comercial, optativo y con GUID estable.

Modificados:

- `Services/CargaComprobantesRepository.cs`: transacción opcional y catálogos de filtros solo cuando se necesitan; configuración y alcance se releen al grabar.
- `Services/ICargaComprobantesService.cs` y `CargaComprobantesService.cs`: APIs con guardas de sesión, permisos/unidad, historial y PDF con marca de prueba.
- `Services/CargaComprobantesElectronicaEmisor.cs`: emisor esperado, fecha explícita, letra con helper compartido y conciliación sin reenviar.
- `Services/IWsfev1Client.cs`, `Wsfev1Client.cs`, `Models/ArcaFacturacionElectronicaModels.cs`: consulta de comprobante aprobado conforme al [contrato oficial FECompConsultar](https://wswhomo.afip.gov.ar/wsfev1/service.asmx?op=FECompConsultar).
- `Models/CargaComprobantesModels.cs`: sistema capturado junto con sesión y usuario para la guarda antes de commit.
- `Components/Pages/VentasCargaComprobantes.razor`: validación, confirmación comercial, recuperación, historial y descarga; Smart Search y shell AlfaDesign existentes.
- `Program.cs`: registro de las nuevas dependencias.
- `tests/AlfaCore.Tests/CargaComprobantesTests.cs` y `CargaComprobantesUiTests.cs`: contratos nuevos, feedback y flujo de confirmación.
- `docs/CATALOGO_RUTINAS.md`, índice/matriz del módulo y manual de usuario: estado, límites y recorrido de prueba.

Las rutas de código son relativas a `src/AlfaCore/`, salvo las que comienzan con `tests/` o `docs/`. Los documentos funcionales viven en `src/AlfaCore/Docs/manual_carga_comprobantes.md`.

## Resultado ejecutado y validación

El 15/10/2026 se ejecutó una operación comercial real de homologación con una unidad del NP `000400008674X` (ID 432549), talonario 10, unidad 4:

- Estado `GRABADA`, FC `100700000186A`, ID comercial 433587.
- Importe $122,45; CAE `86420999246858`, vencimiento 25/10/2026; asiento 2 del mes 10/ejercicio 7.
- Operación estable `e70e0583-6242-4133-9957-e08a4ca2c95d`.
- Se comprobaron una cabecera, un detalle, una salida de stock, tres renglones contables balanceados y una aplicación al pedido. El saldo del pedido disminuyó exactamente una unidad y `FINALIZADA` permaneció en falso.
- Repetir la operación y recuperar su resultado devolvieron la misma factura/CAE sin otra solicitud ni duplicar efectos.
- El PDF se generó mediante los servicios reales de plantillas y Chromium, pasando por la guarda de sesión/permisos del módulo. Archivo local ignorado por Git: `artifacts/ventas/homologacion/factura-comercial-100700000186A.pdf`. Se inspeccionó visualmente y se verificaron una página, marca de homologación, CAE e importe. Conserva la presentación de precios finales de los diseños existentes.

La suite del módulo pasó con 30 pruebas y tres omisiones intencionales (emisión comercial, autorización fiscal aislada y exportación optativa). Tras los cambios de snapshot y contexto se repitieron las pruebas afectadas de escritura/UI/recuperación, sin fallos. La emisión comercial se ejecutó separadamente, con habilitación expresa y GUID estable, y pasó con PDF incluido. No se vuelve a emitir al repetir las pruebas normales.

La emisión comercial optativa exige `ALFACORE_TEST_COMERCIAL_EMITIR=1`, `ALFACORE_TEST_SQL` y `ALFACORE_COMERCIAL_OPERACION`. No habilitar esa variable en una ejecución general de tests. Las credenciales solo se proporcionan en el entorno del proceso y se eliminan al terminar. El catálogo contiene 88 rutinas, sin errores ni advertencias; los archivos revisados están en UTF-8 sin BOM y `git diff --check` es correcto.
