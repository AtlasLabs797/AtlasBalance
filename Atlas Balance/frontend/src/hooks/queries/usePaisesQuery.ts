import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import api, { extractList } from '@/services/api';
import { queryKeys } from '@/queries/queryKeys';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { useAuthStore } from '@/stores/authStore';
import type { Pais } from '@/types';
import { usePaisScopeStore as paisStore } from '@/stores/paisScopeStore';

export function usePaisesQuery() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);

  const query = useQuery<Pais[]>({
    queryKey: queryKeys.catalogo.paises({ usuarioId: usuarioId ?? '', page: 1, pageSize: 500, activos: true }),
    queryFn: ({ signal }) =>
      api.get<unknown>('/paises', {
        params: { page: 1, pageSize: 500, activos: true },
        signal,
      }).then((res) => extractList<Pais>(res.data)),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.CATALOGO_LARGO_MS,
  });

  // En efecto, no en el render: escribir en el store mientras React renderiza
  // actualiza PaisScopeSelect desde dentro del render de Layout y dispara el
  // aviso "Cannot update a component while rendering a different component".
  // La seleccion vigente se lee con getState() en vez de depender de ella: solo
  // hay que revalidarla cuando llegan paises nuevos, no cada vez que el usuario
  // cambia de pais.
  const { data, isFetching, error } = query;
  useEffect(() => {
    const paises = data ?? [];
    if (paises.length > 0) {
      const actual = paisStore.getState().selectedPaisId;
      const stillActive = !actual || paises.some((p) => p.id === actual);
      paisStore.setState({
        paises,
        loading: isFetching,
        lastError: error ? 'No se pudieron cargar paises activos.' : null,
        selectedPaisId: stillActive ? actual : '',
      });
    } else if (error) {
      paisStore.setState({ loading: false, lastError: 'No se pudieron cargar paises activos.' });
    }
  }, [data, isFetching, error]);

  return query;
}
