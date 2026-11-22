import { create } from 'zustand';
import api from '@/services/api';
import { extractErrorMessage } from '@/utils/errorMessage';
import {
  getBrowserStorage,
  getSessionGeneration,
  isSessionGenerationCurrent,
  USER_SCOPED_STORAGE_KEYS,
} from '@/utils/sessionScope';

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
  // Huella del contenido que el usuario oculto. El descarte se ancla al
  // contenido para que recargar o cambiar de pais no lo borre: solo deja de
  // aplicar cuando el contenido cambia de verdad.
  bannerDismissedFor: string | null;
  loading: boolean;
  lastError: string | null;

  setAlertasActivas: (alertas: AlertaActiva[]) => void;
  dismissBanner: () => void;
  restoreBanner: () => void;
  clear: () => void;
  syncAlertas: (
    alertas: AlertaActiva[] | undefined,
    estado: { loading: boolean; error: string | null },
  ) => void;
  loadAlertasActivas: (paisId?: string) => Promise<void>;
}

const SESSION_KEY = USER_SCOPED_STORAGE_KEYS.alertasBannerDismissed;
const sessionStorage = () => getBrowserStorage('sessionStorage');

// Huella del contenido del banner: los pares (alerta, cuenta) que estan por
// debajo del minimo, ordenados. Dos contenidos con la misma huella son el
// mismo aviso, aunque se recargue o se cambie de pais.
function huellaAlertas(alertas: AlertaActiva[]): string {
  return alertas
    .map((alerta) => `${alerta.alerta_id}:${alerta.cuenta_id}`)
    .sort()
    .join('|');
}

function leerHuellaDescartada(): string | null {
  const valor = sessionStorage()?.getItem(SESSION_KEY);
  return valor ? valor : null;
}

export const useAlertasStore = create<AlertasState>((set, get) => ({
  alertasActivas: [],
  bannerDismissed: false,
  bannerDismissedFor: leerHuellaDescartada(),
  loading: false,
  lastError: null,

  setAlertasActivas: (alertasActivas) => set({ alertasActivas, lastError: null }),
  dismissBanner: () => {
    const huella = huellaAlertas(get().alertasActivas);
    sessionStorage()?.setItem(SESSION_KEY, huella);
    set({ bannerDismissed: true, bannerDismissedFor: huella });
  },
  restoreBanner: () => {
    sessionStorage()?.removeItem(SESSION_KEY);
    set({ bannerDismissed: false, bannerDismissedFor: null });
  },
  clear: () => {
    sessionStorage()?.removeItem(SESSION_KEY);
    set({
      alertasActivas: [],
      bannerDismissed: false,
      bannerDismissedFor: null,
      loading: false,
      lastError: null,
    });
  },
  // Unico punto por el que entra el contenido de alertas. `alertas` es
  // undefined mientras la consulta aun no ha devuelto datos: eso no es un
  // cambio de contenido, asi que el descarte del usuario se respeta.
  syncAlertas: (alertas, estado) => {
    if (alertas === undefined) {
      set({ alertasActivas: [], loading: estado.loading, lastError: estado.error });
      return;
    }
    set({
      alertasActivas: alertas,
      loading: estado.loading,
      lastError: estado.error,
      bannerDismissed: get().bannerDismissedFor === huellaAlertas(alertas),
    });
  },
  loadAlertasActivas: async (paisId) => {
    const generation = getSessionGeneration();
    set({ loading: true, lastError: null });
    try {
      const { data } = await api.get<AlertaActiva[]>('/alertas/activas', {
        params: { paisId: paisId || undefined },
      });
      if (!isSessionGenerationCurrent(generation)) return;
      get().syncAlertas(data ?? [], { loading: false, error: null });
    } catch (error: unknown) {
      if (!isSessionGenerationCurrent(generation)) return;
      get().syncAlertas(undefined, {
        loading: false,
        error: extractErrorMessage(error, 'No se pudieron cargar las alertas activas.'),
      });
    } finally {
      if (isSessionGenerationCurrent(generation)) {
        set({ loading: false });
      }
    }
  },
}));

export const useAlertCount = (): number =>
  useAlertasStore((state) => state.alertasActivas.length);
