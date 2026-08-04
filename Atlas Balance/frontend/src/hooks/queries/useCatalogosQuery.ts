import { useQuery } from '@tanstack/react-query';
import api from '@/services/api';
import { queryKeys } from '@/queries/queryKeys';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import type { ImportContextoResponse } from '@/types';

export function useImportacionContextoQuery() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);

  return useQuery<ImportContextoResponse>({
    queryKey: queryKeys.catalogo.importacionContexto({ usuarioId: usuarioId ?? '', paisId: selectedPaisId || null }),
    queryFn: ({ signal }) =>
      api.get<ImportContextoResponse>('/importacion/contexto', {
        params: { paisId: selectedPaisId || undefined },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.CATALOGO_CORTO_MS,
  });
}

export function useCuentasDivisasActivasQuery() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);

  return useQuery<{ data?: { codigo: string }[] } | { codigos?: string[] }>({
    queryKey: queryKeys.catalogo.divisasActivas({ usuarioId: usuarioId ?? '' }),
    queryFn: ({ signal }) =>
      api.get('/cuentas/divisas-activas', { signal }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.CATALOGO_LARGO_MS,
  });
}
