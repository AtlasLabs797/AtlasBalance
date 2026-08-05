import axios from 'axios';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { useUiStore } from '@/stores/uiStore';
import type { PermisoUsuario, Usuario } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { clearQueryClient } from '@/services/queryClient';

const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
  // V-02-05 (LOW-FE-2): timeout duro de 15s. Sin esto, un backend colgado puede
  // dejar requests en vuelo indefinidamente y saturar el limite de conexiones
  // del navegador (6 por host en Chrome).
  timeout: 15_000,
  headers: {
    'Content-Type': 'application/json',
  },
});

const shouldSkipRefreshRetry = (url?: string) =>
  !!url && (url.includes('/auth/login') || url.includes('/auth/refresh-token'));

const syncSessionState = (usuario: Usuario | null | undefined, csrfToken: string | null, permisos: PermisoUsuario[] | null | undefined) => {
  if (usuario) {
    useAuthStore.getState().setUsuario(usuario, csrfToken);
  }

  usePermisosStore.getState().setPermisos(permisos ?? []);
};

const clearSessionState = () => {
  useAuthStore.getState().logout();
  usePermisosStore.getState().clear();
  useAlertasStore.getState().clear();
  usePaisScopeStore.getState().clear();
  // TanStack Query: tras logout, restore o cambio de usuario/permisos la
  // caché en memoria contiene datos del usuario anterior (saldos,
  // extractos, alertas). Limpiarla aqui evita fuga entre sesiones.
  clearQueryClient();
};

const pushErrorToast = (message: string) => {
  useUiStore.getState().addToast({
    type: 'error',
    message,
  });
};

api.interceptors.request.use((config) => {
  const csrfToken = useAuthStore.getState().csrfToken;
  const method = (config.method ?? 'get').toLowerCase();

  if (csrfToken && !['get', 'head', 'options'].includes(method)) {
    config.headers['X-CSRF-Token'] = csrfToken;
  }

  // V-02-05 (LOW-FE-3): no forzar Content-Type application/json cuando el caller
  // envia FormData. Si lo hicieramos, axios quitaba el boundary del multipart
  // y el backend rechazaba el body.
  if (typeof FormData !== 'undefined' && config.data instanceof FormData) {
    if (config.headers) {
      delete config.headers['Content-Type'];
    }
  }

  return config;
});

const MAX_REFRESH_QUEUE = 50;
let isRefreshing = false;
let failedQueue: Array<{
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
}> = [];

const processQueue = (error: unknown | null) => {
  failedQueue.forEach((prom) => {
    if (error) {
      prom.reject(error);
    } else {
      prom.resolve(undefined);
    }
  });
  failedQueue = [];
};

const getSafeErrorLogDetail = (error: unknown): string => {
  const message = extractErrorMessage(error, '');
  if (message) return message;
  if (error instanceof Error) return error.message;
  return 'Error sin detalle seguro';
};

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;
    const status: number | undefined = error.response?.status;

    if (import.meta.env.DEV) {
      console.error(
        `[API] ${originalRequest?.method?.toUpperCase() ?? '?'} ${originalRequest?.url ?? '?'} ->`,
        status ?? 'SIN RESPUESTA',
        getSafeErrorLogDetail(error)
      );
    }

    if (status === 419 || status === 440) {
      clearSessionState();
      pushErrorToast('Sesión caducada. Vuelve a iniciar sesión para continuar.');
      window.location.href = '/login';
      return Promise.reject(error);
    }

    if (status === 429) {
      // Rate limit: el backend manda Retry-After en segundos. No reintentamos,
      // no hacemos logout, no redirigimos: solo avisamos.
      const retryAfterHeader = error.response?.headers?.['retry-after'];
      const retryAfterSeconds = retryAfterHeader != null ? parseInt(retryAfterHeader, 10) : NaN;

      if (!Number.isNaN(retryAfterSeconds) && retryAfterSeconds > 0) {
        // Con Retry-After tenemos el dato mas util (cuanto esperar), asi que
        // preferimos nuestro mensaje con la cifra antes que el generico de
        // extractErrorMessage (que no incluye el tiempo de espera).
        if (retryAfterSeconds > 60) {
          const minutos = Math.round(retryAfterSeconds / 60);
          pushErrorToast(`Demasiadas peticiones. Espera ${minutos} minuto${minutos === 1 ? '' : 's'} y vuelve a intentarlo.`);
        } else {
          pushErrorToast(`Demasiadas peticiones. Espera ${retryAfterSeconds} segundos y vuelve a intentarlo.`);
        }
      } else {
        // Sin Retry-After preferimos el mensaje del backend si viene en el
        // payload; si no, uno generico sin cifra.
        pushErrorToast(extractErrorMessage(error, 'Demasiadas peticiones. Espera un momento y vuelve a intentarlo.'));
      }

      return Promise.reject(error);
    }

    if (
      !originalRequest ||
      status !== 401 ||
      originalRequest._retry ||
      shouldSkipRefreshRetry(originalRequest.url)
    ) {
      if (status !== 401) {
        if (!status) {
          // Sin respuesta del servidor: backend caído, red cortada
          pushErrorToast('No se puede conectar con el servidor. Espera un momento e inténtalo de nuevo.');
        } else {
          pushErrorToast(extractErrorMessage(error, 'La operación no pudo completarse. Revisa los datos e inténtalo de nuevo.'));
        }
      }
      return Promise.reject(error);
    }

    if (isRefreshing) {
      if (failedQueue.length >= MAX_REFRESH_QUEUE) {
        return Promise.reject(error);
      }
      originalRequest._retry = true;
      return new Promise((resolve, reject) => {
        failedQueue.push({ resolve, reject });
      }).then(() => api(originalRequest));
    }

    originalRequest._retry = true;
    isRefreshing = true;

    try {
      const { data } = await api.post('/auth/refresh-token');
      syncSessionState(data.usuario, data.csrf_token, data.permisos);
      processQueue(null);
      return api(originalRequest);
    } catch (refreshError) {
      processQueue(refreshError);
      clearSessionState();
      pushErrorToast('Sesión expirada. Vuelve a iniciar sesión.');
      window.location.href = '/login';
      return Promise.reject(refreshError);
    } finally {
      isRefreshing = false;
    }
  }
);

export default api;

// Algunas rutas devuelven array plano (catalogos pequenos) y otras
// PaginatedResponse<T>. Este helper acepta ambos y devuelve la lista,
// evitando que cada consumidor tenga que recordar cual endpoint usa cual.
export function extractList<T>(payload: unknown): T[] {
  if (!payload) return [];
  if (Array.isArray(payload)) return payload as T[];
  if (typeof payload === 'object' && payload !== null && 'data' in payload) {
    const data = (payload as { data: unknown }).data;
    if (Array.isArray(data)) return data as T[];
  }
  return [];
}
