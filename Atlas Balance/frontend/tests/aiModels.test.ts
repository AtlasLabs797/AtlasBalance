import assert from 'node:assert/strict';
import test from 'node:test';
import { resolveSelectedModelAfterConfigRefresh } from '../src/utils/aiModels.js';

// P5 V-03.01: aiChatStore.ensureConfig debe resetear selectedModel a null si
// deja de estar en la lista de modelos permitidos que devuelve el backend
// (por ejemplo, el admin cambio de provider/modelo). Sin esto, el chat
// seguia mandando un modelo invalido hasta que el usuario cerraba sesion.

void test('resolveSelectedModelAfterConfigRefresh: mantiene el modelo si sigue permitido', () => {
  const resultado = resolveSelectedModelAfterConfigRefresh('qwen/qwen3-coder:free', [
    'openrouter/auto',
    'qwen/qwen3-coder:free',
  ]);
  assert.equal(resultado, 'qwen/qwen3-coder:free');
});

void test('resolveSelectedModelAfterConfigRefresh: resetea a null si ya no esta permitido', () => {
  const resultado = resolveSelectedModelAfterConfigRefresh('qwen/qwen3-coder:free', ['openrouter/auto']);
  assert.equal(resultado, null);
});

void test('resolveSelectedModelAfterConfigRefresh: resetea a null si la lista permitida esta vacia', () => {
  const resultado = resolveSelectedModelAfterConfigRefresh('openrouter/auto', []);
  assert.equal(resultado, null);
});

void test('resolveSelectedModelAfterConfigRefresh: sin seleccion previa, sigue sin seleccion', () => {
  const resultado = resolveSelectedModelAfterConfigRefresh(null, ['openrouter/auto']);
  assert.equal(resultado, null);
});

void test('resolveSelectedModelAfterConfigRefresh: lista ausente (backend antiguo) resetea a null', () => {
  const resultado = resolveSelectedModelAfterConfigRefresh('openrouter/auto', undefined);
  assert.equal(resultado, null);
});
