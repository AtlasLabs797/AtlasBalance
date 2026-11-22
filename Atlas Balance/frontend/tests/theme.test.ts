import assert from 'node:assert/strict';
import test from 'node:test';
import { resolveInitialTheme } from '../src/utils/theme.js';

test('resolveInitialTheme: el valor guardado gana al sistema', () => {
  assert.equal(resolveInitialTheme('dark', false), 'dark');
  assert.equal(resolveInitialTheme('dark', true), 'dark');
  assert.equal(resolveInitialTheme('light', true), 'light');
  assert.equal(resolveInitialTheme('light', false), 'light');
});

test('resolveInitialTheme: sin valor guardado sigue al sistema', () => {
  assert.equal(resolveInitialTheme(null, true), 'dark');
  assert.equal(resolveInitialTheme(null, false), 'light');
});

test('resolveInitialTheme: valor invalido sigue al sistema', () => {
  assert.equal(resolveInitialTheme('', true), 'dark');
  assert.equal(resolveInitialTheme('auto', false), 'light');
  assert.equal(resolveInitialTheme('DARK', true), 'dark');
  assert.equal(resolveInitialTheme('DARK', false), 'light');
});
