# Comparación de grabaciones VB6 / AlfaCore

Actualización: 2026-10-15. Herramientas de diagnóstico implementadas y probadas en DISTRIWALTERP. La comparación pareada contra una ejecución nueva de VB6 sigue pendiente.

## Qué se incorporó

- `CargaComprobantesHistoricosTests.cs` conserva opcionalmente los 100 casos retrospectivos, con identificación, precios históricos, cantidades, descuentos, exenciones, liquidación por línea, cabecera guardada/calculada, diferencias e indicadores. Mantiene el contrato del reporte agregado anterior.
- `CargaComprobantesComparacionTests.cs` exporta documentos **ya existentes** mediante Dapper. No invoca emisión, WSAA ni WSFE. Consulta el tipo real de los objetos y exige tablas confirmadas. Cada captura se lee dentro de una transacción Serializable y se revierte; ante error técnico utiliza el servicio central hacia `AUX_ERR`.
- `tools/ventas/comparar_grabaciones.py` compara dos capturas locales sin conexión SQL y genera un informe JSON. Detecta diferencias de campos y cantidad de filas, incluso duplicados. Para cabecera y secuencias no ambiguas muestra campo, esperado, obtenido y delta numérico.
- `tools/ventas/test_comparar_grabaciones.py` verifica que el comparador no oculte diferencias de centavos, signos, campos fiscales, filas duplicadas, PK con blancos ni columnas ausentes.

El exportador es un diagnóstico técnico con conexión de pruebas explícita. No es una API de usuario ni sustituye los permisos de la aplicación.

## Contenido de una captura

Todas las columnas de las filas seleccionadas, conservando blancos y valores NULL:

| Sección | Fuente y selección |
|---|---|
| Cabecera | `V_MV_CPTE`, por ID explícito, complemento cero y TC FC/NC/ND/FP |
| Articulos | `V_MV_CPTEINSUMOS`, por TC/documento |
| Stock | `V_MV_STOCK`, cantidades tal como están grabadas, con signo |
| Asientos | `MV_ASIENTOS`, por TC contable, sucursal/número/letra y unidad |
| AplicacionesEmitidas | `MV_APLICACION`, por TC/documento emisor |
| AplicacionesRecibidas | `MV_APLICACION`, por TC/documento de origen |
| Electronica | `V_MV_CPTE_ELECTRONICOS`, por TC/documento |
| Tareas / Conceptos | `V_MV_CPTETAREAS` / `V_MV_CPTE_OBSERV`, por TC/documento |
| OrigenCabecera / OrigenArticulos | Cabecera y detalle actuales del pedido/remito o factura asociada indicada en la cabecera; vacíos en carga directa sin origen |

El contexto registra base, instante UTC, documento y claves precisas de cálculo/proforma/equivalencias. La equivalencia contable se obtiene de la **configuración actual**, con fallback de `VALOR` vacío a `ValorAux`; si falta o no se encuentran asientos, la captura falla. No reconstruye una equivalencia histórica cambiada.

No incluye una copia completa de la base, todos los efectos indirectos de cualquier trigger, auditoría legacy, contadores de talonario ni un delta antes/después de cada tabla. Tampoco reconstruye el saldo que tenía el origen al emitir. Estos puntos deben verificarse adicionalmente al certificar el circuito; una captura es evidencia parcial del estado actual.

Los archivos pueden contener nombres, domicilios, documentos fiscales y datos comerciales. Guardarlos en `artifacts/ventas/comparacion/` (ignorado por Git), sin publicarlos en documentación ni adjuntarlos automáticamente. No contienen la conexión ni sus credenciales.

## Ejecución de lectura

Requiere `ALFACORE_TEST_SQL` configurada en el proceso con una conexión a la base de pruebas elegida. No incorporar su valor a scripts versionados. Compilar el proyecto de pruebas según el procedimiento habitual antes de usar `--no-build`.

Ejemplo de captura de las cinco operaciones AlfaCore ya conservadas en DISTRIWALTERP:

```powershell
$env:ALFACORE_COMPARACION_IDS = '433587,433608,433611,433614,433617'
$env:ALFACORE_COMPARACION_DESTINO = 'C:\dev\AlfaCore\artifacts\ventas\comparacion\alfacore'
dotnet test tests/AlfaCore.Tests/AlfaCore.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~CargaComprobantesComparacionTests'
Remove-Item Env:ALFACORE_COMPARACION_IDS,Env:ALFACORE_COMPARACION_DESTINO
```

La captura requiere de 1 a 20 IDs explícitos y un destino local. Si falta alguna variable, la prueba queda omitida. Si se repite en el mismo destino, reemplaza los archivos de esos comprobantes: usar carpetas distintas para preservar evidencia de momentos diferentes. No modifica ni emite movimientos.

