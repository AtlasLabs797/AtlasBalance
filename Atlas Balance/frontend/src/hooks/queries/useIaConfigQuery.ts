import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import api from '@/services/api';
import { queryKeys } from '@/queries/queryKeys';
import { useAuthStore } from '@/stores/authStore';
import type { IaConfig } from '@/types';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import {
  IA_CONFIG_STALE_MS,
  isIaConfigurationWrite,
  notifyIaConfigChanged,
  resolveIaAvailability,
  subscribeIaConfigChanged,
} from '@/utils/iaConfigSource';

// R-8 (BAJA): /ia/config se consultaba cada 30 s en todas las pestañas y la
// configuracion de IA tenia tres fuentes de verdad. La cache de TanStack Query
// es ahora la unica: sin sondeo periodico, con una ventana de frescura y con
// refresco inmediato cuando alguien guarda la configuracion global.
api.interceptors.response.use((response) => {
  // El guardado global es PUT /configuracion y su payload lleva el bloque "ia".
  // Si esa escritura tiene exito, la configuracion de IA puede haber cambiado.
  if (isIaConfigurationWrite(response.config?.method, response.config?.url)) {
    notifyIaConfigChanged();
  }
  return response;
});

export function useIaConfigQuery() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);
  const queryClient = useQueryClient();

  const query = useQuery<IaConfig>({
    queryKey: queryKeys.ia.config({ usuarioId: usuarioId ?? '' }),
    queryFn: ({ signal }) =>
      api.get<IaConfig>('/ia/config', { signal }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    // Ventana de frescura en vez de refetchInterval: la peticion no se repite
    // cada 30 s, solo cuando la cache caduca o cuando alguien invalida.
    staleTime: IA_CONFIG_STALE_MS,
    // Al volver a la pestaña, si la cache ya caduco, se vuelve a consultar.
    refetchOnWindowFocus: true,
  });

  // Si la configuracion global se acaba de guardar, la cache puede estar vieja:
  // se invalida y la peticion sale ya (la query esta activa mientras el hook
  // esta montado).
  useEffect(
    () =>
      subscribeIaConfigChanged(() => {
        void queryClient.invalidateQueries({
          queryKey: queryKeys.ia.config({ usuarioId: usuarioId ?? '' }),
        });
      }),
    [queryClient, usuarioId]
  );

  // Hidratar store compartido con la ultima respuesta y TTL. Va en un efecto:
  // escribir en el store durante el render actualiza otros componentes mientras
  // React esta renderizando y dispara el aviso "Cannot update a component while
  // rendering a different component".
  const { data, isFetching, dataUpdatedAt } = query;
  useEffect(() => {
    useIaAvailabilityStore.setState({
      available: resolveIaAvailability(data),
      checking: isFetching,
      checkedAt: dataUpdatedAt > 0 ? dataUpdatedAt : null,
    });
  }, [data, isFetching, dataUpdatedAt]);

  return query;
}
