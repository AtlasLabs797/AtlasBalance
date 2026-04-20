import axios from 'axios';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { useUiStore } from '@/stores/uiStore';
import type { PermisoUsuario, Usuario } from '@/types';

const API_BASE = import.meta.env.VITE_API_URL || '';

const api = axios.create({
  baseURL: `${API_BASE}/api`,
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

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    if (
      !originalRequest ||
      error.response?.status !== 401 ||
      originalRequest._retry ||
      shouldSkipRefreshRetry(originalRequest.url)
    ) {
      const status = error.response?.status;
      const payloadMessage = error.response?.data?.error;
      if (status !== 401) {
        pushErrorToast(payloadMessage ?? 'La operación no pudo completarse. Revisa los datos e inténtalo de nuevo.');
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
