window.conversacionesAudio = (function () {
    let _recorder = null;
    let _chunks = [];

    const stopTracks = recorder => {
        try {
            recorder?.stream?.getTracks?.().forEach(t => t.stop());
        } catch {
        }
    };

    const buildRecordingBlob = () => new Blob(_chunks, { type: _recorder?.mimeType || 'audio/webm' });

    const resetRecorder = () => {
        _recorder = null;
        _chunks = [];
    };

    const extensionForMime = mimeType => {
        const normalized = (mimeType || '').toLowerCase();
        if (normalized.includes('ogg')) return '.ogg';
        if (normalized.includes('mp4')) return '.mp4';
        if (normalized.includes('mpeg')) return '.mp3';
        return '.webm';
    };

    const normalizeFileName = (fileName, mimeType) => {
        const name = fileName || 'audio';
        return /\.[a-z0-9]{2,5}$/i.test(name)
            ? name
            : `${name}${extensionForMime(mimeType)}`;
    };

    return {
        startRecording: async function () {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            _chunks = [];
            const preferredTypes = [
                'audio/ogg;codecs=opus',
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
                    const blob = buildRecordingBlob();
                    const reader = new FileReader();
                    reader.onloadend = () => resolve(reader.result.split(',')[1]);
                    reader.readAsDataURL(blob);
                    stopTracks(_recorder);
                    resetRecorder();
                };
                _recorder.stop();
            });
        },

        stopRecordingPayload: function () {
            return new Promise(resolve => {
                _recorder.onstop = () => {
                    const mimeType = _recorder.mimeType || 'audio/webm';
                    const blob = buildRecordingBlob();
                    const reader = new FileReader();
                    reader.onloadend = () => resolve({
                        base64: reader.result.split(',')[1],
                        mimeType: mimeType
                    });
                    reader.readAsDataURL(blob);
                    stopTracks(_recorder);
                    resetRecorder();
                };
                _recorder.stop();
            });
        },

        stopRecordingToInput: function (inputId, fileName) {
            return new Promise(resolve => {
                if (!_recorder) {
                    resolve({ ok: false, message: 'No hay una grabación activa.' });
                    return;
                }

                _recorder.onstop = () => {
                    const mimeType = _recorder.mimeType || 'audio/webm';
                    const blob = buildRecordingBlob();
                    stopTracks(_recorder);
                    resetRecorder();

                    const input = document.getElementById(inputId);
                    if (!input) {
                        resolve({ ok: false, message: 'No se encontró el selector de adjuntos.' });
                        return;
                    }

                    try {
                        const normalizedFileName = normalizeFileName(fileName, mimeType);
                        const file = new File([blob], normalizedFileName, {
                            type: mimeType,
                            lastModified: Date.now()
                        });
                        const transfer = new DataTransfer();
                        transfer.items.add(file);
                        input.files = transfer.files;
                        input.dispatchEvent(new Event('change', { bubbles: true }));
                        resolve({ ok: true, mimeType: mimeType, size: blob.size, fileName: normalizedFileName });
                    } catch (error) {
                        resolve({
                            ok: false,
                            message: error?.message || 'No se pudo preparar el audio grabado.'
                        });
                    }
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
    _previewPanWatchers: new WeakMap(),
    _notificationBaseTitle: 'AlfaCore - Alfa Gestión',
    _audioPlayersInitialized: false,
    _audioSpeeds: [1, 1.5, 2],
    _notificationSoundUrl: '/audio/conversaciones/mixkit-alert-quick-chime-766.mp3',
    _notificationAudio: null,
    _audioContext: null,
    _audioUnlocked: false,
    _lastSoundAttemptAt: '',
    _lastSoundError: '',
    _soundInitialized: false,

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
        this._lastSoundError = '';
        this._notificationAudio = new Audio(this._notificationSoundUrl);
        this._notificationAudio.preload = 'auto';
        this._notificationAudio.volume = 0.8;
        this._soundInitialized = true;
        this._notificationAudio.addEventListener('error', () => {
            this._lastSoundError = 'No se pudo cargar el archivo MP3 de notificación.';
            console.error('[conversacionesUi] audio file error', this._notificationSoundUrl);
        });
        this.initAudioPlayers();

        const unlock = () => {
            window.conversacionesUi.unlockNotificationSound();
        };

        window.addEventListener('pointerdown', unlock, { once: true, passive: true });
        window.addEventListener('keydown', unlock, { once: true });
    },

    initAudioPlayers: function () {
        if (this._audioPlayersInitialized) return true;
        this._audioPlayersInitialized = true;

        const getPlayer = target => target?.closest?.('[data-audio-player]');
        const getAudio = player => player?.parentElement?.querySelector?.('audio.wa-audio-player__media');
        const formatTime = value => {
            if (!Number.isFinite(value) || value < 0) return '0:00';
            const total = Math.floor(value);
            const minutes = Math.floor(total / 60);
            const seconds = String(total % 60).padStart(2, '0');
            return `${minutes}:${seconds}`;
        };
        const setProgressFill = (seek, percent) => {
            seek.style.setProperty('--audio-progress', `${Math.max(0, Math.min(100, percent))}%`);
        };
        const updatePlayer = player => {
            const audio = getAudio(player);
            if (!audio) return;

            const playIcon = player.querySelector('[data-audio-play-icon]');
            const playButton = player.querySelector('[data-audio-play]');
            const seek = player.querySelector('[data-audio-seek]');
            const current = player.querySelector('[data-audio-current]');
            const duration = player.querySelector('[data-audio-duration]');
            const hasDuration = Number.isFinite(audio.duration) && audio.duration > 0;
            const percent = hasDuration ? (audio.currentTime / audio.duration) * 100 : 0;

            player.classList.toggle('is-playing', !audio.paused && !audio.ended);
            if (playIcon) {
                playIcon.className = !audio.paused && !audio.ended ? 'bi bi-pause-fill' : 'bi bi-play-fill';
            }
            if (playButton) {
                const label = !audio.paused && !audio.ended ? 'Pausar audio' : 'Reproducir audio';
                playButton.title = label;
                playButton.setAttribute('aria-label', label);
            }
            if (seek) {
                seek.value = hasDuration ? Math.round((audio.currentTime / audio.duration) * Number(seek.max || 1000)) : 0;
                setProgressFill(seek, percent);
            }
            if (current) current.textContent = formatTime(audio.currentTime);
            if (duration) duration.textContent = hasDuration ? formatTime(audio.duration) : '0:00';
        };
        const pauseOtherPlayers = currentAudio => {
            document.querySelectorAll('audio.wa-audio-player__media').forEach(audio => {
                if (audio !== currentAudio && !audio.paused) audio.pause();
            });
        };

        document.addEventListener('click', async event => {
            const playButton = event.target.closest?.('[data-audio-play]');
            if (playButton) {
                const player = getPlayer(playButton);
                const audio = getAudio(player);
                if (!audio) return;

                try {
                    if (audio.paused || audio.ended) {
                        pauseOtherPlayers(audio);
                        await audio.play();
                    } else {
                        audio.pause();
                    }
                    updatePlayer(player);
                } catch {
                    player?.classList.add('has-audio-error');
                }
                return;
            }

            const speedButton = event.target.closest?.('[data-audio-speed]');
            if (speedButton) {
                const player = getPlayer(speedButton);
                const audio = getAudio(player);
                if (!audio) return;

                const currentSpeed = Number(audio.playbackRate || 1);
                const index = this._audioSpeeds.findIndex(speed => Math.abs(speed - currentSpeed) < 0.01);
                const nextSpeed = this._audioSpeeds[(index + 1) % this._audioSpeeds.length];
                audio.playbackRate = nextSpeed;
                speedButton.textContent = Number.isInteger(nextSpeed) ? String(nextSpeed) : String(nextSpeed);
                speedButton.title = `Velocidad ${nextSpeed}x`;
            }
        });

        document.addEventListener('input', event => {
            const seek = event.target.closest?.('[data-audio-seek]');
            if (!seek) return;
            const player = getPlayer(seek);
            const audio = getAudio(player);
            if (!audio || !Number.isFinite(audio.duration) || audio.duration <= 0) return;

            audio.currentTime = (Number(seek.value) / Number(seek.max || 1000)) * audio.duration;
            updatePlayer(player);
        });

        document.addEventListener('loadedmetadata', event => {
            const audio = event.target;
            if (!audio?.matches?.('audio.wa-audio-player__media')) return;
            const player = audio.parentElement?.querySelector?.('[data-audio-player]');
            updatePlayer(player);
        }, true);

        ['timeupdate', 'play', 'pause', 'ended', 'durationchange'].forEach(name => {
            document.addEventListener(name, event => {
                const audio = event.target;
                if (!audio?.matches?.('audio.wa-audio-player__media')) return;
                const player = audio.parentElement?.querySelector?.('[data-audio-player]');
                if (name === 'ended') audio.currentTime = 0;
                updatePlayer(player);
            }, true);
        });

        document.querySelectorAll('[data-audio-player]').forEach(updatePlayer);
        return true;
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
        this._lastSoundAttemptAt = new Date().toISOString();
        try {
            if (this._notificationAudio) {
                this._notificationAudio.pause();
                this._notificationAudio.currentTime = 0;
                this._notificationAudio.volume = 0.85;
                try {
                    await this._notificationAudio.play();
                    this._lastSoundError = '';
                    return;
                } catch (error) {
                    this._lastSoundError = error?.message || 'audio.play() rechazado por autoplay policy.';
                    console.warn('[conversacionesUi] playNewMessageSound mp3 failed, fallback beep', error);
                }
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
            this._lastSoundError = '';
        } catch {
            this._lastSoundError = 'No se pudo reproducir el sonido de notificación.';
        }
    },

    getSoundDiagnostics: function () {
        return {
            soundInitialized: !!this._soundInitialized,
            audioUnlocked: !!this._audioUnlocked,
            hasNotificationAudio: !!this._notificationAudio,
            notificationSoundUrl: this._notificationSoundUrl || '',
            lastAttemptAt: this._lastSoundAttemptAt || '',
            lastError: this._lastSoundError || ''
        };
    },

    testNewMessageSound: async function () {
        await this.playNewMessageSound();
        return this.getSoundDiagnostics();
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

    scrollToMessage: function (messageId) {
        if (!messageId) return false;

        const element = document.getElementById(messageId);
        if (!element) return false;

        element.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'nearest' });
        element.classList.remove('message-row--jump-target');
        void element.offsetWidth;
        element.classList.add('message-row--jump-target');
        window.setTimeout(() => element.classList.remove('message-row--jump-target'), 2400);
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
        const addFilesToInput = files => {
            const input = getInput();
            if (!input || !files || files.length === 0) return false;

            const transfer = new DataTransfer();
            Array.from(files).forEach(file => transfer.items.add(file));
            if (transfer.files.length === 0) return false;

            input.files = transfer.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
            return true;
        };

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

            addFilesToInput(event.dataTransfer.files);
        };

        const paste = event => {
            const items = Array.from(event.clipboardData?.items || []);
            const imageFiles = items
                .filter(item => item.kind === 'file' && item.type?.startsWith('image/'))
                .map(item => item.getAsFile())
                .filter(file => file);

            if (imageFiles.length === 0) return;

            event.preventDefault();
            event.stopPropagation();

            const timestamp = new Date().toISOString().replace(/[-:T.Z]/g, '').slice(0, 14);
            const files = imageFiles.map((file, index) => {
                const extension = file.name && /\.[a-z0-9]{2,5}$/i.test(file.name)
                    ? file.name.slice(file.name.lastIndexOf('.'))
                    : (file.type === 'image/png' ? '.png' : file.type === 'image/webp' ? '.webp' : '.jpg');
                const name = file.name || `imagen_${timestamp}_${index + 1}${extension}`;
                return new File([file], name, { type: file.type || 'image/png', lastModified: Date.now() });
            });

            addFilesToInput(files);
        };

        const events = [
            { name: 'dragenter', handler: dragEnter },
            { name: 'dragover', handler: dragOver },
            { name: 'dragleave', handler: dragLeave },
            { name: 'drop', handler: drop },
            { name: 'paste', handler: paste }
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
    },

    bindAttachmentPreviewPan: function (element) {
        if (!element) return false;

        const previous = this._previewPanWatchers.get(element);
        if (previous) {
            previous.events.forEach(item => element.removeEventListener(item.name, item.handler));
            element.classList.remove('is-dragging');
        }

        let dragging = false;
        let startX = 0;
        let startY = 0;
        let startLeft = 0;
        let startTop = 0;

        const pointerDown = event => {
            if (event.button !== 0) return;
            dragging = true;
            startX = event.clientX;
            startY = event.clientY;
            startLeft = element.scrollLeft;
            startTop = element.scrollTop;
            element.classList.add('is-dragging');
            element.setPointerCapture?.(event.pointerId);
            event.preventDefault();
        };

        const pointerMove = event => {
            if (!dragging) return;
            element.scrollLeft = startLeft - (event.clientX - startX);
            element.scrollTop = startTop - (event.clientY - startY);
            event.preventDefault();
        };

        const endDrag = event => {
            if (!dragging) return;
            dragging = false;
            element.classList.remove('is-dragging');
            element.releasePointerCapture?.(event.pointerId);
        };

        const events = [
            { name: 'pointerdown', handler: pointerDown },
            { name: 'pointermove', handler: pointerMove },
            { name: 'pointerup', handler: endDrag },
            { name: 'pointercancel', handler: endDrag },
            { name: 'pointerleave', handler: endDrag }
        ];

        events.forEach(item => element.addEventListener(item.name, item.handler));
        this._previewPanWatchers.set(element, { events: events });
        return true;
    }
};
