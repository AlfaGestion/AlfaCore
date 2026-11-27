# MCP de consulta (AlfaCore.Mcp) — hoja de ruta

Estado y próximos pasos del servidor MCP (`src/AlfaCore.Mcp`, esquema SQL `dir`).
Publicación: `docs/modulos/mcp-publicacion-iis.md`. Scripts: `docs/base-datos/sql-referencia/dir-mcp/`.

## 1. Publicación (hecho el 27/11/2026)

1. ~~Scripts `004`, `010`, `011`, `012`~~, ~~login local con TOTP~~, ~~sitio IIS en `https://alfacore-mcp.ddns.net/mcp`~~, ~~claude.ai conectado~~.
2. Pendiente: conectar **ChatGPT** y confirmar su redirect URI real (paso 6 de la guía).
3. Pendiente: pasar de la base de **pruebas** a la real cuando se valide con datos conocidos (volver a correr `instalar_mcp_iis.ps1`).
4. Pendiente: correr `SqlAuthStoreIntegracionTests` contra el almacén real (variable `ALFACORE_MCP_TEST_AUTH_CONNECTION`).

## 2. Próxima etapa: herramientas puntuales

Hoy hay 6 herramientas de resumen + `consultar_sql`. Faltan herramientas que respondan una pregunta
concreta sin que la IA tenga que adivinar códigos o armar SQL. En orden de valor:

1. **Búsquedas por texto**: `buscar_clientes`, `buscar_articulos`, `buscar_proveedores` (devuelven código +
   nombre para que la IA resuelva "Pérez" antes de consultar).
2. **Cuenta corriente detallada de un cliente** (movimientos + saldo, paginada) y luego la de **proveedores**.
3. **Compras y proveedores**: vista `dir.vw_Compras` (hoy `dir` es solo ventas).
4. **Libros de IVA** ventas/compras y comparación mensual (se alinea con el control fiscal del Director IA).
5. **Cheques**: cartera de terceros y propios emitidos.
6. **Costo y precio de un artículo a una fecha**, y su historial.

Cada herramienta nueva requiere su vista `dir.*` (script nuevo en `dir-mcp/`, revisado y corrido por Alberto),
verificar el tipo real de cada objeto en SQL Server y cuadrar contra el reporte equivalente de Alfa Gestión.

Mejora menor: poner `Title` legible a cada herramienta (Claude/ChatGPT muestran el nombre técnico).

## 3. Permisos por usuario

- Ocultar costos y márgenes a ciertos usuarios (permiso por área o por herramienta, guardado en `MCP_USUARIO`
  o tabla hija).
- Con multi-base: cada usuario ve solo sus bases / unidades fiscales (`IConexionResolver` por usuario).

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
