# Modelo de Configuración Central - TA_CONFIGURACION

## Conciliación de tarjetas

`TARJETAS-IA-DIAS` (CAJA, inicial `3`, de 0 a 31) define la cercanía entre fechas de operación/presentación de ambos lados. `TARJETAS-IA-MAX-FILAS` (CAJA, inicial `2000`, de 50 a 2000) limita pendientes por lado, incluyendo otras páginas: al excederlo se rechaza el análisis completo. Se agregan mediante `2026-10-27-004`, sin cambiar valores existentes. La IA respeta `TARJETAS-TOLERANCIA` y reutiliza `OPENAI_API_KEY`, `OPENAI_MODEL` y el registro de consumo CONCILIACION existentes. La lectura del PDF sigue siendo local; **Asistir con IA** envía solo el detalle necesario de liquidación y movimientos autorizados, no el PDF. La propuesta se revisa individualmente y requiere confirmación explícita; no registra LT/IVA ni cambia importes.

La actualización `2026-10-27-002` registra en CAJA `TARJETAS-PYTHON` (ejecutable Python: vacío o el valor inicial `python` reutilizan `InterfacesIaComprasPythonExe` de Recepción documental; una ruta explícita permite una instalación propia; requiere pypdf en ese intérprete del servidor), `TARJETAS-TOLERANCIA` (importe absoluto por selección, inicial `1.00`, válido de 0 a 100) y `TARJETAS-ASOCIACIONES` (ValorAux JSON, inicial `[]`). Cada asociación tiene Banco, Comercio, Tarjeta, Cuenta y Sucursal; se recuerda solo al importar con la opción explícita. No se fija 0001/Nación ni 0002/Patagonia. Las cuentas se validan contra MA_CUENTAS TJ activa, imputable y en pesos. La tolerancia no modifica importes y requiere motivo para diferencias. No registra LT, asientos ni IVA. Ver [módulo](modulos/conciliacion-tarjetas.md).

La detección de cuenta al seleccionar un PDF reutiliza `TARJETAS-IA-DIAS` para consultar las fechas de operación con margen, y `TARJETAS-TOLERANCIA` para la evidencia orientativa de bruto diario de lotes. No agrega claves ni usa IA en esa detección; los pares individuales se comparan por importe exacto. Las asociaciones recordadas se validan contra las cuentas TJ activas en pesos. La elección automática es una sugerencia revisable, no confirma conciliaciones.

## Base de demostración reiniciable

`DEMO-BASE-REINICIABLE` (CAJA) identifica únicamente `ALFANET_DEMO` en `SERVER-ALFAVB6`. Su valor debe ser `ALFANET_DEMO`. La herramienta de restablecimiento exige esta marca además de validar servidor, nombre interno del respaldo y archivos físicos antes de cerrar conexiones o restaurar. No habilita un reinicio en otras bases ni se distribuye por el actualizador general. Ver [operación y respaldo de la demo](../tools/conciliacion_demo/README.md).

## Ayuda global con IA

El chat del icono **?** reutiliza `OPENAI_API_KEY` y `OPENAI_MODEL` del servidor, cargados por el mecanismo `.env` existente, con el mismo fallback general que las otras funciones de IA. No agrega parámetros propios ni guarda credenciales en `TA_CONFIGURACION`. El consumo se identifica como `AYUDA`. Sin clave configurada, las guías siguen disponibles. Ver [arquitectura de ayuda](arquitectura/ayuda-contextual.md).

## Conciliación bancaria inteligente

La lectura de extractos PDF reutiliza `InterfacesIaComprasPythonExe` de Recepción documental en la base activa, con fallback a `python` del PATH del servicio si no hay ruta configurada. Requiere pypdf instalado en ese mismo intérprete. No agrega una clave propia ni usa TARJETAS-PYTHON como configuración bancaria; ambos lectores comparten el transporte local y conservan sus formatos y validaciones específicos.


`CONCILIACION-IA-MAX-FILAS` (CAJA): máximo de pendientes por lista en un análisis, inicial 300, permitido 50 a 1000. Si se excede se requiere acotar filtros; no se envía una muestra parcial. `CONCILIACION-IA-MODELO` (CAJA): modelo opcional para esta función; vacío reutiliza OPENAI_MODEL del servidor y su fallback vigente. La clave proviene de OPENAI_API_KEY, cargada por el mecanismo .env existente, y no se guarda en la base del cliente. El consumo se registra con función CONCILIACION. La IA utiliza tolerancia, margen de días y referencia del circuito banco/Alfa; no cambia esos criterios ni confirma automáticamente. Requiere actualización 2026-10-23-001__caja_conciliacion_ia.sql.

