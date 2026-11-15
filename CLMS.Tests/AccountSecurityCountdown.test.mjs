import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const script = readFileSync(new URL('../CLMS.Web/wwwroot/js/account-security.js', import.meta.url), 'utf8');

function page({ seconds = null, storage = new Map(), now = 1_800_000_000_000, blockedStorage = false, resend = false } = {}) {
    const clock = { now };
    const button = { disabled: false, dataset: resend
        ? { resendAt: new Date(now + 60000).toISOString() }
        : { recoveryCooldown: String(seconds ?? 0), recoveryResponse: seconds === null ? 'false' : 'true' } };
    const status = { textContent: '' };
    const label = { textContent: '' };
    const submitting = { textContent: '' };
    let tick = () => {};
    let submit;
    const form = { addEventListener: (_, callback) => { submit = callback; }, querySelectorAll: () => [button] };
    class ClockDate extends Date { static now() { return clock.now; } }
    vm.runInNewContext(script, {
        Date: ClockDate,
        document: {
            querySelector: selector => selector === '[data-resend-at]' ? (resend ? button : null)
                : selector === '[data-recovery-cooldown]' ? (resend ? null : button)
                : selector === '[data-recovery-label]' ? label
                : selector === '[data-submit-status]' ? submitting : status,
            querySelectorAll: () => [form]
        },
        sessionStorage: {
            getItem(key) { if (blockedStorage) throw new Error('disabled'); return storage.get(key) ?? null; },
            setItem(key, value) { if (blockedStorage) throw new Error('disabled'); storage.set(key, value); },
            removeItem(key) { if (blockedStorage) throw new Error('disabled'); storage.delete(key); }
        },
        setInterval(callback) { tick = callback; return 1; },
        clearInterval() { tick = () => {}; }
    });
    return { button, status, label, submitting, storage, clock,
        advance(milliseconds) { clock.now += milliseconds; tick(); }, submit() { submit(); } };
}

test('reset-link countdown disables and re-enables sending', () => {
    const p = page({ seconds: 60 });
    assert.equal(p.button.disabled, true);
    assert.match(p.status.textContent, /01:00/);
    assert.equal(p.label.textContent, 'Send in 01:00');
    p.advance(59000); assert.equal(p.button.disabled, true);
    p.advance(1000); assert.equal(p.button.disabled, false);
    assert.equal(p.label.textContent, 'Send reset link');
    assert.equal(p.storage.size, 0);
});

test('refresh retains only the remaining deadline', () => {
    const first = page({ seconds: 60 }); first.advance(20000);
    assert.deepEqual([...first.storage.keys()], ['clms.passwordRecovery.retryAt']);
    assert.match([...first.storage.values()][0], /^\d+$/);
    const refreshed = page({ storage: first.storage, now: first.clock.now });
    assert.equal(refreshed.button.disabled, true);
    assert.match(refreshed.status.textContent, /00:40/);
    refreshed.advance(40000); assert.equal(refreshed.button.disabled, false);
});

test('server timing replaces a stale longer cooldown', () => {
    const long = page({ seconds: 900 }); assert.match(long.status.textContent, /15:00/);
    const response = page({ seconds: 60, storage: long.storage });
    assert.match(response.status.textContent, /01:00/);
});

test('disabled browser storage does not break the countdown', () => {
    const p = page({ seconds: 60, blockedStorage: true });
    assert.equal(p.button.disabled, true);
    p.advance(61000); assert.equal(p.button.disabled, false);
});

test('verification resend and submitting states still work', () => {
    const p = page({ resend: true }); assert.equal(p.button.disabled, true);
    p.advance(60000); assert.equal(p.button.disabled, false);
    p.submit(); assert.equal(p.button.disabled, true); assert.equal(p.submitting.textContent, 'Submitting…');
    p.advance(1000); assert.equal(p.button.disabled, true);
});
