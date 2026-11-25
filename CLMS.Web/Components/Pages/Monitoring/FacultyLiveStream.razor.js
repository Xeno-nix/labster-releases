const controls = new WeakMap();

function gather(peer, signal) {
    if (signal.aborted) return Promise.reject(new Error('Live stream stopped.'));
    if (peer.iceGatheringState === 'complete') return Promise.resolve();
    return new Promise((resolve, reject) => {
        const changed = () => { if (peer.iceGatheringState === 'complete') finish(); };
        const aborted = () => finish(new Error('Live stream stopped.'));
        const finish = error => {
            clearTimeout(timeout);
            peer.removeEventListener('icegatheringstatechange', changed);
            signal.removeEventListener('abort', aborted);
            error ? reject(error) : resolve();
        };
        const timeout = setTimeout(() => finish(new Error('Workstation connection timed out.')), 10000);
        peer.addEventListener('icegatheringstatechange', changed);
        signal.addEventListener('abort', aborted, { once: true });
    });
}

export function initialize({ open, dialog, laboratory, laboratoryId, cancel, start, stop, status, pickerStatus }, reference) {
    dispose(open);
    laboratory ??= document.getElementById(laboratoryId);
    let active = null;
    let choosing = false;
    let ending = false;
    let disposed = false;
    let monitoringSessionId = null;
    const updateControls = () => {
        const live = active !== null;
        open.disabled = disposed || choosing || ending || !monitoringSessionId || !Number(laboratory.value);
        open.title = monitoringSessionId ? 'Share your screen with this session’s laboratory' : 'Start a session to share your screen';
        open.hidden = live;
        stop.disabled = !live;
        stop.hidden = !live;
        start.disabled = disposed || choosing || live || !monitoringSessionId || !Number(laboratory.value);
        cancel.disabled = choosing;
        status.toggleAttribute('data-live', live);
    };
    const end = async (message = 'Live stream stopped.') => {
        const session = active;
        if (!session) return;
        const restoreFocus = stop.ownerDocument?.activeElement === stop;
        active = null;
        ending = true;
        session.controller.abort();
        clearTimeout(session.timer);
        for (const item of session.peers.values()) { clearTimeout(item.timer); item.peer.onconnectionstatechange = null; item.peer.close(); }
        for (const track of session.media.getTracks()) { track.onended = null; track.stop(); }
        status.textContent = message;
        updateControls();
        try { await reference.invokeMethodAsync('StopBroadcast', session.id); }
        catch { /* Local tracks are already stopped. The server lease also expires. */ }
        finally {
            ending = false;
            updateControls();
            if (restoreFocus && !disposed && open.ownerDocument.activeElement === open.ownerDocument.body) open.focus();
        }
    };
    const showStatus = session => {
        if (active !== session) return;
        let connected = 0, failed = 0;
        for (const item of session.peers.values()) {
            if (item.peer.connectionState === 'connected') connected++;
            if (item.failed || item.peer.connectionState === 'failed') failed++;
        }
        const message = `Live · ${session.laboratory} · ${connected} of ${session.peers.size} workstations connected`;
        const detail = failed ? ` · ${failed} could not connect. Restart sharing to retry.` : '';
        if (status.textContent !== message + detail) status.textContent = message + detail;
    };
    const offer = async (session, remote) => {
        const peer = new RTCPeerConnection({ iceServers: [] });
        const item = { peer, answered: false, failed: false, timer: null };
        session.peers.set(remote.id, item);
        peer.onconnectionstatechange = () => {
            if (['connected', 'failed', 'closed'].includes(peer.connectionState)) { clearTimeout(item.timer); item.timer = null; }
            showStatus(session);
        };
        try {
            for (const track of session.media.getVideoTracks()) peer.addTransceiver(track, { direction: 'sendonly', streams: [session.media] });
            await peer.setLocalDescription(await peer.createOffer());
            await gather(peer, session.controller.signal);
            if (active !== session || session.peers.get(remote.id) !== item) return;
            await reference.invokeMethodAsync('OfferBroadcast', session.id, remote.id, peer.localDescription.sdp);
            if (active !== session || session.peers.get(remote.id) !== item) return;
            item.timer = setTimeout(() => {
                item.timer = null;
                if (active === session && peer.connectionState !== 'connected') { item.failed = true; peer.close(); showStatus(session); }
            }, 30000);
        } catch { item.failed = true; peer.close(); }
    };
    const poll = async session => {
        if (active !== session) return;
        try {
            const state = await reference.invokeMethodAsync('PollBroadcast', session.id);
            if (active !== session) return;
            const ids = new Set(state.peers.map(p => p.id));
            for (const [id, item] of session.peers) {
                if (!ids.has(id)) { clearTimeout(item.timer); item.peer.onconnectionstatechange = null; item.peer.close(); session.peers.delete(id); }
            }
            await Promise.all(state.peers.map(async remote => {
                if (!session.peers.has(remote.id)) await offer(session, remote);
                if (active !== session) return;
                const item = session.peers.get(remote.id);
                if (remote.answerSdp && item && !item.answered && !item.failed) {
                    item.answered = true;
                    try { await item.peer.setRemoteDescription({ type: 'answer', sdp: remote.answerSdp }); }
                    catch { clearTimeout(item.timer); item.failed = true; item.peer.close(); }
                }
            }));
            if (active !== session) return;
            showStatus(session);
            session.timer = setTimeout(() => void poll(session), 2000);
        } catch {
            if (active === session) await end('Live stream ended because the server connection was lost. Start again to resume.');
        }
    };
    const openPicker = () => {
        if (open.disabled || active || dialog.open) return;
        pickerStatus.textContent = '';
        updateControls();
        dialog.showModal();
    };
    const cancelPicker = event => { if (choosing) event.preventDefault(); };
    const begin = async () => {
        if (start.disabled || !dialog.open) return;
        const labId = Number(laboratory.value);
        const sessionId = monitoringSessionId;
        choosing = true;
        pickerStatus.textContent = 'Choose a screen, window, or tab in the browser picker.';
        updateControls();
        let media, state;
        try {
            if (!navigator.mediaDevices?.getDisplayMedia || !globalThis.RTCPeerConnection) throw new Error('unsupported');
            // Direct button handler preserves the user gesture required by the screen picker.
            media = await navigator.mediaDevices.getDisplayMedia({
                video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 15, max: 20 } }, audio: false
            });
            if (disposed || monitoringSessionId !== sessionId) { media.getTracks().forEach(track => track.stop()); return; }
            const videoTrack = media.getVideoTracks()[0];
            if (!videoTrack || videoTrack.readyState === 'ended') throw new Error('Screen sharing ended.');
            state = await reference.invokeMethodAsync('StartBroadcast', labId, sessionId);
            if (!state || disposed || monitoringSessionId !== sessionId) {
                media.getTracks().forEach(track => track.stop());
                if (state) await reference.invokeMethodAsync('StopBroadcast', state.id);
                pickerStatus.textContent = '';
                return;
            }
            if (videoTrack.readyState === 'ended') throw new Error('Screen sharing ended.');
            const session = { id: state.id, laboratory: state.laboratory, media, peers: new Map(), timer: null, controller: new AbortController() };
            active = session;
            videoTrack.onended = () => void end();
            showStatus(session);
            updateControls();
            dialog.close();
            stop.focus();
            void poll(session);
        } catch (error) {
            if (media) media.getTracks().forEach(track => track.stop());
            if (state) { try { await reference.invokeMethodAsync('StopBroadcast', state.id); } catch {} }
            if (!disposed) pickerStatus.textContent = error.name === 'NotAllowedError' ? 'Screen sharing was cancelled or denied. Choose what to share to try again.'
                : 'Could not share your screen. Use an HTTPS connection and a browser that supports screen sharing.';
        } finally { choosing = false; updateControls(); }
    };
    const stopped = () => void end();
    const leaving = () => { disposed = true; void end('Live stream stopped because the page was closed.'); };
    open.addEventListener('click', openPicker);
    dialog.addEventListener('cancel', cancelPicker);
    start.addEventListener('click', begin);
    stop.addEventListener('click', stopped);
    laboratory.addEventListener('change', updateControls);
    window.addEventListener('pagehide', leaving);
    const cleanup = () => {
        disposed = true;
        if (dialog.open) dialog.close();
        open.removeEventListener('click', openPicker);
        dialog.removeEventListener('cancel', cancelPicker);
        start.removeEventListener('click', begin);
        stop.removeEventListener('click', stopped);
        laboratory.removeEventListener('change', updateControls);
        window.removeEventListener('pagehide', leaving);
        void end();
    };
    cleanup.setSession = id => {
        if (monitoringSessionId === id) return;
        monitoringSessionId = id;
        if (dialog.open) dialog.close();
        void end('Live stream stopped because the monitoring session ended.');
        updateControls();
    };
    controls.set(open, cleanup);
    updateControls();
}

export function setSession(open, id) {
    controls.get(open)?.setSession(id);
}

export function dispose(open) {
    const cleanup = controls.get(open);
    controls.delete(open);
    cleanup?.();
}
