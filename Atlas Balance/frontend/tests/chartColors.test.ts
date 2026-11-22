import assert from 'node:assert/strict';
import test from 'node:test';
import { resolveChartColor } from '../src/utils/chartColors.js';

// V-03.02 (LOTE 8, D-8).
//
// El hexadecimal de un token de grafico se resuelve a la VARIABLE del token,
// para que el grafico siga al tema (claro/oscuro) en vez de fijar el color
// que se guardo.

test('D-8: el hexadecimal del tema claro se resuelve a su variable de token', () => {
  assert.equal(resolveChartColor('#0066CC', 'var(--chart-1)'), 'var(--chart-1)');
  assert.equal(resolveChartColor('#1C8A4B', 'var(--chart-5)'), 'var(--chart-5)');
  assert.equal(resolveChartColor('#C92B3F', 'var(--chart-7)'), 'var(--chart-7)');
  assert.equal(resolveChartColor('#6B8F1E', 'var(--chart-1)'), 'var(--chart-10)');
});

test('D-8: el hexadecimal del tema oscuro se resuelve a la misma variable', () => {
  assert.equal(resolveChartColor('#5C8CEB', 'var(--chart-1)'), 'var(--chart-1)');
  assert.equal(resolveChartColor('#4FB87A', 'var(--chart-1)'), 'var(--chart-5)');
  assert.equal(resolveChartColor('#EE909B', 'var(--chart-1)'), 'var(--chart-7)');
  assert.equal(resolveChartColor('#A3C455', 'var(--chart-1)'), 'var(--chart-10)');
});

test('D-8: la comparacion ignora mayusculas y espacios', () => {
  assert.equal(resolveChartColor('  #0066cc  ', 'var(--chart-1)'), 'var(--chart-1)');
  assert.equal(resolveChartColor('#0066CC', 'var(--chart-1)'), 'var(--chart-1)');
});

test('D-8: la paleta anterior del backend sigue resolviendo a su token', () => {
  assert.equal(resolveChartColor('#43b430', 'var(--chart-1)'), 'var(--chart-5)');
  assert.equal(resolveChartColor('#ff4757', 'var(--chart-1)'), 'var(--chart-7)');
  assert.equal(resolveChartColor('#7b7b7b', 'var(--chart-1)'), 'var(--chart-1)');
});

test('D-8: sin valor se usa el token de reserva', () => {
  assert.equal(resolveChartColor(undefined, 'var(--chart-5)'), 'var(--chart-5)');
  assert.equal(resolveChartColor(null, 'var(--chart-5)'), 'var(--chart-5)');
  assert.equal(resolveChartColor('', 'var(--chart-5)'), 'var(--chart-5)');
  assert.equal(resolveChartColor('   ', 'var(--chart-5)'), 'var(--chart-5)');
});

test('D-8: un color propio del administrador se respeta tal cual', () => {
  assert.equal(resolveChartColor('#123456', 'var(--chart-1)'), '#123456');
});
