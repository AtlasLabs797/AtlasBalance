import axios from 'axios';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { useUiStore } from '@/stores/uiStore';
import type { PermisoUsuario, Usuario } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';

const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
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
