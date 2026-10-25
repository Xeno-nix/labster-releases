const observers = new WeakMap();

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
    observers.get(element)?.disconnect();
    observers.delete(element);
}
