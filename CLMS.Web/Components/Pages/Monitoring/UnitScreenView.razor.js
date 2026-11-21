const viewers = new WeakMap();
const enlargements = new WeakMap();

export function closeEnlarged(image) {
    const enlarged = enlargements.get(image);
    if (!enlarged) return;
    enlargements.delete(image);
    enlarged.dialog.removeEventListener('close', enlarged.closed);
    enlarged.mirror.removeAttribute('src');
    if (enlarged.dialog.open) enlarged.dialog.close();
}

export function enlarge(image, dialog, mirror) {
    closeEnlarged(image);
    const enlarged = { dialog, mirror, closed: () => closeEnlarged(image) };
    enlargements.set(image, enlarged);
    const source = image.getAttribute('src');
    if (source) mirror.src = source;
    dialog.addEventListener('close', enlarged.closed);
    // A native modal keeps focus inside, supports Escape, and restores focus on close.
    dialog.showModal();
}

export function stop(image) {
    closeEnlarged(image);
    const viewer = viewers.get(image);
    if (!viewer) return;
    viewers.delete(image);
    viewer.controller.abort();
    clearTimeout(viewer.timer);
    document.removeEventListener('visibilitychange', viewer.visibility);
    window.removeEventListener('pagehide', viewer.pagehide);
    if (viewer.pendingUrl) {
        URL.revokeObjectURL(viewer.pendingUrl);
        viewer.pendingUrl = null;
    }
    if (viewer.url) URL.revokeObjectURL(viewer.url);
    image.removeAttribute('src');
}

export function start(image, sessionId, reference) {
    stop(image);
    const viewer = { controller: new AbortController(), sequence: 0, url: null, pendingUrl: null, timer: null };
    const end = reason => {
        if (viewers.get(image) !== viewer) return;
        stop(image);
        reference.invokeMethodAsync('ViewingEnded', sessionId, reason).catch(() => {});
    };
    viewer.visibility = () => {
        if (document.hidden) end('Viewing stopped because this browser tab is hidden. Start viewing again to resume.');
    };
    viewer.pagehide = () => end('Viewing stopped because the page was closed.');
    viewers.set(image, viewer);
    document.addEventListener('visibilitychange', viewer.visibility);
    window.addEventListener('pagehide', viewer.pagehide);

    const poll = async () => {
        if (viewers.get(image) !== viewer) return;
        if (!image.isConnected) { end('Viewing stopped because the unit details were closed.'); return; }
        if (document.hidden) { viewer.visibility(); return; }
        try {
            const response = await fetch(`/api/screen-cast/${encodeURIComponent(sessionId)}/frame?after=${viewer.sequence}`, {
                credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal: viewer.controller.signal
            });
            if (viewers.get(image) !== viewer) return;
            if (response.status === 204) {
                // This real browser request renewed the viewing lease; no new frame yet.
            } else if (response.ok && response.headers.get('Content-Type')?.startsWith('image/jpeg')) {
                const blob = await response.blob();
                if (viewers.get(image) !== viewer) return;
                const nextUrl = URL.createObjectURL(blob);
                viewer.pendingUrl = nextUrl;
                viewer.sequence = Number(response.headers.get('X-Frame-Sequence')) || viewer.sequence;
                try {
                    // Keep the displayed frame intact until its replacement is decoded.
                    const nextFrame = new Image();
                    nextFrame.src = nextUrl;
                    await nextFrame.decode();
                    if (viewers.get(image) !== viewer) return;
                    if (!image.isConnected) {
                        end('Viewing stopped because the unit details were closed.');
                        return;
                    }
                    const previous = viewer.url;
                    image.src = nextUrl;
                    const enlarged = enlargements.get(image);
                    if (enlarged) enlarged.mirror.src = nextUrl;
                    viewer.url = nextUrl;
                    viewer.pendingUrl = null;
                    if (previous) URL.revokeObjectURL(previous);
                } catch (error) {
                    if (viewers.get(image) !== viewer) return;
                    // A damaged frame should not blank the last successfully displayed one.
                    if (error.name !== 'EncodingError') throw error;
                } finally {
                    if (viewer.pendingUrl === nextUrl) {
                        URL.revokeObjectURL(nextUrl);
                        viewer.pendingUrl = null;
                    }
                }
            } else {
                end('Viewing ended. The agent disconnected, stopped sharing, or your viewing session expired.');
                return;
            }
        } catch (error) {
            if (error.name === 'AbortError') return;
            end('Screen connection lost. Check the server and start viewing again.');
            return;
        }
        if (viewers.get(image) === viewer) viewer.timer = setTimeout(poll, 500);
    };
    void poll();
}
