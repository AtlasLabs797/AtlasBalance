import assert from 'node:assert/strict';
import test from 'node:test';
import { densityRowHeight, resolveInitialDensity } from '../src/utils/density.js';

test('resolveInitialDensity: compacto guardado se respeta', () => {
  assert.equal(resolveInitialDensity('compacto'), 'compacto');
  assert.equal(resolveInitialDensity('comodo'), 'comodo');
});

test('resolveInitialDensity: sin valor o invalido es comodo', () => {
  assert.equal(resolveInitialDensity(null), 'comodo');
  assert.equal(resolveInitialDensity(''), 'comodo');
  assert.equal(resolveInitialDensity('COMPACTO'), 'comodo');
  assert.equal(resolveInitialDensity('dense'), 'comodo');
});

test('densityRowHeight: 44 comodo, 36 compacto, 44 en puntero grueso', () => {
  assert.equal(densityRowHeight('comodo', false), 44);
  assert.equal(densityRowHeight('compacto', false), 36);
  assert.equal(densityRowHeight('compacto', true), 44);
  assert.equal(densityRowHeight('comodo', true), 44);
});
