import { useQuery } from '@tanstack/react-query';
import api from '@/services/api';
import { queryKeys } from '@/queries/queryKeys';
import { useAuthStore } from '@/stores/authStore';
import type { IaConfig } from '@/types';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';

const IA_TTL = 30_000;

export function useIaConfigQuery() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);

  const query = useQuery<IaConfig>({
    queryKey: queryKeys.ia.config({ usuarioId: usuarioId ?? '' }),
    queryFn: ({ signal }) =>
      api.get<IaConfig>('/ia/config', { signal }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    staleTime: IA_TTL,
    refetchInterval: IA_TTL,
    refetchIntervalInBackground: false,
  });

  // Hidratar store compartido con la ultima respuesta y TTL.
  useIaAvailabilityStore.setState({
    available: Boolean(query.data?.habilitada && query.data?.usuario_puede_usar),
    checking: query.isFetching,
    checkedAt: query.dataUpdatedAt > 0 ? query.dataUpdatedAt : null,
  });

  return query;
}
