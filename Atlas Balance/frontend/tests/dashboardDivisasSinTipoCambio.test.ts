import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildAvisoDivisasSinTipoCambio,
  normalizarDivisasSinTipoCambio,
} from '../src/utils/dashboardDivisasSinTipoCambio.js';

// Lote 5 (B-1): una divisa sin tipo de cambio no aporta al total convertido.
// El frontend tiene que avisar de cuales se quedaron fuera, en espanol claro.

test('sin divisas sin tipo de cambio no hay aviso', () => {
  assert.equal(buildAvisoDivisasSinTipoCambio([]), null);
  assert.equal(buildAvisoDivisasSinTipoCambio(null), null);
  assert.equal(buildAvisoDivisasSinTipoCambio(undefined), null);
  assert.equal(buildAvisoDivisasSinTipoCambio(['  ']), null);
});

test('las divisas se normalizan, se quitan duplicados y se ordenan', () => {
  assert.deepEqual(normalizarDivisasSinTipoCambio(['usd', ' GBP ', 'USD', '']), ['GBP', 'USD']);
  assert.deepEqual(normalizarDivisasSinTipoCambio(undefined), []);
});

test('con una sola divisa el aviso habla de esa divisa', () => {
  const aviso = buildAvisoDivisasSinTipoCambio(['USD']);

  assert.ok(aviso);
  assert.equal(aviso.lead, 'El total no incluye USD.');
  assert.equal(aviso.detalle, 'No hay tipo de cambio para esa divisa.');
});

test('con varias divisas el aviso las enumera todas', () => {
  const aviso = buildAvisoDivisasSinTipoCambio(['USD', 'GBP']);

  assert.ok(aviso);
  assert.equal(aviso.lead, 'El total no incluye GBP y USD.');
  assert.equal(aviso.detalle, 'No hay tipo de cambio para esas divisas.');
});

test('el texto completo dice que el total no incluye esas divisas', () => {
  const aviso = buildAvisoDivisasSinTipoCambio(['USD', 'GBP']);

  assert.ok(aviso);
  assert.equal(`${aviso.lead} ${aviso.detalle}`, 'El total no incluye GBP y USD. No hay tipo de cambio para esas divisas.');
});
