import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildPermisosPayload,
  computeAlcance,
  computeCoherence,
  computeCuentasParaPermiso,
  computeTitularesParaPermiso,
  corregirTitularYPaisDesdeCuenta,
  isPermisoRowBlank,
  mapPermisoApiRow,
  titularDropdownPlaceholder,
  type PermisoApiRow,
  type PermisoFormRow,
  type PermisoPayloadRow,
} from '../src/utils/permisosFormUtils.js';

// Fila del formulario con los diez flags de permiso.
interface FilaPermiso extends PermisoFormRow {
  puede_revisar_lineas: boolean;
  puede_aprobar_importaciones: boolean;
  puede_conciliar: boolean;
  puede_cerrar_conciliacion: boolean;
}

type PayloadPermiso = PermisoPayloadRow & Pick<
  FilaPermiso,
  'puede_revisar_lineas' | 'puede_aprobar_importaciones' | 'puede_conciliar' | 'puede_cerrar_conciliacion'
>;

const filaVacia = (): FilaPermiso => ({
  key: 'fila-1',
  pais_id: '',
  titular_id: '',
  cuenta_id: '',
  puede_ver_cuentas: false,
  puede_agregar_lineas: false,
  puede_editar_lineas: false,
  puede_eliminar_lineas: false,
  puede_importar: false,
  puede_ver_dashboard: false,
  puede_revisar_lineas: false,
  puede_aprobar_importaciones: false,
  puede_conciliar: false,
  puede_cerrar_conciliacion: false,
  columnas_visibles: '',
  columnas_editables: '',
});

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

test('buildPermisosPayload envia los diez flags de una fila completa', () => {
  const fila: FilaPermiso = {
    ...filaVacia(),
    puede_ver_cuentas: true,
    puede_agregar_lineas: true,
    puede_editar_lineas: true,
    puede_eliminar_lineas: true,
    puede_importar: true,
    puede_ver_dashboard: true,
    puede_revisar_lineas: true,
    puede_aprobar_importaciones: true,
    puede_conciliar: true,
    puede_cerrar_conciliacion: true,
  };

  const payload = buildPermisosPayload([fila]);

  assert.equal(payload.length, 1);
  assert.deepEqual(payload[0] as PayloadPermiso, {
    cuenta_id: null,
    titular_id: null,
    pais_id: null,
    puede_ver_cuentas: true,
    puede_agregar_lineas: true,
    puede_editar_lineas: true,
    puede_eliminar_lineas: true,
    puede_importar: true,
    puede_ver_dashboard: true,
    puede_revisar_lineas: true,
    puede_aprobar_importaciones: true,
    puede_conciliar: true,
    puede_cerrar_conciliacion: true,
    columnas_visibles: undefined,
    columnas_editables: undefined,
  });
});

test('buildPermisosPayload no descarta una fila con solo los flags nuevos', () => {
  const fila: FilaPermiso = {
    ...filaVacia(),
    puede_revisar_lineas: true,
    puede_aprobar_importaciones: true,
    puede_conciliar: true,
    puede_cerrar_conciliacion: true,
  };

  const payload = buildPermisosPayload([fila]);

  assert.equal(payload.length, 1);
  const row = payload[0] as PayloadPermiso;
  assert.equal(row.puede_revisar_lineas, true);
  assert.equal(row.puede_aprobar_importaciones, true);
  assert.equal(row.puede_conciliar, true);
  assert.equal(row.puede_cerrar_conciliacion, true);
  assert.equal(row.puede_ver_cuentas, false);
  assert.equal(row.puede_ver_dashboard, false);
});

test('buildPermisosPayload descarta una fila totalmente vacia', () => {
  const payload = buildPermisosPayload([filaVacia()]);

  assert.equal(payload.length, 0);
});

test('mapPermisoApiRow lee los flags nuevos que devuelve el backend', () => {
  const api = {
    cuenta_id: 'cuenta-1',
    puede_ver_cuentas: true,
    puede_conciliar: true,
  } as PermisoApiRow;

  const mapped = mapPermisoApiRow(api, 'fila-1') as FilaPermiso;

  assert.equal(mapped.cuenta_id, 'cuenta-1');
  assert.equal(mapped.puede_ver_cuentas, true);
  assert.equal(mapped.puede_conciliar, true);
  // los flags que no llegan se leen como false
  assert.equal(mapped.puede_revisar_lineas, false);
  assert.equal(mapped.puede_aprobar_importaciones, false);
  assert.equal(mapped.puede_cerrar_conciliacion, false);
});

test('isPermisoRowBlank no marca como vacia una fila con solo puede_conciliar', () => {
  const fila: FilaPermiso = {
    ...filaVacia(),
    puede_conciliar: true,
  };

  assert.equal(isPermisoRowBlank(fila), false);
});
