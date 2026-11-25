import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../CLMS.Web/Components/Notification/DesktopNotificationCenter.razor.js', import.meta.url), 'utf8');

async function setup(t) {
    const saved = Object.fromEntries(['Notification', 'isSecureContext', 'localStorage', 'window'].map(key => [key, globalThis[key]]));
    const values = new Map();
    const delivered = [];
    let requests = 0;
    class FakeNotification {
        static permission = 'granted';
        static async requestPermission() { requests++; return this.permission; }
        constructor(title, options) { delivered.push({ title, options, instance: this }); }
        close() { this.closed = true; }
    }
    globalThis.Notification = FakeNotification;
    globalThis.isSecureContext = true;
    globalThis.localStorage = { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value) };
    globalThis.window = { focus() { } };
    t.after(() => Object.assign(globalThis, saved));
    const api = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}#${Math.random()}`);
    const message = () => ({ id: crypto.randomUUID(), title: 'PC notice', body: '<plain text>', expiresAt: new Date(Date.now() + 60000).toISOString() });
    return { api, values, delivered, message, requests: () => requests };
}

test('OS permission alone does not opt in this user, and preferences are user-specific', async t => {
    const { api, values, delivered, message } = await setup(t);
    assert.equal(api.show(message(), 'user-a'), false);
    values.set('clms.desktop-notifications.user-a', 'true');
    assert.equal(api.show(message(), 'user-a'), true);
    assert.equal(api.show(message(), 'user-b'), false);
    assert.equal(delivered.length, 1);
});

test('denied permission, expired payloads, and duplicate IDs do not display', async t => {
    const { api, values, delivered, message, requests } = await setup(t);
    values.set('clms.desktop-notifications.a', 'true');
    Notification.permission = 'denied';
    assert.equal(api.show(message(), 'a'), false);
    assert.equal(requests(), 0);
    Notification.permission = 'granted';
    assert.equal(api.show({ ...message(), expiresAt: 'invalid' }, 'a'), false);
    assert.equal(api.show({ ...message(), expiresAt: new Date(Date.now() - 1000).toISOString() }, 'a'), false);
    const item = message();
    assert.equal(api.show(item, 'a'), true);
    assert.equal(api.show(item, 'a'), false);
    assert.equal(delivered.length, 1);
    assert.equal(delivered[0].options.body, '<plain text>');
});

test('permission is requested only by a direct click and listeners are disposed', async t => {
    const { api, requests } = await setup(t);
    const callbacks = new Map();
    const button = {
        addEventListener: (type, handler) => callbacks.set(type, handler),
        removeEventListener: type => callbacks.delete(type)
    };
    const statuses = [];
    api.initialize(button, { invokeMethodAsync: async (_, value) => statuses.push(value) }, 'a');
    assert.equal(requests(), 0);
    await callbacks.get('click')();
    assert.equal(requests(), 1);
    assert.deepEqual(statuses, ['enabled']);
    await callbacks.get('click')();
    assert.equal(requests(), 1);
    assert.deepEqual(statuses, ['enabled', 'off']);
    api.dispose(button);
    assert.equal(callbacks.size, 0);
});

test('insecure or unsupported browsers fall back without requesting permission', async t => {
    const { api, requests } = await setup(t);
    globalThis.isSecureContext = false;
    assert.equal(api.status('a'), 'unsupported');
    assert.equal(requests(), 0);
});

test('seen IDs are bounded, persisted, and isolated by account', async t => {
    const { api, values } = await setup(t);
    const ids = Array.from({ length: 205 }, () => crypto.randomUUID());
    api.markRead('admin-a', ids);
    assert.deepEqual(api.readIds('admin-a'), ids.slice(-200));
    assert.deepEqual(api.readIds('admin-b'), []);
    assert.equal(JSON.parse(values.get('clms.read-notifications.admin-a')).length, 200);
    api.markRead('admin-a', [ids.at(-1)]);
    assert.equal(api.readIds('admin-a').length, 200);
});

test('clicking a desktop notification marks it read and suppresses redisplay after module reload', async t => {
    const { api, values, delivered, message } = await setup(t);
    values.set('clms.desktop-notifications.a', 'true');
    const notification = message();
    assert.equal(api.show(notification, 'a'), true);
    delivered[0].instance.onclick();
    assert.deepEqual(api.readIds('a'), [notification.id]);
    assert.equal(delivered[0].instance.closed, true);
    const reloaded = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}#${Math.random()}`);
    assert.equal(reloaded.show(notification, 'a'), false);
    assert.equal(delivered.length, 1);
});

test('other CLMS tabs update read state and disposed notification centers stop listening', async t => {
    const { api, values } = await setup(t);
    const events = new Map(), clicks = new Map(), calls = [];
    window.addEventListener = (name, callback) => events.set(name, callback);
    window.removeEventListener = name => events.delete(name);
    const button = { addEventListener: (name, callback) => clicks.set(name, callback), removeEventListener: name => clicks.delete(name) };
    api.initialize(button, { invokeMethodAsync: async (...args) => calls.push(args) }, 'admin-a');
    const id = crypto.randomUUID();
    values.set('clms.read-notifications.admin-a', JSON.stringify([id]));
    events.get('storage')({ key: 'clms.read-notifications.admin-b' });
    assert.equal(calls.length, 0);
    events.get('storage')({ key: 'clms.read-notifications.admin-a' });
    assert.deepEqual(calls, [['ReadStateChanged', 'admin-a', [id]]]);
    api.dispose(button);
    assert.equal(events.size, 0);
    assert.equal(clicks.size, 0);
});

test('read state still works for the session when browser storage is blocked', async t => {
    const { api } = await setup(t);
    globalThis.localStorage = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
    const id = crypto.randomUUID();
    assert.deepEqual(api.markRead('a', [id]), [id]);
    assert.deepEqual(api.readIds('a'), [id]);
});
