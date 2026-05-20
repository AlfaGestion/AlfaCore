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
    _fileDropWatchers: new WeakMap(),
    _notificationBaseTitle: 'AlfaCore - Alfa Gestión',
    _notificationSoundUrl: '/audio/conversaciones/mixkit-alert-quick-chime-766.mp3',
    _notificationAudio: null,
    _audioContext: null,
    _audioUnlocked: false,

    isNearBottom: function (element) {
        if (!element) return false;
        const distance = element.scrollHeight - element.scrollTop - element.clientHeight;
        return distance < 96;
    },

    scrollToBottom: function (element) {
        if (!element) return;
        element.scrollTop = element.scrollHeight;
    },

    initNotifications: function (baseTitle) {
        this._notificationBaseTitle = baseTitle || document.title || this._notificationBaseTitle;
        this.setUnreadCount(0);
        this._notificationAudio = new Audio(this._notificationSoundUrl);
        this._notificationAudio.preload = 'auto';
        this._notificationAudio.volume = 0.8;

        const unlock = () => {
            window.conversacionesUi.unlockNotificationSound();
        };

        window.addEventListener('pointerdown', unlock, { once: true, passive: true });
        window.addEventListener('keydown', unlock, { once: true });
    },

    setUnreadCount: function (count) {
        const unread = Number.isFinite(count) ? Math.max(0, Math.trunc(count)) : 0;
        document.title = unread > 0
            ? `(${unread}) ${this._notificationBaseTitle}`
            : this._notificationBaseTitle;
    },

    unlockNotificationSound: async function () {
        try {
            if (this._notificationAudio) {
                this._notificationAudio.load();
            }

            const context = this._getAudioContext();
            if (!context) return;

            if (context.state === 'suspended') {
                await context.resume();
            }

            const gain = context.createGain();
            gain.gain.setValueAtTime(0.0001, context.currentTime);
            gain.connect(context.destination);
            gain.disconnect();
            this._audioUnlocked = true;
        } catch {
        }
    },

    _getAudioContext: function () {
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        if (!AudioContext) return null;

        if (!this._audioContext || this._audioContext.state === 'closed') {
            this._audioContext = new AudioContext();
        }

        return this._audioContext;
    },

    playNewMessageSound: async function () {
        try {
            if (this._notificationAudio) {
                this._notificationAudio.pause();
                this._notificationAudio.currentTime = 0;
                this._notificationAudio.volume = 0.85;
                await this._notificationAudio.play();
                return;
            }

            const context = this._getAudioContext();
            if (!context) return;

            if (context.state === 'suspended') {
                await context.resume();
            }

            if (context.state !== 'running') return;

            const playTone = (start, frequency, duration, volume) => {
                const oscillator = context.createOscillator();
                const gain = context.createGain();
                oscillator.type = 'triangle';
                oscillator.frequency.setValueAtTime(frequency, start);
                gain.gain.setValueAtTime(0.0001, start);
                gain.gain.exponentialRampToValueAtTime(volume, start + 0.014);
                gain.gain.exponentialRampToValueAtTime(0.0001, start + duration);

                oscillator.connect(gain);
                gain.connect(context.destination);
                oscillator.start(start);
                oscillator.stop(start + duration + 0.02);
                oscillator.onended = () => {
                    oscillator.disconnect();
                    gain.disconnect();
                };
            };

            const now = context.currentTime;
            playTone(now, 880, 0.12, 0.18);
            playTone(now + 0.13, 1175, 0.18, 0.16);
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
    },

    bindFileDrop: function (element, inputId) {
        if (!element || !inputId) return false;

        const previous = this._fileDropWatchers.get(element);
        if (previous) {
            previous.events.forEach(item => element.removeEventListener(item.name, item.handler));
            element.classList.remove('is-file-dragging');
        }

        let dragDepth = 0;
        const getInput = () => document.getElementById(inputId);
        const hasFiles = event => event.dataTransfer && Array.from(event.dataTransfer.types || []).includes('Files');

        const prevent = event => {
            if (!hasFiles(event)) return false;
            event.preventDefault();
            event.stopPropagation();
            event.dataTransfer.dropEffect = 'copy';
            return true;
        };

        const dragEnter = event => {
            if (!prevent(event)) return;
            dragDepth += 1;
            element.classList.add('is-file-dragging');
        };

        const dragOver = event => {
            prevent(event);
        };

        const dragLeave = event => {
            if (!hasFiles(event)) return;
            event.preventDefault();
            event.stopPropagation();
            dragDepth = Math.max(0, dragDepth - 1);
            if (dragDepth === 0) {
                element.classList.remove('is-file-dragging');
            }
        };

        const drop = event => {
            if (!prevent(event)) return;
            dragDepth = 0;
            element.classList.remove('is-file-dragging');

            const input = getInput();
            if (!input || !event.dataTransfer.files || event.dataTransfer.files.length === 0) return;

            const transfer = new DataTransfer();
            Array.from(event.dataTransfer.files).forEach(file => transfer.items.add(file));
            input.files = transfer.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
        };

        const events = [
            { name: 'dragenter', handler: dragEnter },
            { name: 'dragover', handler: dragOver },
            { name: 'dragleave', handler: dragLeave },
            { name: 'drop', handler: drop }
        ];

        events.forEach(item => element.addEventListener(item.name, item.handler));
        this._fileDropWatchers.set(element, { events: events });
        return true;
    },

    unbindFileDrop: function (element) {
        if (!element) return;
        const previous = this._fileDropWatchers.get(element);
        if (!previous) return;

        previous.events.forEach(item => element.removeEventListener(item.name, item.handler));
        element.classList.remove('is-file-dragging');
        this._fileDropWatchers.delete(element);
    }
};
