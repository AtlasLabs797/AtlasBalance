import { AxiosError } from 'axios';

export function extractErrorMessage(err: unknown, fallback = 'Error inesperado'): string {
  if (err instanceof AxiosError) {
    return err.response?.data?.error ?? err.message ?? fallback;
  }

  if (err instanceof Error) {
    return err.message;
  }

  return fallback;
}