Para guardar el detalle retrospectivo, definir `ALFACORE_CALCULO_CASOS` con un archivo local dentro de la carpeta de artefactos y ejecutar el test `CargaComprobantesHistoricosTests`. `ALFACORE_CALCULO_REPORTE` sigue siendo el reporte agregado, sin identificación de documentos. El diagnóstico detallado registra los importes calculados a precisión Currency; el indicador de coincidencia aplica la precisión monetaria configurada.

Comparación de dos capturas del mismo caso:

```powershell
python tools/ventas/comparar_grabaciones.py artifacts/ventas/comparacion/vb6/caso.json artifacts/ventas/comparacion/alfacore/caso.json --salida artifacts/ventas/comparacion/informe-caso.json
```

Códigos de salida: `0` coincide; `1` hay diferencias; `2` entrada inválida o error de lectura/escritura. El destino no puede ser una de las entradas. Los decimales se leen sin pasar por `float`; el informe representa los decimales como cadenas para conservar su precisión.

No hay tolerancia automática ni exclusiones por defecto: `NULL` difiere de cero o cadena vacía; los blancos de PK se conservan. El orden físico de las filas no importa, pero su multiplicidad y el campo `SECUENCIA` sí. Una columna o sección faltante genera diferencia; una captura sin secciones obligatorias se rechaza.

Para excluir un campo generado hay que indicarlo explícitamente, por ejemplo `--ignorar Cabecera.FechaHora_Grabacion`, y justificarlo en la ficha del caso. Las exclusiones se muestran en el informe; los nombres deben existir y coincidir exactamente. No excluir totales, cuentas, cantidades, impuestos, aplicaciones o referencias para conseguir un resultado coincidente. Metadatos y configuración se muestran como contexto, pero no forman parte de la igualdad de las filas; revisar también ese contexto antes de aceptar un caso.

## Resultado del 15/10 y hallazgo a investigar

Se capturaron FC A 1007-186 desde pedido, FC directa A 1007-187, NC A 1007-162, ND A 1007-2 y FP X 0004-6478. Cada una tiene un detalle y tres filas contables. FP no tiene fila electrónica. La FC directa tiene las dos aplicaciones recibidas de NC/ND; las notas tienen una aplicación emitida cada una. Se conservaron las capturas locales y la autocomprobación de cada archivo.

La comparación de un archivo consigo mismo **solo verifica la lectura y ejecución de la herramienta**. No es una comparación entre VB6 y AlfaCore ni certifica su resultado comercial.

El diagnóstico retrospectivo volvió a obtener 79 coincidencias y 21 diferencias sobre 100 FC. De las 21 diferencias:

- En las 21, el IVA guardado coincide, a dos decimales, con neto gravado guardado por su tasa.
- En 17, el neto calculado coincide con el guardado a dos decimales. En las otras cuatro hay también diferencia de neto; no alcanza con cambiar el cálculo de IVA.
- La diferencia máxima de total sigue siendo 0,01. Esto no significa que todas las diferencias de componentes sean como máximo 0,01.

La fuente `VtaTot.frm:2651–2653` llama a `CalculoIVA`; `2912–2923` muestra que la asignación depende de `pHuboCambiosRI` y utiliza el neto disponible en ese momento. `2678–2679` ejecuta después el ajuste final; `2720–2747` depende de configuración y condiciones. `clsTotales.cls:1192–1219` contiene además el ajuste por línea de precio final. Hay que registrar esos valores y ramas en un caso ejecutado: la coincidencia numérica observada no demuestra cuál produjo el histórico. No se cambió el motor monetario ni la configuración para forzar coincidencias.

Verificación realizada: compilación del proyecto de pruebas sin recompilar la aplicación; 14 pruebas de cálculo/diagnóstico correctas; exportador SQL correcto para los cinco documentos; 8 pruebas del comparador correctas y autocomprobación de las cinco capturas. No se ejecutó nuevamente la suite completa ni se solicitó un CAE en esta pasada.

## Corrección de precisión y cierre reproducible del 15/10

Se encontró y corrigió una diferencia de implementación concreta, sin utilizar los históricos como fórmula de ajuste:

- `clsTotales.cls:324–331`: `ImporteIVA` retorna **Double**, no Currency.
- `clsTotales.cls:1144/1153`: la asignación a `ImporteDelIVA@` ocurre **después** de multiplicar ese resultado por cantidad.
- AlfaCore convertía el IVA unitario a Currency antes de multiplicar. `CargaComprobantesCalculador` ahora conserva la precisión decimal de esa operación y redondea a cuatro decimales después de cantidad. Mantiene el ajuste lineal y el cierre vigentes. Esto respeta el momento de la conversión; no afirma reproducir bit por bit todos los casos límite de Double.

