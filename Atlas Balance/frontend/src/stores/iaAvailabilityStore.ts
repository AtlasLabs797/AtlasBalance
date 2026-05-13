import { create } from 'zustand';
import api from '@/services/api';
import type { IaConfig } from '@/types';

interface IaAvailabilityState {
  available: boolean;
  checking: boolean;
  checkedAt: number | null;
  load: (force?: boolean) => Promise<void>;
  clear: () => void;
}

const CHECK_TTL_MS = 30 * 1000;

export const useIaAvailabilityStore = create<IaAvailabilityState>((set, get) => ({
  available: false,
  checking: false,
  checkedAt: null,

  load: async (force = false) => {
    const now = Date.now();
    const { checkedAt, checking } = get();
    if (checking || (!force && checkedAt !== null && now - checkedAt < CHECK_TTL_MS)) {
      return;
    }

    set({ checking: true });
    try {
      const { data } = await api.get<IaConfig>('/ia/config');
      set({
        available: Boolean(data.habilitada && data.usuario_puede_usar),
        checking: false,
        checkedAt: now,
      });
    } catch {
      set({
        available: false,
        checking: false,
        checkedAt: now,
      });
    }
  },

  clear: () =>
    set({
      available: false,
      checking: false,
      checkedAt: null,
    }),
}));
