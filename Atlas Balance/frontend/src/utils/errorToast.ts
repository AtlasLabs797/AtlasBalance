// V-03.02 (F-4): el interceptor de API pinta un toast global por cada error.
// Eso esta bien en una accion normal del usuario, pero los sondeos (estado de
// la actualizacion, estado de un backup) consultan cada 2,5 s con la API caida
// a proposito: un toast cada 2,5 s.
//
// Aqui vive la decision de si una peticion fallida pinta toast y de la opcion
// por peticion `skipErrorToast`, que usan los sondeos. Helper puro y sin
// dependencias en tiempo de ejecucion: lo importan el interceptor y los tests
// (importar axios aqui romperia la compilacion aislada de los tests).

import type { AxiosRequestConfig } from 'axios';

// Opcion por peticion: el interceptor la respeta y no pinta el toast global.
// La ampliapon del modulo axios hace que `api.get(url, { skipErrorToast: true })`
// type-check sin casts.
declare module 'axios' {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars, @typescript-eslint/no-explicit-any -- la ampliapon debe repetir los parametros de tipo que declara axios
  interface AxiosRequestConfig<D = any, P = any> {
    /** Omite el toast global de error de esta peticion (sondeos). */
    skipErrorToast?: boolean;
  }
}

/** Configuracion de peticion con la opcion de no pintar el toast global. */
export type RequestOptions = AxiosRequestConfig & { skipErrorToast?: boolean };

export type ErrorToastKind = 'none' | 'network' | 'server' | 'rate-limit';

export interface ErrorToastDecision {
  kind: ErrorToastKind;
  show: boolean;
}

export interface ErrorToastOptions {
  /** La peticion se marco con skipErrorToast (sondeo). */
  skipErrorToast?: boolean;
}

const readNumericStatus = (value: unknown): number | null => {
  if (!value || typeof value !== 'object') {
    return null;
  }

  const candidate = value as { status?: unknown };
  return typeof candidate.status === 'number' ? candidate.status : null;
};

const readErrorStatus = (error: unknown): number | null => {
  if (!error || typeof error !== 'object') {
    return null;
  }

  const candidate = error as { status?: unknown; response?: unknown };
  const response = candidate.response as { status?: unknown } | undefined;

  return readNumericStatus(candidate) ?? readNumericStatus(response);
};

// axios.isCancel no se puede usar aqui: este helper tambien se compila aislado
// en los tests, sin axios. Se cubren las tres senales de cancelacion de axios.
export const isCanceledRequestError = (error: unknown): boolean => {
  if (!error || typeof error !== 'object') {
    return false;
  }

  const candidate = error as { code?: unknown; name?: unknown; __CANCEL__?: unknown };
  return (
    candidate.__CANCEL__ === true ||
    candidate.code === 'ERR_CANCELED' ||
    candidate.name === 'CanceledError'
  );
};

/**
 * Decide si una peticion fallida debe pintar el toast global de error:
 * - 'none': no hay toast. Peticion cancelada, 401 (lo lleva la rama de refresh)
 *   o peticion marcada con skipErrorToast.
 * - 'network': no llego respuesta del servidor (red cortada, backend caido).
 * - 'server': el servidor respondio con error.
 * - 'rate-limit': 429; lo lleva su propia rama del interceptor, que siempre avisa.
 */
export const decideErrorToast = (
  error: unknown,
  options: ErrorToastOptions = {},
): ErrorToastDecision => {
  if (isCanceledRequestError(error)) {
    return { kind: 'none', show: false };
  }

  const status = readErrorStatus(error);

  if (status === 401) {
    return { kind: 'none', show: false };
  }

  if (status === 429) {
    return { kind: 'rate-limit', show: true };
  }

  if (options.skipErrorToast) {
    return { kind: 'none', show: false };
  }

  if (status === null) {
    return { kind: 'network', show: true };
  }

  return { kind: 'server', show: true };
};
