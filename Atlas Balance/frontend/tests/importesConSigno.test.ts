import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { getAmountTone } from '../src/utils/formatters.js';

// D-11 (LOTE 8): "los importes positivos se distinguen solo por el color".
// El "+" visible lo antepone SignedAmount cuando showSign esta activo y el
// tono resuelto es "positive"; los negativos ya muestran el "-" del formato,
// asi que no se duplica el signo.
//
// Estos tests comprueban el contrato desde el fuente (el proyecto no tiene
// DOM en la suite) y la regla de tono que decide cuando aparece el "+".

function fuente(relativa: string): string {
  return readFileSync(new URL(`../../${relativa}`, import.meta.url), 'utf8');
}

test('D-11: SignedAmount solo antepone "+" con showSign y tono positivo', () => {
  const source = fuente('src/components/common/SignedAmount.tsx');
  assert.match(source, /showSign && resolvedTone === 'positive' \? '\+' : ''/);
  assert.match(source, /showSign = false/);
});

test('D-11: el tono distingue positivo, negativo y neutro', () => {
  assert.equal(getAmountTone(1200), 'positive');
  assert.equal(getAmountTone('1200'), 'positive');
  assert.equal(getAmountTone(-1200), 'negative');
  assert.equal(getAmountTone('-1200'), 'negative');
  assert.equal(getAmountTone(0), 'neutral');
});

const CON_SIGNO: [string, RegExp][] = [
  ['src/pages/TitularesPage.tsx', /<SignedAmount value=\{item\.total_convertido\} showSign>/],
  ['src/pages/TitularesPage.tsx', /<SignedAmount value=\{saldoTotal\} showSign>/],
  ['src/pages/TitularDetailPage.tsx', /<SignedAmount value=\{c\.saldo_actual\} showSign>/],
  ['src/pages/CuentaDetailPage.tsx', /<SignedAmount value=\{summary\.saldo_actual\} showSign>/],
  ['src/pages/RevisionPage.tsx', /<SignedAmount value=\{item\.monto\} showSign>/],
  ['src/pages/RevisionPage.tsx', /<SignedAmount value=\{item\.importe\} showSign>/],
  ['src/pages/ConciliacionPage.tsx', /<SignedAmount value=\{item\.movimiento_esperado\.monto\} showSign>/],
  ['src/pages/ConciliacionPage.tsx', /<SignedAmount value=\{item\.extracto\.monto\} showSign>/],
  ['src/pages/CuentasPage.tsx', /<SignedAmount value=\{item\.saldo_actual\} showSign>/],
  ['src/pages/CuentasPage.tsx', /<SignedAmount value=\{saldoValue\} showSign>/],
  ['src/pages/AlertasPage.tsx', /<SignedAmount value=\{item\.saldo_actual\} showSign>/],
  ['src/pages/PapeleraPage.tsx', /<SignedAmount value=\{row\.monto\} showSign>/],
  ['src/components/extractos/AuditCellModal.tsx', /<SignedAmount value=\{item\.valor_anterior\} showSign=\{sinSignoPrevio\(item\.valor_anterior\)\}>/],
  ['src/components/extractos/AuditCellModal.tsx', /<SignedAmount value=\{item\.valor_nuevo\} showSign=\{sinSignoPrevio\(item\.valor_nuevo\)\}>/],
  ['src/pages/ImportacionPage.tsx', /<SignedAmount value=\{row\.datos\.monto\} showSign=\{sinSignoPrevio\(row\.datos\.monto\)\}>/],
  ['src/pages/ImportacionPage.tsx', /<SignedAmount value=\{row\.datos\.saldo\} showSign=\{sinSignoPrevio\(row\.datos\.saldo\)\}>/],
  ['src/components/dashboard/SaldoPorDivisaCard.tsx', /<SignedAmount value=\{item\.saldo_total \?\? item\.saldo\} showSign>/],
  ['src/components/dashboard/SaldoPorDivisaCard.tsx', /<SignedAmount value=\{item\.saldo_disponible \?\? item\.saldo\} showSign>/],
  ['src/components/dashboard/SaldoPorDivisaCard.tsx', /<SignedAmount value=\{item\.saldo_inmovilizado \?\? 0\} showSign>/],
  ['src/components/dashboard/SaldoPorDivisaCard.tsx', /<SignedAmount value=\{item\.saldo_total_convertido \?\? item\.saldo_convertido\} showSign>/],
  ['src/components/extractos/ExtractoTable.tsx', /<SignedAmount value=\{amountValue\} showSign>/],
];

