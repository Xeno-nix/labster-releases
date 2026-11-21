import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../CLMS.Web/Components/Pages/Monitoring/AllScreens.razor.js', import.meta.url), 'utf8');
const { startLease, stopLease } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const flush = () => new Promise(resolve => setImmediate(resolve));

function browser(t, fetcher, hidden = false) {
    const saved = Object.fromEntries(['document', 'window', 'fetch', 'setTimeout', 'clearTimeout'].map(key => [key, globalThis[key]]));
    const listeners = new Map();
    const timers = new Map();
    const ended = [];
    const events = {
        addEventListener: (name, callback) => listeners.set(name, callback),
        removeEventListener: name => listeners.delete(name)
    };
    globalThis.document = { hidden, ...events };
    globalThis.window = events;
    globalThis.fetch = fetcher;
    globalThis.setTimeout = callback => { timers.set(1, callback); return 1; };
    globalThis.clearTimeout = id => timers.delete(id);
    const element = { isConnected: true };
    const reference = { invokeMethodAsync(...args) { ended.push(args); return Promise.resolve(); } };
    t.after(() => {
        stopLease(element);
        for (const [key, value] of Object.entries(saved)) {
            if (value === undefined) delete globalThis[key];
            else globalThis[key] = value;
        }
    });
    return { element, reference, listeners, timers, ended };
}

test('A visible lab wall renews its lease and stopping releases browser resources', async t => {
    const requests = [];
    const env = browser(t, async (url, options) => {
        requests.push({ url, options });
        return new Response(null, { status: 204 });
    });
    startLease(env.element, 12, env.reference);
    await flush();
    assert.equal(requests[0].url, '/api/screen-cast/labs/12/lease');
    assert.equal(requests[0].options.cache, 'no-store');
    assert.equal(requests[0].options.credentials, 'same-origin');
    env.timers.get(1)();
    await flush();
    assert.equal(requests.length, 2);
    stopLease(env.element);
    assert.equal(env.timers.size, 0);
    assert.equal(env.listeners.size, 0);
    assert.equal(requests[0].options.signal.aborted, true);
});

test('A hidden tab does not renew the lab session', async t => {
    let requests = 0;
    const env = browser(t, async () => { requests++; }, true);
    startLease(env.element, 12, env.reference);
    await flush();
    assert.equal(requests, 0);
    assert.deepEqual(env.ended, [['LabSessionEnded', 12]]);
    assert.equal(env.listeners.size, 0);
});

test('Leaving the page ends the lab session even before document.hidden changes', async t => {
    const env = browser(t, async () => new Response(null, { status: 204 }));
    startLease(env.element, 12, env.reference);
    await flush();
    env.listeners.get('pagehide')();
    assert.deepEqual(env.ended, [['LabSessionEnded', 12]]);
    assert.equal(env.timers.size, 0);
});

test('An expired or unauthorized lab session stops polling', async t => {
    const env = browser(t, async () => new Response(null, { status: 404 }));
    startLease(env.element, 12, env.reference);
    await flush();
    assert.deepEqual(env.ended, [['LabSessionEnded', 12]]);
    assert.equal(env.timers.size, 0);
});

test('An old response cannot restart polling after the lab session stops', async t => {
    let resolve;
    const env = browser(t, () => new Promise(done => { resolve = done; }));
    startLease(env.element, 12, env.reference);
    stopLease(env.element);
    resolve(new Response(null, { status: 204 }));
    await flush();
    assert.equal(env.timers.size, 0);
    assert.deepEqual(env.ended, []);
});
