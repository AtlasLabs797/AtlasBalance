import { useEffect } from 'react';
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

  // Hidratar store compartido con la ultima respuesta y TTL. Va en un efecto:
  // escribir en el store durante el render actualiza otros componentes mientras
  // React esta renderizando y dispara el aviso "Cannot update a component while
  // rendering a different component".
  const { data, isFetching, dataUpdatedAt } = query;
  useEffect(() => {
    useIaAvailabilityStore.setState({
      available: Boolean(data?.habilitada && data?.usuario_puede_usar),
      checking: isFetching,
      checkedAt: dataUpdatedAt > 0 ? dataUpdatedAt : null,
    });
  }, [data, isFetching, dataUpdatedAt]);

  return query;
}
