import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

let resize;
let disconnected = false;
globalThis.ResizeObserver = class {
    constructor(callback) { resize = callback; }
    observe() {}
    disconnect() { disconnected = true; }
};
const source = await readFile(new URL('../CLMS.Web/Components/Pages/Monitoring/AllScreens.razor.js', import.meta.url), 'utf8');
const { observe, disconnect } = await import(`data:text/javascript,${encodeURIComponent(source)}`);
const styles = {};
const element = { style: { setProperty: (key, value) => styles[key] = value } };
let capacity = 0;
observe(element, { invokeMethodAsync: async (_, value) => capacity = value });
for (const [width, height] of [[1256, 660], [366, 600], [1000, 200], [1900, 900]]) {
    resize([{ contentRect: { width, height } }]);
    const columns = styles['--wall-columns'];
    const tileWidth = parseFloat(styles['--wall-tile-width']);
    const rows = capacity / columns;
    assert.ok(capacity >= 1);
    assert.ok(columns * tileWidth + (columns - 1) * 8 <= width + 0.01);
    assert.ok(rows * tileWidth * 9 / 16 + (rows - 1) * 8 <= height + 0.01);
}
disconnect(element);
assert.ok(disconnected);
console.log('Screen-wall sizing checks passed.');
