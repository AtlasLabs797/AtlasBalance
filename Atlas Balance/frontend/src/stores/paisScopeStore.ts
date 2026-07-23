import { create } from 'zustand';
import api from '@/services/api';
import type { Pais } from '@/types';

const STORAGE_KEY = 'atlas_balance_selected_pais_id';

interface PaisScopeState {
  selectedPaisId: string;
  paises: Pais[];
  loading: boolean;
  lastError: string | null;
  setSelectedPaisId: (paisId: string) => void;
  loadPaises: () => Promise<void>;
  clear: () => void;
}

function readStoredPaisId() {
  return localStorage.getItem(STORAGE_KEY) ?? '';
}

export const usePaisScopeStore = create<PaisScopeState>((set, get) => ({
  selectedPaisId: readStoredPaisId(),
  paises: [],
  loading: false,
  lastError: null,

  setSelectedPaisId: (paisId) => {
    const next = paisId.trim();
    if (next) {
      localStorage.setItem(STORAGE_KEY, next);
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
    set({ selectedPaisId: next });
  },

  loadPaises: async () => {
    set({ loading: true, lastError: null });
    try {
      const { data } = await api.get<Pais[]>('/paises', {
        params: { page: 1, pageSize: 500, activos: true },
      });
      const paises = data ?? [];
      const selectedPaisId = get().selectedPaisId;
      const selectedStillActive = !selectedPaisId || paises.some((pais) => pais.id === selectedPaisId);
      if (!selectedStillActive) {
        localStorage.removeItem(STORAGE_KEY);
      }
      set({
        paises,
        selectedPaisId: selectedStillActive ? selectedPaisId : '',
        loading: false,
        lastError: null,
      });
    } catch {
      set({ paises: [], loading: false, lastError: 'No se pudieron cargar paises activos.' });
    }
  },

  clear: () => {
    set({ selectedPaisId: readStoredPaisId(), paises: [], loading: false, lastError: null });
  },
}));
