const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const scripts = path.join(__dirname, '../src/AlfaCore/wwwroot/js');

function reconnectHarness(reconnect = async () => true) {
    let state = 'hide';
    let observer;
    let reloads = 0;
    let nextTimer = 0;
    const timers = new Map();
    const status = {};
    const buttons = {};
    const attributes = {};
    const modal = {
        dataset: {},
        classList: { contains: value => value === `components-reconnect-${state}` },
        setAttribute: (key, value) => { attributes[key] = value; },
        querySelector: selector => {
            if (selector === '#components-reconnect-status') return status;
            return buttons[selector] ??= { addEventListener: (_, fn) => { buttons[selector].click = fn; } };
        }
    };
    const window = {
        location: { reload: () => { reloads++; } },
        Blazor: { reconnect },
        setTimeout: fn => { timers.set(++nextTimer, fn); return nextTimer; },
        clearTimeout: id => timers.delete(id),
        addEventListener: () => {}
    };
    vm.runInNewContext(fs.readFileSync(path.join(scripts, 'reconnect-modal.js'), 'utf8'), {
        window,
        document: { readyState: 'complete', getElementById: () => modal },
        MutationObserver: class {
            constructor(fn) { observer = fn; }
            observe() {}
        }
    });
    return {
        state: value => { state = value; observer(); },
        flushTimers: () => { const pending = [...timers.values()]; timers.clear(); pending.forEach(fn => fn()); },
        retry: () => buttons['[data-reconnect-action="retry"]'].click(),
        reload: () => buttons['[data-reconnect-action="reload"]'].click(),
        get reloads() { return reloads; },
        status, attributes, window
    };
}

test('un corte breve recupera el circuito sin recargar ni perder el estado', () => {
    const h = reconnectHarness();
    h.state('show');
    assert.equal(h.attributes['aria-hidden'], 'false');
    h.state('hide');
    h.flushTimers();
    assert.equal(h.reloads, 0);
    assert.equal(h.attributes['aria-hidden'], 'true');
});

test('un reintento pendiente se cancela si la conexión vuelve', () => {
    let attempts = 0;
    const h = reconnectHarness(async () => { attempts++; return true; });
    h.state('failed');
    h.state('hide');
    h.flushTimers();
    assert.equal(attempts, 0);
    assert.equal(h.reloads, 0);
});

test('un circuito descartado espera la recarga manual', () => {
    const h = reconnectHarness();
    h.state('show');
    h.state('rejected');
    h.flushTimers();
    assert.equal(h.reloads, 0);
    h.reload();
    assert.equal(h.reloads, 1);
});

test('el fallo del reintento no fuerza una recarga', async () => {
    const h = reconnectHarness(async () => { throw new Error('offline'); });
    h.state('failed');
    h.retry();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(h.reloads, 0);
    assert.match(h.status.textContent, /recargar manualmente/);
    h.window.Blazor = null;
    h.retry();
    assert.equal(h.reloads, 0);
});

test('dos Enter durante un envío, incluso tras reenlazar el handler, invocan una sola operación', async () => {
    const context = vm.createContext({ window: {}, document: {}, console });
    vm.runInContext(fs.readFileSync(path.join(scripts, 'conversaciones.js'), 'utf8'), context);
    const ui = context.window.conversacionesUi;
    ui.autoResizeTextarea = () => {};
    const handlers = {};
    const element = {
        addEventListener: (name, fn) => { handlers[name] = fn; },
        removeEventListener: name => { delete handlers[name]; }
    };
    let calls = 0;
    let finish;
    const ref = { invokeMethodAsync: () => { calls++; return new Promise(resolve => { finish = resolve; }); } };
    const enter = overrides => handlers.keydown({ key: 'Enter', preventDefault() {}, ...overrides });
    ui.bindReplyEnter(element, ref);
    enter({ shiftKey: true });
    enter({ repeat: true });
    enter({ isComposing: true });
    assert.equal(calls, 0);
    enter();
    ui.bindReplyEnter(element, ref);
    enter();
    assert.equal(calls, 1);
    finish();
    await new Promise(resolve => setImmediate(resolve));
    enter();
    assert.equal(calls, 2);
    finish();
});
