import { QueryClient } from '@tanstack/react-query';

export const QUERY_STALE_TIMES = {
  DASHBOARD_MS: 15_000,
  EXTRACTOS_MS: 10_000,
  CUENTA_RESUMEN_MS: 5_000,
  ALERTAS_MS: 10_000,
  NOTIFICACIONES_MS: 10_000,
  LISTADO_PAGINADO_MS: 30_000,
  USUARIOS_MS: 30_000,
  AUDITORIA_MS: 30_000,
  CATALOGO_CORTO_MS: 60_000,
  CATALOGO_LARGO_MS: 5 * 60_000,
  IA_MODELOS_MS: 30 * 60_000,
} as const;

export const QUERY_GC_TIMES = {
  DEFAULT_MS: 5 * 60_000,
  EXTRACTOS_MS: 90_000,
  POLLING_MS: 30_000,
} as const;

const shouldRefetchOnWindowFocus = (): boolean => {
  if (typeof window === 'undefined') {
    return false;
  }
  return window.location.pathname !== '/login' && window.location.pathname !== '/cambiar-password';
};

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        refetchOnWindowFocus: shouldRefetchOnWindowFocus,
        refetchOnReconnect: true,
        staleTime: QUERY_STALE_TIMES.CATALOGO_CORTO_MS,
        gcTime: QUERY_GC_TIMES.DEFAULT_MS,
        networkMode: 'online',
        structuralSharing: true,
      },
      mutations: {
        retry: false,
        networkMode: 'online',
      },
    },
  });
}

export const queryClient = createQueryClient();

export function clearQueryClient(): void {
  queryClient.clear();
}
