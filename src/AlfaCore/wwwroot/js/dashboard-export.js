window.dashboardExport = {
    printCurrentPage: function (pageTitle) {
        const previousTitle = document.title;
        const suffix = pageTitle ? ` - ${pageTitle}` : "";

        try {
            document.title = `Dashboard de Compras${suffix}`;
            window.print();
        } finally {
            window.setTimeout(() => {
                document.title = previousTitle;
            }, 250);
        }
    },
    startDictation: function (targetId) {
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SpeechRecognition) {
            throw new Error("El navegador no soporta reconocimiento de voz.");
        }

        const target = document.getElementById(targetId);
        if (!target) {
            throw new Error("No se encontró el cuadro de consulta para cargar el dictado.");
        }

        if (window.dashboardExport._recognition) {
            window.dashboardExport._recognition.stop();
        }

        const recognition = new SpeechRecognition();
        recognition.lang = "es-AR";
        recognition.interimResults = false;
        recognition.maxAlternatives = 1;

        recognition.onresult = function (event) {
            const transcript = event.results?.[0]?.[0]?.transcript || "";
            if (!transcript) {
                return;
            }

            const nextValue = target.value ? `${target.value.trim()} ${transcript}` : transcript;
            target.value = nextValue.trim();
            target.dispatchEvent(new Event("input", { bubbles: true }));
        };

        recognition.onend = function () {
            if (window.dashboardExport._recognition === recognition) {
                window.dashboardExport._recognition = null;
            }
        };

        window.dashboardExport._recognition = recognition;
        recognition.start();
    },
    openInNewTab: function (url) {
        window.open(url, "_blank", "noopener,noreferrer");
    },
    downloadFile: function (url) {
        if (!url) {
            return;
        }

        const frameId = "dashboard-export-download-frame";
        let frame = document.getElementById(frameId);
        if (!frame) {
            frame = document.createElement("iframe");
            frame.id = frameId;
            frame.style.display = "none";
            document.body.appendChild(frame);
        }

        frame.src = url;
    }
};

window.openInNewTab = function (url) {
    window.dashboardExport.openInNewTab(url);
};
