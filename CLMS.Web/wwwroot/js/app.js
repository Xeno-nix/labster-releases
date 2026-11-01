// Small app-wide helpers shared across pages (no libraries).

window.clms = window.clms || {};

// Smoothly bring an element to the top of the viewport. Used by the class-list
// pager after navigating to a new page.
window.clms.scrollToTop = function (elementId) {
    const el = document.getElementById(elementId);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};

// Popovers live in the browser's top layer, so scrollable tables cannot clip them.
document.addEventListener('beforetoggle', event => {
    const panel = event.target;
    if (!panel.matches?.('.action-menu-panel') || event.newState !== 'open') return;
    const trigger = document.querySelector(`[popovertarget="${panel.id}"]`);
    if (!trigger) return;
    const rect = trigger.getBoundingClientRect();
    panel.style.left = `${Math.max(8, Math.min(rect.right - 208, innerWidth - 216))}px`;
    panel.style.top = `${Math.max(8, rect.bottom + 4)}px`;
    requestAnimationFrame(() => {
        if (!panel.matches(':popover-open')) return;
        const height = panel.getBoundingClientRect().height;
        if (rect.bottom + 4 + height > innerHeight - 8)
            panel.style.top = `${Math.max(8, rect.top - height - 4)}px`;
        panel.querySelector('button:not(:disabled), a[href]')?.focus({ preventScroll: true });
    });
}, true);

document.addEventListener('click', event => {
    const action = event.target.closest('.action-menu-panel button, .action-menu-panel a');
    if (action && !action.disabled) action.closest('.action-menu-panel').hidePopover();
});

document.addEventListener('keydown', event => {
    const panel = event.target.closest('.action-menu-panel');
    if (!panel || !['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
    const items = [...panel.querySelectorAll('button:not(:disabled), a[href]')];
    if (!items.length) return;
    event.preventDefault();
    const index = items.indexOf(document.activeElement);
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 :
        (index + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    items[next].focus();
});

function closeActions(event) {
    if (event?.target?.closest?.('.action-menu-panel')) return;
    document.querySelectorAll('.action-menu-panel:popover-open').forEach(panel => panel.hidePopover());
}
document.addEventListener('scroll', closeActions, true);
window.addEventListener('resize', closeActions);
