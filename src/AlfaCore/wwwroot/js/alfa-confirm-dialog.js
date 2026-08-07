let activeState = null;

export function activate(dialog, cancelButton, dotNetReference) {
    const previousFocus = document.activeElement;
    const keydown = async event => {
        if (event.key === "Escape") {
            event.preventDefault();
            await dotNetReference.invokeMethodAsync("RequestCancelAsync");
            return;
        }

        if (event.key !== "Tab") return;
        const focusable = [...dialog.querySelectorAll("button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex='-1'])")];
        if (!focusable.length) {
            event.preventDefault();
            dialog.focus();
            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    };

    dialog.addEventListener("keydown", keydown);
    activeState = { dialog, previousFocus, keydown };
    requestAnimationFrame(() => cancelButton?.focus());
}

export function deactivate(dialog) {
    const state = activeState;
    if (!state) return;
    state.dialog.removeEventListener("keydown", state.keydown);
    activeState = null;
    if (state.previousFocus instanceof HTMLElement && state.previousFocus.isConnected) {
        state.previousFocus.focus();
    }
}
