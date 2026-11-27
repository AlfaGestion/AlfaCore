// Ajustes visuales del Punto de Venta guardados en el navegador de cada PC (localStorage):
// tamaño de los botones de artículos, ancho del carrito, ubicación y tamaño de los rubros.
// Todo se aplica con variables CSS sobre <html>, así sobrevive a los re-render de Blazor y
// arrastrar una barra no genera ida y vuelta al servidor.
(function () {
    var CLAVE = 'alfacore.pos.layout.v1';
    var ESCALA_MIN = 0.7, ESCALA_MAX = 2, ESCALA_PASO = 0.1;
    var root = document.documentElement;

    function leer() {
        try { return JSON.parse(localStorage.getItem(CLAVE) || '{}') || {}; }
        catch (e) { return {}; }
    }

    function guardar(estado) {
        try { localStorage.setItem(CLAVE, JSON.stringify(estado)); } catch (e) { /* sin almacenamiento: solo esta sesión */ }
    }

    function redondear(v) { return Math.round(v * 100) / 100; }

    function aplicar(estado) {
        var escala = Number(estado.escala) || 1;
        root.style.setProperty('--pos-tile-scale', String(escala));

        if (estado.carrito) {
            root.style.setProperty('--pos-cart-width', estado.carrito + 'px');
            root.setAttribute('data-pos-cart', 'custom');
        } else {
            root.style.removeProperty('--pos-cart-width');
            root.removeAttribute('data-pos-cart');
        }

        if (estado.rubrosAlto)
            root.style.setProperty('--pos-rubros-alto', estado.rubrosAlto + 'px');
        else
            root.style.removeProperty('--pos-rubros-alto');

        if (estado.rubrosAncho)
            root.style.setProperty('--pos-rubros-ancho', estado.rubrosAncho + 'px');
        else
            root.style.removeProperty('--pos-rubros-ancho');

        root.setAttribute('data-pos-rubros', estado.rubros === 'costado' ? 'costado' : 'arriba');
        sincronizarPanel(estado);
    }

    function sincronizarPanel(estado) {
        var escala = Number(estado.escala) || 1;
        document.querySelectorAll('[data-pos-layout="escala"]').forEach(function (el) { el.value = String(escala); });
        document.querySelectorAll('[data-pos-layout-valor]').forEach(function (el) { el.textContent = Math.round(escala * 100) + '%'; });
    }

    function cambiar(fn) {
        var estado = leer();
        fn(estado);
        guardar(estado);
        aplicar(estado);
    }

    function fijarEscala(valor) {
        cambiar(function (e) {
            var v = Math.min(ESCALA_MAX, Math.max(ESCALA_MIN, redondear(valor)));
            if (Math.abs(v - 1) < 0.001) delete e.escala; else e.escala = v;
        });
    }

    window.alfaPosLayout = {
        sincronizar: function () { aplicar(leer()); }
    };

    // Controles del panel "Tamaño y distribución" (delegación: el panel lo dibuja Blazor).
    document.addEventListener('click', function (ev) {
        var el = ev.target.closest && ev.target.closest('[data-pos-layout]');
        if (!el || el.tagName === 'INPUT') return;
        var accion = el.getAttribute('data-pos-layout');
        var actual = Number(leer().escala) || 1;
        if (accion === 'menos') fijarEscala(actual - ESCALA_PASO);
        else if (accion === 'mas') fijarEscala(actual + ESCALA_PASO);
        else if (accion === 'rubros-arriba') cambiar(function (e) { delete e.rubros; });
        else if (accion === 'rubros-costado') cambiar(function (e) { e.rubros = 'costado'; });
        else if (accion === 'restablecer') { guardar({}); aplicar({}); }
    });

    document.addEventListener('input', function (ev) {
        var el = ev.target;
        if (el && el.getAttribute && el.getAttribute('data-pos-layout') === 'escala')
            fijarEscala(Number(el.value));
    });

    // Barras arrastrables: data-pos-split="carrito" (ancho del carrito) o "rubros" (alto o ancho de los rubros).
    var arrastre = null;

    document.addEventListener('pointerdown', function (ev) {
        var barra = ev.target.closest && ev.target.closest('[data-pos-split]');
        if (!barra || ev.button !== 0) return;
        var tipo = barra.getAttribute('data-pos-split');
        var costado = root.getAttribute('data-pos-rubros') === 'costado';
        var ref = tipo === 'carrito'
            ? document.querySelector('.pos-stage')
            : barra.parentElement;
        var objetivo = tipo === 'carrito'
            ? document.querySelector('.pos-stage > .pos-sidebar')
            : barra.parentElement && barra.parentElement.querySelector('.pos-catalog__rubros');
        if (!ref || !objetivo) return;

        ev.preventDefault();
        arrastre = {
            tipo: tipo === 'carrito' ? 'carrito' : (costado ? 'rubrosAncho' : 'rubrosAlto'),
            ref: ref.getBoundingClientRect(),
            objetivo: objetivo.getBoundingClientRect(),
            barra: barra,
            valor: null
        };
        barra.classList.add('is-dragging');
        root.classList.add('pos-arrastrando');
        try { barra.setPointerCapture(ev.pointerId); } catch (e) { /* algunos navegadores táctiles */ }
    });

    document.addEventListener('pointermove', function (ev) {
        if (!arrastre) return;
        var r = arrastre.ref, o = arrastre.objetivo, v;
        if (arrastre.tipo === 'carrito') {
            v = Math.min(Math.max(ev.clientX - r.left, 280), r.width * 0.7);
            root.style.setProperty('--pos-cart-width', Math.round(v) + 'px');
            root.setAttribute('data-pos-cart', 'custom');
        } else if (arrastre.tipo === 'rubrosAlto') {
            v = Math.min(Math.max(ev.clientY - o.top, 36), r.height * 0.75);
            root.style.setProperty('--pos-rubros-alto', Math.round(v) + 'px');
        } else {
            v = Math.min(Math.max(ev.clientX - o.left, 100), r.width * 0.6);
            root.style.setProperty('--pos-rubros-ancho', Math.round(v) + 'px');
        }
        arrastre.valor = Math.round(v);
    });

    function terminar() {
        if (!arrastre) return;
        var a = arrastre;
        arrastre = null;
        a.barra.classList.remove('is-dragging');
        root.classList.remove('pos-arrastrando');
        if (a.valor === null) return;
        cambiar(function (e) {
            if (a.tipo === 'carrito') e.carrito = a.valor;
            else if (a.tipo === 'rubrosAlto') e.rubrosAlto = a.valor;
            else e.rubrosAncho = a.valor;
        });
    }

    document.addEventListener('pointerup', terminar);
    document.addEventListener('pointercancel', terminar);

    // Doble clic sobre una barra: vuelve esa medida a la de fábrica.
    document.addEventListener('dblclick', function (ev) {
        var barra = ev.target.closest && ev.target.closest('[data-pos-split]');
        if (!barra) return;
        var tipo = barra.getAttribute('data-pos-split');
        var costado = root.getAttribute('data-pos-rubros') === 'costado';
        cambiar(function (e) {
            if (tipo === 'carrito') delete e.carrito;
            else if (costado) delete e.rubrosAncho;
            else delete e.rubrosAlto;
        });
    });

    aplicar(leer());
})();
