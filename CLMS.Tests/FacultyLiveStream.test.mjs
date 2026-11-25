import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../CLMS.Web/Components/Pages/Monitoring/FacultyLiveStream.razor.js', import.meta.url), 'utf8');
const { initialize, dispose, setSession } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const flush = async () => { for (let i = 0; i < 6; i++) await new Promise(resolve => setImmediate(resolve)); };
const element = () => ({ listeners: new Map(), addEventListener(name, fn) { this.listeners.set(name, fn); }, removeEventListener(name) { this.listeners.delete(name); } });

function browser(t) {
    const descriptors = Object.fromEntries(['navigator', 'window', 'RTCPeerConnection', 'setTimeout', 'clearTimeout'].map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
    const open = { ...element(), dataset: { preferredLab: '1' } };
    const dialog = { ...element(), open: false, showModal() { this.open = true; }, close() { this.open = false; } };
    const start = element(), stop = { ...element(), focus() { this.focused = true; } };
    const cancel = element(), lab = { ...element(), value: '2' }, pickerStatus = {};
    const status = { toggleAttribute(name, enabled) { this[name] = enabled; } };
    const window = element(), timers = new Map(), peers = [], calls = [];
    let nextTimer = 0;
    let remotePeers = [{ id: 'pc-1', unitName: 'PC 1', answerSdp: null }];
    let pollOverride;
    let gathering = false;
    const track = { stopped: 0, stop() { this.stopped++; } };
    const media = { getTracks: () => [track], getVideoTracks: () => [track] };
    let getDisplayMedia = async options => { assert.equal(options.audio, false); return media; };
    class Peer {
        constructor() { peers.push(this); this.listeners = new Map(); this.iceGatheringState = gathering ? 'gathering' : 'complete'; this.connectionState = 'new'; }
        addEventListener(name, fn) { this.listeners.set(name, fn); }
        removeEventListener(name) { this.listeners.delete(name); }
        addTransceiver(track, options) { assert.equal(options.direction, 'sendonly'); }
        async createOffer() { return { type: 'offer', sdp: 'v=0\r\ns=offer' }; }
        async setLocalDescription(value) { this.localDescription = value; }
        async setRemoteDescription(value) { this.remoteDescription = value; this.connectionState = 'connected'; this.onconnectionstatechange?.(); }
        close() { this.closed = true; this.connectionState = 'closed'; }
    }
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { mediaDevices: { getDisplayMedia: (...args) => getDisplayMedia(...args) } } });
    Object.assign(globalThis, { window, RTCPeerConnection: Peer,
        setTimeout: (callback, delay) => { timers.set(++nextTimer, { callback, delay }); return nextTimer; }, clearTimeout: id => timers.delete(id) });
    const reference = { async invokeMethodAsync(name, ...args) {
        calls.push([name, ...args]);
        if (name === 'StartBroadcast') return { id: 'stream-1', laboratory: 'Lab 1' };
        if (name === 'PollBroadcast') return pollOverride ? await pollOverride() : { peers: remotePeers };
    } };
    initialize({ open, dialog, start, stop, laboratory: lab, cancel, status, pickerStatus }, reference);
    setSession(open, 12);
    t.after(() => {
        dispose(open);
        for (const [key, descriptor] of Object.entries(descriptors)) {
            if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
        }
    });
    return { open, dialog, start, stop, lab, status, pickerStatus, window, peers, calls, timers, track, media,
        choose: () => {
            open.listeners.get('click')();
            return start.listeners.get('click')();
        },
        setCapture: fn => getDisplayMedia = fn, setPeers: value => remotePeers = value,
        setPoll: fn => pollOverride = fn,
        setGathering: value => gathering = value,
        tick: async (delay = 2000) => {
            const pending = [...timers].filter(([, timer]) => timer.delay === delay);
            pending.forEach(([id, timer]) => { timers.delete(id); timer.callback(); });
            await flush();
        } };
}

test('Share screen opens the lab dialog, then successful sharing closes it and shows live toolbar controls', async t => {
    const f = browser(t);
    assert.equal(f.open.disabled, false);
    assert.equal(f.stop.hidden, true);
    await f.start.listeners.get('click')();
    f.open.listeners.get('click')();
    assert.equal(f.dialog.open, true);
    assert.equal(f.calls.length, 0);
    assert.equal(f.lab.value, '2');
    assert.equal(f.start.disabled, false);
    await f.start.listeners.get('click')(); await flush();
    assert.deepEqual(f.calls[0], ['StartBroadcast', 2, 12]);
    assert.equal(f.dialog.open, false);
    assert.equal(f.open.hidden, true);
    assert.equal(f.stop.hidden, false);
    assert.equal(f.stop.focused, true);
    assert.ok(f.calls.some(c => c[0] === 'OfferBroadcast'));
    f.setPeers([{ id: 'pc-1', answerSdp: 'v=0\r\ns=answer' }]);
    await f.tick();
    assert.equal(f.peers[0].remoteDescription.type, 'answer');
    assert.match(f.status.textContent, /1 of 1/);
    f.stop.listeners.get('click')(); await flush();
    assert.equal(f.track.stopped, 1);
    assert.equal(f.peers[0].closed, true);
    assert.equal(f.open.hidden, false);
    assert.equal(f.stop.hidden, true);
    assert.equal(f.timers.size, 0);
    assert.ok(f.calls.some(c => c[0] === 'StopBroadcast'));
    assert.equal(f.lab.value, '2');
});

test('Cancelling the screen picker never starts a server broadcast', async t => {
    const f = browser(t);
    f.setCapture(async () => { throw Object.assign(new Error(), { name: 'NotAllowedError' }); });
    await f.choose();
    assert.equal(f.calls.length, 0);
    assert.equal(f.dialog.open, true);
    assert.equal(f.stop.hidden, true);
    assert.match(f.pickerStatus.textContent, /cancelled or denied/);
    assert.equal(f.start.disabled, false);
});

