import { create } from 'zustand';
import api from '@/services/api';
import { extractErrorMessage } from '@/utils/errorMessage';

export interface AlertaActiva {
  alerta_id: string;
  cuenta_id: string;
  titular_id: string;
  cuenta_nombre: string;
  titular_nombre: string;
  saldo_actual: number;
  saldo_minimo: number;
  divisa: string;
}

interface AlertasState {
  alertasActivas: AlertaActiva[];
  bannerDismissed: boolean;
  loading: boolean;
  lastError: string | null;

  setAlertasActivas: (alertas: AlertaActiva[]) => void;
  dismissBanner: () => void;
  resetBanner: () => void;
  clear: () => void;
  loadAlertasActivas: () => Promise<void>;
}

const SESSION_KEY = 'alertas_banner_dismissed_session';

export const useAlertasStore = create<AlertasState>((set) => ({
  alertasActivas: [],
  bannerDismissed: sessionStorage.getItem(SESSION_KEY) === '1',
  loading: false,
  lastError: null,

  setAlertasActivas: (alertasActivas) => set({ alertasActivas, lastError: null }),
  dismissBanner: () => {
    sessionStorage.setItem(SESSION_KEY, '1');
    set({ bannerDismissed: true });
  },
  resetBanner: () => {
    sessionStorage.removeItem(SESSION_KEY);
    set({ bannerDismissed: false });
  },
  clear: () => {
    sessionStorage.removeItem(SESSION_KEY);
    set({ alertasActivas: [], bannerDismissed: false, loading: false, lastError: null });
  },
  loadAlertasActivas: async () => {
    set({ loading: true, lastError: null });
    try {
      const { data } = await api.get<AlertaActiva[]>('/alertas/activas');
      set({ alertasActivas: data, lastError: null });
      if (data.length === 0) {
        sessionStorage.removeItem(SESSION_KEY);
        set({ bannerDismissed: false });
      }
    } catch (error: unknown) {
      set({
        alertasActivas: [],
        bannerDismissed: false,
        lastError: extractErrorMessage(error, 'No se pudieron cargar las alertas activas.'),
      });
    } finally {
      set({ loading: false });
    }
  },
}));

export const useAlertCount = (): number =>
  useAlertasStore((state) => state.alertasActivas.length);
