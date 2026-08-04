export const DEFAULT_INTERNAL_ROUTE = '/dashboard';

function safeDecode(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function hasControlChar(value: string): boolean {
  for (let i = 0; i < value.length; i += 1) {
    const code = value.charCodeAt(i);
    if (code <= 0x1f || code === 0x7f) return true;
  }
  return false;
}

function isSafeInternalPath(value: string): boolean {
  if (!value.startsWith('/')) return false;
  if (value.startsWith('//')) return false;
  if (value.startsWith('/\\')) return false;
  if (value.includes('\\')) return false;
  if (hasControlChar(value)) return false;

  const decoded = safeDecode(value);
  if (decoded !== value) {
    if (decoded.includes('\\')) return false;
    if (decoded.startsWith('//')) return false;
    if (decoded.startsWith('/\\')) return false;
    if (hasControlChar(decoded)) return false;
  }

  return true;
}

export function sanitizeInternalPath(value: string | null | undefined, fallback: string = DEFAULT_INTERNAL_ROUTE): string {
  const candidate = value?.trim();
  if (!candidate) return fallback;
  if (!isSafeInternalPath(candidate)) return fallback;
  return candidate;
}

export function isInternalPath(value: string | null | undefined): boolean {
  if (!value) return false;
  return isSafeInternalPath(value.trim());
}
