import axios from 'axios';

export function extractErrorMessage(err: unknown, fallback = 'La operación no pudo completarse. Reinténtalo.'): string {
  if (axios.isAxiosError(err)) {
    if (!err.response) {
      return 'No se puede conectar con Atlas Balance. Comprueba la red o inténtalo de nuevo en unos segundos.';
    }

    const payloadMessage = getPayloadMessage(err.response.data);
    if (payloadMessage) {
      return payloadMessage;
    }

    return getStatusMessage(err.response.status) ?? fallback;
  }

  if (err instanceof Error) {
    return cleanMessage(err.message) ?? fallback;
  }

  return fallback;
}

function getPayloadMessage(data: unknown): string | null {
  if (typeof data === 'string') {
    return cleanMessage(data);
  }

  if (!data || typeof data !== 'object') {
    return null;
  }

  const payload = data as Record<string, unknown>;
  const directMessage = firstCleanString(
    payload.error,
    payload.detail,
    payload.title,
    payload.message,
    payload.mensaje,
  );

  if (directMessage) {
    return directMessage;
  }

  return getValidationErrorsMessage(payload.errors);
}

function firstCleanString(...values: unknown[]): string | null {
  for (const value of values) {
    if (typeof value === 'string') {
      const cleaned = cleanMessage(value);
      if (cleaned) {
        return cleaned;
      }
    }
  }

  return null;
}

function getValidationErrorsMessage(errors: unknown): string | null {
  if (!errors || typeof errors !== 'object') {
    return null;
  }

  const messages = Object.values(errors as Record<string, unknown>)
    .flatMap((value) => (Array.isArray(value) ? value : [value]))
    .filter((value): value is string => typeof value === 'string')
    .map((value) => cleanMessage(value))
    .filter((value): value is string => Boolean(value))
    .slice(0, 3);

  return messages.length > 0 ? messages.join(' ') : null;
}

function getStatusMessage(status?: number): string | null {
  switch (status) {
    case 400:
      return 'Revisa los datos del formulario y vuelve a intentarlo.';
    case 401:
      return 'Tu sesión ha caducado. Vuelve a iniciar sesión.';
    case 403:
      return 'No tienes permiso para hacer esto. Pide acceso a un administrador.';
    case 404:
      return 'No encontramos el recurso. Recarga la vista y vuelve a intentarlo.';
    case 409:
      return 'Los datos cambiaron mientras trabajabas. Recarga y vuelve a intentarlo.';
    case 413:
      return 'La operación supera el límite permitido. Reduce el volumen y vuelve a intentarlo.';
    case 429:
      return 'Demasiados intentos. Espera un momento y vuelve a intentarlo.';
    case 500:
      return 'El servidor falló al procesar la operación. Reintenta y, si se repite, avisa al administrador.';
    case 502:
    case 503:
    case 504:
      return 'El servidor o un proveedor externo no responde. Espera unos segundos y vuelve a intentarlo.';
    default:
      return null;
  }
}

function cleanMessage(value: string): string | null {
  const cleaned = value.replace(/\s+/g, ' ').trim();
  if (!cleaned) {
    return null;
  }

  if (/^network error$/i.test(cleaned)) {
    return 'No se puede conectar con Atlas Balance. Comprueba la red o inténtalo de nuevo en unos segundos.';
  }

  const statusMatch = cleaned.match(/^request failed with status code (\d{3})$/i);
  if (statusMatch) {
    return getStatusMessage(Number(statusMatch[1])) ?? 'La operación no pudo completarse. Reinténtalo.';
  }

  return cleaned.length > 500 ? `${cleaned.slice(0, 497)}...` : cleaned;
}
