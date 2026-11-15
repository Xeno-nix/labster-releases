import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../CLMS.Web/Components/Pages/Monitoring/UnitScreenView.razor.js', import.meta.url), 'utf8');
const { start, stop, enlarge, closeEnlarged } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const flush = () => new Promise(resolve => setImmediate(resolve));

function browser(t, fetcher, hidden = false) {
    const saved = Object.fromEntries(['document', 'window', 'fetch', 'setTimeout', 'clearTimeout', 'Image'].map(key => [key, globalThis[key]]));
    const create = URL.createObjectURL;
    const revoke = URL.revokeObjectURL;
    const listeners = new Map();
    const timers = new Map();
    const revoked = [];
    const ended = [];
    let urls = 0;
    let decoder = () => Promise.resolve();
    globalThis.Image = class { decode() { return decoder(this); } };
    const events = {
        addEventListener: (name, callback) => listeners.set(name, callback),
        removeEventListener: name => listeners.delete(name)
    };
    globalThis.document = { hidden, ...events };
    globalThis.window = { ...events };
    globalThis.fetch = fetcher;
    globalThis.setTimeout = callback => { timers.set(1, callback); return 1; };
    globalThis.clearTimeout = id => timers.delete(id);
    URL.createObjectURL = () => `blob:frame-${++urls}`;
    URL.revokeObjectURL = url => revoked.push(url);
    const image = { isConnected: true, getAttribute(name) { return this[name] ?? null; }, removeAttribute(name) { delete this[name]; } };
    const reference = { invokeMethodAsync(...args) { ended.push(args); return Promise.resolve(); } };
    t.after(() => {
        stop(image);
        for (const [key, value] of Object.entries(saved)) {
            if (value === undefined) delete globalThis[key];
            else globalThis[key] = value;
        }
        URL.createObjectURL = create;
        URL.revokeObjectURL = revoke;
    });
    return { image, reference, listeners, timers, revoked, ended, setDecoder(callback) { decoder = callback; } };
}

function enlargedView() {
    const listeners = new Map();
    const dialog = {
        open: false,
        addEventListener: (name, callback) => listeners.set(name, callback),
        removeEventListener: name => listeners.delete(name),
        showModal() { this.open = true; },
        close() { this.open = false; listeners.get('close')?.(); }
    };
    const mirror = { removeAttribute(name) { delete this[name]; } };
    return { dialog, mirror, listeners };
}

test('Enlarging shares live frames without another request loop and closing preserves capture', async t => {
    let requests = 0;
    const env = browser(t, async () => {
        requests++;
        return new Response(new Blob(['jpeg']), { headers: { 'Content-Type': 'image/jpeg' } });
    });
    start(env.image, 'session-one', env.reference);
    await flush();
    const view = enlargedView();
    enlarge(env.image, view.dialog, view.mirror);
    assert.equal(view.dialog.open, true);
    assert.equal(view.mirror.src, env.image.src);
    assert.equal(requests, 1);
    env.timers.get(1)();
    await flush();
    assert.equal(view.mirror.src, 'blob:frame-2');
    assert.equal(requests, 2);
    closeEnlarged(env.image);
    assert.equal(view.dialog.open, false);
    assert.equal(view.mirror.src, undefined);
    assert.equal(view.listeners.size, 0);
    assert.equal(env.image.src, 'blob:frame-2');
    assert.equal(env.timers.size, 1);
    assert.deepEqual(env.ended, []);
});

test('Native dialog close releases the enlarged view and stopping capture closes an open dialog', async t => {
    const env = browser(t, async () => new Response(null, { status: 204 }));
    start(env.image, 'session-one', env.reference);
    await flush();
    const view = enlargedView();
    enlarge(env.image, view.dialog, view.mirror);
    view.dialog.close();
    assert.equal(view.listeners.size, 0);
    assert.equal(env.timers.size, 1);
    enlarge(env.image, view.dialog, view.mirror);
    stop(env.image);
    assert.equal(view.dialog.open, false);
    assert.equal(view.listeners.size, 0);
    assert.equal(env.timers.size, 0);
});

test('JPEG frames are displayed and stopping releases the image, timer and listeners', async t => {
    const requests = [];
    const env = browser(t, async (url, options) => {
        requests.push({ url, options });
        return new Response(new Blob(['jpeg']), { headers: { 'Content-Type': 'image/jpeg', 'X-Frame-Sequence': '7' } });
    });
    start(env.image, 'session-one', env.reference);
    await flush();
    assert.equal(env.image.src, 'blob:frame-1');
    assert.match(requests[0].url, /session-one\/frame\?after=0$/);
    assert.equal(requests[0].options.credentials, 'same-origin');
    assert.equal(requests[0].options.cache, 'no-store');
    env.timers.get(1)();
    await flush();
    assert.match(requests[1].url, /after=7$/);
    stop(env.image);
    assert.equal(env.image.src, undefined);
    assert.equal(env.timers.size, 0);
    assert.equal(env.listeners.size, 0);
    assert.deepEqual(env.revoked, ['blob:frame-1', 'blob:frame-2']);
});

