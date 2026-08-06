import assert from 'node:assert/strict';
import test from 'node:test';
import { friendlyIaError } from '../src/utils/iaErrors.js';

void test('friendlyIaError: detecta IA desactivada por mensaje', () => {
  const resultado = friendlyIaError(new Error('La IA esta desactivada globalmente.'), 'fallback');
  assert.match(resultado.texto, /desactivada/);
  assert.equal(resultado.accion, 'contactar_admin');
});

void test('friendlyIaError: detecta falta de permisos', () => {
  const resultado = friendlyIaError(new Error('Tu usuario no tiene permiso para usar IA.'), 'fallback');
  assert.match(resultado.texto, /permiso/);
  assert.equal(resultado.accion, 'contactar_admin');
});

void test('friendlyIaError: detecta pregunta fuera de alcance', () => {
  const resultado = friendlyIaError(new Error('Solo puedo responder sobre Atlas Balance.'), 'fallback');
  assert.match(resultado.texto, /Reformula/);
  assert.equal(resultado.accion, 'reformular');
});

void test('friendlyIaError: detecta rate limit', () => {
  const resultado = friendlyIaError(new Error('Demasiadas consultas de IA en un minuto.'), 'fallback');
  assert.match(resultado.texto, /limite/);
  assert.equal(resultado.accion, 'esperar');
});

void test('friendlyIaError: detecta timeout del proveedor', () => {
  const resultado = friendlyIaError(new Error('La IA tardo demasiado en responder.'), 'fallback');
  assert.match(resultado.texto, /tardo/);
  assert.equal(resultado.accion, 'esperar');
});

void test('friendlyIaError: detecta proveedor caido', () => {
  const resultado = friendlyIaError(new Error('OpenRouter devolvio error 502.'), 'fallback');
  assert.match(resultado.texto, /proveedor/);
  assert.equal(resultado.accion, 'esperar');
});

void test('friendlyIaError: detecta presupuesto agotado', () => {
  const resultado = friendlyIaError(new Error('Presupuesto mensual de IA agotado.'), 'fallback');
  assert.match(resultado.texto, /presupuesto/);
  assert.equal(resultado.accion, 'contactar_admin');
});

void test('friendlyIaError: detecta falta de datos', () => {
  const resultado = friendlyIaError(new Error('No hay datos suficientes para el periodo.'), 'fallback');
  assert.match(resultado.texto, /datos/);
  assert.equal(resultado.accion, 'reformular');
});

void test('friendlyIaError: usa el fallback para errores no clasificados', () => {
  const resultado = friendlyIaError(new Error('Algo raro paso.'), 'mensaje generico');
  assert.equal(resultado.texto, 'mensaje generico');
  assert.equal(resultado.accion, undefined);
});

void test('friendlyIaError: maneja strings no-Error sin lanzar excepcion', () => {
  const resultado = friendlyIaError('texto cualquiera' as unknown, 'fallback');
  assert.equal(resultado.texto, 'fallback');
});

void test('friendlyIaError: maneja null/undefined', () => {
  const resultado = friendlyIaError(null, 'fallback');
  assert.equal(resultado.texto, 'fallback');
});
