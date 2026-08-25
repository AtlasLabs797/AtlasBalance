import { useEffect } from 'react';
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

  // En efecto, no en el render: ver nota en useIaConfigQuery.
  const { data, isFetching, error } = query;
  useEffect(() => {
    if (data) {
      useUpdateStore.setState({
        checking: isFetching,
        available: Boolean(data.actualizacion_disponible),
        installable: Boolean(data.instalable),
        blockers: data.bloqueos ?? [],
        preflight: {
          assetZipDetected: Boolean(data.asset_zip_detectado),
          signatureDetected: Boolean(data.firma_detectada),
          digestPresent: Boolean(data.digest_presente),
          publicKeyConfigured: Boolean(data.clave_publica_configurada),
          watchdogAvailable: Boolean(data.watchdog_disponible),
          assetZipName: data.asset_zip_nombre ?? null,
        },
        currentVersion: data.version_actual ?? null,
        availableVersion: data.version_disponible ?? null,
        message: data.mensaje ?? null,
      });
    } else if (error) {
      useUpdateStore.setState({
        checking: false,
        available: false,
        installable: false,
        blockers: [],
        message: 'No se pudo verificar actualización.',
      });
    }
  }, [data, isFetching, error]);

  return query;
}
