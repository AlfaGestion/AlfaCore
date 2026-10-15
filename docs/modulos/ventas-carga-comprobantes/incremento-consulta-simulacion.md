# Incremento 1 — consulta y simulación desde un origen

Fecha: 2026-10-14. Implementado en AlfaCore y validado contra DISTRIWALTERP, que el usuario confirmó como base de pruebas modificable.

## Comportamiento entregado

- Ruta `/ventas/carga-comprobantes` y variante SaaS `/{idweb}/{idbase}/ventas/carga-comprobantes`.
- Shell AlfaDesign con Context Toolbar, propietario `ventas`, paginación principal y pestaña propia sin enlaces a otras opciones. `ventas` ya estaba registrado en `AlfaDesignManagedModules`.
- Listado server-side NP/RM con búsqueda literal por cliente/cuenta/comprobante y `OFFSET/FETCH`.
- Encabezado histórico, detalle, selección de talonario compatible y cantidades editables por teclado.
- Simulación que reconsulta origen/pendientes antes de validar; no usa la grilla del navegador como fuente confiable.
- Proyección proporcional del `TotalFinal` histórico de cada renglón seleccionado. Es un importe de referencia, **no un port completo de `clsTotales` ni una liquidación fiscal certificada**.
- Indicación del efecto previsto de stock, incluida revisión ante devoluciones previas.
- Errores funcionales conservan la carga; cambios de usuario/base invalidan el contexto y el servicio descarta resultados de la sesión anterior.
- Permisos de módulo, validación de sesión autorizada y logging técnico mediante `IAppEventService` hacia `AUX_ERR`.

Los datos se consultan en la base activa; no hay conexión ni credenciales del cliente dentro del código. Se reutilizan `PagedResult`, guardas tenant, operación UI, `IPageHeaderService` y componentes AlfaDesign.

## Escrituras realizadas en la base de pruebas

Se aplicó `2026-10-14-002__ventas_carga_comprobantes_menu_web.sql` dos veces dentro de una transacción de instalación, comprobando una sola entrada web bajo Ventas. La clave nueva es `D60CCW`.

Antes de elegirla se consultó el árbol: `D6091` estaba ocupada por una opción legacy y se conservó. `D60CCW` no existía. El script tiene guardia de colisión para otras instalaciones; actualiza solo su mapeo, agrega permisos explícitos legacy y web, y no desactiva configuraciones de clientes.

Se modificaron únicamente menú y permisos necesarios para este incremento. Las pruebas de consulta/simulación no crearon comprobantes ni asientos. No se instaló `sp_web_CreaAsientoFactura` ni se modificaron triggers, reservas, centros de costo o autorizaciones de emisión.

## Archivos

| Archivo | Responsabilidad |
|---|---|
| `Components/Pages/VentasCargaComprobantes.razor` y `.razor.css` | Listado/editor y presentación adaptable |
| `Models/CargaComprobantesModels.cs` | Contexto, filtros, orígenes, detalle y simulación |
| `Services/ICargaComprobantesService.cs` | Contrato de aplicación |
| `Services/CargaComprobantesService.cs` | Sesión, permisos, orquestación, recarga y logging |
| `Services/CargaComprobantesRepository.cs` | Consultas Dapper sobre objetos confirmados |
| `Services/CargaComprobantesSimulador.cs` | Proyección histórica y validaciones de selección |
| `Services/CargaComprobantesReglaException.cs` | Separación de condiciones funcionales y errores técnicos |
| `App_Data/updates/2026-10-14-002__ventas_carga_comprobantes_menu_web.sql` | Alta idempotente de menú/permisos |
| `Program.cs` | Registro de repositorio y servicio scoped |
| `Components/Shared/AlfaDesign/AlfaInput.razor` | Parámetro opcional `Step` para admitir cantidades fraccionarias; default sin cambio |
| `tests/AlfaCore.Tests/CargaComprobantesTests.cs` | Validación de selección, SQL real, permisos y ausencia de movimientos nuevos |
| `tests/AlfaCore.Tests/CargaComprobantesUiTests.cs` | Render del componente, handlers reales con servicio de prueba, conservación de estado y Chromium |

El servicio exige sesión activa autorizada, sin fallback hacia una base global. Si el servicio compartido de permisos rechaza un conjunto vacío, se comprueba en SQL que no haya restricciones reales en las tablas legacy y web antes de aplicar la política de acceso irrestricto. Un fallo de esa comprobación se registra como error; no se interpreta como autorización.

## Validaciones ejecutadas

- Build .NET/Razor correcto.
- Nueve pruebas correctas, sin omisiones en la ejecución con SQL de pruebas.
- Consulta Dapper real de contexto, búsqueda paginada y detalle.
- Servicio con permisos reales `D60CCW`; rechazo de sesión no autorizada.
- Comparación del recuento de cabeceras y asientos antes/después de simular, sin diferencias.
- Script de menú ejecutado dos veces, sin duplicar la entrada.
- Chromium sobre HTML renderizado del listado y editor a 2048/1440/1024: sin desborde horizontal del documento; scroll interno de tabla.
- Foco accesible por etiqueta y desplazamiento con Tab; cantidad fraccionaria mediante `step=any`.
- Handler de simulación con cantidad inválida: conserva el renglón y valor ingresado, elimina la vista previa anterior.

La prueba de Chromium utiliza el componente y CSS compilados con datos sintéticos; no sustituye una prueba de login y navegación del shell completo en una instalación en ejecución. No hay comparación de grabación VB6/web ni emisión ARCA en esta entrega.

## Repetir pruebas

```powershell
dotnet test tests/AlfaCore.Tests/AlfaCore.Tests.csproj --filter FullyQualifiedName~CargaComprobantes
```

La prueba SQL requiere `ALFACORE_TEST_SQL` en el entorno del proceso y opcionalmente `ALFACORE_TEST_USUARIO`; no guardar su valor en scripts versionados. Si no está presente, se omite explícitamente esa prueba. La visual requiere Chromium instalado o `ALFACORE_TEST_BROWSER` con la ruta al ejecutable.

## Próximo incremento

Portar el cálculo completo y la grabación individual NP/RM → FC con sus actualizaciones de detalle de origen, asientos, stock, aplicaciones y numeración. Usar dos estados iniciales equivalentes para comparar VB6/web en esta base de pruebas o copias derivadas. Cerrar antes el tratamiento de descuentos globales, percepciones y centros de costo obligatorios de las cuentas admitidas.

Facturación múltiple, artículos repetidos entre renglones/orígenes, moneda extranjera, servicios y modificaciones/anulaciones siguen pendientes de certificación. La autorización para modificar la base de pruebas no habilita emisión fiscal real duplicada.
