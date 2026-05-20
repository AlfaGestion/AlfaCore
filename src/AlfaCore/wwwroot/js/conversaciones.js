window.conversacionesAudio = (function () {
    let _recorder = null;
    let _chunks = [];

    return {
        startRecording: async function () {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            _chunks = [];
            const preferredTypes = [
                'audio/ogg;codecs=opus',
                'audio/mp4',
                'audio/webm;codecs=opus',
                'audio/webm'
            ];
            const mimeType = preferredTypes.find(type => window.MediaRecorder && MediaRecorder.isTypeSupported(type));
            _recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
            _recorder.ondataavailable = e => { if (e.data.size > 0) _chunks.push(e.data); };
            _recorder.start();
            return true;
        },

        stopRecording: function () {
            return new Promise(resolve => {
                _recorder.onstop = () => {
                    const blob = new Blob(_chunks, { type: _recorder.mimeType || 'audio/webm' });
                    const reader = new FileReader();
                    reader.onloadend = () => resolve(reader.result.split(',')[1]);
                    reader.readAsDataURL(blob);
                    _recorder.stream.getTracks().forEach(t => t.stop());
                    _recorder = null;
                    _chunks = [];
                };
                _recorder.stop();
            });
        },

        stopRecordingPayload: function () {
            return new Promise(resolve => {
                _recorder.onstop = () => {
                    const mimeType = _recorder.mimeType || 'audio/webm';
                    const blob = new Blob(_chunks, { type: mimeType });
                    const reader = new FileReader();
                    reader.onloadend = () => resolve({
                        base64: reader.result.split(',')[1],
                        mimeType: mimeType
                    });
                    reader.readAsDataURL(blob);
                    _recorder.stream.getTracks().forEach(t => t.stop());
                    _recorder = null;
                    _chunks = [];
                };
                _recorder.stop();
            });
        },

        isRecording: function () {
            return _recorder !== null && _recorder.state === 'recording';
        }
    };
})();

window.conversacionesUi = {
    _threadWatchers: new WeakMap(),

    isNearBottom: function (element) {
        if (!element) return false;
        const distance = element.scrollHeight - element.scrollTop - element.clientHeight;
        return distance < 96;
    },

    scrollToBottom: function (element) {
        if (!element) return;
        element.scrollTop = element.scrollHeight;
    },

    playNewMessageSound: async function () {
        try {
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (!AudioContext) return;

            const context = new AudioContext();
            if (context.state === 'suspended') {
                await context.resume();
            }

            const oscillator = context.createOscillator();
            const gain = context.createGain();
            oscillator.type = 'sine';
            oscillator.frequency.setValueAtTime(740, context.currentTime);
            oscillator.frequency.exponentialRampToValueAtTime(980, context.currentTime + 0.08);
            gain.gain.setValueAtTime(0.0001, context.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.08, context.currentTime + 0.015);
            gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + 0.18);

            oscillator.connect(gain);
            gain.connect(context.destination);
            oscillator.start();
            oscillator.stop(context.currentTime + 0.2);
            oscillator.onended = () => context.close();
        } catch {
        }
    },

    watchThreadScroll: function (element, dotNetRef) {
        if (!element || !dotNetRef) return false;

        const previous = this._threadWatchers.get(element);
        if (previous) {
            element.removeEventListener('scroll', previous.handler);
        }

        let scheduled = false;
        let lastAway = !this.isNearBottom(element);
        const notify = () => {
            scheduled = false;
            const away = !window.conversacionesUi.isNearBottom(element);
            if (away === lastAway) return;
            lastAway = away;
            dotNetRef.invokeMethodAsync('OnThreadScrollStateChanged', away).catch(() => {});
        };

        const handler = () => {
            if (scheduled) return;
            scheduled = true;
            window.requestAnimationFrame(notify);
        };

        element.addEventListener('scroll', handler, { passive: true });
        this._threadWatchers.set(element, { handler: handler });
        return lastAway;
    },

    bindReplyEnter: function (element, dotNetRef) {
        if (!element || !dotNetRef) return false;

        if (element._conversacionesReplyEnterHandler) {
            element.removeEventListener('keydown', element._conversacionesReplyEnterHandler);
        }

        const handler = (event) => {
            if (event.key !== 'Enter' || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) {
                return;
            }

            event.preventDefault();
            dotNetRef.invokeMethodAsync('SendComposerFromEnter').catch(() => {});
        };

        element.addEventListener('keydown', handler);
        element._conversacionesReplyEnterHandler = handler;
        return true;
    }
};
