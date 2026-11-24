// POS Salon: edicion del plano de mesas (arrastrar) e impresion de precuenta.
// Coordenadas virtuales: el plano mide 1000 x 600 unidades, independiente del tamano de pantalla.
(function () {
    "use strict";

    const PLANO_W = 1000;
    const PLANO_H = 600;

    function clamp(v, min, max) {
        return Math.max(min, Math.min(max, v));
    }

    function enableDrag(canvas) {
        if (!canvas || canvas.dataset.salonDrag === "1") {
            return;
        }
        canvas.dataset.salonDrag = "1";

        // El pointerup de un arrastre también genera un click sobre el botón Blazor.
        // Se cancela solo ese click cuando realmente hubo movimiento, no el toque simple
        // que debe abrir la edición de la mesa.
        canvas.addEventListener("click", function (ev) {
            const mesa = ev.target.closest("[data-mesa-id]");
            if (mesa && mesa.dataset.salonSuppressClick === "1") {
                mesa.dataset.salonSuppressClick = "0";
                ev.preventDefault();
                ev.stopPropagation();
            }
        }, true);

        let drag = null;

        canvas.addEventListener("pointerdown", function (ev) {
            if (canvas.dataset.salonEditing !== "1") {
                return;
            }
            const mesa = ev.target.closest("[data-mesa-id]");
            if (!mesa || !canvas.contains(mesa)) {
                return;
            }
            ev.preventDefault();
            const rect = canvas.getBoundingClientRect();
            const mesaRect = mesa.getBoundingClientRect();
            const resize = ev.target.closest("[data-mesa-resize]")
                || (ev.clientX >= mesaRect.right - 34 && ev.clientY >= mesaRect.bottom - 34);
            drag = {
                el: mesa,
                rect: rect,
                offsetX: ev.clientX - mesa.getBoundingClientRect().left,
                offsetY: ev.clientY - mesa.getBoundingClientRect().top,
                resize: !!resize,
                startWidth: mesaRect.width,
                startHeight: mesaRect.height,
                startX: ev.clientX,
                startY: ev.clientY,
                moved: false
            };
            mesa.setPointerCapture(ev.pointerId);
            mesa.classList.add("salon-mesa--dragging");
        });

        canvas.addEventListener("pointermove", function (ev) {
            if (!drag) {
                return;
            }
            const r = drag.rect;
            const w = drag.el.offsetWidth;
            const h = drag.el.offsetHeight;
            if (Math.abs(ev.clientX - drag.startX) > 3 || Math.abs(ev.clientY - drag.startY) > 3) {
                drag.moved = true;
            }
            if (drag.resize) {
                const width = clamp(drag.startWidth + ev.clientX - drag.startX, 56, r.width - (drag.el.offsetLeft));
                const height = clamp(drag.startHeight + ev.clientY - drag.startY, 56, r.height - (drag.el.offsetTop));
                drag.el.style.width = width + "px";
                drag.el.style.height = height + "px";
                return;
            }
            const left = clamp(ev.clientX - r.left - drag.offsetX, 0, r.width - w);
            const top = clamp(ev.clientY - r.top - drag.offsetY, 0, r.height - h);
            drag.el.style.left = (left / r.width * 100) + "%";
            drag.el.style.top = (top / r.height * 100) + "%";
        });

        function end(ev) {
            if (!drag) {
                return;
            }
            try { drag.el.releasePointerCapture(ev.pointerId); } catch (e) { /* sin captura */ }
            drag.el.classList.remove("salon-mesa--dragging");
            if (drag.moved) {
                drag.el.dataset.salonSuppressClick = "1";
            }
            drag = null;
        }

        canvas.addEventListener("pointerup", end);
        canvas.addEventListener("pointercancel", end);
    }

    window.alfaPosSalon = {
        // Menu Caja: la barra de acciones tiene scroll horizontal y recortaria un desplegable
        // absoluto; el panel va fijo en pantalla, debajo del boton.
        posicionMenu: function (selectorBoton, selectorPanel) {
            var boton = document.querySelector(selectorBoton);
            var panel = document.querySelector(selectorPanel);
            if (!boton || !panel) {
                return;
            }
            var r = boton.getBoundingClientRect();
            var ancho = panel.offsetWidth || 272;
            var izquierda = Math.max(8, Math.min(r.left, window.innerWidth - ancho - 8));
            panel.style.top = (r.bottom + 6) + "px";
            panel.style.left = izquierda + "px";
            panel.style.visibility = "visible";
        },

        setEditing: function (canvasId, editing) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) {
                return;
            }
            enableDrag(canvas);
            canvas.dataset.salonEditing = editing ? "1" : "0";
        },

        // Devuelve [{ id, x, y, w, h }] en unidades virtuales del plano.
        getPositions: function (canvasId) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) {
                return [];
            }
            const rect = canvas.getBoundingClientRect();
            if (rect.width <= 0 || rect.height <= 0) {
                return [];
            }
            return Array.from(canvas.querySelectorAll("[data-mesa-id]")).map(function (el) {
                const r = el.getBoundingClientRect();
                return {
                    id: parseInt(el.dataset.mesaId, 10),
                    x: Math.round((r.left - rect.left) / rect.width * PLANO_W),
                    y: Math.round((r.top - rect.top) / rect.height * PLANO_H),
                    w: Math.round(r.width / rect.width * PLANO_W),
                    h: Math.round(r.height / rect.height * PLANO_H)
                };
            });
        },

        // Aviso sonoro corto (comanda nueva en cocina / llamado en el salon). El navegador solo deja
        // sonar despues de que el usuario toco la pagina al menos una vez.
        beep: function (veces) {
            try {
                const Ctx = window.AudioContext || window.webkitAudioContext;
                if (!Ctx) {
                    return;
                }
                const ctx = window.__alfaPosSalonAudio || (window.__alfaPosSalonAudio = new Ctx());
                const n = Math.max(1, Math.min(4, veces || 1));
                for (let i = 0; i < n; i++) {
                    const osc = ctx.createOscillator();
                    const gain = ctx.createGain();
                    const t = ctx.currentTime + i * 0.28;
                    osc.type = "sine";
                    osc.frequency.value = 880;
                    gain.gain.setValueAtTime(0.0001, t);
                    gain.gain.exponentialRampToValueAtTime(0.35, t + 0.02);
                    gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.22);
                    osc.connect(gain).connect(ctx.destination);
                    osc.start(t);
                    osc.stop(t + 0.24);
                }
            } catch (e) {
                /* sin audio */
            }
        },

        // Configuracion local del dispositivo (pantalla / comandera), por punto de venta.
        getLocal: function (key) {
            try { return window.localStorage.getItem(key); } catch (e) { return null; }
        },
        setLocal: function (key, value) {
            try { window.localStorage.setItem(key, value); } catch (e) { /* sin storage */ }
        },

        // Imprime HTML en un iframe oculto (precuenta). Con Chrome --kiosk-printing sale directo.
        printHtml: function (html) {
            let frame = document.getElementById("pos-salon-print-frame");
            if (!frame) {
                frame = document.createElement("iframe");
                frame.id = "pos-salon-print-frame";
                frame.setAttribute("aria-hidden", "true");
                frame.style.position = "fixed";
                frame.style.width = "0";
                frame.style.height = "0";
                frame.style.border = "0";
                frame.style.right = "0";
                frame.style.bottom = "0";
                document.body.appendChild(frame);
            }
            const doc = frame.contentWindow.document;
            doc.open();
            doc.write(html || "");
            doc.close();
            return new Promise(function (resolve) {
                window.setTimeout(function () {
                    frame.contentWindow.focus();
                    frame.contentWindow.print();
                    resolve(true);
                }, 180);
            });
        }
    };
})();
