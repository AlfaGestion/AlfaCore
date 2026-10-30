/*
 * AlfaCore - Chat del sitio web (canal WEBCHAT de Conversaciones).
 *
 * Uso (lo genera Configuración de Conversaciones > Canales > Chat del sitio web):
 *   <script src="https://<alfacore>/webchat/widget.js" data-site="AC-..." data-contexto="opcional" defer></script>
 *
 * Atributos opcionales:
 *   data-modo="ventana"  el chat ocupa toda la ventana y arranca abierto, sin burbuja ni boton de
 *                        cerrar (lo usa /webchat/soporte, que abre Alfa Gestion desde su menu).
 *   data-nombre="..."    nombre precargado del visitante (si no escribio otro antes).
 *
 * Vanilla JS sin dependencias y encapsulado en Shadow DOM para no chocar con el tema/plugins del
 * sitio (WordPress u otro). Todo el texto se pinta con textContent: nunca se inyecta HTML.
 */
(function () {
    "use strict";

    var script = document.currentScript;
    if (!script || window.__alfaCoreWebChatLoaded) return;
    window.__alfaCoreWebChatLoaded = true;

    var siteKey = (script.getAttribute("data-site") || "").trim();
    if (!/^AC-[a-f0-9]{32}$/.test(siteKey)) return;

    var contexto = (script.getAttribute("data-contexto") || "").trim().slice(0, 200);
    var modoVentana = (script.getAttribute("data-modo") || "").trim().toLowerCase() === "ventana";
    var nombreInicial = (script.getAttribute("data-nombre") || "").trim().slice(0, 80);
    var apiBase = new URL(script.src, location.href).origin + "/api/webchat/" + encodeURIComponent(siteKey);

    var POLL_ACTIVE_MS = 3500;
    var IMAGEN_MAX_BYTES = 5 * 1024 * 1024;   // mismo límite que el servidor
    var IMAGEN_LADO_MAX = 1920;               // fotos más grandes se achican antes de enviar
    var IMAGEN_REDUCIR_DESDE = 1536 * 1024;   // o si pesan más que esto
    var imagenesCache = {};                   // idAdjunto -> object URL (no se vuelve a pedir)
    var AUDIO_MAX_SEGUNDOS = 120;             // la grabación corta sola a los 2 minutos
    var grabacion = null;                     // { recorder, stream, chunks, inicio, timer, cancelada }
    var STORAGE_VISITOR = "alfacore-webchat-visitor";
    var STORAGE_NAME = "alfacore-webchat-nombre";

    // sessionStorage: la charla se mantiene al navegar por el sitio o recargar, y se descarta al
    // cerrar la pestaña/navegador (la próxima visita empieza de cero). Si no está disponible (modo
    // privado, bloqueo de cookies) se sigue con un id en memoria.
    function storageGet(key) { try { return window.sessionStorage.getItem(key); } catch (e) { return null; } }
    function storageSet(key, value) { try { window.sessionStorage.setItem(key, value); } catch (e) { /* sin persistencia */ } }

    // Versiones anteriores guardaban la charla en localStorage (sin vencimiento): se limpia.
    try {
        window.localStorage.removeItem(STORAGE_VISITOR);
        window.localStorage.removeItem(STORAGE_NAME);
    } catch (e) { /* sin acceso a storage */ }

    function randomId(length) {
        var chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var out = "";
        var bytes = new Uint8Array(length);
        (window.crypto || window.msCrypto).getRandomValues(bytes);
        for (var i = 0; i < length; i++) out += chars[bytes[i] % chars.length];
        return out;
    }

    var visitorId = storageGet(STORAGE_VISITOR);
    if (!visitorId || !/^[A-Za-z0-9_-]{8,64}$/.test(visitorId)) {
        visitorId = randomId(24);
        storageSet(STORAGE_VISITOR, visitorId);
    }

    var state = {
        generation: 0, // cambia con "Nueva conversación": descarta respuestas de pedidos de la charla anterior
        open: false,
        cursor: 0,
        historyLoaded: false,
        pollTimer: null,
        polling: false,
        renderedIds: {},
        pending: [] // mensajes propios enviados y todavía sin IdMensaje del servidor
    };

    var ui = {};

    fetch(apiBase + "/config", { method: "GET", mode: "cors", credentials: "omit" })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (config) { if (config) render(config); })
        .catch(function () { /* canal inactivo, dominio no permitido o servidor caído: no se muestra nada */ });

    function render(config) {
        var color = /^#[0-9a-fA-F]{6}$/.test(config.color || "") ? config.color : "#0ea5e9";

        var host = document.createElement("div");
        host.setAttribute("data-alfacore-webchat", "");
        host.style.cssText = modoVentana
            ? "position:fixed;z-index:2147483000;inset:0;"
            : "position:fixed;z-index:2147483000;right:0;bottom:0;width:0;height:0;";
        var root = host.attachShadow ? host.attachShadow({ mode: "open" }) : host;

        var style = document.createElement("style");
        style.textContent = buildCss(color) + (modoVentana ? buildCssVentana() : "");
        root.appendChild(style);

        var bubble = el("button", "ac-bubble");
        bubble.type = "button";
        bubble.setAttribute("aria-label", "Abrir chat");
        bubble.appendChild(svgIcon("M4 4h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H8l-4 4V6a2 2 0 0 1 2-2z"));
        bubble.addEventListener("click", toggle);

        var panel = el("section", "ac-panel");
        panel.setAttribute("role", "dialog");
        panel.setAttribute("aria-label", config.titulo || "Chat");
        panel.hidden = true;

        var header = el("header", "ac-header");
        var title = el("strong", "ac-title");
        title.textContent = config.titulo || "¿Te ayudamos?";
        var close = el("button", "ac-close");
        close.type = "button";
        close.setAttribute("aria-label", "Cerrar chat");
        close.textContent = "×";
        close.addEventListener("click", toggle);
        var restart = el("button", "ac-restart");
        restart.type = "button";
        restart.title = "Nueva conversación";
        restart.setAttribute("aria-label", "Nueva conversación");
        restart.appendChild(svgIcon("M17.65 6.35A8 8 0 1 0 19.73 14h-2.08A6 6 0 1 1 12 6a5.9 5.9 0 0 1 4.22 1.78L13 11h7V4z"));
        restart.addEventListener("click", nuevaConversacion);
        var actions = el("div", "ac-actions");
        actions.appendChild(restart);
        if (!modoVentana) actions.appendChild(close);
        header.appendChild(title);
        header.appendChild(actions);

        var list = el("div", "ac-list");
        list.setAttribute("aria-live", "polite");

        if (config.mensajeBienvenida) appendMessage(list, { texto: config.mensajeBienvenida, esVisitante: false });

        var nameRow = el("div", "ac-name");
        var nameInput = el("input", "ac-name-input");
        nameInput.type = "text";
        nameInput.maxLength = 80;
        nameInput.placeholder = "Tu nombre (opcional)";
        nameInput.value = storageGet(STORAGE_NAME) || nombreInicial;
        nameInput.addEventListener("change", function () { storageSet(STORAGE_NAME, nameInput.value.trim()); });
        nameRow.appendChild(nameInput);

        var form = el("form", "ac-form");
        var input = el("textarea", "ac-input");
        input.rows = 1;
        input.maxLength = 2000;
        input.placeholder = "Escribí tu consulta…";
        input.setAttribute("aria-label", "Mensaje");
        input.addEventListener("keydown", function (e) {
            if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); submit(); }
        });
        var send = el("button", "ac-send");
        send.type = "submit";
        send.setAttribute("aria-label", "Enviar");
        send.appendChild(svgIcon("M3 20l18-8L3 4v6l12 2-12 2z"));
        if (config.permiteImagenes) {
            var fileInput = el("input", "ac-file");
            fileInput.type = "file";
            fileInput.accept = "image/png,image/jpeg,image/webp,image/gif";
            fileInput.hidden = true;
            fileInput.addEventListener("change", function () {
                if (fileInput.files && fileInput.files[0]) enviarImagen(fileInput.files[0]);
                fileInput.value = "";
            });
            var attach = el("button", "ac-attach");
            attach.type = "button";
            attach.title = "Enviar una imagen (también podés pegar una captura con Ctrl+V)";
            attach.setAttribute("aria-label", "Enviar una imagen");
            attach.appendChild(svgIcon("M16.5 6v11.5a4 4 0 0 1-8 0V5a2.5 2.5 0 0 1 5 0v10.5a1 1 0 0 1-2 0V6H10v9.5a2.5 2.5 0 0 0 5 0V5a4 4 0 0 0-8 0v12.5a5.5 5.5 0 0 0 11 0V6z"));
            attach.addEventListener("click", function () { fileInput.click(); });
            form.appendChild(fileInput);
            form.appendChild(attach);
            // Ctrl+V de una captura: se manda la imagen; si lo pegado es texto, sigue el pegado normal.
            input.addEventListener("paste", function (e) {
                var items = (e.clipboardData && e.clipboardData.items) || [];
                for (var i = 0; i < items.length; i++) {
                    if (items[i].kind === "file" && /^image\//.test(items[i].type)) {
                        var f = items[i].getAsFile();
                        if (f) { e.preventDefault(); enviarImagen(f); return; }
                    }
                }
            });
        }
        var puedeGrabar = config.permiteAudios && window.MediaRecorder
            && navigator.mediaDevices && navigator.mediaDevices.getUserMedia;
        var mic = null, micCancel = null, micTime = null;
        if (puedeGrabar) {
            mic = el("button", "ac-mic");
            mic.type = "button";
            mic.title = "Grabar una nota de voz";
            mic.setAttribute("aria-label", "Grabar una nota de voz");
            mic.appendChild(svgIcon("M12 14a3 3 0 0 0 3-3V5a3 3 0 0 0-6 0v6a3 3 0 0 0 3 3zm5-3a5 5 0 0 1-10 0H5a7 7 0 0 0 6 6.92V21h2v-3.08A7 7 0 0 0 19 11z"));
            mic.addEventListener("click", function () { if (grabacion) detenerGrabacion(false); else iniciarGrabacion(); });
            micCancel = el("button", "ac-mic-cancel");
            micCancel.type = "button";
            micCancel.title = "Descartar la grabación";
            micCancel.setAttribute("aria-label", "Descartar la grabación");
            micCancel.textContent = "×";
            micCancel.hidden = true;
            micCancel.addEventListener("click", function () { detenerGrabacion(true); });
            micTime = el("span", "ac-mic-time");
            micTime.hidden = true;
        }
        // Emojis (2026-10-29): boton que abre una franja con los mas usados; el elegido se inserta
        // donde esta el cursor. Son caracteres de texto: viajan como cualquier mensaje.
        var emojiPanel = el("div", "ac-emojis");
        emojiPanel.hidden = true;
        emojiPanel.setAttribute("role", "group");
        emojiPanel.setAttribute("aria-label", "Emojis");
        EMOJIS.forEach(function (em) {
            var op = el("button", "ac-emoji-op");
            op.type = "button";
            op.textContent = em;
            op.setAttribute("aria-label", em);
            op.addEventListener("click", function () { insertarEnInput(input, em); });
            emojiPanel.appendChild(op);
        });
        var emojiBtn = el("button", "ac-emoji");
        emojiBtn.type = "button";
        emojiBtn.title = "Emojis";
        emojiBtn.setAttribute("aria-label", "Agregar un emoji");
        emojiBtn.setAttribute("aria-expanded", "false");
        emojiBtn.appendChild(svgIcon("M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm3.5-9c.83 0 1.5-.67 1.5-1.5S16.33 8 15.5 8 14 8.67 14 9.5s.67 1.5 1.5 1.5zm-7 0c.83 0 1.5-.67 1.5-1.5S9.33 8 8.5 8 7 8.67 7 9.5 7.67 11 8.5 11zm3.5 6.5c2.33 0 4.31-1.46 5.11-3.5H6.89c.8 2.04 2.78 3.5 5.11 3.5z"));
        emojiBtn.addEventListener("click", function () {
            emojiPanel.hidden = !emojiPanel.hidden;
            emojiBtn.setAttribute("aria-expanded", emojiPanel.hidden ? "false" : "true");
            if (emojiPanel.hidden) input.focus();
        });
        form.appendChild(emojiBtn);
        form.appendChild(input);
        if (mic) {
            form.appendChild(micTime);
            form.appendChild(micCancel);
            form.appendChild(mic);
        }
        form.appendChild(send);
        form.addEventListener("submit", function (e) { e.preventDefault(); submit(); });

        var status = el("div", "ac-status");
        status.hidden = true;

        panel.appendChild(header);
        panel.appendChild(list);
        panel.appendChild(status);
        panel.appendChild(nameRow);
        panel.appendChild(emojiPanel);
        panel.appendChild(form);
        root.appendChild(panel);
        if (!modoVentana) root.appendChild(bubble);
        document.body.appendChild(host);

        ui = { bubble: bubble, panel: panel, list: list, input: input, nameInput: nameInput, status: status, bienvenida: config.mensajeBienvenida || "",
               emojiPanel: emojiPanel, emojiBtn: emojiBtn,
               mic: mic, micCancel: micCancel, micTime: micTime };

        if (!modoVentana) panel.addEventListener("keydown", function (e) { if (e.key === "Escape") toggle(); });
        document.addEventListener("visibilitychange", schedulePoll);
        if (modoVentana) toggle();
    }

    // Empieza una charla de cero: visitorId nuevo (en la bandeja queda como otra conversación; la
    // anterior se conserva para el equipo) y panel limpio con el saludo.
    function nuevaConversacion() {
        stopPoll();
        state.generation++;
        state.cursor = 0;
        state.historyLoaded = false;
        state.polling = false;
        state.renderedIds = {};
        state.pending = [];
        visitorId = randomId(24);
        storageSet(STORAGE_VISITOR, visitorId);
        while (ui.list.firstChild) ui.list.removeChild(ui.list.firstChild);
        if (ui.bienvenida) appendMessage(ui.list, { texto: ui.bienvenida, esVisitante: false });
        showStatus("");
        ui.input.value = "";
        ui.input.focus();
        schedulePoll();
    }

    function toggle() {
        state.open = !state.open;
        ui.panel.hidden = !state.open;
        ui.bubble.setAttribute("aria-label", state.open ? "Cerrar chat" : "Abrir chat");
        if (state.open) {
            ui.input.focus();
            poll();
        } else {
            stopPoll();
            if (!modoVentana) ui.bubble.focus();
        }
    }

    function submit() {
        // grabando una nota de voz: la flecha de enviar (o Enter) la manda
        if (grabacion) { detenerGrabacion(false); return; }
        var texto = ui.input.value.trim();
        if (!texto) return;
        ui.input.value = "";
        if (ui.emojiPanel && !ui.emojiPanel.hidden) { ui.emojiPanel.hidden = true; ui.emojiBtn.setAttribute("aria-expanded", "false"); }
        var item = { clientMessageId: randomId(20), texto: texto, node: null, visitorId: visitorId, generation: state.generation };
        item.node = appendMessage(ui.list, { texto: texto, esVisitante: true, pendiente: true });
        state.pending.push(item);
        postMessage(item);
    }

    function postMessage(item) {
        item.node.classList.add("ac-msg--pending");
        item.node.classList.remove("ac-msg--error");
        showStatus("");

        var nombre = ui.nameInput.value.trim();
        if (nombre) storageSet(STORAGE_NAME, nombre);

        var payload = {
            visitorId: item.visitorId,
            clientMessageId: item.clientMessageId,
            texto: item.texto,
            nombre: nombre || null,
            paginaUrl: location.href,
            contexto: contexto || null
        };
        if (item.tipoArchivo === "imagen") payload.imagenBase64 = item.base64;
        if (item.tipoArchivo === "audio") payload.audioBase64 = item.base64;
        if (item.tipoArchivo) payload.nombreArchivo = item.nombreArchivo;
        var destino = item.tipoArchivo === "imagen" ? "/imagenes" : item.tipoArchivo === "audio" ? "/audios" : "/mensajes";
        fetch(apiBase + destino, {
            method: "POST",
            mode: "cors",
            credentials: "omit",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        }).then(function (r) {
            if (r.ok) return r.json();
            // el servidor explica por qué rechazó la imagen (formato, tamaño...)
            return r.json().catch(function () { return {}; }).then(function (b) {
                throw { status: r.status, error: b && b.error };
            });
        }).then(function (result) {
            if (item.generation !== state.generation) return; // la charla ya se reinició
            item.node.classList.remove("ac-msg--pending");
            resolvePending(item, result.idMensaje);
            poll();
        }).catch(function (fallo) {
            var statusCode = fallo && fallo.status !== undefined ? fallo.status : fallo;
            if (item.generation !== state.generation) return;
            item.node.classList.remove("ac-msg--pending");
            item.node.classList.add("ac-msg--error");
            item.node.title = "Tocá para reintentar";
            item.node.onclick = function () { item.node.onclick = null; postMessage(item); };
            if (item.tipoArchivo && fallo && fallo.error && statusCode !== 429) {
                // formato o tamaño rechazado: reintentar no cambia nada
                item.node.onclick = null;
                item.node.title = "";
                state.pending = state.pending.filter(function (p) { return p !== item; });
                showStatus(fallo.error);
                return;
            }
            showStatus(statusCode === 429
                ? "Estás enviando muchos mensajes seguidos. Esperá un momento y tocá el mensaje para reintentar."
                : "No se pudo enviar. Tocá el mensaje para reintentar.");
        });
    }

    function resolvePending(item, id) {
        if (id) {
            if (state.renderedIds[id] && state.renderedIds[id] !== item.node) {
                // El polling ya lo había pintado: se descarta la copia optimista.
                item.node.parentNode && item.node.parentNode.removeChild(item.node);
            } else {
                state.renderedIds[id] = item.node;
            }
        }
        state.pending = state.pending.filter(function (p) { return p !== item; });
    }

    function poll() {
        if (state.polling || !state.open) return;
        state.polling = true;
        stopPoll();

        var generation = state.generation;
        var url = apiBase + "/mensajes?visitorId=" + encodeURIComponent(visitorId) + "&since=" + state.cursor;
        fetch(url, { method: "GET", mode: "cors", credentials: "omit", cache: "no-store" })
            .then(function (r) {
                if (r.status === 404) {
                    showStatus("El chat no está disponible en este momento.");
                    return null;
                }
                return r.ok ? r.json() : null;
            })
            .then(function (data) {
                if (!data || generation !== state.generation) return;
                (data.mensajes || []).forEach(function (m) {
                    if (state.renderedIds[m.id]) return;
                    if (m.esVisitante) {
                        var tipoMsg = (m.audios && m.audios.length > 0) ? "audio"
                            : (m.imagenes && m.imagenes.length > 0) ? "imagen" : "";
                        var pending = state.pending.filter(function (p) {
                            return tipoMsg ? p.tipoArchivo === tipoMsg : (!p.tipoArchivo && p.texto === m.texto);
                        })[0];
                        if (pending) {
                            state.renderedIds[m.id] = pending.node;
                            return;
                        }
                    }
                    state.renderedIds[m.id] = appendMessage(ui.list, m);
                });
                if (data.cursor > state.cursor) state.cursor = data.cursor;
                state.historyLoaded = true;
            })
            .catch(function () { /* red intermitente: se reintenta en el próximo ciclo */ })
            .then(function () {
                if (generation !== state.generation) return; // nuevaConversacion ya reprogramó el polling
                state.polling = false;
                schedulePoll();
            });
    }

    function schedulePoll() {
        stopPoll();
        if (state.open && document.visibilityState !== "hidden") {
            state.pollTimer = setTimeout(poll, POLL_ACTIVE_MS);
        }
    }

    function stopPoll() {
        if (state.pollTimer) clearTimeout(state.pollTimer);
        state.pollTimer = null;
    }

    function appendMessage(list, m) {
        var node = el("div", "ac-msg " + (m.esVisitante ? "ac-msg--me" : "ac-msg--them"));
        if (m.imagenSrc) {
            node.appendChild(crearImagen(m.imagenSrc));
        }
        (m.imagenes || []).forEach(function (id) {
            var img = crearImagen("");
            node.appendChild(img);
            cargarImagen(id, img);
        });
        if (m.audioSrc) node.appendChild(crearAudio(m.audioSrc));
        (m.audios || []).forEach(function (id) {
            var audio = crearAudio("");
            node.appendChild(audio);
            cargarImagen(id, audio);   // mismo endpoint y la misma cache que las imágenes
        });
        if (m.texto) {
            var txt = el("div", "ac-text");
            txt.textContent = m.texto;
            node.appendChild(txt);
        }
        list.appendChild(node);
        list.scrollTop = list.scrollHeight;
        return node;
    }

    function crearImagen(src) {
        var img = el("img", "ac-img");
        img.alt = "Imagen";
        if (src) img.src = src;
        img.addEventListener("load", function () { if (ui.list) ui.list.scrollTop = ui.list.scrollHeight; });
        img.addEventListener("click", function () { if (img.src) window.open(img.src, "_blank"); });
        return img;
    }

    function crearAudio(src) {
        var audio = el("audio", "ac-audio");
        audio.controls = true;
        audio.preload = "metadata";
        if (src) audio.src = src;
        return audio;
    }

    // ---- Notas de voz ----
    function formatoGrabacion() {
        var tipos = ["audio/webm;codecs=opus", "audio/webm", "audio/ogg;codecs=opus", "audio/mp4"];
        for (var i = 0; i < tipos.length; i++) {
            if (MediaRecorder.isTypeSupported && MediaRecorder.isTypeSupported(tipos[i])) return tipos[i];
        }
        return "";
    }

    function mostrarGrabando(activo) {
        ui.mic.classList.toggle("is-recording", activo);
        ui.mic.title = activo ? "Enviar la nota de voz" : "Grabar una nota de voz";
        ui.mic.setAttribute("aria-label", ui.mic.title);
        ui.micCancel.hidden = !activo;
        ui.micTime.hidden = !activo;
        ui.input.hidden = activo;
        ui.micTime.textContent = "0:00";
    }

    function iniciarGrabacion() {
        showStatus("");
        navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
            var tipo = formatoGrabacion();
            var recorder;
            try {
                recorder = tipo ? new MediaRecorder(stream, { mimeType: tipo }) : new MediaRecorder(stream);
            } catch (e) {
                stream.getTracks().forEach(function (tr) { tr.stop(); });
                showStatus("Este navegador no puede grabar audio.");
                return;
            }
            grabacion = { recorder: recorder, stream: stream, chunks: [], inicio: Date.now(), cancelada: false };
            recorder.addEventListener("dataavailable", function (e) { if (e.data && e.data.size) grabacion.chunks.push(e.data); });
            recorder.addEventListener("stop", finGrabacion);
            recorder.start();
            mostrarGrabando(true);
            grabacion.timer = setInterval(function () {
                var seg = Math.floor((Date.now() - grabacion.inicio) / 1000);
                ui.micTime.textContent = Math.floor(seg / 60) + ":" + ("0" + (seg % 60)).slice(-2);
                if (seg >= AUDIO_MAX_SEGUNDOS) detenerGrabacion(false);
            }, 250);
        }).catch(function () {
            showStatus("No se pudo usar el micrófono. Revisá el permiso del navegador.");
        });
    }

    function detenerGrabacion(cancelar) {
        if (!grabacion) return;
        grabacion.cancelada = cancelar;
        clearInterval(grabacion.timer);
        if (grabacion.recorder.state !== "inactive") grabacion.recorder.stop();
        else finGrabacion();
    }

    function finGrabacion() {
        var g = grabacion;
        grabacion = null;
        if (!g) return;
        g.stream.getTracks().forEach(function (tr) { tr.stop(); });
        mostrarGrabando(false);
        ui.input.focus();
        if (g.cancelada || !g.chunks.length) return;
        if (Date.now() - g.inicio < 700) {
            showStatus("La grabación fue muy corta.");
            return;
        }
        var blob = new Blob(g.chunks, { type: g.recorder.mimeType || "audio/webm" });
        if (blob.size > IMAGEN_MAX_BYTES) {
            showStatus("La nota de voz es muy larga.");
            return;
        }
        enviarArchivo(blob, "audio", "nota-de-voz");
    }

    // La imagen se pide con fetch (lleva el Origin que controla la lista de dominios) y se muestra
    // como object URL; queda en memoria para no pedirla de nuevo.
    function cargarImagen(id, img) {
        if (imagenesCache[id]) { img.src = imagenesCache[id]; return; }
        var url = apiBase + "/adjuntos/" + encodeURIComponent(id) + "?visitorId=" + encodeURIComponent(visitorId);
        fetch(url, { method: "GET", mode: "cors", credentials: "omit" })
            .then(function (r) { if (!r.ok) throw r.status; return r.blob(); })
            .then(function (blob) {
                imagenesCache[id] = URL.createObjectURL(blob);
                img.src = imagenesCache[id];
            })
            .catch(function () { img.alt = "Imagen no disponible"; img.classList.add("ac-img--error"); });
    }

    function enviarImagen(file) {
        if (!/^image\/(png|jpeg|webp|gif)$/.test(file.type || "")) {
            showStatus("Formato no admitido: se aceptan JPG, PNG, GIF y WEBP.");
            return;
        }
        prepararImagen(file, function (blob) {
            if (!blob || blob.size > IMAGEN_MAX_BYTES) {
                showStatus("La imagen es muy grande (máximo 5 MB).");
                return;
            }
            enviarArchivo(blob, "imagen", file.name || "captura.png");
        });
    }

    // Imagen o nota de voz: se muestra al toque (pendiente) y se manda en base64.
    function enviarArchivo(blob, tipo, nombreArchivo) {
        var reader = new FileReader();
        reader.onload = function () {
            var item = {
                clientMessageId: randomId(20),
                texto: "",
                tipoArchivo: tipo,
                base64: String(reader.result || ""),
                nombreArchivo: nombreArchivo,
                node: null,
                visitorId: visitorId,
                generation: state.generation
            };
            var local = URL.createObjectURL(blob);
            item.node = appendMessage(ui.list, tipo === "audio"
                ? { texto: "", esVisitante: true, audioSrc: local }
                : { texto: "", esVisitante: true, imagenSrc: local });
            state.pending.push(item);
            postMessage(item);
        };
        reader.onerror = function () { showStatus("No se pudo leer el archivo."); };
        reader.readAsDataURL(blob);
    }

    // Fotos grandes (celular, cámara) se achican a IMAGEN_LADO_MAX y van como JPEG: suben más rápido.
    // Las capturas chicas y los GIF (pueden ser animados) se mandan tal cual.
    function prepararImagen(file, done) {
        if (file.type === "image/gif" || !window.createImageBitmap || !document.createElement("canvas").toBlob) {
            done(file);
            return;
        }
        createImageBitmap(file).then(function (bmp) {
            var lado = Math.max(bmp.width, bmp.height);
            if (lado <= IMAGEN_LADO_MAX && file.size <= IMAGEN_REDUCIR_DESDE) { done(file); return; }
            var escala = Math.min(1, IMAGEN_LADO_MAX / lado);
            var canvas = document.createElement("canvas");
            canvas.width = Math.round(bmp.width * escala);
            canvas.height = Math.round(bmp.height * escala);
            var ctx = canvas.getContext("2d");
            ctx.fillStyle = "#fff";   // las transparencias del PNG no quedan negras en el JPEG
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.drawImage(bmp, 0, 0, canvas.width, canvas.height);
            canvas.toBlob(function (b) { done(b || file); }, "image/jpeg", 0.85);
        }).catch(function () { done(file); });
    }

    function showStatus(text) {
        if (!ui.status) return;
        ui.status.textContent = text;
        ui.status.hidden = !text;
    }

    // Los mas usados en soporte. Escritos como \u para que se vean bien aunque la pagina del
    // cliente no sea UTF-8.
    var EMOJIS = ["\uD83D\uDE00", "\uD83D\uDE03", "\uD83D\uDE0A", "\uD83D\uDE42", "\uD83D\uDE09", "\uD83D\uDE0D", "\uD83D\uDE0E", "\uD83E\uDD14", "\uD83D\uDE05", "\uD83D\uDE02", "\uD83D\uDE4F", "\uD83D\uDC4D", "\uD83D\uDC4E", "\uD83D\uDC4F", "\uD83D\uDE4C", "\uD83D\uDCAA", "\uD83D\uDC4C", "\uD83E\uDD1D", "\uD83D\uDC4B", "\u2764\uFE0F", "\u2705", "\u274C", "\u26A0\uFE0F", "\u2753", "\u2757", "\uD83D\uDD25", "\u2B50", "\uD83C\uDF89", "\uD83D\uDCCE", "\uD83D\uDCDE", "\uD83D\uDCBB", "\uD83D\uDDA8\uFE0F", "\uD83D\uDCC4", "\uD83D\uDCB0", "\uD83D\uDD50", "\uD83D\uDE80"];

    // Inserta el texto donde esta el cursor (o reemplaza lo seleccionado), respetando el maximo.
    function insertarEnInput(input, texto) {
        var ini = typeof input.selectionStart === "number" ? input.selectionStart : input.value.length;
        var fin = typeof input.selectionEnd === "number" ? input.selectionEnd : ini;
        var nuevo = input.value.slice(0, ini) + texto + input.value.slice(fin);
        if (input.maxLength > 0 && nuevo.length > input.maxLength) return;
        input.value = nuevo;
        var pos = ini + texto.length;
        input.focus();
        try { input.setSelectionRange(pos, pos); } catch (e) { /* navegadores viejos */ }
    }

    function el(tag, className) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        return node;
    }

    function svgIcon(path) {
        var ns = "http://www.w3.org/2000/svg";
        var svg = document.createElementNS(ns, "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("aria-hidden", "true");
        var p = document.createElementNS(ns, "path");
        p.setAttribute("d", path);
        p.setAttribute("fill", "currentColor");
        svg.appendChild(p);
        return svg;
    }

    // Modo ventana: el panel ocupa toda la ventana (sin bordes redondeados ni sombra).
    function buildCssVentana() {
        return [
            ".ac-panel{position:fixed;inset:0;right:0;bottom:0;width:100%;max-width:none;height:100%;max-height:none;border-radius:0;box-shadow:none}",
            "@media (max-width:480px){.ac-panel{left:0;right:0;bottom:0;height:100%}}"
        ].join("");
    }

    function buildCss(color) {
        return [
            ":host{all:initial}",
            "*{box-sizing:border-box;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif}",
            ".ac-bubble{position:fixed;right:20px;bottom:20px;width:56px;height:56px;border-radius:50%;border:0;cursor:pointer;",
            "background:" + color + ";color:#fff;box-shadow:0 6px 20px rgba(0,0,0,.25);display:flex;align-items:center;justify-content:center}",
            ".ac-bubble svg{width:26px;height:26px}",
            ".ac-bubble:focus-visible,.ac-send:focus-visible,.ac-close:focus-visible{outline:3px solid #fff;outline-offset:2px}",
            ".ac-panel{position:fixed;right:20px;bottom:88px;width:360px;max-width:calc(100vw - 32px);height:520px;max-height:calc(100vh - 120px);",
            "background:#fff;color:#1f2937;border-radius:14px;box-shadow:0 12px 40px rgba(0,0,0,.28);display:flex;flex-direction:column;overflow:hidden}",
            ".ac-panel[hidden]{display:none}",
            ".ac-header{background:" + color + ";color:#fff;padding:14px 16px;display:flex;align-items:center;justify-content:space-between}",
            ".ac-title{font-size:15px;font-weight:600}",
            ".ac-close{background:transparent;border:0;color:#fff;font-size:24px;line-height:1;cursor:pointer;padding:0 4px}",
            ".ac-actions{display:flex;align-items:center;gap:6px}",
            ".ac-restart{background:transparent;border:0;color:#fff;cursor:pointer;padding:4px;display:flex;opacity:.85;border-radius:6px}",
            ".ac-restart:hover{opacity:1;background:rgba(255,255,255,.15)}",
            ".ac-restart svg{width:18px;height:18px}",
            ".ac-restart:focus-visible{outline:3px solid #fff;outline-offset:2px}",
            ".ac-list{flex:1;overflow-y:auto;padding:14px;display:flex;flex-direction:column;gap:8px;background:#f8fafc}",
            ".ac-msg{max-width:82%;padding:9px 12px;border-radius:12px;font-size:14px;line-height:1.4;white-space:pre-wrap;word-wrap:break-word}",
            ".ac-msg--them{align-self:flex-start;background:#fff;border:1px solid #e5e7eb}",
            ".ac-msg--me{align-self:flex-end;background:" + color + ";color:#fff}",
            ".ac-msg--pending{opacity:.6}",
            ".ac-img{display:block;max-width:100%;max-height:240px;border-radius:8px;cursor:zoom-in;background:#e5e7eb;min-width:60px;min-height:40px}",
            ".ac-img--error{cursor:default;min-height:0}",
            ".ac-msg .ac-img+.ac-text{margin-top:6px}",
            ".ac-attach{width:40px;height:40px;border-radius:50%;border:1px solid #d1d5db;background:#fff;color:#4b5563;cursor:pointer;display:flex;align-items:center;justify-content:center;flex:none}",
            ".ac-attach:hover{color:" + color + ";border-color:" + color + "}",
            ".ac-attach svg{width:18px;height:18px}",
            ".ac-emoji{width:40px;height:40px;border-radius:50%;border:1px solid #d1d5db;background:#fff;color:#4b5563;cursor:pointer;display:flex;align-items:center;justify-content:center;flex:none}",
            ".ac-emoji:hover,.ac-emoji[aria-expanded=true]{color:" + color + ";border-color:" + color + "}",
            ".ac-emoji svg{width:20px;height:20px}",
            ".ac-emojis{display:grid;grid-template-columns:repeat(auto-fill,minmax(36px,1fr));gap:2px;padding:8px 12px;border-top:1px solid #e5e7eb;max-height:128px;overflow-y:auto;background:#fff}",
            ".ac-emojis[hidden]{display:none}",
            ".ac-emoji-op{border:0;background:transparent;font-size:22px;line-height:1;height:36px;border-radius:8px;cursor:pointer;font-family:'Segoe UI Emoji','Apple Color Emoji','Noto Color Emoji',sans-serif}",
            ".ac-emoji-op:hover,.ac-emoji-op:focus-visible{background:#f1f5f9;outline:none}",
            ".ac-mic{width:40px;height:40px;border-radius:50%;border:1px solid #d1d5db;background:#fff;color:#4b5563;cursor:pointer;display:flex;align-items:center;justify-content:center;flex:none}",
            ".ac-mic:hover{color:" + color + ";border-color:" + color + "}",
            ".ac-mic svg{width:18px;height:18px}",
            ".ac-mic.is-recording{background:#dc2626;border-color:#dc2626;color:#fff;animation:ac-pulse 1.2s infinite}",
            "@keyframes ac-pulse{0%,100%{box-shadow:0 0 0 0 rgba(220,38,38,.45)}50%{box-shadow:0 0 0 6px rgba(220,38,38,0)}}",
            ".ac-mic-cancel{width:32px;height:40px;border:0;background:transparent;color:#6b7280;font-size:22px;cursor:pointer;flex:none}",
            ".ac-mic-cancel[hidden],.ac-mic-time[hidden],.ac-input[hidden]{display:none}",
            ".ac-mic-time{flex:1;align-self:center;font-size:14px;color:#dc2626;font-variant-numeric:tabular-nums;padding-left:4px}",
            ".ac-audio{display:block;width:240px;max-width:100%;height:40px}",
            ".ac-msg--error{background:#fee2e2;color:#991b1b;cursor:pointer}",
            ".ac-status{padding:6px 14px;font-size:12px;color:#991b1b;background:#fef2f2}",
            ".ac-status[hidden]{display:none}",
            ".ac-name{padding:8px 12px 0;border-top:1px solid #e5e7eb}",
            ".ac-name-input{width:100%;border:0;font-size:12px;color:#4b5563;padding:2px 0;background:transparent;outline:none}",
            ".ac-form{display:flex;gap:8px;padding:8px 12px 12px;align-items:flex-end}",
            ".ac-input{flex:1;resize:none;border:1px solid #d1d5db;border-radius:10px;padding:9px 10px;font-size:14px;max-height:120px;color:#1f2937;background:#fff}",
            ".ac-input:focus{outline:2px solid " + color + ";outline-offset:-1px}",
            ".ac-send{width:40px;height:40px;border-radius:50%;border:0;background:" + color + ";color:#fff;cursor:pointer;display:flex;align-items:center;justify-content:center;flex:none}",
            ".ac-send svg{width:18px;height:18px}",
            "@media (max-width:480px){.ac-panel{right:16px;left:16px;width:auto;max-width:none;bottom:84px;height:calc(100vh - 110px)}.ac-bubble{right:16px;bottom:16px}}"
        ].join("");
    }
})();
