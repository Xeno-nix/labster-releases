import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../CLMS.Web/wwwroot/js/inventory-scanner.js', import.meta.url), 'utf8');

function setup() {
    let decode;
    let options;
    let stopped = 0;
    let destroyed = 0;
    let releasedTracks = 0;
    let invokeCount = 0;
    const listeners = new Map();
    const video = {
        style: { setProperty() {} },
        srcObject: { getTracks: () => [{ stop: () => releasedTracks++ }] }
    };
    class QrScanner {
        constructor(_, callback, configuration) { decode = callback; options = configuration; }
        async start() {}
        stop() { stopped++; }
        destroy() { destroyed++; }
    }
    const window = { addEventListener: (name, callback) => listeners.set(name, callback) };
    vm.runInNewContext(source, { window, QrScanner, document: { getElementById: () => video }, console });
    return {
        scanner: window.clmsInventoryScanner,
        report: { async invokeMethodAsync() { invokeCount++; } },
        decode: value => decode({ data: value }),
        pagehide: () => listeners.get('pagehide')?.(),
        state: () => ({ stopped, destroyed, releasedTracks, invokeCount, camera: options?.preferredCamera })
    };
}

test('mobile QR scanner prefers the rear camera and submits only one decode per scan', async () => {
    const fixture = setup();
    await fixture.scanner.start('scanner', fixture.report);
    fixture.decode('EQ-01');
    fixture.decode('EQ-01');
    assert.equal(fixture.state().camera, 'environment');
    assert.equal(fixture.state().invokeCount, 1);
    assert.equal(fixture.state().releasedTracks, 1);
});

test('leaving the page releases the scanner', async () => {
    const fixture = setup();
    await fixture.scanner.start('scanner', fixture.report);
    fixture.pagehide();
    assert.equal(fixture.state().destroyed, 1);
    assert.equal(fixture.state().releasedTracks, 1);
});
