import assert from 'node:assert/strict';
import test from 'node:test';
import { resumenUserAgent } from '../src/utils/userAgent.js';

const WIN = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)';

test('Chrome en Windows', () => {
  assert.equal(resumenUserAgent(`${WIN} Chrome/157.0.0.0 Safari/537.36`), 'Chrome 157 · Windows');
});

test('Edge se detecta antes que Chrome', () => {
  assert.equal(resumenUserAgent(`${WIN} Chrome/120.0.0.0 Safari/537.36 Edg/120.0.2210.91`), 'Edge 120 · Windows');
});

test('Opera se detecta antes que Chrome', () => {
  assert.equal(resumenUserAgent(`${WIN} Chrome/110.0.0.0 Safari/537.36 OPR/96.0.0.0`), 'Opera 96 · Windows');
});

test('Claude (Electron) en Windows', () => {
  assert.equal(
    resumenUserAgent(`${WIN} Claude/1.2.3 Chrome/130.0.0.0 Electron/33.0.0 Safari/537.36`),
    'Claude 1 · Windows',
  );
  assert.equal(
    resumenUserAgent(`${WIN} Chrome/130.0.0.0 Electron/33.0.0 Safari/537.36`),
    'Electron 33 · Windows',
  );
});

test('Firefox en Linux', () => {
  assert.equal(
    resumenUserAgent('Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0'),
    'Firefox 121 · Linux',
  );
});

test('Safari en macOS', () => {
  assert.equal(
    resumenUserAgent(
      'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15',
    ),
    'Safari 17 · macOS',
  );
});

test('Safari en iOS y Chrome en Android', () => {
  assert.equal(
    resumenUserAgent(
      'Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1',
    ),
    'Safari 17 · iOS',
  );
  assert.equal(
    resumenUserAgent(
      'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36',
    ),
    'Chrome 120 · Android',
  );
});

test('texto desconocido o vacio', () => {
  assert.equal(resumenUserAgent('curl/8.4.0'), 'curl/8.4.0');
  assert.equal(resumenUserAgent('   '), '');
  assert.equal(resumenUserAgent(null), '');
});
