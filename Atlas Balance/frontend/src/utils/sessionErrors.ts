// V-03.02 (F-1): un fallo al renovar el token solo demuestra que la sesion ya
// no vale cuando el backend responde 401 o 403. Un error de red, un 5xx, un
// 429 o un timeout son fallos transitorios: la sesion sigue viva y no debe
// cerrarse ni expulsar al usuario de la aplicacion.
//
// Helper puro y sin dependencias: lo comparten el interceptor de API y el
// coordinador de refresh, y los tests lo compilan de forma aislada (importar
// axios aqui romperia esa compilacion).

const readNumericStatus = (value: unknown): number | null => {
  if (!value || typeof value !== 'object') {
    return null;
  }

  const candidate = value as { status?: unknown };
  return typeof candidate.status === 'number' ? candidate.status : null;
};

export const isSessionInvalidatingError = (error: unknown): boolean => {
  if (!error || typeof error !== 'object') {
    return false;
  }

  const candidate = error as { status?: unknown; response?: unknown };
  const statuses = [readNumericStatus(candidate), readNumericStatus(candidate.response)];

  return statuses.some((status) => status === 401 || status === 403);
};
