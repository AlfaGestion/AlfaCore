# Manual - Chat del sitio web

Con el chat del sitio web, los visitantes de tu página (por ejemplo, un sitio WordPress) pueden
escribirte desde una burbuja en la esquina de la pantalla. Los mensajes llegan a **Conversaciones**,
junto con los de WhatsApp, Instagram, Facebook y Mercado Libre.

## Activarlo

1. Entrá a **Conversaciones → Configuración → Canales → Chat del sitio web**.
2. Tocá **Generar clave**. Se hace una sola vez por base.
3. Tildá **Activar chat del sitio web**.
4. Elegí el título, el color y el mensaje de bienvenida que ve el visitante.
5. En **Dominios permitidos** escribí el dominio de tu sitio (por ejemplo `misitio.com`). Así el chat
   solo funciona desde tu página. Si lo dejás vacío, funciona desde cualquier sitio que tenga el código.
6. Tocá **Guardar chat del sitio web**.
7. Tocá **Copiar código** y pegalo en tu sitio, antes de `</body>`. En WordPress podés usar un plugin
   para insertar código en el pie de página, o un bloque **HTML personalizado** dentro de una entrada.

## Distinguir desde qué página escriben

El código tiene un campo `data-contexto=""`. Si pegás el código en una entrada puntual, podés
completarlo con un texto corto que la identifique, por ejemplo:

```html
data-contexto="actualizacion-v3-2"
```

En la bandeja, la conversación muestra la página desde la que escribió el visitante y ese contexto.

## Atender las consultas

- Las conversaciones del sitio aparecen en la bandeja con el ícono de globo, en **Todos** o filtrando
  por **Chat del sitio web**.
- Si el **Asistente IA** está activo (Automatización), responde primero. Cuando no puede resolver la
  consulta, la deriva a un agente.
- Para responder vos, escribí normalmente en la conversación. El visitante ve tu respuesta en
  unos segundos, mientras tenga el chat abierto.

## Tené en cuenta

- El visitante no se identifica: aparece como "Visitante web" salvo que deje su nombre. Por eso el
  asistente no informa saldos ni pedidos en este canal.
- No se pueden enviar archivos adjuntos por este canal.
- Si el visitante cierra el navegador y vuelve desde el mismo navegador, ve la conversación anterior.
