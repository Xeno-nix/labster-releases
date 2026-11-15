const observers = new WeakMap();
const leases = new WeakMap();

export function stopLease(element) {
    const lease = leases.get(element);
    if (!lease) return;
    leases.delete(element);
    clearTimeout(lease.timer);
    lease.controller.abort();
    document.removeEventListener('visibilitychange', lease.visibility);
    window.removeEventListener('pagehide', lease.pagehide);
}

export function startLease(element, id, reference) {
    stopLease(element);
    const lease = { controller: new AbortController(), timer: null };
    const end = () => {
        if (leases.get(element) !== lease) return;
        stopLease(element);
        reference.invokeMethodAsync('LabSessionEnded', id).catch(() => {});
    };
    lease.visibility = () => { if (document.hidden || !element.isConnected) end(); };
    lease.pagehide = end;
    leases.set(element, lease);
    document.addEventListener('visibilitychange', lease.visibility);
    window.addEventListener('pagehide', lease.pagehide);
    const poll = async () => {
        if (leases.get(element) !== lease) return;
        if (document.hidden || !element.isConnected) { end(); return; }
        try {
            const response = await fetch(`/api/screen-cast/labs/${id}/lease`, {
                credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal: lease.controller.signal
            });
            if (leases.get(element) !== lease) return;
            if (response.status !== 204) { end(); return; }
        } catch (error) {
            if (error.name === 'AbortError') return;
            end(); return;
        }
        if (leases.get(element) === lease) lease.timer = setTimeout(poll, 5000);
    };
    void poll();
}

export function observe(element, reference) {
    let previousCapacity = 0;
    const observer = new ResizeObserver(entries => {
        const { width, height } = entries[0].contentRect;
        if (width <= 0 || height <= 0) return;
        const gap = 8;
        const columns = Math.max(1, Math.min(6, Math.floor((width + gap) / (280 + gap))));
        const tileWidth = Math.min((width - gap * (columns - 1)) / columns, height * 16 / 9);
        const rows = Math.max(1, Math.floor((height + gap) / (tileWidth * 9 / 16 + gap)));
        element.style.setProperty('--wall-columns', columns);
        element.style.setProperty('--wall-tile-width', `${tileWidth}px`);
        const capacity = columns * rows;
        if (capacity !== previousCapacity) {
            previousCapacity = capacity;
            reference.invokeMethodAsync('ResizeWall', capacity).catch(() => {});
        }
    });
    observer.observe(element);
    observers.set(element, observer);
}

export function disconnect(element) {
    stopLease(element);
    observers.get(element)?.disconnect();
    observers.delete(element);
}
