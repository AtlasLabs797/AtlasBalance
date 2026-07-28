import { useQuery } from '@tanstack/react-query';
import api from '@/services/api';
import { queryKeys } from '@/queries/queryKeys';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import type { Pais } from '@/types';
import { usePaisScopeStore as paisStore } from '@/stores/paisScopeStore';

export function usePaisesQuery() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);

  const query = useQuery<Pais[]>({
    queryKey: queryKeys.catalogo.paises({ usuarioId: usuarioId ?? '', page: 1, pageSize: 500, activos: true }),
    queryFn: ({ signal }) =>
      api.get<Pais[]>('/paises', {
        params: { page: 1, pageSize: 500, activos: true },
        signal,
      }).then((res) => res.data ?? []),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.CATALOGO_LARGO_MS,
  });

  const paises = query.data ?? [];
  if (paises.length > 0) {
    const stillActive = !selectedPaisId || paises.some((p) => p.id === selectedPaisId);
    paisStore.setState({
      paises,
      loading: query.isFetching,
      lastError: query.error ? 'No se pudieron cargar paises activos.' : null,
      selectedPaisId: stillActive ? selectedPaisId : '',
    });
  } else if (query.error) {
    paisStore.setState({ loading: false, lastError: 'No se pudieron cargar paises activos.' });
  }

  return query;
}
