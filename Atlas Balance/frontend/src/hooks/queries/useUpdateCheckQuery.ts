import { useQuery } from '@tanstack/react-query';
import api from '@/services/api';
import { queryKeys } from '@/queries/queryKeys';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { useAuthStore } from '@/stores/authStore';
import type { VersionDisponibleResponse } from '@/types';
import { useUpdateStore } from '@/stores/updateStore';

export function useUpdateCheckQuery(force = false) {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);

  const query = useQuery<VersionDisponibleResponse>({
    queryKey: queryKeys.sistema.versionDisponible({ usuarioId: usuarioId ?? '' }),
    queryFn: ({ signal }) =>
      api.get<VersionDisponibleResponse>('/sistema/version-disponible', { signal }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.CATALOGO_CORTO_MS,
    refetchOnMount: force ? 'always' : false,
  });

  const data = query.data;
  if (data) {
    useUpdateStore.setState({
      checking: query.isFetching,
      available: Boolean(query.data.actualizacion_disponible),
      installable: Boolean(query.data.instalable),
      blockers: query.data.bloqueos ?? [],
      preflight: {
        assetZipDetected: Boolean(query.data.asset_zip_detectado),
        signatureDetected: Boolean(query.data.firma_detectada),
        digestPresent: Boolean(query.data.digest_presente),
        publicKeyConfigured: Boolean(query.data.clave_publica_configurada),
        watchdogAvailable: Boolean(query.data.watchdog_disponible),
        assetZipName: query.data.asset_zip_nombre ?? null,
      },
      currentVersion: query.data.version_actual ?? null,
      availableVersion: query.data.version_disponible ?? null,
      message: query.data.mensaje ?? null,
    });
  } else if (query.error) {
    useUpdateStore.setState({
      checking: false,
      available: false,
      installable: false,
      blockers: [],
      message: 'No se pudo verificar actualización.',
    });
  }

  return query;
}
