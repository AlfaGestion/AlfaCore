# POS Salón: mesas, comandas, cocina y carta QR

Módulo de AlfaCore para bares y restaurantes. Es el modo **Salón** del Punto de Venta: se elige en
`Ventas → Punto de Venta` y comparte el cobro con Mostrador (mismos comprobantes, medios de pago,
Mercado Pago Point y turno de caja).

## Qué resuelve

| Para quién | Qué ve | Ruta |
|---|---|---|
| Mozo / encargado | Plano de mesas por sector con el estado de cada mesa (libre, ocupada, con cosas sin enviar, platos listos para servir, pidió la cuenta, llama al mozo), total y minutos. Abrir mesa, cargar por rondas, enviar a cocina, precuenta, cobrar, cambiar, unir y liberar mesas. | `/ventas/punto-venta/salon?idPuntoVenta=N` |
| Cocina / barra | Pantalla de comandas por destino (Cocina, Barra, Barra 2): cada plato pasa de *nuevo* a *en marcha* y a *listo* con un toque. Demoras por color, aviso sonoro, agotados. Opcional: imprime cada comanda en la comandera de esa PC. | `/ventas/punto-venta/cocina?idPuntoVenta=N` |
| Comensal | Escanea el QR de la mesa y abre la carta en el celular, sin descargar nada ni registrarse. Abre la mesa o se suma con el código, pide, ve el estado de cada plato, llama al mozo y pide la cuenta. | `/{empresa}/mesa/{token}` |
| Encargado | Hoja de QR para imprimir y pegar en las mesas. | `/ventas/punto-venta/salon/qr?idPuntoVenta=N` |

## Puesta en marcha (checklist para el cliente)

1. **Punto de venta**: en *Agregar o editar puntos* crear uno con modo **Salón**, caja y sucursal.
2. **Sectores y mesas**: en el mismo ABM, cargar los sectores (Salón, Terraza…) y sus mesas con su capacidad.
3. **Plano**: abrir el salón → *Editar plano* → arrastrar cada mesa a su lugar → *Guardar plano*.
4. **Cocina / barra**: en cada artículo que se prepara, `TalleDefault` con **C** (cocina), **B** (barra) o **R** (barra 2), igual que en el salón VB6. Lo que no tiene letra (gaseosas, agua, cubiertos) no va a la cocina y queda servido al enviar.
5. **Mozos**: se toman de Vendedores (`V_TA_VENDEDORES`). `GOUR_RequiereMozo = SI` obliga a elegirlo al abrir.
6. **Cubiertos** (opcional): `GOUR_ArtCubiertos` con el código del artículo de cubierto; se cobra uno por persona.
7. **Carta QR**: en el salón → *Carta QR*:
   - *Solo ver la carta* o *Ver, pedir, llamar al mozo y pedir la cuenta*.
   - *Enviar a cocina sin que el mozo confirme* (si no, el mozo acepta cada pedido QR con un toque).
   - Mensaje de bienvenida y familias que forman la carta. Marcar una familia incluye sus subfamilias.
   - *Imprimir QR de las mesas*.
8. **PC de la cocina**:
   - Pantalla: abrir `Cocina` y dejar tildado *Pantalla*.
   - Comandera: tildar *Comandera*. Imprime cada comanda nueva en la impresora predeterminada de esa PC.
     Para que salga sola, sin el cuadro de impresión, abrir Chrome/Edge con `--kiosk-printing` (igual que
     la caja del Mostrador). Se puede tener pantalla y comandera a la vez, o una sola. Si hay dos
     comanderas para el mismo destino, cada comanda sale una sola vez.
9. **Caja**: el cobro abre el Mostrador con la mesa; necesita el turno de caja abierto como siempre.

## Circuito

