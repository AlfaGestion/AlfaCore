# Paquete estudio: revisión previa y Control ARCA

La página existente `/contabilidad/paquete-estudio` revisa el mes y la unidad seleccionados antes de generar el ZIP. Reutiliza el validador fiscal de los libros y `ControlArcaService.ControlarEnConexionAsync`; no implementa otro comparador ni modifica comprobantes.

Cada libro se controla por CUIT informante. Un archivo mensual disponible se compara nuevamente contra la base actual. Se informa el nombre y la fecha del archivo: esto no consulta novedades posteriores en ARCA. Si falta el original, un registro histórico no se considera validación vigente y se propone usar Control ARCA.

Los controles manuales se registran mediante `ArcaTransmisionRegistro` en `MV_Transmisiones` y `AUX_MV_CTROL_CPTE`, igual que los automáticos. Los Excel/CSV mensuales con CUIT propio identificado se conservan atómicamente en la carpeta tenant que resuelve `ArcaServidorService`. La revisión del paquete es de lectura: no descarga ni registra nuevas corridas de Control ARCA.

Un duplicado es una repetición de tipo, letra, punto de venta y número, más CUIT del proveedor en compras. Las unidades de distintos CUIT informantes se mantienen separadas. En Control ARCA, las repeticiones quedan como `PosibleDuplicado` incluso si la suma coincide con ARCA; un rango de números distintos no se considera duplicado.

La revisión muestra primero duplicados y diferencias, y permite consultar también datos fiscales. Las observaciones ya calculadas se presentan de a 50. El enlace a Asientos busca el comprobante y usa el mes de su fecha en el sistema, incluyendo diferencias de período. La corrección conserva las restricciones de los módulos existentes. Los enlaces a Control ARCA conservan mes, unidad y `directo=1`.

El ZIP agrega `00_Revision/Estado_controles_ARCA.txt` y `00_Revision/Duplicados_diferencias_y_observaciones.csv`. El CSV incluye todas las observaciones y protege textos que Excel podría interpretar como fórmulas. Un control pendiente no bloquea los libros; el TXT fiscal sigue sujeto al validador de exportación.

El portal del estudio ya disponía de la ruta y el permiso D9832 en los perfiles Consulta y Completo. El botón ahora dice **Paquete estudio** y conserva el mes del portal. No se agregó módulo, tabla ni opción de menú; no se necesita una migración SQL.

## Archivos

| Archivo | Cambio |
|---|---|
| `src/AlfaCore/Components/Pages/ContabilidadPaqueteEstudio.razor` | Revisión previa, estados, filtros, paginación y enlaces de corrección. |
| `src/AlfaCore/Components/Pages/ContabilidadPaqueteEstudio.razor.css` | Presentación de controles y tabla con tokens AlfaDesign. |
| `src/AlfaCore/Components/Pages/ContabilidadControlArca.razor` | Mes/tipo de entrada, regreso al paquete y duplicados visibles. |
| `src/AlfaCore/Components/Pages/ContabilidadControlArca.razor.css` | Estado de posible duplicado. |
| `src/AlfaCore/Components/Pages/ContabilidadAsientos.razor` | Entrada por búsqueda de comprobante y período. |
| `src/AlfaCore/Components/Pages/EstudioPortal.razor` | Acceso explícito al paquete con el mes seleccionado. |
| `src/AlfaCore/Models/EstudioPaqueteModels.cs` | Estado de revisión y observaciones identificadas. |
| `src/AlfaCore/Models/ControlArcaModels.cs` | Estado `PosibleDuplicado`. |
| `src/AlfaCore/Services/EstudioPaqueteService.cs` | Informe en el ZIP y revisión al generar. |
| `src/AlfaCore/Services/EstudioPaqueteService.Revision.cs` | Coordinación de validadores, archivos e historial; CSV. |
| `src/AlfaCore/Services/LibroIvaArcaValidator.cs` | Observaciones con referencia a la fila y separación por CUIT informante. |
| `src/AlfaCore/Services/ControlArcaService.cs` | Duplicados, registro manual y conservación del archivo original. |
| `src/AlfaCore/Services/ArcaDescargaService.cs` | Reconocimiento de CSV manuales en la carpeta de controles. |
| `src/AlfaCore/Services/ArcaTransmisionRegistro.cs` | Controles manuales, identificación de CUIT y duplicados. |
| `src/AlfaCore/Services/ArcaDescargaHostedService.cs` | Duplicados incluidos entre las novedades del control automático. |
| `src/AlfaCore/Docs/manual_contabilidad.md` | Instrucciones funcionales y límites de la validación. |
| `tests/AlfaCore.Tests/EstudioPaqueteRevisionTests.cs` | Regresiones de duplicados, CUIT, CSV y conservación de originales. |
| `tests/AlfaCore.Tests/LibroIvaFernandezTests.cs` | Revisión y generación contra FERNANDEZF2, octubre de 2026. |

## Verificación

Ejecutar las pruebas de `LibroIva`, `EstudioPaquete`, `ControlArca`, `EstudioContable` y `AsientosTests`. La prueba de FERNANDEZF2 requiere `ALFACORE_TEST_SQL` y `ALFACORE_IVA_FERNANDEZF2=1`; no guarda credenciales en el repositorio. Comprueba los dos controles pendientes cuando no hay archivos disponibles en el entorno de prueba, las tres compras duplicadas, las dos diferencias de totales y los seis libros en ambos formatos.

La generación registra la fecha del último paquete y su auditoría; los comprobantes permanecen bajo las acciones de sus módulos de origen. Ejecutar también `python tools/catalogo/check_catalogo.py`.
