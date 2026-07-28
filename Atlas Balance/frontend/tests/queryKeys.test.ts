import assert from 'node:assert/strict';
import test from 'node:test';
import { normalizeQueryParams, queryKeys } from '../src/queries/queryKeys.js';

test('normalizeQueryParams omite valores vacios, nulos e indefinidos', () => {
  const params = normalizeQueryParams({
    a: 'x',
    b: '',
    c: null,
    d: undefined,
    e: 0,
    f: false,
  });
  assert.deepEqual(params, { a: 'x', e: 0, f: false });
});

test('queryKeys.dashboard.principal produce una clave estable', () => {
  const a = queryKeys.dashboard.principal({ usuarioId: 'u1', paisId: 'p1', divisaPrincipal: 'EUR' });
  const b = queryKeys.dashboard.principal({ usuarioId: 'u1', paisId: 'p1', divisaPrincipal: 'EUR' });
  assert.deepEqual(a, b);
});

test('queryKeys.dashboard.principal cambia con cualquier parametro', () => {
  const base = queryKeys.dashboard.principal({ usuarioId: 'u1', paisId: 'p1', divisaPrincipal: 'EUR' });
  const otroUsuario = queryKeys.dashboard.principal({ usuarioId: 'u2', paisId: 'p1', divisaPrincipal: 'EUR' });
  const otroPais = queryKeys.dashboard.principal({ usuarioId: 'u1', paisId: 'p2', divisaPrincipal: 'EUR' });
  const otraDivisa = queryKeys.dashboard.principal({ usuarioId: 'u1', paisId: 'p1', divisaPrincipal: 'USD' });
  assert.notDeepEqual(base, otroUsuario);
  assert.notDeepEqual(base, otroPais);
  assert.notDeepEqual(base, otraDivisa);
});

test('queryKeys.extractos.cuentaResumen depende del periodo y del pais', () => {
  const a = queryKeys.extractos.cuentaResumen({ usuarioId: 'u1', cuentaId: 'c1', periodo: '1m', paisId: 'p1' });
  const mismoPeriodo = queryKeys.extractos.cuentaResumen({ usuarioId: 'u1', cuentaId: 'c1', periodo: '1m', paisId: 'p1' });
  const otroPeriodo = queryKeys.extractos.cuentaResumen({ usuarioId: 'u1', cuentaId: 'c1', periodo: '3m', paisId: 'p1' });
  const sinPais = queryKeys.extractos.cuentaResumen({ usuarioId: 'u1', cuentaId: 'c1', periodo: '1m', paisId: null });
  assert.deepEqual(a, mismoPeriodo);
  assert.notDeepEqual(a, otroPeriodo);
  assert.notDeepEqual(a, sinPais);
});

test('queryKeys.extractos.list separa pagina y pageSize', () => {
  const page1 = queryKeys.extractos.list({
    usuarioId: 'u1',
    cuentaId: 'c1',
    paisId: 'p1',
    page: 1,
    pageSize: 50,
    sortBy: 'fila_numero',
    sortDir: 'desc',
  });
  const page2 = queryKeys.extractos.list({
    usuarioId: 'u1',
    cuentaId: 'c1',
    paisId: 'p1',
    page: 2,
    pageSize: 50,
    sortBy: 'fila_numero',
    sortDir: 'desc',
  });
  const sizeGrande = queryKeys.extractos.list({
    usuarioId: 'u1',
    cuentaId: 'c1',
    paisId: 'p1',
    page: 1,
    pageSize: 100,
    sortBy: 'fila_numero',
    sortDir: 'desc',
  });
  assert.notDeepEqual(page1, page2);
  assert.notDeepEqual(page1, sizeGrande);
});