`CONCILIACION-ASIENTOS` (CAJA, ValorAux JSON): subdiario y reglas de conceptos para proponer contrapartidas de CC desde extractos. La regla identifica nombre, clasificación, cuenta bancaria opcional, texto contenido en la descripción, contrapartida y detalle predeterminado. Las reglas por banco/texto tienen prioridad; los empates no se resuelven automáticamente. El subdiario vacío conserva el comportamiento observado de CC legacy. No hay cuentas predeterminadas inventadas: deben elegirse del catálogo contable activo en pesos. Ver el módulo de conciliación y su manual.

Si no hay reglas, el editor propone un borrador a partir de descripciones específicas y únicas del catálogo activo: gastos/comisiones bancarios, intereses pagados/ganados, retenciones bancarias de IIBB, IVA específico de comisiones bancarias y Ley 25.413. Los códigos se toman de la base, sin códigos fijos. Se conserva el subdiario; la propuesta no se guarda ni participa del matching hasta que el usuario guarda la configuración. Las reglas existentes nunca se completan ni sustituyen automáticamente.

Desde **Conciliar cuenta → Criterios de propuestas** se guardan en esta base las claves `CONCILIACION-TOLERANCIA-BANCO` (importe absoluto por par, inicial `1.00`, de 0 a 100 con dos decimales), `CONCILIACION-COMPARAR-FECHA` (SI inicial) y `CONCILIACION-COMPARAR-REFERENCIA` (NO inicial). Se utiliza el margen existente `CONCILIACION-DIAS-BANCO`. La cuenta, moneda y sentido siempre deben coincidir. Referencia activada exige un valor no vacío e igual, sin espacios exteriores. Desactivar fecha elimina solo la prueba de cercanía: se conserva el período consultado. Se distinguen estas claves del matching de facturas/cobranzas y su tolerancia existente. La tolerancia no modifica importes ni genera ajustes contables. Confirmar una diferencia dentro del margen exige aceptación explícita y motivo; se auditan diferencia, tolerancia usada y motivo original en BK_Conciliacion.

`CONCILIACION-DIAS-BANCO` (CAJA): margen de fecha para sugerencias banco/Alfa, valor inicial `3`, entero entre 0 y 31. Se conserva el valor existente al actualizar. No confirma vínculos ni habilita diferencias de importe.

`CONCILIACION-TOLERANCIA` (grupo `CAJA`): margen para sugerencias exactas y múltiples, con valor inicial `1.00` y punto decimal. Se conserva el valor existente durante la actualización. No habilita ajustes contables automáticos. Ver [documentación del módulo](modulos/conciliacion-bancaria.md).

## ⚠️ Regla crítica del sistema

La tabla `TA_CONFIGURACION` es el **repositorio único de configuración del sistema**.

👉 TODO parámetro configurable debe registrarse en esta tabla.  
👉 NO se deben hardcodear valores en código.  
👉 NO se deben crear tablas paralelas de configuración sin justificación.

## ⛔ Regla absoluta: nunca vaciar `TA_CONFIGURACION`

`TA_CONFIGURACION` funciona como el **registro/configuración central de Alfa Gestión y AlfaCore**.

Nunca debe incluirse en scripts de limpieza, reinicio, depuración, pruebas, vaciado de colas, borrado masivo o mantenimiento general.

Prohibido ejecutar sobre esta tabla, salvo pedido explícito y acotado sobre claves concretas:

- `DELETE FROM TA_CONFIGURACION`
- `TRUNCATE TABLE TA_CONFIGURACION`
- borrados por `GRUPO` completo
- updates masivos sin `WHERE CLAVE = ...`
- cualquier limpieza automática que la trate como tabla operativa

Si una tarea necesita limpiar datos de un módulo, se deben identificar únicamente sus tablas operativas. `TA_CONFIGURACION` solo puede modificarse clave por clave, con objetivo claro y verificable.

---

## 🧱 Estructura de la tabla

```sql
TA_CONFIGURACION
```

| Campo                   | tipo | Descripción |
|------------------------|------------|
| GRUPO                  | [nvarchar](50) NOT NULL| Agrupación funcional (COMPRAS, VENTAS, DATOS, CONVERSACIONES, etc.) |
| CLAVE                  | [nvarchar](50) NOT NULL| Identificador único de configuración (NO se repite) |
| VALOR                  | [nvarchar](150) NULL|Valor principal (SI/NO, texto corto, código, etc.) |
| ValorAux               | [ntext] NULL|Valor extendido (JSON, texto largo, configuraciones complejas) |
| DESCRIPCION            | [nvarchar](50) NULL| Descripción funcional para el usuario |
| FechaHora_Grabacion    |  [datetime] NULL| Auditoría |
| FechaHora_Modificacion |  [datetime] NULL| Auditoría |

