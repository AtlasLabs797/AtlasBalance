import assert from 'node:assert/strict';
import test from 'node:test';
import { createQueryClient, clearQueryClient } from '../src/services/queryClient.js';
import { mutationInvalidation, invalidateExtractosFamilia } from '../src/queries/invalidation.js';

test('mutationInvalidation expone familias de claves para cada mutacion critica', () => {
  const expected = [
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
