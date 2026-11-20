// Display only. Editing browser storage or disabling JavaScript never bypasses server limits.
(() => {
    const resendButton = document.querySelector('[data-resend-at]');
    const recoveryButton = document.querySelector('[data-recovery-cooldown]');
    const storageKey = 'clms.passwordRecovery.retryAt';
    let recoveryDeadline = 0;
    if (recoveryButton) {
        try {
            const saved = Number(sessionStorage.getItem(storageKey));
            if (Number.isFinite(saved) && saved > Date.now()) recoveryDeadline = Math.min(saved, Date.now() + 900000);
        } catch { /* Storage can be disabled; the server still enforces the limit. */ }
        if (recoveryButton.dataset.recoveryResponse === 'true') {
            const seconds = Math.max(0, Math.min(900, Number(recoveryButton.dataset.recoveryCooldown) || 0));
            recoveryDeadline = Date.now() + seconds * 1000;
            try { sessionStorage.setItem(storageKey, String(recoveryDeadline)); } catch { }
        }
    }
    function update() {
        if (resendButton) {
            const seconds = Math.ceil((Date.parse(resendButton.dataset.resendAt) - Date.now()) / 1000);
            resendButton.disabled = seconds > 0;
            const status = document.querySelector('[data-resend-status]');
            if (status) status.textContent = seconds > 0 ? ` Available in ${seconds} seconds.` : '';
        }
        if (recoveryButton) {
            const seconds = Math.max(0, Math.ceil((recoveryDeadline - Date.now()) / 1000));
            recoveryButton.disabled = seconds > 0;
            const countdown = `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
            const label = document.querySelector('[data-recovery-label]');
            if (label) label.textContent = seconds > 0 ? `Send in ${countdown}` : 'Send reset link';
            const status = document.querySelector('[data-recovery-status]');
            if (status) status.textContent = seconds > 0 ? `You can send again in ${countdown}.` : '';
            if (seconds === 0) { try { sessionStorage.removeItem(storageKey); } catch { } }
        }
    }
    update();
    const timer = setInterval(update, 1000);
    for (const form of document.querySelectorAll('form')) form.addEventListener('submit', () => {
        clearInterval(timer);
        for (const submit of form.querySelectorAll('button')) submit.disabled = true;
        const status = document.querySelector('[data-submit-status]');
        if (status) status.textContent = 'Submitting…';
    });
})();
