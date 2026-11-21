window.clms = window.clms || {};

window.clms.scrollToTop = function (elementId) {
    document.getElementById(elementId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

window.clms.refreshIcons = () => window.lucide?.createIcons?.();

document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    document.querySelectorAll('.month-cell:hover, .month-cell:focus-within').forEach(cell => {
        cell.dataset.dismissed = 'true';
    });
});

function resetCalendarPreview(event) {
    const cell = event.target.closest?.('.month-cell[data-dismissed]');
    if (cell && !cell.contains(event.relatedTarget)) delete cell.dataset.dismissed;
}
document.addEventListener('pointerover', resetCalendarPreview);
document.addEventListener('focusin', resetCalendarPreview);

function actionTrigger(panel) {
    return document.querySelector(`[data-action-menu="${panel.id}"]`);
}

function positionActionMenu(panel) {
    const trigger = actionTrigger(panel);
    const rect = trigger?.getBoundingClientRect();
    const scrollBounds = trigger?.closest('.table-scroll')?.getBoundingClientRect();
    if (!rect?.width || !rect.height || rect.bottom < 0 || rect.top >= innerHeight ||
        rect.right < 0 || rect.left >= innerWidth ||
        (scrollBounds && (rect.bottom <= scrollBounds.top || rect.top >= scrollBounds.bottom ||
            rect.right <= scrollBounds.left || rect.left >= scrollBounds.right))) {
        panel.hidePopover();
        return;
    }

    const { width, height } = panel.getBoundingClientRect();
    panel.style.left = `${Math.max(8, Math.min(rect.right - width, innerWidth - width - 8))}px`;
    const below = rect.bottom + 4;
    const top = below + height <= innerHeight - 8 ? below : rect.top - height - 4;
    panel.style.top = `${Math.max(8, Math.min(top, innerHeight - height - 8))}px`;
}

window.clms.toggleActionMenu = function (id) {
    const panel = document.getElementById(id);
    if (panel?.matches(':popover-open')) {
        panel.hidePopover();
        return;
    }
    document.querySelectorAll('[data-action-menu][aria-expanded="true"]').forEach(trigger =>
        trigger.setAttribute('aria-expanded', 'false'));
    panel?.showPopover();
    if (panel?.matches(':popover-open')) positionActionMenu(panel);
};

document.addEventListener('toggle', event => {
    const panel = event.target;
    if (!panel.matches?.('.action-menu-panel')) return;
    const trigger = actionTrigger(panel);
    const open = event.newState === 'open';
    trigger?.setAttribute('aria-expanded', String(open));
    if (open) {
        positionActionMenu(panel);
        if (panel.matches(':popover-open')) panel.querySelector('button:not(:disabled), a[href]')?.focus({ preventScroll: true });
    } else if (document.activeElement === document.body || panel.contains(document.activeElement)) {
        trigger?.focus({ preventScroll: true });
    }
}, true);

document.addEventListener('focusin', event => {
    document.querySelectorAll('.action-menu-panel:popover-open').forEach(panel => {
        if (!panel.contains(event.target) && event.target !== actionTrigger(panel)) panel.hidePopover();
    });
});

document.addEventListener('click', event => {
    const action = event.target.closest?.('.action-menu-panel button, .action-menu-panel a');
    if (action && !action.disabled) action.closest('.action-menu-panel').hidePopover();
    else if (!event.target.closest?.('.action-menu-panel, [data-action-menu]')) {
        document.querySelectorAll('.action-menu-panel:popover-open').forEach(panel => panel.hidePopover());
    }
});

document.addEventListener('keydown', event => {
    const panel = event.target.closest?.('.action-menu-panel');
    if (!panel && event.key === 'Escape') {
        document.querySelector('.action-menu-panel:popover-open')?.hidePopover();
        return;
    }
    if (!panel || !['ArrowDown', 'ArrowUp', 'Home', 'End', 'Escape'].includes(event.key)) return;
    if (event.key === 'Escape') {
        event.preventDefault();
        panel.hidePopover();
        actionTrigger(panel)?.focus({ preventScroll: true });
        return;
    }
    const items = [...panel.querySelectorAll('button:not(:disabled), a[href]')];
    if (!items.length) return;
    event.preventDefault();
    const index = items.indexOf(document.activeElement);
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 :
        (index + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    items[next].focus();
});

function updateActionMenus(event) {
    if (event?.target?.closest?.('.action-menu-panel')) return;
    document.querySelectorAll('.action-menu-panel:popover-open').forEach(positionActionMenu);
}
document.addEventListener('scroll', updateActionMenus, true);
window.addEventListener('resize', updateActionMenus);
