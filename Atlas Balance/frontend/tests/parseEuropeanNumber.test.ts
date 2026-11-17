import assert from 'node:assert/strict';
import test from 'node:test';
import { parseEuropeanNumber, parseStrictDecimal, toSafeNumber } from '../src/utils/formatters.js';

test('parseEuropeanNumber: formato europeo con coma decimal', () => {
  assert.equal(parseEuropeanNumber('12,5'), 12.5);
  assert.equal(parseEuropeanNumber('1.234,50'), 1234.5);
  assert.equal(parseEuropeanNumber('0,92'), 0.92);
  assert.equal(parseEuropeanNumber('0,000001'), 0.000001);
  assert.equal(parseEuropeanNumber('0,00000001'), 0.00000001);
});

test('parseEuropeanNumber: punto decimal simple', () => {
  assert.equal(parseEuropeanNumber('12.5'), 12.5);
});

test('parseStrictDecimal: el unico separador siempre es decimal', () => {
  assert.equal(parseStrictDecimal('1.085'), 1.085);
  assert.equal(parseStrictDecimal('58.125'), 58.125);
  assert.equal(parseStrictDecimal('1,085'), 1.085);
  assert.equal(parseStrictDecimal('0,000001'), 0.000001);
  assert.equal(parseStrictDecimal('0.00000001'), 0.00000001);
  assert.equal(parseStrictDecimal(' 12,5 '), 12.5);
  assert.equal(parseStrictDecimal('1200'), 1200);
});

test('parseStrictDecimal: miles, negativos y basura son error', () => {
  assert.equal(parseStrictDecimal('1.234,50'), null);
  assert.equal(parseStrictDecimal('1,234.50'), null);
  assert.equal(parseStrictDecimal('-3'), null);
  assert.equal(parseStrictDecimal('1,2,3'), null);
  assert.equal(parseStrictDecimal('abc'), null);
  assert.equal(parseStrictDecimal(''), null);
  assert.equal(parseStrictDecimal(null), null);
});

test('parseEuropeanNumber: un punto con grupos de tres sigue siendo millar', () => {
  assert.equal(parseEuropeanNumber('1.234'), 1234);
  assert.equal(parseEuropeanNumber('1.234.567'), 1234567);
});

test('parseEuropeanNumber: vacio e invalido devuelven null, negativos se respetan', () => {
  assert.equal(parseEuropeanNumber(''), null);
  assert.equal(parseEuropeanNumber('abc'), null);
  assert.equal(parseEuropeanNumber('-3,5'), -3.5);
});

// B-13 (LOTE 5): una parte entera a cero no es una agrupacion de miles.
// "0,500" es 0,5 (coma decimal), no 500. Misma regla que el backend
// (ImportacionService.IsThousandsGrouped).
test('parseEuropeanNumber: parte entera a cero no se agrupa en miles (B-13)', () => {
  assert.equal(parseEuropeanNumber('0.500'), 0.5);
  assert.equal(parseEuropeanNumber('0,500'), 0.5);
  assert.equal(parseEuropeanNumber('00.500'), 0.5);
  assert.equal(parseEuropeanNumber('-0.500'), -0.5);
  assert.equal(toSafeNumber('0.500'), 0.5);
});

test('parseEuropeanNumber: los millares reales no cambian (B-13)', () => {
  assert.equal(parseEuropeanNumber('1.234'), 1234);
  assert.equal(parseEuropeanNumber('12.345'), 12345);
  assert.equal(parseEuropeanNumber('10.500'), 10500);
  assert.equal(parseEuropeanNumber('1.234.567'), 1234567);
  assert.equal(parseEuropeanNumber('1.234,56'), 1234.56);
  assert.equal(parseEuropeanNumber('1234.56'), 1234.56);
  assert.equal(parseEuropeanNumber('0'), 0);
});
