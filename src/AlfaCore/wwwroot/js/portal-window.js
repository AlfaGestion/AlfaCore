/* Administra la pestaña provisoria del Portal Cliente mientras se genera el auto login. */
window.alfaCorePortalWindow = window.alfaCorePortalWindow || (() => {
    const windows = new Map();

    return {
        openPlaceholder(name) {
            const target = String(name || '_blank');
            const win = window.open('about:blank', target);
            if (!win) return false;

            // La generación del token puede requerir una conexión a la base activa. La pestaña
            // se abre antes de terminar esa operación para evitar el bloqueo del navegador; no
            // debe quedar visualmente en blanco durante la espera.
            try {
                win.document.open();
                win.document.write(`<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Portal Cliente</title><style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#071321;color:#e5eefb;font-family:system-ui,-apple-system,Segoe UI,sans-serif}.card{text-align:center;padding:32px 40px;border:1px solid #213650;border-radius:16px;background:#0d1d31;box-shadow:0 18px 50px #0004}.spinner{width:28px;height:28px;margin:0 auto 18px;border:3px solid #29435f;border-top-color:#16a6ff;border-radius:50%;animation:spin 1s linear infinite}@keyframes spin{to{transform:rotate(360deg)}}h1{font-size:20px;margin:0 0 8px}p{margin:0;color:#a9bed7}</style></head><body><main class="card"><div class="spinner" aria-hidden="true"></div><h1>Abriendo Portal Cliente</h1><p>Estamos preparando el acceso automático...</p></main></body></html>`);
                win.document.close();
            } catch {
                // La navegación posterior sigue siendo válida aunque el navegador no permita
                // escribir el contenido provisional de la pestaña.
            }

            windows.set(target, win);
            return true;
        },

        setMessage(message, name) {
            const target = String(name || '_blank');
            const win = windows.get(target);
            if (!win || win.closed) return false;
            try {
                const text = String(message || 'No se pudo abrir el Portal Cliente.');
                win.document.body.innerHTML = '<main class="card"><h1>Portal Cliente</h1><p></p></main>';
                win.document.querySelector('.card p').textContent = text;
                return true;
            } catch {
                return false;
            }
        },

        navigate(url, name) {
            const target = String(name || '_blank');
            let win = windows.get(target);
            if (!win || win.closed) {
                win = window.open('about:blank', target);
                if (!win) return false;
                windows.set(target, win);
            }

            win.location.href = String(url || '');
            return true;
        }
    };
})();
