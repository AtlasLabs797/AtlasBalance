import assert from 'node:assert/strict';
import test from 'node:test';
import {
  advanceSessionGeneration,
  clearUserScopedStorage,
  getSessionGeneration,
  isSessionGenerationCurrent,
  isSessionResponseCurrent,
  shouldResetUserScopedState,
  USER_SCOPED_STORAGE_KEYS,
  type StorageLike,
} from '../src/utils/sessionScope.js';

class MemoryStorage implements StorageLike {
  private readonly values = new Map<string, string>();

  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }

  removeItem(key: string): void {
    this.values.delete(key);
  }
}

test('clearUserScopedStorage elimina solo las claves ligadas a la sesion', () => {
  const local = new MemoryStorage();
  const session = new MemoryStorage();
  local.setItem(USER_SCOPED_STORAGE_KEYS.selectedPaisId, 'pais-usuario-a');
  local.setItem('theme', 'dark');
  session.setItem(USER_SCOPED_STORAGE_KEYS.alertasBannerDismissed, '1');
  session.setItem('atlas_balance_update_message', 'neutral');

  clearUserScopedStorage(local, session);

  assert.equal(local.getItem(USER_SCOPED_STORAGE_KEYS.selectedPaisId), null);
  assert.equal(session.getItem(USER_SCOPED_STORAGE_KEYS.alertasBannerDismissed), null);
  assert.equal(local.getItem('theme'), 'dark');
  assert.equal(session.getItem('atlas_balance_update_message'), 'neutral');
});

test('un cambio de usuario fuerza limpieza, pero refrescar el mismo usuario no', () => {
  assert.equal(shouldResetUserScopedState(null, 'usuario-a'), false);
  assert.equal(shouldResetUserScopedState('usuario-a', 'usuario-a'), false);
  assert.equal(shouldResetUserScopedState('usuario-a', 'usuario-b'), true);
});

test('una respuesta async de una generacion anterior deja de ser aplicable', () => {
  const previousGeneration = getSessionGeneration();
  assert.equal(isSessionGenerationCurrent(previousGeneration), true);

  advanceSessionGeneration();

  assert.equal(isSessionGenerationCurrent(previousGeneration), false);
  assert.equal(isSessionGenerationCurrent(getSessionGeneration()), true);
});

test('una respuesta de refresh exige la misma generacion y el mismo usuario', () => {
  const generation = getSessionGeneration();
  assert.equal(isSessionResponseCurrent(generation, 'usuario-a', 'usuario-a'), true);
  assert.equal(isSessionResponseCurrent(generation, 'usuario-a', 'usuario-b'), false);

  advanceSessionGeneration();

  assert.equal(isSessionResponseCurrent(generation, 'usuario-a', 'usuario-a'), false);
});
