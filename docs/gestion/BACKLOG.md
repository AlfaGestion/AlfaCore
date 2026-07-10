# Backlog — Dashboard de Compras · Alfa Gestión

Ítems pendientes ordenados por prioridad. Mover a CHANGELOG.md cuando se implementen.

---

## Alta prioridad

- [x] **Sort numérico en tabla de resultado principal**: actualmente ordena como texto; columnas numéricas deberían ordenar por valor (igual que la tabla agrupada).
- [x] **PODER CONFIGURAR AGRUPAR Y TOTALIZAR AL CREAR O MODIFICAR UNA CONSULTA** 
- [x] CUANDO ABRO UNA CONSULTA, LUEGO HAGO CLIC EN EDITAR, AL ABRIR EL EDITOR DE CONSULTAS DEBERIA TRAER LA TABLA ACTUAL DE LA CONSULTA, LA MISMA DEL FROM
- [x] **Exportar tabla agrupada a Excel**: hoy el botón Excel descarga el resultado original, no el resultado del agrupador dinámico.

---

## Media prioridad

- [x] **Manual `docs/manual_consultas.md`**: copiar (o enlazar) el manual embebido a la carpeta raíz `docs/` para que sea legible directamente desde el repositorio.
- [ ] **Agrupador persistente entre ejecuciones**: opcionalmente conservar la configuración de agrupación al re-ejecutar con parámetros distintos.
- [x] **Diseñador de Consultas en el Launcher**: el badge "Próximamente" en la pantalla principal puede activarse ahora que el módulo está completo.
- [x] **Paginación en tabla agrupada**: si hay muchos grupos, mostrar paginación como en la tabla principal.
- [ ] **Navegación y pantallas derivadas para módulos de gestión**: desarrollar menús laterales, drill-down y páginas específicas para `Ventas`, `Stock`, `Caja y Bancos` y `Contabilidad`.
  Referencia: `docs/issue_modulos_gestion_navegacion.md`

---

## Baja prioridad / Ideas

- [ ] Guardar consultas favoritas por usuario (localStorage).
- [ ] Columna de última ejecución en el árbol de consultas.
- [ ] Vista previa del SQL al pasar el cursor por un nodo del árbol.
- [ ] Vistas SQL resumidas por módulo para performance (`ventas mensual`, `stock valorizado mensual`, `caja/bancos`, `contabilidad`).