Dos pruebas nuevas fallaron con el motor anterior y pasan con la corrección: una evita que un IVA unitario pequeño se pierda al multiplicar 10.000 unidades; la otra reproduce el neto ajustado de una cantidad de 200 unidades. No se modificaron configuraciones ni comprobantes conservados.

La regresión completa del módulo posterior al cambio pasó: **61 casos, 54 correctos, 7 opcionales omitidos, 0 fallos**, incluyendo SQL con rollback, interfaz, notas/proforma, SOAP y recuperación. Los opcionales de emisión real no se activaron. Compilación correcta; preview reiniciado y carga directa HTTP 200.

La misma muestra retrospectiva de 100 FC pasó de 79 a **81 coincidencias completas**. Las cuatro diferencias de netos desaparecieron; quedan **19 diferencias de IVA/total de hasta 0,01**, todas en documentos entre el 12 y el 29 de noviembre de 2024. El reporte agregado se actualizó; el detalle anterior se conserva localmente en `historicos-casos-antes-iva-cantidad.json` y el nuevo en `historicos-casos.json`.

También se agregó un modelo **solo de diagnóstico**, en `CargaComprobantesCierreLegacyDiagnostico.cs`, con seis casos de prueba. Reproduce el paso de importes hacia el formulario de totales y permite contrastar el cierre con/sin el ajuste final:

| Paso | Evidencia |
|---|---|
| Precisión de entrada distinta para letra A y B | `clsTotales.cls:533–536`: B usa cuatro decimales; las otras letras usan precisión monetaria configurada |
| Redondeo antes de entrar al formulario | `clsTotales.cls:588–591` |
| Recálculo inicial de IVA activado | `VtaTot.frm:3190–3193` |
| IVA recalculado sobre el neto recibido | `VtaTot.frm:2912–2923` |
| Ajuste final opcional de hasta dos centavos | `VtaTot.frm:2720–2747`, agregado en fuentes el 02/10/2026 |

Primer caso reproducible: precio 12.853,27, neto unitario 10.622,54, dos unidades, IVA 21 %. El ajuste lineal obtiene neto 21.245,0744 e IVA 4.461,4656. Para letra A, el formulario recibe neto 21.245,07 y recalcula IVA 4.461,4647: redondeado queda 4.461,46 y el total 25.706,53. Con el ajuste final nuevo, agrega 0,01 al IVA y cierra en 25.706,54. Para B, conservar cuatro decimales de entrada produce otro recorrido. Los tests distinguen estos caminos sin alterar la liquidación comercial.

Sobre los **89 casos compatibles con este modelo simple**, el cierre diagnóstico sin ajuste final coincide en los 89; con el ajuste final coincide en 72. Los otros 11 tienen capacidades excluidas por ese modelo, como descuento por renglón. No son 100 casos certificados, ni una nueva ejecución de VB6. Las fuentes actuales contienen una regla posterior a los documentos de 2024 y falta confirmar binario/configuración histórica: no se desactivó el ajuste actual para forzar coincidencias ni se conectó el cierre diagnóstico al grabador.

El diagnóstico detallado incluye fecha, ambos cierres y sus valores intermedios por caso. Las huellas y rangos consultados de las tres fuentes originales están en `artifacts/ventas/comparacion/fuentes-redondeo.json`. Esos archivos se leyeron y no se modificaron.

## Ampliación posterior: descuentos y exentos

El modelo diagnóstico ahora cubre también descuento por artículo, artículos exentos y documentos mixtos de una alícuota más exentos. Se mantiene fuera del motor comercial y no habilita estas capacidades en la pantalla.

La fuente distingue dos descuentos distintos: `clsTotales.cls:1127–1129` aplica el porcentaje de cada artículo al neto unitario **una vez**; `613–616` transfiere los descuentos globales al formulario. La guarda de `VtaTot.frm:2727` consulta estos últimos. Por eso el cierre no vuelve a descontar el neto de un artículo ni se rechaza un diagnóstico solo por tener descuento por renglón. Los ajustes globales continúan fuera del modelo.

Para exentos, `clsTotales.cls:593` transfiere el neto no gravado con la precisión de entrada de la letra; `VtaTot.frm:2843–2845` lo conserva antes del formateo/suma final. El diagnóstico acumula gravado y no gravado por separado, calcula IVA solo sobre gravado y admite documentos sin grupos de IVA. La comparación ahora verifica explícitamente ambos netos, IVA, total y ausencia de internos; no basta con que coincida el total.

