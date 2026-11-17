import assert from 'node:assert/strict';
import test from 'node:test';
import { formatearTiempoRestante, isUsuarioBloqueado } from '../src/utils/usuarioBloqueo.js';

const AHORA = new Date('2026-11-16T10:00:00.000Z');

test('isUsuarioBloqueado: fecha futura bloquea, null o fecha pasada no', () => {
  assert.equal(isUsuarioBloqueado('2026-11-16T10:30:00.000Z', AHORA), true);
  assert.equal(isUsuarioBloqueado(null, AHORA), false);
  assert.equal(isUsuarioBloqueado(undefined, AHORA), false);
  assert.equal(isUsuarioBloqueado('2026-11-16T09:59:00.000Z', AHORA), false);
  assert.equal(isUsuarioBloqueado('2026-11-16T10:00:00.000Z', AHORA), false, 'la fecha exacta ya no bloquea');
  assert.equal(isUsuarioBloqueado('no-es-una-fecha', AHORA), false);
});

test('formatearTiempoRestante: minutos, horas y dias', () => {
  assert.equal(formatearTiempoRestante('2026-11-16T10:29:00.000Z', AHORA), '29 min');
  assert.equal(formatearTiempoRestante('2026-11-16T10:01:00.000Z', AHORA), '1 min');
  assert.equal(formatearTiempoRestante('2026-11-16T11:00:00.000Z', AHORA), '1 h');
  assert.equal(formatearTiempoRestante('2026-11-16T11:05:00.000Z', AHORA), '1 h 5 min');
  assert.equal(formatearTiempoRestante('2026-11-17T13:00:00.000Z', AHORA), '1 d 3 h');
  assert.equal(formatearTiempoRestante('2026-11-18T10:00:00.000Z', AHORA), '2 d');
});

test('formatearTiempoRestante: si ya expiro devuelve "ahora"', () => {
  assert.equal(formatearTiempoRestante('2026-11-16T09:59:00.000Z', AHORA), 'ahora');
  assert.equal(formatearTiempoRestante('2026-11-16T10:00:00.000Z', AHORA), 'ahora');
});
