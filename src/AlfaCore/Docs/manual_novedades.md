# Novedades y Comunicados

## Dónde se ven

- **Campana (barra superior):** cuando hay algo nuevo aparece con un punto azul. Al tocarlo se
  abre completo.
- **Página Novedades:** desde la campana, **Ver todas las novedades**. Tiene dos secciones:
  - **Novedades de AlfaCore:** lo nuevo de cada versión del sistema. Las escribe el equipo de
    AlfaCore y son las mismas para todas las empresas.
  - **Comunicados:** los avisos que publica tu empresa para su equipo (solo aparece si hay alguno).

Cada novedad se abre y se cierra tocando su título, y hay botones para **Abrir todas** y
**Cerrar todas**. Abrir una la marca como leída: deja de aparecer en la campana, pero queda guardada
en la página.

## Escribir un comunicado (Informes → Comunicados)

1. **Nuevo comunicado** crea un borrador con un ejemplo.
2. La lista está separada por pestañas: **Borradores**, **Programados**, **Publicados** y
   **Archivados**, cada una con su cantidad.
3. En **Detalles** se escribe el contenido. Los botones de arriba y el menú de comandos (⋮) suman
   títulos, listas, llamados (información, éxito, atención), imágenes, botones y dos bloques más:
   - **Antes / Después:** dos columnas para mostrar cómo era y cómo queda.
   - **Tarjetas con ícono:** tres puntos destacados, cada uno con su ícono.
4. En **Ajustes** van el título, una etiqueta opcional, el color y el ícono.
5. **Publicar ahora** (o **Programar** para una fecha): todo el equipo recibe el aviso en la campana.

Un comunicado publicado muestra el aviso "Ya está publicado": lo que se guarde se actualiza
enseguida para todos. **Vista previa** muestra cómo lo va a ver el usuario.

## Novedades de AlfaCore (para el equipo de desarrollo)

No se cargan desde la pantalla. Cada versión suma una entrada en `NovedadesSistemaCatalogo`
(`src/AlfaCore/Models/NovedadesSistemaModels.cs`) y su contenido en
`src/AlfaCore/App_Data/novedades-sistema/` (HTML con los mismos bloques del editor; capturas en
`wwwroot/img/novedades/`). Llegan a todas las empresas con el deploy.
