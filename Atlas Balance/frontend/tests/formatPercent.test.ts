import assert from 'node:assert/strict';
import test from 'node:test';
import { formatPercent, signoPorcentaje } from '../src/utils/formatters.js';

// Separador que emite el locale es-ES entre la cifra y el "%": espacio fino no
// separable (U+00A0), no un espacio normal.
const SEP = '\u00A0';
// Signo menos real (U+2212), no el guion ASCII.
const MINUS_REAL = '\u2212';
// Marcador de valor ausente.
const AUSENTE = '—';

test('formatPercent: coma decimal y separador del locale antes del %', () => {
  assert.equal(formatPercent(12.34), `12,3${SEP}%`);
  assert.equal(formatPercent(12.34, 2), `12,34${SEP}%`);
  assert.equal(formatPercent(12.6, 0), `13${SEP}%`);
});

test('formatPercent: los negativos usan el menos real (U+2212)', () => {
  const negativo = formatPercent(-5.2);
  assert.equal(negativo, `${MINUS_REAL}5,2${SEP}%`);
  assert.equal(negativo.codePointAt(0), 0x2212);
  assert.ok(!negativo.includes('-'), 'no debe quedar el guion ASCII');
});

test('formatPercent: el cero se escribe "0,0 %" y nunca "+0,0 %"', () => {
  const cero = formatPercent(0);
  assert.equal(cero, `0,0${SEP}%`);
  assert.ok(!cero.includes('+'), 'el cero no lleva signo mas');
});

test('formatPercent: valor ausente o no numerico', () => {
  assert.equal(formatPercent(undefined), AUSENTE);
  assert.equal(formatPercent(null), AUSENTE);
  assert.equal(formatPercent(''), AUSENTE);
  assert.equal(formatPercent('abc'), AUSENTE);
});

test('formatPercent: acepta texto en formato europeo', () => {
  assert.equal(formatPercent('12,34'), `12,3${SEP}%`);
  assert.equal(formatPercent('-5,2'), `${MINUS_REAL}5,2${SEP}%`);
});

test('formatPercent: nunca escribe punto decimal ni "%" pegado', () => {
  const salidas = [
    formatPercent(12.34),
    formatPercent(-5.2),
    formatPercent(0),
    formatPercent(12.34, 0),
    formatPercent(12.34, 2),
  ];

  for (const salida of salidas) {
    assert.ok(!salida.includes('12.3%'), `punto decimal en "${salida}"`);
    assert.ok(!salida.includes('.0%'), `punto decimal en "${salida}"`);
    assert.ok(salida.endsWith(`${SEP}%`), `el "%" va separado en "${salida}"`);
  }
});

test('formatPercent: positivos, negativos y ceros mantienen el signo correcto', () => {
  assert.equal(formatPercent(12.34), `12,3${SEP}%`);
  assert.equal(formatPercent(0.06), `0,1${SEP}%`);
  assert.equal(formatPercent(0.4, 0), `0${SEP}%`);
  assert.equal(formatPercent(0.6, 0), `1${SEP}%`);
  assert.equal(formatPercent(-5.2), `${MINUS_REAL}5,2${SEP}%`);
});

test('signoPorcentaje: el signo sale del valor ya redondeado', () => {
  assert.equal(signoPorcentaje(12.34), 1);
  assert.equal(signoPorcentaje(-5.2), -1);
  assert.equal(signoPorcentaje(0.04), 0);
  assert.equal(signoPorcentaje(-0.04), 0);
  assert.equal(signoPorcentaje(0), 0);
  assert.equal(signoPorcentaje(undefined), 0);
});
