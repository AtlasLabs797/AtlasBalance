import assert from 'node:assert/strict';
import test from 'node:test';
import { isInternalPath, sanitizeInternalPath } from '../src/utils/safeRoute.js';

test('sanitizeInternalPath accepts a simple internal path', () => {
  assert.equal(sanitizeInternalPath('/dashboard'), '/dashboard');
  assert.equal(sanitizeInternalPath('/dashboard?t=p'), '/dashboard?t=p');
  assert.equal(sanitizeInternalPath('/cuentas/123'), '/cuentas/123');
});

test('sanitizeInternalPath rejects protocol-relative and external URLs', () => {
  assert.equal(sanitizeInternalPath('//evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('///evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('https://evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('http://evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('javascript:alert(1)'), '/dashboard');
  assert.equal(sanitizeInternalPath('data:text/html,<script>alert(1)</script>'), '/dashboard');
});

test('sanitizeInternalPath rejects backslash-based bypass', () => {
  assert.equal(sanitizeInternalPath('/\\evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('\\\\evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('/\\\\evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('/path\\evil'), '/dashboard');
});

test('sanitizeInternalPath rejects percent-encoded bypasses', () => {
  assert.equal(sanitizeInternalPath('/%2F/evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('/%5C/evil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('/%5c%5cevil.com'), '/dashboard');
  assert.equal(sanitizeInternalPath('/%2f%2fevil.com'), '/dashboard');
});

test('sanitizeInternalPath rejects empty, whitespace and non-string values', () => {
  assert.equal(sanitizeInternalPath(null), '/dashboard');
  assert.equal(sanitizeInternalPath(undefined), '/dashboard');
  assert.equal(sanitizeInternalPath(''), '/dashboard');
  assert.equal(sanitizeInternalPath('   '), '/dashboard');
});

test('sanitizeInternalPath rejects control characters', () => {
  assert.equal(sanitizeInternalPath('/dashboard\x00'), '/dashboard');
  assert.equal(sanitizeInternalPath('/dashboard\n'), '/dashboard');
  assert.equal(sanitizeInternalPath('/dashboard\r'), '/dashboard');
  assert.equal(sanitizeInternalPath('/dashboard\t'), '/dashboard');
});

test('sanitizeInternalPath respects the provided fallback', () => {
  assert.equal(sanitizeInternalPath('//evil.com', '/login'), '/login');
  assert.equal(sanitizeInternalPath('', '/'), '/');
  assert.equal(sanitizeInternalPath(null, '/custom'), '/custom');
});

test('sanitizeInternalPath trims surrounding whitespace and keeps the path', () => {
  assert.equal(sanitizeInternalPath('  /dashboard?q=1  '), '/dashboard?q=1');
});

test('isInternalPath returns true only for safe internal paths', () => {
  assert.equal(isInternalPath('/dashboard'), true);
  assert.equal(isInternalPath('/cuentas/1?t=2'), true);
  assert.equal(isInternalPath('//evil'), false);
  assert.equal(isInternalPath('/\\evil'), false);
  assert.equal(isInternalPath('https://evil'), false);
  assert.equal(isInternalPath(null), false);
  assert.equal(isInternalPath(''), false);
});