for (const [archivo, patron] of CON_SIGNO) {
  test(`D-11: ${archivo} marca el positivo con "+" visible`, () => {
    assert.match(fuente(archivo), patron);
  });
}

// Magnitudes que NO son importes con signo: su etiqueta ya dice de que va
// ("Ingresos", "Egresos", "Saldo minimo" es un umbral >= 0 validado por el
// backend). Anadirles "+" las convertiria en deltas que no son.
const SIN_SIGNO: [string, RegExp][] = [
  ['src/pages/AlertasPage.tsx', /<SignedAmount value=\{item\.saldo_minimo\}>/],
  ['src/pages/TitularDetailPage.tsx', /<SignedAmount value=\{c\.ingresos_mes\}>/],
  ['src/pages/TitularDetailPage.tsx', /<SignedAmount value=\{c\.egresos_mes\}>/],
  ['src/pages/CuentaDetailPage.tsx', /<SignedAmount value=\{summary\.ingresos_mes\}>/],
  ['src/pages/CuentaDetailPage.tsx', /<SignedAmount value=\{summary\.egresos_mes\} tone="negative">/],
];

for (const [archivo, patron] of SIN_SIGNO) {
  test(`D-11: ${archivo} no marca con "+" una magnitud sin signo`, () => {
    assert.match(fuente(archivo), patron);
  });
}

// Barrido del alcance: ningun SignedAmount puede quedarse sin "+" si el
// importe es con signo (saldos y movimientos). Solo se aceptan las
// excepciones documentadas arriba.
const ALCANCE = [
  'src/pages/TitularesPage.tsx',
  'src/pages/TitularDetailPage.tsx',
  'src/pages/CuentaDetailPage.tsx',
  'src/pages/RevisionPage.tsx',
  'src/pages/ImportacionPage.tsx',
  'src/pages/ConciliacionPage.tsx',
  'src/pages/CuentasPage.tsx',
  'src/pages/AlertasPage.tsx',
  'src/pages/PapeleraPage.tsx',
  'src/components/extractos/AuditCellModal.tsx',
  'src/components/extractos/ExtractoTable.tsx',
  'src/components/dashboard/SaldoPorDivisaCard.tsx',
];

const EXCEPCIONES = new Set([
  'src/pages/TitularDetailPage.tsx | c.ingresos_mes',
  'src/pages/TitularDetailPage.tsx | c.egresos_mes',
  'src/pages/CuentaDetailPage.tsx | summary.ingresos_mes',
  'src/pages/CuentaDetailPage.tsx | summary.egresos_mes',
  'src/pages/AlertasPage.tsx | item.saldo_minimo',
]);

test('D-11: en los ficheros del alcance no queda ningun importe con signo sin "+"', () => {
  const sin_signo: string[] = [];
  for (const archivo of ALCANCE) {
    const source = fuente(archivo);
    const tags = source.match(/<SignedAmount\b[^>]*>/g) ?? [];
    for (const tag of tags) {
      if (tag.includes('showSign')) {
        continue;
      }
      const valor = /value=\{([^}]*)\}/.exec(tag)?.[1] ?? '';
      const clave = `${archivo} | ${valor}`;
      if (!EXCEPCIONES.has(clave)) {
        sin_signo.push(clave);
      }
    }
  }
  assert.deepEqual(sin_signo, [], `importes con signo sin "+" visible: ${sin_signo.join(' | ')}`);
});
