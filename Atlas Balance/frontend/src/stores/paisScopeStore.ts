import { create } from 'zustand';
import api from '@/services/api';
import type { Pais } from '@/types';
import { getBrowserStorage, USER_SCOPED_STORAGE_KEYS } from '@/utils/sessionScope';

const STORAGE_KEY = USER_SCOPED_STORAGE_KEYS.selectedPaisId;

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
  return getBrowserStorage('localStorage')?.getItem(STORAGE_KEY) ?? '';
}

export const usePaisScopeStore = create<PaisScopeState>((set, get) => ({
  selectedPaisId: readStoredPaisId(),
  paises: [],
  loading: false,
  lastError: null,

  setSelectedPaisId: (paisId) => {
    const next = paisId.trim();
    const storage = getBrowserStorage('localStorage');
    if (next) {
      storage?.setItem(STORAGE_KEY, next);
    } else {
      storage?.removeItem(STORAGE_KEY);
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
        getBrowserStorage('localStorage')?.removeItem(STORAGE_KEY);
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
    // Debe borrar tambien el localStorage: si no, el pais seleccionado por el
    // usuario anterior queda persistido y se carga para el siguiente usuario
    // que inicie sesion en el mismo navegador (maquina compartida en LAN).
    getBrowserStorage('localStorage')?.removeItem(STORAGE_KEY);
    set({ selectedPaisId: '', paises: [], loading: false, lastError: null });
  },
}));
