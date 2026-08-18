const controllers = new WeakMap();

export function activate(root, dotnetRef) {
    if (!root || !dotnetRef) {
        return;
    }

    deactivate(root);

    const controller = {
        root,
        dotnetRef,
        onPointerDown: null,
        onDoubleClick: null,
        cleanupDrag: null
    };

    controller.onPointerDown = event => {
        const handle = event.target?.closest?.("[data-alfa-column-resizer]");
        if (!handle || !root.contains(handle) || event.button !== 0) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();

        const key = handle.dataset.alfaColumnKey || "";
        if (!key) {
            return;
        }

        const min = readNumber(handle.dataset.alfaColumnMin, 72);
        const max = readNumber(handle.dataset.alfaColumnMax, 640);
        const startWidth = readNumber(handle.dataset.alfaColumnWidth, readNumber(handle.dataset.alfaColumnDefault, min));
        const startX = event.clientX;
        const pointerId = event.pointerId;
        let lastWidth = startWidth;

        root.classList.add("is-resizing-columns");
        handle.classList.add("is-active");

        try {
            handle.setPointerCapture(pointerId);
        } catch {
        }

        const onPointerMove = moveEvent => {
            if (moveEvent.pointerId !== pointerId) {
                return;
            }

            const nextWidth = clamp(Math.round(startWidth + moveEvent.clientX - startX), min, max);
            if (nextWidth === lastWidth) {
                return;
            }

            lastWidth = nextWidth;
            handle.dataset.alfaColumnWidth = String(nextWidth);
            dotnetRef.invokeMethodAsync("PreviewAlfaDataViewColumnWidth", key, nextWidth);
        };

        const finish = async upEvent => {
            if (upEvent.pointerId !== pointerId) {
                return;
            }

            cleanup();
            try {
                await dotnetRef.invokeMethodAsync("CommitAlfaDataViewColumnWidth", key, lastWidth);
            } catch {
            }
        };

        const cancel = cancelEvent => {
            if (cancelEvent.pointerId !== pointerId) {
                return;
            }

            cleanup();
            dotnetRef.invokeMethodAsync("PreviewAlfaDataViewColumnWidth", key, startWidth);
        };

        const cleanup = () => {
            handle.removeEventListener("pointermove", onPointerMove);
            handle.removeEventListener("pointerup", finish);
            handle.removeEventListener("pointercancel", cancel);
            root.classList.remove("is-resizing-columns");
            handle.classList.remove("is-active");

            try {
                handle.releasePointerCapture(pointerId);
            } catch {
            }

            if (controller.cleanupDrag === cleanup) {
                controller.cleanupDrag = null;
            }
        };

        controller.cleanupDrag = cleanup;
        handle.addEventListener("pointermove", onPointerMove);
        handle.addEventListener("pointerup", finish);
        handle.addEventListener("pointercancel", cancel);
    };

    controller.onDoubleClick = event => {
        const handle = event.target?.closest?.("[data-alfa-column-resizer]");
        if (!handle || !root.contains(handle)) {
            return;
        }

        const key = handle.dataset.alfaColumnKey || "";
        if (!key) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        dotnetRef.invokeMethodAsync("ResetAlfaDataViewColumnWidth", key);
    };

    root.addEventListener("pointerdown", controller.onPointerDown);
    root.addEventListener("dblclick", controller.onDoubleClick);
    controllers.set(root, controller);
}

export function deactivate(root) {
    const controller = controllers.get(root);
    if (!controller) {
        return;
    }

    controller.cleanupDrag?.();
    controller.root.removeEventListener("pointerdown", controller.onPointerDown);
    controller.root.removeEventListener("dblclick", controller.onDoubleClick);
    controllers.delete(root);
}

function readNumber(value, fallback) {
    const parsed = Number.parseInt(value ?? "", 10);
    return Number.isFinite(parsed) ? parsed : fallback;
}

function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
}