Resultado de esta pasada en la misma muestra de 100 FC:

| Evaluación | Coincidencias completas |
|---|---:|
| Motor comercial actual, sin cambios en esta pasada | 81 de 100 |
| Modelo diagnóstico con ajuste final | 81 de 100 |
| Modelo diagnóstico sin ajuste final | 100 de 100 |

La muestra incluye cinco documentos con descuentos por artículo y seis con exenciones; son categorías observadas, no perfiles nuevos de configuración. El [resumen agregado del cierre](calculo-cierre-diagnostico-resultados.json) no contiene identificadores de comprobantes ni datos personales. Los casos identificables siguen únicamente en los artefactos locales.

Se agregaron once casos de prueba: descuentos 8/9/15 % en letras A/B, mezcla de líneas con y sin descuento con precisión Currency, y cuatro combinaciones de exentos puros/mixtos en A/B. Compilación correcta y **33 pruebas seleccionadas correctas, cero fallos y cero omitidas**, incluyendo la lectura SQL de los 100 históricos. No se repitió la regresión comercial completa: los cambios de esta pasada son del modelo y pruebas, sin modificación del motor ni nuevas emisiones.

El resultado reproduce numéricamente los históricos; **no demuestra la rama ejecutada por el binario instalado ni certifica toda la grabación**. Sigue pendiente confirmar versión/configuración y ejecutar la comparación pareada. No se desactivó `AjustaRedondeoTotal` ni se cambió el importe de documentos existentes para lograr el resultado. Las fuentes actuales contienen el ajuste agregado en 2026, mientras los 19 documentos distintos son de 2024.

Este diagnóstico queda preparado para ese contraste. La próxima ampliación funcional debe concentrarse en precios/descuentos editables con sus permisos y reglas originales, manteniendo la comparación ejecutada como pendiente explícito. No seguir cambiando fórmulas para hacer coincidir retrospectivamente una versión desconocida.

## Próximo caso controlado

1. Partir de dos copias equivalentes de una misma base de pruebas, con configuración y maestros iguales. Registrar versión/huella del ejecutable original y estado inicial; la correspondencia binario/fuentes aún no está certificada.
2. Elegir primero un caso simple que reproduce la diferencia, y luego FC desde NP/RM, FC directa, NC, ND y FP con/sin IVA. No comparar documentos arbitrarios de distinto precio/fecha/configuración.
3. Registrar entradas y valores de los pasos de cálculo del original y de AlfaCore; para electrónica, usar únicamente el circuito de homologación previsto y conservar la identidad de operación.
4. Exportar cada resultado en carpetas separadas con las conexiones de esas copias y sus IDs reales. Comparar filas, revisar diferencias y justificar cualquier exclusión de identidad/instante generado.
5. Completar control antes/después de pendientes, numeración, estados y auditoría/efectos indirectos. La aceptación requiere evidencia de cada circuito y configuración, además de la revisión funcional del operador.

No ejecutar automáticamente el EXE instalado contra una base distinta ni cambiar archivos de la carpeta legacy. La preparación de capturas y comparador está terminada; la ejecución pareada y la explicación definitiva de las diferencias siguen pendientes en el [registro de continuidad](../../gestion/ventas-carga-comprobantes-avances-y-pendientes.md).

## Ampliación del 17/10/2026: varias referencias de origen

El exportador recorre también las aplicaciones emitidas y conserva todos los orígenes y sus artículos. La cabecera de una factura consolidada puede tener TCORIGEN/COMPROBANTEORIGEN vacíos; eso ya no produce una captura sin orígenes. Las secciones mantienen su contrato de listas planas, compatible con el comparador. Se agregaron las claves de exentos, unidades y descuentos al contexto. Nueve pruebas Python verifican el comparador, incluida la pérdida de una aplicación secundaria.

Ejecutable instalado leído: versión 2.02.0409, modificación 09/10/2026. Las fuentes consultadas en esta pasada tienen SHA-256: `V_mv_vta.frm` = `21DE407AE0445741387C606872059DC95D79093D50CDA9E43F136A62A4E60AF3`; `Rt_Stock.bas` = `97C09F4C63B5BDE48DD56EE66A18AA24BD71ABE45B8BF51C70F39FA1C066F03B`. Se leyeron los originales sin modificarlos. Esto identifica evidencia; no prueba correspondencia completa de binario, fuentes y DLL.

La ejecución pareada sigue pendiente. No se cambió el cierre monetario para eliminar las 19 diferencias retrospectivas. Ver [nuevo contrato de consolidación, IVA y unidades](consolidacion-iva-unidades.md).
