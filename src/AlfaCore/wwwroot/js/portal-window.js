/* Administra la pestaña provisoria del Portal Cliente mientras se genera el auto login. */
window.alfaCorePortalWindow = window.alfaCorePortalWindow || (() => {
    const windows = new Map();

    return {
        openPlaceholder(name) {
            const target = String(name || '_blank');
            const win = window.open('about:blank', target);
            if (!win) return false;
            windows.set(target, win);
            return true;
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