test('Sharing is disabled before a monitoring session starts and ending it releases the stream', async t => {
    const f = browser(t);
    setSession(f.open, null);
    assert.equal(f.open.disabled, true);
    assert.match(f.open.title, /Start a session/);
    await f.choose();
    assert.equal(f.calls.length, 0);
    setSession(f.open, 13);
    assert.equal(f.open.disabled, false);
    await f.choose(); await flush();
    assert.equal(f.dialog.open, false);
    setSession(f.open, null); await flush();
    assert.equal(f.track.stopped, 1);
    assert.equal(f.open.disabled, true);
    assert.equal(f.stop.hidden, true);
    assert.ok(f.calls.some(call => call[0] === 'StopBroadcast'));
});

test('Ending the monitoring session while the browser picker is open cannot start a broadcast', async t => {
    const f = browser(t);
    let resolve;
    f.setCapture(() => new Promise(done => resolve = done));
    const pending = f.choose();
    setSession(f.open, null);
    resolve(f.media);
    await pending;
    assert.equal(f.track.stopped, 1);
    assert.equal(f.calls.length, 0);
    assert.equal(f.dialog.open, false);
});

test('Stopping during connection setup cancels ICE timers and never sends a late offer', async t => {
    const f = browser(t);
    f.setGathering(true);
    await f.choose(); await flush();
    assert.equal(f.timers.size, 1);
    f.stop.listeners.get('click')(); await flush();
    assert.equal(f.timers.size, 0);
    assert.equal(f.peers[0].listeners.size, 0);
    assert.equal(f.track.stopped, 1);
    assert.equal(f.calls.some(call => call[0] === 'OfferBroadcast'), false);
});

test('Leaving while the picker is open stops a late capture without creating a broadcast', async t => {
    const f = browser(t);
    let resolve;
    f.setCapture(() => new Promise(r => resolve = r));
    const starting = f.choose();
    dispose(f.open);
    resolve(f.media); await starting;
    assert.equal(f.track.stopped, 1);
    assert.equal(f.calls.length, 0);
    assert.equal(f.window.listeners.size, 0);
});

test('Late workstations receive offers and disconnected recipients are released', async t => {
    const f = browser(t);
    await f.choose(); await flush();
    f.setPeers([{ id: 'pc-2', answerSdp: null }]); await f.tick();
    assert.equal(f.peers[0].closed, true);
    assert.equal(f.peers.length, 2);
    assert.ok(f.calls.some(c => c[0] === 'OfferBroadcast' && c[2] === 'pc-2'));
    f.track.onended(); await flush();
    assert.equal(f.peers[1].closed, true);
    assert.equal(f.track.stopped, 1);
});

test('A late polling response cannot create a peer after the faculty stops', async t => {
    const f = browser(t);
    await f.choose(); await flush();
    let resolve;
    f.setPoll(() => new Promise(r => resolve = r));
    await f.tick();
    f.stop.listeners.get('click')(); await flush();
    resolve({ peers: [{ id: 'too-late' }] }); await flush();
    assert.equal(f.peers.length, 1);
    assert.equal(f.timers.size, 0);
});

test('Losing the server stops local screen sharing and closes all media connections', async t => {
    const f = browser(t);
    await f.choose(); await flush();
    f.setPoll(async () => { throw new Error('Server down'); });
    await f.tick();
    assert.equal(f.track.stopped, 1);
    assert.equal(f.peers[0].closed, true);
    assert.equal(f.stop.hidden, true);
    assert.equal(f.timers.size, 0);
    assert.match(f.status.textContent, /server connection was lost/);
});

test('An unresponsive or outdated workstation becomes a failed connection instead of waiting forever', async t => {
    const f = browser(t);
    await f.choose(); await flush();
    await f.tick(30000);
    assert.equal(f.peers[0].closed, true);
    assert.match(f.status.textContent, /1 could not connect/);
});

test('Workstation HTML is receive-only, creates an answer, and closes its peer without requesting local media', async () => {
    const html = await readFile(new URL('../CLMS.Agent/Resources/Raw/faculty-stream.html', import.meta.url), 'utf8');
    const elements = { screen: { play: async () => {} }, status: {}, play: {} };
    let current;
    const context = vm.createContext({ document: { getElementById: id => elements[id] }, window: {}, setTimeout, clearTimeout,
        RTCPeerConnection: class {
            constructor() { current = this; this.iceGatheringState = 'complete'; }
            async setRemoteDescription(value) { this.remote = value; }
            async createAnswer() { return { type: 'answer', sdp: 'v=0\r\ns=answer' }; }
            async setLocalDescription(value) { this.localDescription = value; }
            close() { this.closed = true; }
        } });
    vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1], context);
    assert.equal(context.window.facultyViewerReady, true);
    await context.window.receiveOffer('v=0\r\ns=offer');
    assert.equal(current.remote.type, 'offer');
    assert.equal(context.window.broadcastResult.sdp, 'v=0\r\ns=answer');
    const remoteMedia = {};
    current.ontrack({ streams: [remoteMedia] });
    assert.equal(elements.screen.srcObject, remoteMedia);
    current.connectionState = 'connected';
    current.onconnectionstatechange();
    assert.match(elements.status.textContent, /Faculty live stream/);
    context.window.stopStream();
    assert.equal(current.closed, true);
    assert.equal(elements.screen.srcObject, null);
    assert.equal(context.window.startFacultyOffer('v=0\r\ns=offer'), true);
    await flush();
    assert.equal(context.window.broadcastResult.sdp, 'v=0\r\ns=answer');
    context.window.stopStream();
});
