export function openSheet(dialog, receiver) {
    const previousFocus = document.activeElement;
    const cancel = event => {
        event.preventDefault();
        receiver.invokeMethodAsync('RequestClose');
    };
    let startedOutside = false;
    const outside = event => {
        const rect = dialog.getBoundingClientRect();
        return event.target === dialog && (event.clientX < rect.left || event.clientX > rect.right ||
            event.clientY < rect.top || event.clientY > rect.bottom);
    };
    const down = event => { startedOutside = outside(event); };
    const click = event => {
        if (startedOutside && outside(event)) receiver.invokeMethodAsync('RequestClose');
        startedOutside = false;
    };
    dialog.addEventListener('cancel', cancel);
    dialog.addEventListener('pointerdown', down);
    dialog.addEventListener('click', click);
    dialog.showModal();
    // Start at the heading rather than opening the phone keyboard immediately.
    const heading = dialog.querySelector('h2, h3, h4, h5');
    if (heading) {
        heading.tabIndex = -1;
        heading.focus({ preventScroll: true });
    }
    return {
        dispose() {
            dialog.removeEventListener('cancel', cancel);
            dialog.removeEventListener('pointerdown', down);
            dialog.removeEventListener('click', click);
            dialog.close();
            if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
        }
    };
}
