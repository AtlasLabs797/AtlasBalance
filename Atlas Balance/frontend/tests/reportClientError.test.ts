import assert from 'node:assert/strict';
import test from 'node:test';
import { LIMITES_TELEMETRIA, recortarPayloadTelemetria, recortarTexto } from '../src/utils/reportClientError.js';

test('recortarTexto: deja intacto lo que cabe y corta lo que excede', () => {
  assert.equal(recortarTexto('abc', 5), 'abc');
  assert.equal(recortarTexto('abcdef', 5), 'abcde');
  assert.equal(recortarTexto('abcde', 5), 'abcde');
});

test('recortarTexto: no deja un surrogate alto suelto al final', () => {
  // U+1F600 ocupa dos unidades UTF-16. Cortar en medio dejaria un surrogate suelto.
  const emoji = '\uD83D\uDE00';
  const resultado = recortarTexto(`ab${emoji}`, 3);
  assert.equal(resultado, 'ab');
  assert.ok(!/[\uD800-\uDBFF]$/.test(resultado), 'no debe terminar en surrogate alto');
});

test('recortarPayloadTelemetria: recorta cada campo a su maximo del DTO', () => {
  const payload = recortarPayloadTelemetria({
    mensaje: 'm'.repeat(600),
    stack: 's'.repeat(5000),
    componentStack: 'c'.repeat(5000),
    path: 'p'.repeat(300),
    timestamp: 't'.repeat(60),
  });

  assert.equal(payload.mensaje.length, LIMITES_TELEMETRIA.mensaje);
  assert.equal(payload.stack?.length, LIMITES_TELEMETRIA.stack);
  assert.equal(payload.componentStack?.length, LIMITES_TELEMETRIA.componentStack);
  assert.equal(payload.path?.length, LIMITES_TELEMETRIA.path);
  assert.equal(payload.timestamp.length, LIMITES_TELEMETRIA.timestamp);
});

test('recortarPayloadTelemetria: valores cortos, ausentes o null no cambian', () => {
  const payload = recortarPayloadTelemetria({
    mensaje: 'error de render',
    stack: undefined,
    componentStack: 'en Componente',
    path: null,
    timestamp: '2026-11-19T10:00:00.000Z',
  });

  assert.deepEqual(payload, {
    mensaje: 'error de render',
    stack: undefined,
    componentStack: 'en Componente',
    path: null,
    timestamp: '2026-11-19T10:00:00.000Z',
  });
});

test('LIMITES_TELEMETRIA: coinciden con los topes del backend', () => {
  assert.deepEqual(LIMITES_TELEMETRIA, {
    mensaje: 500,
    stack: 4000,
    componentStack: 4000,
    path: 200,
    timestamp: 40,
  });
});
