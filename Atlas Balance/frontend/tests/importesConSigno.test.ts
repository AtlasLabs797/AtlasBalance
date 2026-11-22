import assert from 'node:assert/strict';
import test from 'node:test';
import { formatCurrency, getAmountTone, sinSignoPositivo } from '../src/utils/formatters.js';

test('los importes positivos y el cero no muestran signo; los negativos conservan el menos', () => {
  assert.ok(!formatCurrency(1200).includes('+'));
  assert.ok(!formatCurrency(0).includes('+'));
  assert.ok(formatCurrency(-1200).startsWith('−'));
  assert.equal(getAmountTone(1200), 'positive');
  assert.equal(getAmountTone(-1200), 'negative');
  assert.equal(getAmountTone(0), 'neutral');
});

test('los importes crudos pierden solo el signo positivo al mostrarse', () => {
  assert.equal(sinSignoPositivo('+1.234,56'), '1.234,56');
  assert.equal(sinSignoPositivo(' +0'), ' 0');
  assert.equal(sinSignoPositivo('-123'), '-123');
  assert.equal(sinSignoPositivo('(+123)'), '(+123)');
  assert.equal(sinSignoPositivo('+texto'), '+texto');
});
