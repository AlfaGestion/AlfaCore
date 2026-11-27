# MCP de consulta (AlfaCore.Mcp) — hoja de ruta

Estado y próximos pasos del servidor MCP (`src/AlfaCore.Mcp`, esquema SQL `dir`).
Publicación: `docs/modulos/mcp-publicacion-iis.md`. Scripts: `docs/base-datos/sql-referencia/dir-mcp/`.

## 1. Terminar lo encaminado (en curso)

1. Correr `004_config_ia_mcp.sql` (clave `IA_MCP_LECTURA_SIN_BLOQUEOS`) y `012_config_oauth.sql` en la base de datos.
2. Probar el login local con usuario + clave + código TOTP (README de `src/AlfaCore.Mcp`, sección 3).
3. Correr `010` y `011`, crear usuarios con `crear-usuario --qr` y publicar en el IIS de ALFACENTRAL.
4. Conectar claude.ai y ChatGPT; confirmar la redirect URI real de ChatGPT.

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
