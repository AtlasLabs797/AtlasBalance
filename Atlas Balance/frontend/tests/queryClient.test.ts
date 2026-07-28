import assert from 'node:assert/strict';
import test from 'node:test';
import { createQueryClient, clearQueryClient, queryClient, QUERY_STALE_TIMES } from '../src/services/queryClient.js';

test('createQueryClient devuelve un cliente con defaults coherentes', () => {
  const qc = createQueryClient();
  const defaults = qc.getDefaultOptions();
  assert.equal(defaults.queries?.retry, false, 'retry debe estar desactivado globalmente');
  assert.equal(defaults.queries?.refetchOnReconnect, true);
  assert.equal(defaults.queries?.staleTime, QUERY_STALE_TIMES.CATALOGO_CORTO_MS);
  assert.equal(defaults.mutations?.retry, false);
  qc.clear();
});

test('clearQueryClient vacia la cache del cliente singleton compartido', () => {
  queryClient.setQueryData(['demo'], { ok: true });
  assert.deepEqual(queryClient.getQueryData(['demo']), { ok: true });
  clearQueryClient();
  assert.equal(queryClient.getQueryData(['demo']), undefined);
});

test('QUERY_STALE_TIMES expone valores esperados para dashboard y catalogos', () => {
  assert.equal(QUERY_STALE_TIMES.DASHBOARD_MS, 15_000);
  assert.equal(QUERY_STALE_TIMES.ALERTAS_MS, 10_000);
  assert.equal(QUERY_STALE_TIMES.CATALOGO_LARGO_MS, 5 * 60_000);
});
