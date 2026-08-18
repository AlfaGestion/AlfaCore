(function () {
    const state = {
        started: false,
        observer: null,
        scheduled: 0,
        onChange: null
    };

    const settings = {
        gap: 6,
        gutter: 12,
        compactMaxWidth: 408,
        standardMaxWidth: 520,
        wideMaxWidth: 760,
        mobileBreakpoint: 720,
        minHeight: 160
    };

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), max);
    }

    function getPanelVariant(panel) {
        if (panel.classList.contains("smart-search__panel--compact"))
            return "compact";

        if (panel.classList.contains("smart-search__panel--standard"))
            return "standard";

        if (panel.classList.contains("smart-search__panel--wide") || panel.querySelector(".smart-search__custom") !== null)
            return "wide";

        return "standard";
    }

    function getPanelWidth(panel, viewportWidth) {
        const maxAvailable = Math.max(0, viewportWidth - settings.gutter * 2);
        const variant = getPanelVariant(panel);
        const requested = variant === "wide"
            ? settings.wideMaxWidth
            : variant === "standard"
                ? settings.standardMaxWidth
                : settings.compactMaxWidth;

        panel.classList.toggle("smart-search__panel--wide", variant === "wide");
        panel.classList.toggle("smart-search__panel--standard", variant === "standard");
        panel.classList.toggle("smart-search__panel--compact", variant === "compact");

        return Math.min(requested, maxAvailable);
    }

    function positionPanel(panel) {
        const root = panel.closest(".smart-search");
        if (!root)
            return;

        const trigger = root.querySelector(".smart-search__bar") || root;
        const triggerRect = trigger.getBoundingClientRect();
        const viewportWidth = window.innerWidth || document.documentElement.clientWidth;
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
        const width = getPanelWidth(panel, viewportWidth);
        const idealLeft = triggerRect.left + triggerRect.width / 2 - width / 2;
        const maxLeft = Math.max(settings.gutter, viewportWidth - width - settings.gutter);
        const left = clamp(idealLeft, settings.gutter, maxLeft);
        const top = Math.max(settings.gutter, triggerRect.bottom + settings.gap);
        const maxHeight = Math.max(settings.minHeight, viewportHeight - top - settings.gutter);

        panel.style.position = "fixed";
        panel.style.left = `${Math.round(left)}px`;
        panel.style.right = "auto";
        panel.style.top = `${Math.round(top)}px`;
        panel.style.bottom = "auto";
        panel.style.width = `${Math.round(width)}px`;
        panel.style.maxWidth = `${Math.round(Math.max(0, viewportWidth - settings.gutter * 2))}px`;
        panel.style.maxHeight = `${Math.round(maxHeight)}px`;
        panel.style.transform = "none";
        panel.style.transformOrigin = "top center";
        panel.style.overflowX = "hidden";
        panel.style.overflowY = "auto";

        root.classList.add("smart-search--popover-positioned");
    }

    function refresh() {
        const panels = document.querySelectorAll(".shell.shell--alfa-design .smart-search__panel");
        panels.forEach(positionPanel);
    }

    function scheduleRefresh() {
        if (state.scheduled)
            return;

        state.scheduled = window.requestAnimationFrame(function () {
            state.scheduled = 0;
            refresh();
        });
    }

    function start() {
        if (state.started)
            return;

        state.started = true;
        state.onChange = scheduleRefresh;
        state.observer = new MutationObserver(scheduleRefresh);
        state.observer.observe(document.body, { childList: true, subtree: true });

        window.addEventListener("resize", state.onChange, { passive: true });
        window.addEventListener("scroll", state.onChange, { passive: true, capture: true });
        document.addEventListener("focusin", state.onChange, true);
        document.addEventListener("click", state.onChange, true);

        scheduleRefresh();
    }

    function stop() {
        if (!state.started)
            return;

        state.started = false;

        if (state.scheduled) {
            window.cancelAnimationFrame(state.scheduled);
            state.scheduled = 0;
        }

        if (state.observer) {
            state.observer.disconnect();
            state.observer = null;
        }

        window.removeEventListener("resize", state.onChange);
        window.removeEventListener("scroll", state.onChange, true);
        document.removeEventListener("focusin", state.onChange, true);
        document.removeEventListener("click", state.onChange, true);
        state.onChange = null;
    }

    window.AlfaSmartSearchPopover = {
        start,
        stop,
        refresh
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start, { once: true });
    } else {
        start();
    }
})();
