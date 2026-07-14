import { create } from 'zustand';
import api from '@/services/api';
import type { VersionDisponibleResponse } from '@/types';

interface UpdateState {
  checking: boolean;
  available: boolean;
  installable: boolean;
  blockers: string[];
  preflight: {
    assetZipDetected: boolean;
    signatureDetected: boolean;
    digestPresent: boolean;
    publicKeyConfigured: boolean;
    watchdogAvailable: boolean;
    assetZipName: string | null;
  };
  currentVersion: string | null;
  availableVersion: string | null;
  message: string | null;
  checkedAt: number | null;
  check: (force?: boolean) => Promise<void>;
  clear: () => void;
}

const CHECK_TTL_MS = 60 * 1000;

export const useUpdateStore = create<UpdateState>((set, get) => ({
  checking: false,
  available: false,
  installable: false,
  blockers: [],
  preflight: {
    assetZipDetected: false,
    signatureDetected: false,
    digestPresent: false,
    publicKeyConfigured: false,
    watchdogAvailable: false,
    assetZipName: null,
  },
  currentVersion: null,
  availableVersion: null,
  message: null,
  checkedAt: null,

  check: async (force = false) => {
    const now = Date.now();
    const checkedAt = get().checkedAt;
    if (!force && checkedAt !== null && now - checkedAt < CHECK_TTL_MS) {
      return;
    }

    set({ checking: true });
    try {
      const { data } = await api.get<VersionDisponibleResponse>('/sistema/version-disponible');
      set({
        checking: false,
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
        checkedAt: now,
      });
    } catch {
      set({
        checking: false,
        available: false,
        installable: false,
        blockers: [],
        preflight: {
          assetZipDetected: false,
          signatureDetected: false,
          digestPresent: false,
          publicKeyConfigured: false,
          watchdogAvailable: false,
          assetZipName: null,
        },
        message: 'No se pudo verificar actualización.',
        checkedAt: now,
      });
    }
  },

  clear: () =>
    set({
      checking: false,
      available: false,
      installable: false,
      blockers: [],
      preflight: {
        assetZipDetected: false,
        signatureDetected: false,
        digestPresent: false,
        publicKeyConfigured: false,
        watchdogAvailable: false,
        assetZipName: null,
      },
      currentVersion: null,
      availableVersion: null,
      message: null,
      checkedAt: null,
    }),
}));