test('An ended server session stops polling and reports its own session ID', async t => {
    const env = browser(t, async () => new Response(null, { status: 404 }));
    start(env.image, 'expired-session', env.reference);
    await flush();
    assert.equal(env.ended.length, 1);
    assert.deepEqual(env.ended[0].slice(0, 2), ['ViewingEnded', 'expired-session']);
    assert.equal(env.timers.size, 0);
    assert.equal(env.listeners.size, 0);
});

test('A hidden tab does not request frames or keep the viewing lease alive', async t => {
    let requests = 0;
    const env = browser(t, async () => { requests++; }, true);
    start(env.image, 'hidden-session', env.reference);
    await flush();
    assert.equal(requests, 0);
    assert.equal(env.ended[0][1], 'hidden-session');
    assert.equal(env.listeners.size, 0);
});

test('An in-flight response cannot restore a frame after stop', async t => {
    let finish;
    const env = browser(t, () => new Promise(resolve => { finish = resolve; }));
    start(env.image, 'stopped-session', env.reference);
    stop(env.image);
    finish(new Response(new Blob(['jpeg']), { headers: { 'Content-Type': 'image/jpeg' } }));
    await flush();
    assert.equal(env.image.src, undefined);
    assert.equal(env.timers.size, 0);
    assert.equal(env.listeners.size, 0);
    assert.equal(env.ended.length, 0);
});

test('Removing the view stops renewing the lease even if component cleanup cannot reach the browser', async t => {
    let requests = 0;
    const env = browser(t, async () => { requests++; return new Response(null, { status: 204 }); });
    start(env.image, 'removed-view', env.reference);
    await flush();
    assert.equal(requests, 1);
    env.image.isConnected = false;
    env.timers.get(1)();
    await flush();
    assert.equal(requests, 1);
    assert.equal(env.ended[0][1], 'removed-view');
    assert.equal(env.timers.size, 0);
});

test('The previous frame stays displayed and valid until the next frame finishes decoding', async t => {
    const env = browser(t, async () => new Response(new Blob(['jpeg']), { headers: { 'Content-Type': 'image/jpeg' } }));
    start(env.image, 'buffered-session', env.reference);
    await flush();
    assert.equal(env.image.src, 'blob:frame-1');
    let finishDecode;
    env.setDecoder(() => new Promise(resolve => { finishDecode = resolve; }));
    env.timers.get(1)();
    await flush();
    assert.equal(env.image.src, 'blob:frame-1');
    assert.deepEqual(env.revoked, []);
    finishDecode();
    await flush();
    assert.equal(env.image.src, 'blob:frame-2');
    assert.deepEqual(env.revoked, ['blob:frame-1']);
});

test('Stopping during decode releases both frames and cannot restore the stopped image', async t => {
    const env = browser(t, async () => new Response(new Blob(['jpeg']), { headers: { 'Content-Type': 'image/jpeg' } }));
    start(env.image, 'decode-stop-session', env.reference);
    await flush();
    let finishDecode;
    env.setDecoder(() => new Promise(resolve => { finishDecode = resolve; }));
    env.timers.get(1)();
    await flush();
    stop(env.image);
    finishDecode();
    await flush();
    assert.equal(env.image.src, undefined);
    assert.deepEqual(env.revoked.sort(), ['blob:frame-1', 'blob:frame-2']);
    assert.equal(env.timers.size, 0);
    assert.equal(env.ended.length, 0);
});

test('An undecodable frame is skipped without blanking the previous frame or ending the session', async t => {
    const env = browser(t, async () => new Response(new Blob(['jpeg']), { headers: { 'Content-Type': 'image/jpeg' } }));
    start(env.image, 'bad-frame-session', env.reference);
    await flush();
    env.setDecoder(() => Promise.reject(new DOMException('Invalid JPEG', 'EncodingError')));
    env.timers.get(1)();
    await flush();
    assert.equal(env.image.src, 'blob:frame-1');
    assert.deepEqual(env.revoked, ['blob:frame-2']);
    assert.equal(env.ended.length, 0);
    assert.equal(env.timers.size, 1);
});
