# Modelo de Configuración Central - TA_CONFIGURACION

## ⚠️ Regla crítica del sistema

La tabla `TA_CONFIGURACION` es el **repositorio único de configuración del sistema**.

👉 TODO parámetro configurable debe registrarse en esta tabla.  
👉 NO se deben hardcodear valores en código.  
👉 NO se deben crear tablas paralelas de configuración sin justificación.

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
