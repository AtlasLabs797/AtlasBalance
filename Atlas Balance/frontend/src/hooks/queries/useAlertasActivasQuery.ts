import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import api from '@/services/api';
import { queryKeys } from '@/queries/queryKeys';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { useAlertasStore } from '@/stores/alertasStore';
import type { AlertaActiva } from '@/stores/alertasStore';
import { extractErrorMessage } from '@/utils/errorMessage';

export function useAlertasActivasQuery() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);

  const query = useQuery<AlertaActiva[]>({
    queryKey: queryKeys.alertas.activas({ usuarioId: usuarioId ?? '', paisId: selectedPaisId || null }),
    queryFn: ({ signal }) =>
      api.get<AlertaActiva[]>('/alertas/activas', {
        params: { paisId: selectedPaisId || undefined },
        signal,
      }).then((res) => res.data ?? []),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.ALERTAS_MS,
  });

  useEffect(() => {
    const alertas = query.data ?? [];
    useAlertasStore.setState({
      alertasActivas: alertas,
      loading: query.isLoading,
      lastError: query.error
        ? extractErrorMessage(query.error, 'No se pudieron cargar las alertas activas.')
        : null,
    });
    if (alertas.length === 0) {
      useAlertasStore.getState().resetBanner();
    }
  }, [query.data, query.error, query.isLoading]);

  return query;
}
