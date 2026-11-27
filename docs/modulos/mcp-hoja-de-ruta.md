# MCP de consulta (AlfaCore.Mcp) — hoja de ruta

Estado y próximos pasos del servidor MCP (`src/AlfaCore.Mcp`, esquema SQL `dir`).
Publicación: `docs/modulos/mcp-publicacion-iis.md`. Base: esquema `dir` en `App_Data/updates/2026-11-27-040__ia_mcp_esquema_dir.sql` (automático); `ALFA_CENTRAL` y logins: ver `docs/base-datos/sql-referencia/dir-mcp/README.md`.

## 1. Publicación (hecho el 27/11/2026)

1. ~~Scripts `004`, `010`, `011`, `012`~~, ~~login local con TOTP~~, ~~sitio IIS en `https://alfacore-mcp.ddns.net/mcp`~~, ~~claude.ai conectado~~.
2. Pendiente: conectar **ChatGPT** y confirmar su redirect URI real (paso 6 de la guía).
3. Pendiente: pasar de la base de **pruebas** a la real cuando se valide con datos conocidos (volver a correr `instalar_mcp_iis.ps1`).
4. Pendiente: correr `SqlAuthStoreIntegracionTests` contra el almacén real (variable `ALFACORE_MCP_TEST_AUTH_CONNECTION`).

## 2. Próxima etapa: herramientas puntuales

**Hecho (27/11/2026), sin scripts SQL nuevos (usan las vistas `dir` existentes):** `buscar_clientes`, `buscar_articulos`,
`ficha_cliente` (datos, deuda, compras 12 meses y comprobantes abiertos), `ficha_articulo` (precios, stock por depósito, ventas 12 meses),
filtros `cliente` / `articulo` en `ventas_resumen`, `ranking_articulos` y `clientes_top`, agrupación por artículo y por comprobante,
y `Title` legible en todas las herramientas.

Trampa hallada: el código **Consumidor Final** junta ventas con el nombre de cada comprador en la factura. `clientes_top` ahora muestra el
nombre del maestro y avisa cuántos nombres distintos agrupa; `buscar_clientes` busca en el maestro, así que a un particular facturado como
Consumidor Final no lo encuentra por su nombre.

Falta, en orden de valor (cada vista nueva va en un script nuevo de `App_Data/updates/`, con el patron del `2026-11-27-040`):

1. ~~**Compras y proveedores**~~: hecho el 27/11/2026 (vistas en la actualizacion `2026-11-27-040` + `buscar_proveedores`, `compras_resumen`, `deuda_proveedores`,
   `ficha_proveedor`, `compras_articulo`). Fuente fiscal = libro de IVA compras, no la cabecera (ver reglas en el README de `dir-mcp`).
2. **Cuenta corriente con movimientos** (no solo lo abierto) de clientes y luego de proveedores.
3. **Libros de IVA** ventas/compras y comparación mensual (se alinea con el control fiscal del Director IA).
4. **Cheques**: cartera de terceros y propios emitidos.
5. **Costo y precio de un artículo a una fecha**, y su historial.
6. Acelerar la cuenta corriente (hoy ~12 s por cliente: `VE_CPTES_SALDOS_VENTAS` calcula todos los saldos antes de filtrar).

Cada vista nueva: verificar el tipo real de cada objeto en SQL Server y cuadrar contra el reporte equivalente de Alfa Gestión.

## 3. Multi-base y permisos por usuario

**Hecho (27/11/2026):** multi-base con ingreso por mail. La base va atada a cada autorización y la resuelve
`ConexionPorBaseResolver` desde `ALFA_CENTRAL`. Tablas `MCP_BASE`/`MCP_ACCESO`/`MCP_CODIGO_EMAIL` (`2026-11-27-042__alfa_central_mcp_multibase.sql`) y comandos
`habilitar-base`/`invitar`/`quitar-acceso`/`accesos`. Falta:
- ~~Pantalla en `/admin` (AlfaCore)~~: hecha el 27/11/2026 (`CentralMcpService`). Bases → "Consultas IA" crea el login
  `mcp_ro_<idbase>` y Usuarios → "Consultas IA" asigna bases e invita. Clave compartida con el MCP por DPAPI de la máquina.
- Ocultar costos y márgenes a ciertos usuarios (permiso por herramienta en `MCP_ACCESO`).
- Pendiente de AlfaCore (no del MCP): las contraseñas de `ALFA_CENTRAL.users` están en texto plano, se muestran en `/admin`
  y en algunos clientes son el CUIT. Guardarlas con hash, no mostrarlas y forzar el cambio de la inicial.

## 4. Producto (vender a otros clientes de ALFA NET)

Decisión pendiente de arquitectura: hoy el MCP corre en ALFACENTRAL y llega a la base por la red interna.
Un cliente externo no va a publicar su SQL Server ni abrir puertos. Alternativa a evaluar: **agente local**
en el servidor del cliente que abre una conexión **saliente** hacia un servicio central (el MCP público),
que le reenvía las consultas. Implica: credencial por agente, revocación, separación estricta entre clientes
y control de vigencia de la suscripción (`ClienteModulos` / `Planes` en `ALFA_CENTRAL`).

## Referencia: comparación con SysWork AI Connector (2026-11-27)

Producto similar de un colega (syswork.com.ar/syswork-ai-connector). De ahí salen las ideas de las
secciones 2, 3 y 4. Lo que ya tenemos y ellos no muestran:

- Herramientas marcadas como **solo lectura** (`ReadOnly = true`): Claude no pide aprobación en cada consulta.
  En su conector aparecen como "escritura/eliminación — requiere aprobación".
- Auditoría de cada consulta en `IA_LOG_CONSULTAS`.
- Segundo factor TOTP propio (ellos: login con Google).
- Lectura sin bloqueos y prioridad baja ante deadlock para no frenar Alfa Gestión.

No se adopta el login con Google: para ≤10 usuarios alcanza usuario + TOTP y no depende de terceros.
