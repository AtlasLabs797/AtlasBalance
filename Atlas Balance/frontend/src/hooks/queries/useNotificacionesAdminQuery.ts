import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import api from '@/services/api';
import { queryKeys } from '@/queries/queryKeys';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { useAuthStore } from '@/stores/authStore';
import { useNotificacionesAdminStore } from '@/stores/notificacionesAdminStore';

interface Resumen {
  exportaciones_pendientes: number;
  total_pendientes: number;
}

export function useNotificacionesAdminQuery() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);
  const rol = useAuthStore((state) => state.usuario?.rol ?? null);

  const query = useQuery<Resumen>({
    queryKey: queryKeys.notificaciones.adminResumen({ usuarioId: usuarioId ?? '' }),
    queryFn: ({ signal }) =>
      api.get<Resumen>('/notificaciones-admin/resumen', { signal }).then((res) => res.data),
    enabled: Boolean(usuarioId && rol === 'ADMIN'),
    staleTime: QUERY_STALE_TIMES.NOTIFICACIONES_MS,
  });

  // En efecto, no en el render: ver nota en useIaConfigQuery.
  const { data, isFetching } = query;
  useEffect(() => {
    if (!data) {
      return;
    }
    useNotificacionesAdminStore.setState({
      exportacionesPendientes: data.exportaciones_pendientes ?? 0,
      totalPendientes: data.total_pendientes ?? 0,
      loading: isFetching,
    });
  }, [data, isFetching]);

  return query;
}
