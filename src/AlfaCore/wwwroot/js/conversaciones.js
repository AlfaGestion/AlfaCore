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
    _columnResizerWatchers: new Map(),
    _previewPanWatchers: new WeakMap(),
    _previewKeyboardWatcher: null,
    _dateDividerWatchers: new WeakMap(),
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

    // El disparador usa el atributo nativo `popovertarget` (sin round-trip a Blazor para abrir/cerrar).
    // Este handler se cablea con un `onclick` HTML plano en el propio botón (no interop de Blazor),
    // así corre siempre en el mismo click nativo que abre el popover, sin depender de que un bind
    // previo (p.ej. en OnAfterRenderAsync) se haya registrado a tiempo.
    positionModeMenu: function (triggerElement) {
        const menu = document.getElementById('conversations-mode-menu');
        if (!menu || !triggerElement) return;
        const rect = triggerElement.getBoundingClientRect();
        const menuWidth = menu.offsetWidth || 178;
        const top = rect.bottom + 6;
        const left = Math.max(8, Math.min(rect.right - menuWidth, window.innerWidth - menuWidth - 8));
        menu.style.top = top + 'px';
        menu.style.left = left + 'px';
    },

    bindColumnResizers: function (rootId) {
        const root = document.getElementById(rootId);
        if (!root || this._columnResizerWatchers.has(rootId)) return false;

        const storageKeys = {
            inbox: 'alfacore.conversaciones.inboxWidth',
            ai: 'alfacore.conversaciones.aiWidth'
        };
        const cssProperties = {
            inbox: '--conversations-inbox-width',
            ai: '--conversations-ai-width'
        };
        const clampWidth = (kind, value) => {
            const container = kind === 'inbox'
                ? root.querySelector('.conversations-layout')
                : root;
            const available = container?.getBoundingClientRect().width || root.clientWidth;
            const minimum = kind === 'inbox' ? 280 : 320;
            const maximum = kind === 'inbox'
                ? Math.min(520, available * .46)
                : Math.min(650, available * .48);
            return Math.round(Math.max(minimum, Math.min(maximum, Number(value) || minimum)));
        };
        const setWidth = (kind, value, persist) => {
            const width = clampWidth(kind, value);
            root.style.setProperty(cssProperties[kind], `${width}px`);
            if (persist) {
                try {
                    localStorage.setItem(storageKeys[kind], String(width));
                } catch {
                }
            }
            return width;
        };

        Object.keys(storageKeys).forEach(kind => {
            try {
                const stored = Number(localStorage.getItem(storageKeys[kind]));
                if (stored > 0) setWidth(kind, stored, false);
            } catch {
            }
        });

        const cleanups = [];
        root.querySelectorAll('[data-conversations-resizer]').forEach(handle => {
            const kind = handle.dataset.conversationsResizer;
            if (!cssProperties[kind]) return;

            const startDrag = event => {
                if (event.button !== 0 || window.innerWidth <= 900) return;
                event.preventDefault();
                root.classList.add('conversations-is-resizing');
                handle.classList.add('is-dragging');

                const move = moveEvent => {
                    const bounds = (kind === 'inbox'
                        ? root.querySelector('.conversations-layout')
                        : root).getBoundingClientRect();
                    const width = kind === 'inbox'
                        ? moveEvent.clientX - bounds.left
                        : bounds.right - moveEvent.clientX;
                    setWidth(kind, width, false);
                };
                const stop = stopEvent => {
                    move(stopEvent);
                    const current = parseFloat(getComputedStyle(root).getPropertyValue(cssProperties[kind]));
                    setWidth(kind, current, true);
                    root.classList.remove('conversations-is-resizing');
                    handle.classList.remove('is-dragging');
                    window.removeEventListener('pointermove', move);
                    window.removeEventListener('pointerup', stop);
                    window.removeEventListener('pointercancel', stop);
                };

                window.addEventListener('pointermove', move, { passive: true });
                window.addEventListener('pointerup', stop, { once: true });
                window.addEventListener('pointercancel', stop, { once: true });
            };
            const resizeWithKeyboard = event => {
                if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
                event.preventDefault();
                const current = parseFloat(getComputedStyle(root).getPropertyValue(cssProperties[kind]))
                    || (kind === 'inbox' ? 410 : 390);
                const direction = event.key === 'ArrowRight' ? 1 : -1;
                const delta = kind === 'ai' ? -direction * 16 : direction * 16;
                setWidth(kind, current + delta, true);
            };

            handle.addEventListener('pointerdown', startDrag);
            handle.addEventListener('keydown', resizeWithKeyboard);
            cleanups.push(() => {
                handle.removeEventListener('pointerdown', startDrag);
                handle.removeEventListener('keydown', resizeWithKeyboard);
            });
        });

        const keepWidthsInRange = () => {
            Object.keys(cssProperties).forEach(kind => {
                const current = parseFloat(getComputedStyle(root).getPropertyValue(cssProperties[kind]));
                if (current > 0) setWidth(kind, current, false);
            });
        };
        window.addEventListener('resize', keepWidthsInRange, { passive: true });
        cleanups.push(() => window.removeEventListener('resize', keepWidthsInRange));
        this._columnResizerWatchers.set(rootId, cleanups);
        return true;
    },

    unbindColumnResizers: function (rootId) {
        const cleanups = this._columnResizerWatchers.get(rootId);
        if (!cleanups) return false;
        cleanups.forEach(cleanup => cleanup());
        this._columnResizerWatchers.delete(rootId);
        document.getElementById(rootId)?.classList.remove('conversations-is-resizing');
        return true;
    },

    isNearBottom: function (element) {
        if (!element) return false;
        const distance = element.scrollHeight - element.scrollTop - element.clientHeight;
        return distance < 96;
    },

    getThreadScrollState: function (element) {
        if (!element) {
            return { nearBottom: false, scrollTop: 0, scrollHeight: 0, clientHeight: 0 };
        }

        return {
            nearBottom: this.isNearBottom(element),
            scrollTop: element.scrollTop || 0,
            scrollHeight: element.scrollHeight || 0,
            clientHeight: element.clientHeight || 0
        };
    },

    scrollToBottom: function (element) {
        if (!element) return;
        element.scrollTop = Math.max(0, element.scrollHeight - element.clientHeight);
    },

    restoreScrollAfterPrepend: function (element, previousScrollTop, previousScrollHeight) {
        if (!element) return false;
        const currentScrollHeight = element.scrollHeight || 0;
        const previousTop = Number(previousScrollTop) || 0;
        const previousHeight = Number(previousScrollHeight) || 0;
        element.scrollTop = Math.max(0, previousTop + (currentScrollHeight - previousHeight));
        return true;
    },

    scrollToBottomStable: function (element) {
        if (!element) return Promise.resolve(false);

        return new Promise(resolve => {
            const startedAt = performance.now();
            const maximumDurationMs = 2200;
            const requiredStableFrames = 6;
            let lastScrollHeight = -1;
            let stableFrames = 0;
            let finished = false;
            let resolved = false;
            let frameId = 0;

            const forceBottom = () => {
                if (finished || !element.isConnected) return;

                const maximumScrollTop = Math.max(0, element.scrollHeight - element.clientHeight);
                element.scrollTop = maximumScrollTop;
                const distanceFromBottom = Math.abs(maximumScrollTop - element.scrollTop);
                const sameHeight = element.scrollHeight === lastScrollHeight;
                stableFrames = sameHeight && distanceFromBottom <= 1 ? stableFrames + 1 : 0;
                lastScrollHeight = element.scrollHeight;
            };

            const resizeObserver = typeof ResizeObserver === 'undefined'
                ? null
                : new ResizeObserver(() => {
                    stableFrames = 0;
                    forceBottom();
                });
            const mutationObserver = new MutationObserver(() => {
                stableFrames = 0;
                forceBottom();
            });

            const finish = () => {
                if (finished) return;
                forceBottom();
                finished = true;
                window.cancelAnimationFrame(frameId);
                resizeObserver?.disconnect();
                mutationObserver.disconnect();
                if (!resolved) {
                    resolved = true;
                    resolve(true);
                }
            };

            const tick = () => {
                forceBottom();
                const elapsed = performance.now() - startedAt;
                if (!resolved && stableFrames >= requiredStableFrames) {
                    resolved = true;
                    resolve(true);
                }
                if (elapsed >= maximumDurationMs) {
                    finish();
                    return;
                }

                frameId = window.requestAnimationFrame(tick);
            };

            resizeObserver?.observe(element);
            mutationObserver.observe(element, { childList: true, subtree: true, attributes: true });
            element.querySelectorAll('img, video, audio').forEach(media => {
                media.addEventListener('load', forceBottom, { once: true });
                media.addEventListener('loadedmetadata', forceBottom, { once: true });
                media.addEventListener('error', forceBottom, { once: true });
            });

            forceBottom();
            frameId = window.requestAnimationFrame(tick);
        });
    },

    bindDateDividers: function (element) {
        if (!element) return false;

        const previous = this._dateDividerWatchers.get(element);
        if (previous) {
            previous.disconnect();
        }

        const dividers = Array.from(element.querySelectorAll('.conversation-date-divider'));
        if (dividers.length === 0) return false;

        if (!('IntersectionObserver' in window)) {
            dividers.forEach(divider => divider.classList.add('is-visible'));
            return true;
        }

        const observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                entry.target.classList.toggle('is-visible', entry.isIntersecting);
            });
        }, {
            root: element,
            threshold: 0.01,
            rootMargin: '-2px 0px -2px 0px'
        });

        dividers.forEach(divider => observer.observe(divider));
        this._dateDividerWatchers.set(element, observer);
        return true;
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
        if (element._conversacionesReplyResizeHandler) {
            element.removeEventListener('input', element._conversacionesReplyResizeHandler);
        }

        const handler = (event) => {
            if (event.key !== 'Enter' || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) {
                return;
            }

            event.preventDefault();
            dotNetRef.invokeMethodAsync('SendComposerFromEnter').catch(() => {});
        };

        const resize = () => window.conversacionesUi.autoResizeTextarea(element, 44, 120);
        element.addEventListener('keydown', handler);
        element.addEventListener('input', resize);
        element._conversacionesReplyEnterHandler = handler;
        element._conversacionesReplyResizeHandler = resize;
        resize();
        return true;
    },

    bindAiInstructionEnter: function (element, dotNetRef) {
        if (!element || !dotNetRef) return false;

        if (element._conversacionesAiInstructionEnterHandler) {
            element.removeEventListener('keydown', element._conversacionesAiInstructionEnterHandler);
        }

        const handler = (event) => {
            if (event.isComposing
                || event.key !== 'Enter'
                || event.shiftKey
                || event.ctrlKey
                || event.altKey
                || event.metaKey) {
                return;
            }

            event.preventDefault();
            const value = element.value || '';
            if (!value.trim()) return;

            element.value = '';
            dotNetRef.invokeMethodAsync('SendAiInstructionFromEnter', value)
                .catch(() => {});
        };

        element.addEventListener('keydown', handler);
        element._conversacionesAiInstructionEnterHandler = handler;
        return true;
    },

    bindInternalNoteEnter: function (element, dotNetRef) {
        if (!element || !dotNetRef) return false;

        if (element._conversacionesInternalNoteEnterHandler) {
            element.removeEventListener('keydown', element._conversacionesInternalNoteEnterHandler);
        }
        if (element._conversacionesInternalNoteResizeHandler) {
            element.removeEventListener('input', element._conversacionesInternalNoteResizeHandler);
        }

        const handler = (event) => {
            if (event.key !== 'Enter' || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) {
                return;
            }

            event.preventDefault();
            dotNetRef.invokeMethodAsync('SendInternalNoteFromEnter', element.value || '').catch(() => {});
        };

        const resize = () => window.conversacionesUi.autoResizeTextarea(element, 44, 120);
        element.addEventListener('keydown', handler);
        element.addEventListener('input', resize);
        element._conversacionesInternalNoteEnterHandler = handler;
        element._conversacionesInternalNoteResizeHandler = resize;
        resize();
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

    scrollActiveAttachmentPreviewThumb: function (attachmentId) {
        if (!attachmentId) return false;

        const strip = document.querySelector('.attachment-preview-strip');
        const thumb = strip?.querySelector(`[data-preview-attachment-id="${attachmentId}"]`);
        if (!strip || !thumb) return false;

        thumb.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
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
        let lastPasteKey = '';
        let lastPasteAt = 0;
        const getInput = () => document.getElementById(inputId);
        const hasFiles = event => event.dataTransfer && Array.from(event.dataTransfer.types || []).includes('Files');
        const normalizeImageForWhatsApp = file => {
            return new Promise(resolve => {
                if (!file || !file.type?.startsWith('image/') || file.type === 'image/gif') {
                    resolve(file);
                    return;
                }

                const image = new Image();
                const objectUrl = URL.createObjectURL(file);
                image.onload = () => {
                    try {
                        const maxSide = 1600;
                        const scale = Math.min(1, maxSide / Math.max(image.naturalWidth || image.width, image.naturalHeight || image.height));
                        const width = Math.max(1, Math.round((image.naturalWidth || image.width) * scale));
                        const height = Math.max(1, Math.round((image.naturalHeight || image.height) * scale));
                        const canvas = document.createElement('canvas');
                        canvas.width = width;
                        canvas.height = height;
                        const ctx = canvas.getContext('2d');
                        ctx.fillStyle = '#ffffff';
                        ctx.fillRect(0, 0, width, height);
                        ctx.drawImage(image, 0, 0, width, height);
                        canvas.toBlob(blob => {
                            URL.revokeObjectURL(objectUrl);
                            if (!blob) {
                                resolve(file);
                                return;
                            }

                            const baseName = (file.name || 'imagen').replace(/\.[a-z0-9]{2,5}$/i, '');
                            resolve(new File([blob], `${baseName}.jpg`, {
                                type: 'image/jpeg',
                                lastModified: Date.now()
                            }));
                        }, 'image/jpeg', 0.88);
                    } catch {
                        URL.revokeObjectURL(objectUrl);
                        resolve(file);
                    }
                };
                image.onerror = () => {
                    URL.revokeObjectURL(objectUrl);
                    resolve(file);
                };
                image.src = objectUrl;
            });
        };

        const normalizeFilesForWhatsApp = async files => Promise.all(Array.from(files || []).map(normalizeImageForWhatsApp));

        const addFilesToInput = async files => {
            const input = getInput();
            if (!input || !files || files.length === 0) return false;

            const transfer = new DataTransfer();
            const normalizedFiles = await normalizeFilesForWhatsApp(files);
            normalizedFiles.forEach(file => transfer.items.add(file));
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

        const drop = async event => {
            if (!prevent(event)) return;
            dragDepth = 0;
            element.classList.remove('is-file-dragging');

            await addFilesToInput(event.dataTransfer.files);
        };

        const paste = async event => {
            const items = Array.from(event.clipboardData?.items || []);
            const imageFiles = items
                .filter(item => item.kind === 'file' && item.type?.startsWith('image/'))
                .map(item => item.getAsFile())
                .filter(file => file);

            if (imageFiles.length === 0) return;

            const pasteKey = imageFiles
                .map(file => `${file.name || 'clipboard'}:${file.type}:${file.size}:${file.lastModified || 0}`)
                .join('|');
            const now = Date.now();
            if (pasteKey === lastPasteKey && now - lastPasteAt < 900) {
                event.preventDefault();
                event.stopPropagation();
                return;
            }

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

            lastPasteKey = pasteKey;
            lastPasteAt = now;
            await addFilesToInput(files);
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
            previous.events.forEach(item => (item.target || element).removeEventListener(item.name, item.handler, item.options));
            element.classList.remove('is-dragging');
        }

        const surface = element.querySelector('.attachment-preview-drag-surface');
        const frame = element.querySelector('.attachment-preview-media-frame');
        const media = frame?.querySelector('img, video');
        if (!surface || !frame) {
            this._previewPanWatchers.delete(element);
            return false;
        }

        let dragging = false;
        let lastX = 0;
        let lastY = 0;
        let panX = 0;
        let panY = 0;
        let activePointerId = null;

        const readPixels = name => {
            const value = window.getComputedStyle(frame).getPropertyValue(name).trim();
            const parsed = Number.parseFloat(value);
            return Number.isFinite(parsed) ? parsed : 0;
        };

        const readZoom = () => {
            if (!media) return 1;
            const value = window.getComputedStyle(media).getPropertyValue('--preview-zoom').trim();
            const parsed = Number.parseFloat(value);
            return Number.isFinite(parsed) && parsed > 0 ? parsed : 1;
        };

        const stabilizeFrameSize = () => {
            if (!media) return;
            const zoom = readZoom();
            const rect = media.getBoundingClientRect();
            const width = rect.width / zoom;
            const height = rect.height / zoom;
            if (width > 0 && height > 0) {
                frame.style.setProperty('--preview-frame-width', `${width}px`);
                frame.style.setProperty('--preview-frame-height', `${height}px`);
            }
        };

        const applyPan = () => {
            frame.style.setProperty('--preview-pan-x', `${panX}px`);
            frame.style.setProperty('--preview-pan-y', `${panY}px`);
        };

        const resetPan = () => {
            panX = 0;
            panY = 0;
            applyPan();
        };

        stabilizeFrameSize();
        if (media instanceof HTMLImageElement && !media.complete)
            media.addEventListener('load', stabilizeFrameSize, { once: true });
        if (media instanceof HTMLVideoElement && media.readyState < 1)
            media.addEventListener('loadedmetadata', stabilizeFrameSize, { once: true });

        const startDrag = event => {
            if (event.pointerType === 'mouse' && event.button !== 0) return;
            dragging = true;
            activePointerId = event.pointerId;
            lastX = event.clientX;
            lastY = event.clientY;
            panX = readPixels('--preview-pan-x');
            panY = readPixels('--preview-pan-y');
            surface.setPointerCapture?.(event.pointerId);
            element.classList.add('is-dragging');
            event.preventDefault();
        };

        const moveDrag = event => {
            if (!dragging) return;
            if (activePointerId !== null && event.pointerId !== activePointerId) return;
            const deltaX = event.clientX - lastX;
            const deltaY = event.clientY - lastY;
            lastX = event.clientX;
            lastY = event.clientY;
            panX += deltaX;
            panY += deltaY;
            applyPan();
            event.preventDefault();
        };

        const endDrag = event => {
            if (!dragging) return;
            if (activePointerId !== null && event?.pointerId !== undefined && event.pointerId !== activePointerId) return;
            dragging = false;
            if (activePointerId !== null)
                surface.releasePointerCapture?.(activePointerId);
            activePointerId = null;
            element.classList.remove('is-dragging');
        };

        const events = [
            { target: surface, name: 'pointerdown', handler: startDrag, options: { capture: true, passive: false } },
            { target: surface, name: 'pointermove', handler: moveDrag, options: { capture: true, passive: false } },
            { target: surface, name: 'pointerup', handler: endDrag, options: { capture: true } },
            { target: surface, name: 'pointercancel', handler: endDrag, options: { capture: true } },
            { target: surface, name: 'lostpointercapture', handler: endDrag, options: { capture: true } }
        ];

        events.forEach(item => item.target.addEventListener(item.name, item.handler, item.options));
        this._previewPanWatchers.set(element, { events: events });
        resetPan();
        return true;
    },

    bindAttachmentPreviewKeyboard: function (dotNetRef) {
        if (!dotNetRef) return false;

        this.unbindAttachmentPreviewKeyboard();

        const keydown = event => {
            if (event.key !== 'Escape') return;
            const overlay = document.querySelector('.attachment-preview-overlay');
            if (!overlay) return;

            event.preventDefault();
            dotNetRef.invokeMethodAsync('CloseAttachmentPreviewFromKeyboard').catch(() => {});
        };

        document.addEventListener('keydown', keydown);
        this._previewKeyboardWatcher = keydown;
        return true;
    },

    unbindAttachmentPreviewKeyboard: function () {
        if (!this._previewKeyboardWatcher) return;

        document.removeEventListener('keydown', this._previewKeyboardWatcher);
        this._previewKeyboardWatcher = null;
    },

    autoResizeTextarea: function (element, minHeight, maxHeight) {
        if (!element || typeof element.style === 'undefined') return false;

        const min = Number(minHeight) > 0 ? Number(minHeight) : 44;
        const max = Number(maxHeight) > 0 ? Number(maxHeight) : 120;
        element.style.height = 'auto';
        const target = Math.min(max, Math.max(min, element.scrollHeight || min));
        element.style.height = `${target}px`;
        element.style.overflowY = (element.scrollHeight || 0) > max ? 'auto' : 'hidden';
        return true;
    },

    getElementValue: function (element) {
        return element?.value || '';
    },

    setElementValue: function (element, value, focus) {
        if (!element) return false;

        const text = value || '';
        element.value = text;

        try {
            const position = text.length;
            element.setSelectionRange(position, position);
        } catch {
        }

        this.autoResizeTextarea(element, 44, 120);

        if (focus) {
            try {
                element.focus({ preventScroll: true });
            } catch {
                element.focus();
            }
        }

        return true;
    },

    insertTextAtCursor: function (element, text) {
        if (!element) return text || '';

        const value = element.value || '';
        const insert = text || '';
        const start = Number.isInteger(element.selectionStart) ? element.selectionStart : value.length;
        const end = Number.isInteger(element.selectionEnd) ? element.selectionEnd : start;
        const nextValue = `${value.slice(0, start)}${insert}${value.slice(end)}`;
        element.value = nextValue;

        const nextPosition = start + insert.length;
        try {
            element.setSelectionRange(nextPosition, nextPosition);
        } catch {
        }

        this.autoResizeTextarea(element, 44, 120);
        try {
            element.focus({ preventScroll: true });
        } catch {
            element.focus();
        }

        return nextValue;
    }
};
