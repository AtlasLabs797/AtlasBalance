import { create } from 'zustand';
import api from '@/services/api';
import type { VersionDisponibleResponse } from '@/types';

interface UpdateState {
  checking: boolean;
  available: boolean;
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
        currentVersion: data.version_actual ?? null,
        availableVersion: data.version_disponible ?? null,
        message: data.mensaje ?? null,
        checkedAt: now,
      });
    } catch {
      set({
        checking: false,
        available: false,
        message: 'No se pudo verificar actualización.',
        checkedAt: now,
      });
    }
  },

  clear: () =>
    set({
      checking: false,
      available: false,
      currentVersion: null,
      availableVersion: null,
      message: null,
      checkedAt: null,
    }),
}));
