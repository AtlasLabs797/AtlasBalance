import assert from 'node:assert/strict';
import test from 'node:test';
import {
  estaRevocado,
  etiquetaDispositivo,
  etiquetaIp,
  resumenVencimiento,
  textoConfirmacionRevocar,
  type DispositivoMfa,
} from '../src/utils/dispositivosMfa.js';
import {
  esEstadoReversible,
  puedeRevertirLote,
  textoConfirmacionRevertir,
  type LoteParaRevertir,
} from '../src/utils/revertirLote.js';

// V-03.02 (LOTE 6, seccion 8.1 del informe de revision integral): helpers de
// la pantalla de dispositivos MFA de confianza y de la accion "Revertir" del
// historial de importacion.

const AHORA = new Date('2026-11-18T10:00:00.000Z');
const DIA_MS = 24 * 60 * 60 * 1000;

const dispositivo = (overrides: Partial<DispositivoMfa> = {}): DispositivoMfa => ({
  user_agent_summary: 'Chrome 120 en Windows',
  ip_address_summary: '192.168.1.20',
  revoked_at: null,
  current: false,
  ...overrides,
});

const lote = (overrides: Partial<LoteParaRevertir> = {}): LoteParaRevertir => ({
  id: 'lote-1',
  estado: 'confirmado',
  cuenta_nombre: 'Cuenta Principal',
  ...overrides,
});

// --- dispositivos MFA de confianza -----------------------------------------

test('etiquetaDispositivo devuelve el resumen del user agent', () => {
  assert.equal(etiquetaDispositivo(dispositivo()), 'Chrome 120 en Windows');
});

test('etiquetaDispositivo cae al texto generico cuando no hay resumen', () => {
  assert.equal(
    etiquetaDispositivo(dispositivo({ user_agent_summary: null })),
    'Dispositivo desconocido'
  );
  assert.equal(
    etiquetaDispositivo(dispositivo({ user_agent_summary: '   ' })),
    'Dispositivo desconocido'
  );
});

test('etiquetaIp devuelve la IP o el marcador cuando no se conoce', () => {
  assert.equal(etiquetaIp(dispositivo()), '192.168.1.20');
  assert.equal(etiquetaIp(dispositivo({ ip_address_summary: null })), 'IP no disponible');
});

test('estaRevocado distingue los dispositivos ya revocados', () => {
  assert.equal(estaRevocado(dispositivo()), false);
  assert.equal(estaRevocado(dispositivo({ revoked_at: '2026-11-18T09:00:00.000Z' })), true);
});

test('resumenVencimiento dice que ya expiro cuando la fecha ya paso', () => {
  const expira = new Date(AHORA.getTime() - DIA_MS).toISOString();
  assert.equal(resumenVencimiento(expira, AHORA), 'Expirado');
});

test('resumenVencimiento indica los dias que quedan de confianza', () => {
  const expira = new Date(AHORA.getTime() + 5 * DIA_MS).toISOString();
  assert.equal(resumenVencimiento(expira, AHORA), 'Expira en 5 días');
});

test('resumenVencimiento dice manana cuando queda un dia', () => {
  const expira = new Date(AHORA.getTime() + DIA_MS).toISOString();
  assert.equal(resumenVencimiento(expira, AHORA), 'Expira mañana');
});

test('resumenVencimiento usa horas cuando queda menos de un dia', () => {
  const expira = new Date(AHORA.getTime() + 12 * 60 * 60 * 1000).toISOString();
  assert.equal(resumenVencimiento(expira, AHORA), 'Expira en 12 h');
});

test('resumenVencimiento avisa cuando no hay fecha de expiracion', () => {
  assert.equal(resumenVencimiento(null, AHORA), 'Sin fecha de expiración');
  assert.equal(resumenVencimiento('no-es-una-fecha', AHORA), 'Sin fecha de expiración');
});

test('textoConfirmacionRevocar explica el dispositivo y la consecuencia', () => {
  const texto = textoConfirmacionRevocar(dispositivo());
  assert.ok(texto.includes('Chrome 120 en Windows'));
  assert.ok(texto.includes('192.168.1.20'));
  assert.ok(texto.includes('código MFA'));
});

test('textoConfirmacionRevocar avisa cuando el dispositivo es el actual', () => {
  const texto = textoConfirmacionRevocar(dispositivo({ current: true }));
  assert.ok(texto.includes('Es el dispositivo desde el que estás viendo esta página.'));
});

// --- revertir un lote de importacion ---------------------------------------

test('esEstadoReversible solo admite el estado confirmado', () => {
  assert.equal(esEstadoReversible('confirmado'), true);
  assert.equal(esEstadoReversible('CONFIRMADO'), true);
  assert.equal(esEstadoReversible('revertido'), false);
  assert.equal(esEstadoReversible('validando'), false);
  assert.equal(esEstadoReversible(''), false);
  assert.equal(esEstadoReversible(null), false);
  assert.equal(esEstadoReversible(undefined), false);
});

test('puedeRevertirLote permite revertir un lote confirmado con permiso', () => {
  assert.equal(puedeRevertirLote(lote(), true), true);
});

test('puedeRevertirLote NO permite revertir sin permiso de eliminar lineas', () => {
  assert.equal(puedeRevertirLote(lote(), false), false);
});

test('puedeRevertirLote NO permite revertir un lote que no esta confirmado', () => {
  assert.equal(puedeRevertirLote(lote({ estado: 'revertido' }), true), false);
  assert.equal(puedeRevertirLote(lote({ estado: 'validando' }), true), false);
});

test('textoConfirmacionRevertir explica el borrado, las conciliaciones y el permiso', () => {
  const texto = textoConfirmacionRevertir(lote());
  assert.ok(texto.includes('Cuenta Principal'));
  assert.ok(texto.includes('Se borrarán'));
  assert.ok(texto.includes('excepción'));
  assert.ok(texto.includes('eliminar líneas'));
  assert.ok(texto.includes('La importación se puede volver a hacer'));
});

test('textoConfirmacionRevertir funciona sin nombre de cuenta', () => {
  const texto = textoConfirmacionRevertir(lote({ cuenta_nombre: null }));
  assert.ok(texto.includes('Se borrarán'));
  assert.ok(!texto.includes('null'));
});
