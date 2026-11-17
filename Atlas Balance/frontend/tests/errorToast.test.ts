import assert from 'node:assert/strict';
import test from 'node:test';
import { decideErrorToast, isCanceledRequestError } from '../src/utils/errorToast.js';
import { createPollLifecycle } from '../src/utils/pollLifecycle.js';

// V-03.02 (F-4): el interceptor de API pinta un toast global por cada error.
// En una accion normal del usuario esta bien; en los sondeos (API caida a
// proposito) es un toast cada 2,5 s. Estos tests fijan la decision de toast y
// el ciclo de vida del sondeo.

test('un error de red sin status pinta el aviso de conexion', () => {
  const decision = decideErrorToast({ message: 'Network Error' });
  assert.equal(decision.kind, 'network');
  assert.equal(decision.show, true);
});

test('un error del servidor (500) pinta el aviso de error generico', () => {
  const decision = decideErrorToast({ response: { status: 500 } });
  assert.equal(decision.kind, 'server');
  assert.equal(decision.show, true);
});

test('un 429 conserva su aviso de limite de peticiones', () => {
  const decision = decideErrorToast({ response: { status: 429 } });
  assert.equal(decision.kind, 'rate-limit');
  assert.equal(decision.show, true);
});

test('un 401 con refresh no pinta el toast generico (lo lleva la rama de refresh)', () => {
  const decision = decideErrorToast({ response: { status: 401 } });
  assert.equal(decision.kind, 'none');
  assert.equal(decision.show, false);
});

test('un error de red cancelado no pinta ningun toast', () => {
  const decision = decideErrorToast({ code: 'ERR_CANCELED', message: 'canceled' });
  assert.equal(decision.kind, 'none');
  assert.equal(decision.show, false);
  assert.equal(isCanceledRequestError({ code: 'ERR_CANCELED' }), true);
  assert.equal(isCanceledRequestError({ name: 'CanceledError' }), true);
  assert.equal(isCanceledRequestError({ __CANCEL__: true }), true);
  assert.equal(isCanceledRequestError({ message: 'Network Error' }), false);
});

test('una peticion con skipErrorToast no pinta el toast global', () => {
  const decision = decideErrorToast({ response: { status: 500 } }, { skipErrorToast: true });
  assert.equal(decision.kind, 'none');
  assert.equal(decision.show, false);

  const decisionRed = decideErrorToast({ message: 'Network Error' }, { skipErrorToast: true });
  assert.equal(decisionRed.kind, 'none');
  assert.equal(decisionRed.show, false);
});

test('el sondeo espera el retardo indicado y sigue activo', async () => {
  const poll = createPollLifecycle();
  const inicio = Date.now();
  assert.equal(await poll.wait(20), true);
  assert.ok(Date.now() - inicio >= 15, 'el sondeo debe esperar antes de la siguiente iteracion.');
  assert.equal(poll.active, true);
});

test('stop() cancela el timer pendiente y el sondeo deja de continuar', async () => {
  const poll = createPollLifecycle();
  const espera = poll.wait(5000);
  poll.stop();
  assert.equal(await espera, false);
  assert.equal(poll.active, false);
});

test('un wait posterior a stop() no espera y devuelve false', async () => {
  const poll = createPollLifecycle();
  poll.stop();
  assert.equal(await poll.wait(5000), false);
  assert.equal(poll.active, false);
});
