export function openSheet(dialog, receiver) {
    const previousFocus = document.activeElement;
    const grip = dialog.querySelector('.sheet-grip');
    const phone = matchMedia('(width < 640px)');
    let snap = 70;
    let drag = null;
    let outsideDown = false;
    let suppressClick = false;
    let closing = false;
    let disposed = false;

    const settle = next => {
        snap = next;
        dialog.dataset.snap = String(next);
        dialog.style.removeProperty('--sheet-drag-height');
        dialog.removeAttribute('data-dragging');
        grip?.setAttribute('aria-expanded', String(next === 95));
        grip?.setAttribute('aria-label', next === 95 ? 'Collapse sheet' : 'Expand sheet');
    };

    const requestClose = async () => {
        if (closing || disposed) return;
        closing = true;
        try {
            const dismissed = await receiver.invokeMethodAsync('RequestClose');
            if (!dismissed && !disposed) settle(snap);
        } catch {
            if (!disposed) settle(snap);
        } finally {
            closing = false;
        }
    };

    const cancel = event => {
        event.preventDefault();
        void requestClose();
    };
    const outside = event => {
        const rect = dialog.getBoundingClientRect();
        return event.target === dialog && (event.clientX < rect.left || event.clientX > rect.right ||
            event.clientY < rect.top || event.clientY > rect.bottom);
    };
    const down = event => { outsideDown = outside(event); };
    const click = event => {
        if (outsideDown && outside(event)) void requestClose();
        outsideDown = false;
    };

    const gripDown = event => {
        if (!phone.matches || closing || !event.isPrimary || event.button !== 0) return;
        drag = {
            pointer: event.pointerId, startY: event.clientY,
            startHeight: dialog.getBoundingClientRect().height, viewportHeight: window.innerHeight
        };
        grip.setPointerCapture(event.pointerId);
    };

    const gripMove = event => {
        if (drag?.pointer !== event.pointerId) return;
        const distance = drag.startY - event.clientY;
        if (Math.abs(distance) < 6 && !dialog.hasAttribute('data-dragging')) return;
        dialog.setAttribute('data-dragging', '');
        const height = Math.max(0, Math.min(drag.startHeight + distance, drag.viewportHeight * .95));
        dialog.style.setProperty('--sheet-drag-height', `${height}px`);
    };

    const finish = (event, cancelled = false) => {
        if (drag?.pointer !== event.pointerId) return;
        const { startY, startHeight, viewportHeight } = drag;
        drag = null;
        if (grip.hasPointerCapture(event.pointerId)) grip.releasePointerCapture(event.pointerId);
        const distance = startY - event.clientY;
        const moved = dialog.hasAttribute('data-dragging');
        if (cancelled || !moved) { settle(snap); return; }
        suppressClick = true;
        setTimeout(() => { suppressClick = false; }, 0);
        const height = startHeight + distance;
        if (height <= viewportHeight * .42) void requestClose();
        else if (distance > 36) settle(95);
        else if (distance < -36) settle(70);
        else settle(snap);
    };

    const gripUp = event => finish(event);
    const gripCancel = event => finish(event, true);
    const gripClick = event => {
        if (suppressClick) { event.preventDefault(); return; }
        if (phone.matches && !closing) settle(snap === 70 ? 95 : 70);
    };
    const gripKey = event => {
        if (!phone.matches || !['ArrowUp', 'ArrowDown'].includes(event.key)) return;
        event.preventDefault();
        settle(event.key === 'ArrowUp' ? 95 : 70);
    };

    dialog.addEventListener('cancel', cancel);
    dialog.addEventListener('pointerdown', down);
    dialog.addEventListener('click', click);
    grip?.addEventListener('pointerdown', gripDown);
    grip?.addEventListener('pointermove', gripMove);
    grip?.addEventListener('pointerup', gripUp);
    grip?.addEventListener('pointercancel', gripCancel);
    grip?.addEventListener('lostpointercapture', gripCancel);
    grip?.addEventListener('click', gripClick);
    grip?.addEventListener('keydown', gripKey);
    settle(70);
    dialog.showModal();

    const heading = dialog.querySelector('h2, h3, h4, h5');
    if (heading) {
        heading.tabIndex = -1;
        heading.focus({ preventScroll: true });
    }

    return {
        dispose() {
            disposed = true;
            dialog.removeEventListener('cancel', cancel);
            dialog.removeEventListener('pointerdown', down);
            dialog.removeEventListener('click', click);
            grip?.removeEventListener('pointerdown', gripDown);
            grip?.removeEventListener('pointermove', gripMove);
            grip?.removeEventListener('pointerup', gripUp);
            grip?.removeEventListener('pointercancel', gripCancel);
            grip?.removeEventListener('lostpointercapture', gripCancel);
            grip?.removeEventListener('click', gripClick);
            grip?.removeEventListener('keydown', gripKey);
            dialog.close();
            if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
        }
    };
}
