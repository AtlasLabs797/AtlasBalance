import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { createQueryClient, clearQueryClient } from '../src/services/queryClient.js';
import { mutationInvalidation, invalidateExtractosFamilia, type QueryClientApi } from '../src/queries/invalidation.js';

type Invalidator = (qc: QueryClientApi) => Promise<void>;

test('mutationInvalidation expone familias de claves para cada mutacion critica', () => {
  const expected = [
    'importacionLoteCrear',
    'importarConfirmar',
    'importarRevertir',
    'extractoCreate',
    'extractoUpdate',
    'extractoDelete',
    'extractoRestaurar',
    'extractoCheck',
    'extractoFlag',
    'extractoDesglose',
    'extractoColumnasVisibles',
    'cuentaCreate',
    'cuentaUpdate',
    'cuentaDelete',
    'cuentaRestaurar',
    'cuentaNotas',
    'cuentaPlazoRenovar',
    'titularCreate',
    'titularUpdate',
    'titularDelete',
    'titularRestaurar',
    'divisa',
    'tipoCambio',
    'formato',
    'alerta',
    'pais',
    'conciliacion',
    'revision',
    'usuario',
    'exportacion',
    'backup',
    'integracionToken',
  ];
  for (const key of expected) {
    assert.equal(typeof (mutationInvalidation as Record<string, unknown>)[key], 'function', `${key} debe existir`);
  }
});

test('invalidateExtractosFamilia invalida todas las claves de extractos', async () => {
  const qc = createQueryClient();
  qc.setQueryData(['extractos', 'list', { page: 1 }], { rows: [] });
  qc.setQueryData(['extractos', 'cuenta-resumen', { cuentaId: 'c1' }], { saldo: 100 });
  qc.setQueryData(['dashboard', 'principal'], { ok: true });

  await invalidateExtractosFamilia(qc);

  // Tras invalidarQueries los datos siguen en cache pero se marcan stale,
  // por lo que la siguiente consulta los recarga.
  assert.equal(qc.getQueryState(['extractos', 'list', { page: 1 }])?.isInvalidated, true);
  assert.equal(qc.getQueryState(['extractos', 'cuenta-resumen', { cuentaId: 'c1' }])?.isInvalidated, true);
  // Dashboard no se invalida con la familia de extractos.
  assert.equal(qc.getQueryState(['dashboard', 'principal'])?.isInvalidated, false);
  clearQueryClient();
});

test('crear un lote de importacion invalida el catalogo', async () => {
  const invalidar = (mutationInvalidation as Record<string, unknown>).importacionLoteCrear;
  assert.equal(typeof invalidar, 'function', 'mutationInvalidation debe exponer importacionLoteCrear');

  const qc = createQueryClient();
  qc.setQueryData(['catalogo', 'importacion-lotes', { cuentaId: 'c1', page: 1 }], { lotes: [] });
  qc.setQueryData(['dashboard', 'principal'], { ok: true });

  await (invalidar as Invalidator)(qc);

  assert.equal(
    qc.getQueryState(['catalogo', 'importacion-lotes', { cuentaId: 'c1', page: 1 }])?.isInvalidated,
    true
  );
  // El lote solo cambia catalogos: los agregados no se tocan.
  assert.equal(qc.getQueryState(['dashboard', 'principal'])?.isInvalidated, false);
  clearQueryClient();
});

test('mutationInvalidation no expone claves con el nombre de un prefijo interno', () => {
  // 'catalogo' es un prefijo de query, no una mutacion: usarlo como clave
  // rompia la invalidacion (A-7).
  assert.equal('catalogo' in mutationInvalidation, false);
});

test('ImportacionPage invalida el catalogo con una clave real y sin casts', () => {
  const source = readFileSync(new URL('../../src/pages/ImportacionPage.tsx', import.meta.url), 'utf8');
  assert.equal(source.includes('as never'), false, 'ImportacionPage.tsx no debe usar `as never`');
  assert.match(source, /invalidate\('importacionLoteCrear'\)/);
});
