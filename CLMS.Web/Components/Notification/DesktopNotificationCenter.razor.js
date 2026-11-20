const handlers = new WeakMap();
const shown = new Set();
const readCache = new Map();
const key = userId => `clms.desktop-notifications.${userId}`;
const readKey = userId => `clms.read-notifications.${userId}`;
const readEvent = 'clms-notifications-read';
const validId = id => typeof id === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id);
const supported = () => globalThis.isSecureContext && typeof globalThis.Notification === 'function';

export function readIds(userId) {
    try {
        const raw = localStorage.getItem(readKey(userId));
        if (raw && raw.length <= 20000) {
            const parsed = JSON.parse(raw);
            if (Array.isArray(parsed)) return [...new Set(parsed.filter(validId).map(id => id.toLowerCase()))].slice(-200);
        }
    } catch { /* Use session memory if browser storage is unavailable. */ }
    return readCache.get(userId) || [];
}

export function markRead(userId, ids) {
    const merged = [...new Set([...readIds(userId), ...ids.filter(validId).map(id => id.toLowerCase())])].slice(-200);
    readCache.set(userId, merged);
    if (readCache.size > 32) readCache.delete(readCache.keys().next().value);
    try { localStorage.setItem(readKey(userId), JSON.stringify(merged)); } catch { /* Session-only read state remains available. */ }
    if (window.dispatchEvent) window.dispatchEvent(new CustomEvent(readEvent, { detail: { userId } }));
    return merged;
}

function enabled(userId) {
    try { return localStorage.getItem(key(userId)) === 'true'; } catch { return false; }
}

export function status(userId) {
    if (!supported()) return 'unsupported';
    if (Notification.permission === 'denied') return 'denied';
    return Notification.permission === 'granted' && enabled(userId) ? 'enabled' : 'off';
}

export function initialize(button, reference, userId) {
    dispose(button);
    // Ask directly inside the browser click handler, preserving Safari's user gesture requirement.
    const handler = async () => {
        button.disabled = true;
        try {
            if (supported()) {
                if (status(userId) === 'enabled') localStorage.setItem(key(userId), 'false');
                else {
                    const permission = await Notification.requestPermission();
                    localStorage.setItem(key(userId), String(permission === 'granted'));
                }
            }
            await reference.invokeMethodAsync('PermissionChanged', status(userId));
        } catch {
            try { await reference.invokeMethodAsync('PermissionChanged', 'unavailable'); } catch { /* circuit closed */ }
        } finally { button.disabled = false; }
    };
    const syncReadState = () => {
        reference.invokeMethodAsync('ReadStateChanged', userId, readIds(userId)).catch(() => {});
    };
    const storage = event => { if (event.key === readKey(userId)) syncReadState(); };
    const read = event => { if (event.detail?.userId === userId) syncReadState(); };
    handlers.set(button, { handler, storage, read });
    button.addEventListener('click', handler);
    window.addEventListener?.('storage', storage);
    window.addEventListener?.(readEvent, read);
    return status(userId);
}

export function show(notification, userId) {
    if (status(userId) !== 'enabled' || !notification?.id || shown.has(notification.id) || readIds(userId).includes(notification.id.toLowerCase())
        || !notification.title || !notification.body || notification.title.length > 100 || notification.body.length > 1000
        || !Number.isFinite(Date.parse(notification.expiresAt)) || Date.parse(notification.expiresAt) <= Date.now()) return false;
    const toast = new Notification(notification.title, { body: notification.body, tag: `clms-${notification.id}` });
    shown.add(notification.id);
    if (shown.size > 256) shown.delete(shown.values().next().value);
    toast.onclick = () => { markRead(userId, [notification.id]); window.focus(); toast.close(); };
    return true;
}

export function dispose(button) {
    const binding = handlers.get(button);
    if (binding) {
        button.removeEventListener('click', binding.handler);
        window.removeEventListener?.('storage', binding.storage);
        window.removeEventListener?.(readEvent, binding.read);
    }
    handlers.delete(button);
}
