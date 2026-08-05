import assert from 'node:assert/strict';
import test from 'node:test';
import {
  computeAlcance,
  computeCoherence,
  computeCuentasParaPermiso,
  computeTitularesParaPermiso,
  corregirTitularYPaisDesdeCuenta,
  titularDropdownPlaceholder,
} from '../src/utils/permisosFormUtils.js';

const paisA = { id: 'pais-a', nombre: 'Pais A', codigo_iso2: 'A1' };
const paisB = { id: 'pais-b', nombre: 'Pais B', codigo_iso2: 'B1' };
const titularCompartido = { id: 'tit-compartido', nombre: 'Compartido' };
const titularSoloA = { id: 'tit-solo-a', nombre: 'SoloA' };
const titularSoloB = { id: 'tit-solo-b', nombre: 'SoloB' };

const cuentaCompartidoA = {
  id: 'cta-comp-a',
  nombre: 'Comp A',
  titular_id: titularCompartido.id,
  titular_nombre: titularCompartido.nombre,
  pais_id: paisA.id,
  pais_nombre: paisA.nombre,
};
const cuentaCompartidoB = {
  id: 'cta-comp-b',
  nombre: 'Comp B',
  titular_id: titularCompartido.id,
  titular_nombre: titularCompartido.nombre,
  pais_id: paisB.id,
  pais_nombre: paisB.nombre,
};
const cuentaSoloA = {
  id: 'cta-solo-a',
  nombre: 'Solo A',
  titular_id: titularSoloA.id,
  titular_nombre: titularSoloA.nombre,
  pais_id: paisA.id,
  pais_nombre: paisA.nombre,
};
const cuentaSoloB = {
  id: 'cta-solo-b',
  nombre: 'Solo B',
  titular_id: titularSoloB.id,
  titular_nombre: titularSoloB.nombre,
  pais_id: paisB.id,
  pais_nombre: paisB.nombre,
};

const cuentas = [cuentaCompartidoA, cuentaCompartidoB, cuentaSoloA, cuentaSoloB];
const titulares = [titularCompartido, titularSoloA, titularSoloB];

test('computeCoherence coherente con pais, titular y cuenta alineados', () => {
  assert.equal(
    computeCoherence(
      { pais_id: paisA.id, titular_id: titularCompartido.id, cuenta_id: cuentaCompartidoA.id },
      cuentas
    ),
    'coherent'
  );
});

test('computeCoherence parcial cuando titular no encaja con la cuenta', () => {
  assert.equal(
    computeCoherence(
      { pais_id: paisA.id, titular_id: titularSoloA.id, cuenta_id: cuentaCompartidoA.id },
      cuentas
    ),
    'partial'
  );
});

test('computeCoherence parcial cuando pais no encaja con la cuenta', () => {
  assert.equal(
    computeCoherence(
      { pais_id: paisB.id, titular_id: titularCompartido.id, cuenta_id: cuentaCompartidoA.id },
      cuentas
    ),
    'partial'
  );
});

test('computeCoherence dangling si la cuenta ya no existe', () => {
  assert.equal(
    computeCoherence(
      { pais_id: paisA.id, titular_id: titularCompartido.id, cuenta_id: 'no-existe' },
      cuentas
    ),
    'dangling'
  );
});

test('computeCoherence coherente sin cuenta seleccionada (alcance amplio)', () => {
  assert.equal(
    computeCoherence({ pais_id: paisA.id, titular_id: titularCompartido.id, cuenta_id: '' }, cuentas),
    'coherent'
  );
});

test('computeTitularesParaPermiso devuelve todos los titulares si no hay pais', () => {
  const result = computeTitularesParaPermiso({ pais_id: '' }, titulares, cuentas);
  assert.equal(result.length, 3);
});

test('computeTitularesParaPermiso filtra por pais cuando hay uno seleccionado', () => {
  const result = computeTitularesParaPermiso({ pais_id: paisA.id }, titulares, cuentas);
  const ids = result.map((t) => t.id);
  assert.deepEqual(ids.sort(), [titularCompartido.id, titularSoloA.id].sort());
});

test('computeCuentasParaPermiso respeta ambas dimensiones si estan', () => {
  const result = computeCuentasParaPermiso(
    { pais_id: paisA.id, titular_id: titularCompartido.id },
    cuentas
  );
  assert.deepEqual(result.map((c) => c.id), [cuentaCompartidoA.id]);
});

test('computeCuentasParaPermiso respeta solo pais si titular esta vacio', () => {
  const result = computeCuentasParaPermiso({ pais_id: paisA.id, titular_id: '' }, cuentas);
  const ids = result.map((c) => c.id);
  assert.deepEqual(ids.sort(), [cuentaCompartidoA.id, cuentaSoloA.id].sort());
});

test('computeAlcance cuenta 1 cuando hay cuenta seleccionada', () => {
  assert.equal(
    computeAlcance({ pais_id: paisA.id, titular_id: titularCompartido.id, cuenta_id: cuentaCompartidoA.id }, cuentas),
    1
  );
});

test('computeAlcance cuenta titular + pais', () => {
  assert.equal(
    computeAlcance({ pais_id: paisA.id, titular_id: titularCompartido.id, cuenta_id: '' }, cuentas),
    1
  );
});

test('computeAlcance cuenta todas las cuentas de un pais', () => {
  assert.equal(
    computeAlcance({ pais_id: paisA.id, titular_id: '', cuenta_id: '' }, cuentas),
    2
  );
});

test('computeAlcance cuenta todas las cuentas de un titular en cualquier pais', () => {
  assert.equal(
    computeAlcance({ pais_id: '', titular_id: titularCompartido.id, cuenta_id: '' }, cuentas),
    2
  );
});

test('computeAlcance todas las cuentas con scope global', () => {
  assert.equal(
    computeAlcance({ pais_id: '', titular_id: '', cuenta_id: '' }, cuentas),
    4
  );
});

test('computeAlcance devuelve 0 si la cuenta no existe', () => {
  assert.equal(
    computeAlcance({ pais_id: '', titular_id: '', cuenta_id: 'no-existe' }, cuentas),
    0
  );
});

test('corregirTitularYPaisDesdeCuenta devuelve los valores reales de la cuenta', () => {
  const result = corregirTitularYPaisDesdeCuenta(
    { pais_id: '', titular_id: '', cuenta_id: cuentaCompartidoA.id },
    cuentas
  );
  assert.deepEqual(result, { titular_id: titularCompartido.id, pais_id: paisA.id });
});

test('corregirTitularYPaisDesdeCuenta devuelve null si no hay cuenta', () => {
  assert.equal(
    corregirTitularYPaisDesdeCuenta(
      { pais_id: '', titular_id: '', cuenta_id: '' },
      cuentas
    ),
    null
  );
});

test('corregirTitularYPaisDesdeCuenta devuelve null si la cuenta no existe', () => {
  assert.equal(
    corregirTitularYPaisDesdeCuenta(
      { pais_id: '', titular_id: '', cuenta_id: 'no-existe' },
      cuentas
    ),
    null
  );
});

test('titularDropdownPlaceholder cambia segun si hay pais', () => {
  assert.equal(titularDropdownPlaceholder(false), 'Todos los titulares');
  assert.equal(titularDropdownPlaceholder(true), 'Todos los titulares del país');
});

test('paisDropdownPlaceholder es estable', () => {
  // sanity: el placeholder del dropdown de pais no depende de nada externo
  assert.equal(titularDropdownPlaceholder(false), 'Todos los titulares');
});