---

## 🧠 Concepto de diseño

La combinación:

GRUPO + CLAVE

es la clave primaria lógica del sistema de configuración.

👉 El sistema SIEMPRE debe buscar configuraciones por `CLAVE`.

---

## 📌 Reglas de uso obligatorias

### 1. Uso de CLAVE

- Debe ser única en todo el sistema
- Debe ser descriptiva
- Debe seguir una convención clara

Ejemplos:

CUENTA-CAJA  
WHATSAPP-TOKEN  
CONVERSACIONES-AUTOASIGNACION  
STOCK-CONTROL-NEGATIVO  

---

### 2. Uso de GRUPO

COMPRAS  
VENTAS  
STOCK  
CAJA  
CONTABILIDAD  
CONVERSACIONES  
SISTEMA  
USUARIOS  

IMPORTANTE: El GRUPO es organizativo, NO es clave de búsqueda principal.
---

### 3. Uso de VALOR
Para valores simples:
SI / NO  
CODIGOS  
NUMEROS  
TEXTOS CORTOS  

Ejemplo: CLAVE: CONVERSACIONES-HABILITADO VALOR: SI

SI el valor a grabar supera los 150 caracteres (maximo para el campo valor) debe dejar valor en blanco y grabar en valor_aux
al leer si valor esta vacio y valor_aux tiene datos debe tomar valor_aux
---

### 4. Uso de ValorAux

Para configuraciones complejas: 
JSON Texto largo 
Estructuras avanzadas 
Ejemplo: { "timeout": 30, "autoAsignacion": true, "usuarios": [1,2,3] }

SI el valor a grabar supera los 150 caracteres (maximo para el campo valor) debe dejar valor en blanco y grabar en valor_aux
al leer si valor esta vacio y valor_aux tiene datos debe tomar valor_aux
---

### 5. Configuración por usuario
Cuando la configuración es específica de usuario:
Se debe usar esta convención en CLAVE:
USUARIO_{ID}_{CLAVE}

Ejemplo:
USUARIO_5_CUENTA-CAJA

Ejemplo: USUARIO_5_CUENTA-CAJA USUARIO_3_CONVERSACIONES-FILTRO
Alternativa válida (si ya la usás): USUARIO_CUENTA-CAJA 
(Pero la recomendación es incluir ID para evitar ambigüedad)
---
### 6. Descripción 
Debe explicar claramente: 
Qué hace la configuración Para qué sirve 
Ejemplo: "Define si el módulo de conversaciones está habilitado"

---

## 🚫 Anti-patrones

- Hardcodear valores
- Duplicar claves
- Crear tablas paralelas

---
## Uso en desarrollo (obligatorio)
Antes de agregar lógica configurable: 
Verificar si ya existe la CLAVE 
Si no existe → crear en TA_CONFIGURACION 
Documentar en este archivo si es relevante 
Usar siempre acceso centralizado (service/helper)

Ejemplos reales 
Habilitar módulo 
GRUPO: CONVERSACIONES 
CLAVE: CONVERSACIONES-HABILITADO 
VALOR: SI 

Carga de viajes
GRUPO: VIAJES
CLAVE: VIAJES-PORCENTAJES-ADICIONALES-HABILITADOS
VALOR: 3
DESCRIPCION: Cantidad de porcentajes adicionales habilitados en carga de viajes

Login central multi-cliente
ConnectionStrings:AlfaCentral
Debe apuntar a la base ALFA_CENTRAL y contiene users, Clientes y bases.
El selector de bases del sistema toma la conexión activa desde esa estructura.

Token WhatsApp 
GRUPO: CONVERSACIONES 
CLAVE: WHATSAPP-TOKEN 
VALOR: (vacío) 
ValorAux: token largo Cuenta por usuario 
GRUPO: CAJA 
CLAVE: USUARIO_5_CUENTA-CAJA 
VALOR: 110101

## Beneficio 
Permite que el sistema sea: 
Configurable sin deploy 
Escalable 
Multiempresa 
Adaptable a distintos clientes

### Decisión arquitectónica
Esta tabla funciona como: 
Feature flags 
Configuración funcional 
Parametrización por cliente 
Parametrización por usuario 
Integración con APIs externas

---
## 🔒 Regla final

👉 Si algo puede configurarse, debe vivir en TA_CONFIGURACION