```
Mozo abre la mesa (o el comensal desde el QR) ─┐
                                               ▼
           POS_PEDIDO (MODALIDAD SALON, ESTADO ABIERTA, PIN)
                                               │  rondas
Mozo: Agregar artículos (Mostrador)  ──────────┤  líneas PENDIENTE
Comensal: Enviar pedido (QR) ──────────────────┘  (origen QR: el mozo acepta)
                                               │  Enviar a cocina/barra
                                               ▼
     POS_COMANDA por destino + líneas ENVIADO  ──►  Pantalla cocina / comandera
                                               │  EN_PREPARACION → LISTO → SERVIDO
                                               ▼
     Precuenta / el comensal pide la cuenta  (ESTADO CUENTA)
                                               │  Cobrar
                                               ▼
     Mostrador ?idPedido=X&accionPedido=cerrar → PuntoVentaService.CreateSaleAsync
     (comprobante + CAE + asiento + cobranza) → pedido ENTREGADO, mesa libre
```

## Modelo de datos (tablas propias, prefijo `POS_`)

- `POS_PEDIDO`: una mesa abierta = un pedido con `MODALIDAD = 'SALON'`, `IDMESA`, `MOZO`,
  `CUBIERTOS`, `PIN` (código de 4 dígitos para sumarse desde el QR) y `FECHAHORA_CUENTA`.
  Estados: `ABIERTA`, `CUENTA`, `ENTREGADO` (cobrada) y `ANULADO` (liberada sin cobrar o unida a otra).
- `POS_PEDIDO_DET`: además del artículo, cada línea tiene `ESTADO` (`PENDIENTE`, `ENVIADO`,
  `EN_PREPARACION`, `LISTO`, `SERVIDO`, `ANULADO`), `DESTINO`, `ORIGEN` (`MOZO`/`QR`), `IDCOMANDA`,
  horarios y motivo de anulación. Lo enviado a cocina no se borra: se anula con motivo y queda auditado.
- `POS_COMANDA`: una por envío y destino. Es el ticket de cocina y la cola de la comandera (`IMPRESA`, `ESTACION`).
- `POS_ARTICULO_AGOTADO`: agotados por punto de venta.
- `POS_MESA_LLAMADO`: "llamar al mozo" / "pedir la cuenta" desde el QR.
- `POS_CARTA_FAMILIA`: familias de la carta QR.
- `POS_PUNTOVENTA.QR_MODO` / `QR_AUTOACEPTA` / `QR_MENSAJE`.
- Scripts: `App_Data/updates/2026-11-14-001__ventas_pos_salon_mesas.sql` y `2026-11-14-002__ventas_pos_salon_cocina_qr.sql`.

**No** usa `V_MV_COMANDA` ni el comprobante PE del salón VB6 (`fs_v_mv_comanda.vbp`): una base trabaja
el salón con VB6 **o** con AlfaCore, no con los dos a la vez. Del legacy se reutilizan `TalleDefault`,
`GOUR_ArtCubiertos`, `GOUR_RequiereMozo` y los vendedores como mozos.

## Seguridad de la carta QR

- El QR lleva un token de `ALFA_CENTRAL.ALFA_PUBLIC_LINK` (tipo `MESA`): resuelve base y mesa; la base
  nunca se toma de la URL. Se puede revocar o regenerar como los demás links públicos.
- Para pedir o llamar hace falta el PIN de la mesa abierta. El PIN cambia en cada apertura y muere al cobrar.
- El servidor revalida todo: mesa abierta, PIN, artículo en la carta, no agotado y precio del POS (el
  precio del celular se ignora). Límites: 30 productos por envío, 60 líneas por minuto por mesa y 8
  intentos de PIN cada 5 minutos.
- Por defecto el mozo confirma cada pedido QR antes de que salga a la cocina.

## Actualización en vivo

El salón se refresca cada 10 s, la cocina cada 5 s y la carta cada 8 s, con aviso sonoro cuando entra
una comanda o un llamado. No depende de un servicio aparte.

## Pendiente (próximas etapas)

- Propina, dividir la cuenta por persona y pago desde el celular.
- Pasar platos por tiempos (entrada, principal, postre).
- Reservas: adaptar `COMANDA_RESERVAS` del legacy.
- Cierre X/Z del salón (hoy se usa el cierre de turno de caja del POS).
