import assert from 'node:assert/strict';
import test from 'node:test';
import { computeBulkFlagToggle } from '../src/utils/bulkFlagToggle.js';

test('computeBulkFlagToggle desmarca cuando todas las filas seleccionadas ya tienen alerta', () => {
  const result = computeBulkFlagToggle([
    { id: 'a', flagged: true },
    { id: 'b', flagged: true },
  ]);

  assert.deepEqual(result, { action: 'unflag', targetIds: ['a', 'b'] });
});

test('computeBulkFlagToggle marca solo las filas sin alerta cuando la seleccion es mixta', () => {
  const result = computeBulkFlagToggle([
    { id: 'a', flagged: true },
    { id: 'b', flagged: false },
    { id: 'c', flagged: false },
  ]);

  assert.deepEqual(result, { action: 'flag', targetIds: ['b', 'c'] });
});

test('computeBulkFlagToggle marca todas cuando ninguna tiene alerta', () => {
  const result = computeBulkFlagToggle([
    { id: 'a', flagged: false },
    { id: 'b', flagged: false },
  ]);

  assert.deepEqual(result, { action: 'flag', targetIds: ['a', 'b'] });
});

test('computeBulkFlagToggle no sugiere accion cuando no hay seleccion', () => {
  const result = computeBulkFlagToggle([]);

  assert.deepEqual(result, { action: 'flag', targetIds: [] });
});