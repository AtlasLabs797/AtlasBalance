import assert from 'node:assert/strict';
import test from 'node:test';
import {
  IMPORTACION_OPERATION_TIMEOUT_MS,
  buildConfirmImportacionLoteRequest,
  buildCreateImportacionLoteRequest,
} from '../src/utils/importacionRequest.js';

test('la creacion de lote envia divisa_esperada, timeout especifico e Idempotency-Key', () => {
  const request = buildCreateImportacionLoteRequest({
    cuentaId: 'cuenta-1',
    rawData: '01/01/2026\tCobro\t12,50',
    separator: 'tab',
    mapeo: { fecha: 0, concepto: 1, monto: 2 },
    divisaEsperada: 'USD',
    divisaCuenta: 'EUR',
    idempotencyKey: 'create-key',
  });

  assert.equal(request.url, '/importacion/lotes');
  assert.equal(request.body.divisa_esperada, 'USD');
  assert.equal(request.config.timeout, IMPORTACION_OPERATION_TIMEOUT_MS);
  assert.equal(request.config.timeout, 300_000);
  assert.equal(request.config.headers['Idempotency-Key'], 'create-key');
});

test('la confirmacion usa timeout especifico e Idempotency-Key sin alterar filas', () => {
  const request = buildConfirmImportacionLoteRequest({
    loteId: 'lote-1',
    filasAImportar: [1, 4],
    aceptaAdvertencias: true,
    forceConfirmDivisaMismatch: true,
    idempotencyKey: 'confirm-key',
  });

  assert.equal(request.url, '/importacion/lotes/lote-1/confirmar');
  assert.deepEqual(request.body, {
    filas_a_importar: [1, 4],
    acepta_advertencias: true,
    force_confirm_divisa_mismatch: true,
  });
  assert.equal(request.config.timeout, 300_000);
  assert.equal(request.config.headers['Idempotency-Key'], 'confirm-key');
});

test('la divisa de la cuenta se usa solo como fallback y una clave vacia se rechaza', () => {
  const fallback = buildCreateImportacionLoteRequest({
    cuentaId: 'cuenta-1',
    rawData: 'datos',
    separator: 'comma',
    mapeo: {},
    divisaEsperada: '',
    divisaCuenta: 'EUR',
    idempotencyKey: 'fallback-key',
  });

  assert.equal(fallback.body.divisa_esperada, 'EUR');
  assert.throws(
    () => buildConfirmImportacionLoteRequest({
      loteId: 'lote-1',
      filasAImportar: [],
      aceptaAdvertencias: false,
      forceConfirmDivisaMismatch: false,
      idempotencyKey: ' ',
    }),
    /idempotencia/i,
  );
});
