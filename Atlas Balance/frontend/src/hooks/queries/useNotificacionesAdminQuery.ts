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

  if (query.data) {
    useNotificacionesAdminStore.setState({
      exportacionesPendientes: query.data.exportaciones_pendientes ?? 0,
      totalPendientes: query.data.total_pendientes ?? 0,
      loading: query.isFetching,
    });
  }

  return query;
}
