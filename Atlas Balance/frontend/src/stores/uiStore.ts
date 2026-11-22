import { create } from 'zustand';
import { getBrowserStorage } from '@/utils/sessionScope';
import {
  applyThemeToDocument,
  resolveInitialTheme,
  systemPrefersDark,
  type Theme,
} from '@/utils/theme';
import {
  applyDensityToDocument,
  DENSITY_STORAGE_KEY,
  resolveInitialDensity,
  type Density,
} from '@/utils/density';

const MAX_TOASTS = 5;

function initialTheme(): Theme {
  let stored: string | null;
  try {
    stored = getBrowserStorage('localStorage')?.getItem('theme') ?? null;
  } catch {
    stored = null;
  }
  return resolveInitialTheme(stored, systemPrefersDark());
}

function initialDensity(): Density {
  let stored: string | null;
  try {
    stored = getBrowserStorage('localStorage')?.getItem(DENSITY_STORAGE_KEY) ?? null;
  } catch {
    stored = null;
  }
  return resolveInitialDensity(stored);
}

interface Toast {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  message: string;
}

interface UiState {
  theme: Theme;
  density: Density;
  sidebarCollapsed: boolean;
  activeModal: string | null;
  blockingOverlayCount: number;
  toasts: Toast[];

  // Actions
  toggleTheme: () => void;
  setTheme: (theme: Theme) => void;
  setDensity: (density: Density) => void;
  toggleSidebar: () => void;
  setSidebarCollapsed: (collapsed: boolean) => void;
  openModal: (modalId: string) => void;
  closeModal: () => void;
  registerBlockingOverlay: () => void;
  unregisterBlockingOverlay: () => void;
  addToast: (toast: Omit<Toast, 'id'>) => void;
  removeToast: (id: string) => void;
  clearSessionTransientState: () => void;
}

const storage = () => getBrowserStorage('localStorage');

export const useUiStore = create<UiState>((set) => ({
  theme: initialTheme(),
  density: initialDensity(),
  sidebarCollapsed: false,
  activeModal: null,
  blockingOverlayCount: 0,
  toasts: [],

  toggleTheme: () =>
    set((state) => {
      const newTheme = state.theme === 'light' ? 'dark' : 'light';
      storage()?.setItem('theme', newTheme);
      applyThemeToDocument(newTheme);
      return { theme: newTheme };
    }),

  setTheme: (theme) => {
    storage()?.setItem('theme', theme);
    applyThemeToDocument(theme);
    set({ theme });
  },

  setDensity: (density) => {
    try {
      storage()?.setItem(DENSITY_STORAGE_KEY, density);
    } catch {
      // Sin almacenamiento: la densidad solo dura la sesion.
    }
    applyDensityToDocument(density);
    set({ density });
  },

  toggleSidebar: () =>
    set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),

  setSidebarCollapsed: (collapsed) => set({ sidebarCollapsed: collapsed }),

  openModal: (modalId) => set({ activeModal: modalId }),
  closeModal: () => set({ activeModal: null }),
  registerBlockingOverlay: () =>
    set((state) => ({ blockingOverlayCount: state.blockingOverlayCount + 1 })),
  unregisterBlockingOverlay: () =>
    set((state) => ({ blockingOverlayCount: Math.max(0, state.blockingOverlayCount - 1) })),

  addToast: (toast) =>
    set((state) => ({
      // V-02.08: tope maximo. Con la pestana oculta los timers de
      // auto-dismiss se pausan y errores en rafaga podian acumular toasts
      // sin limite.
      toasts: [...state.toasts, { ...toast, id: crypto.randomUUID() }].slice(-MAX_TOASTS),
    })),

  removeToast: (id) =>
    set((state) => ({
      toasts: state.toasts.filter((t) => t.id !== id),
    })),

  clearSessionTransientState: () =>
    set({ activeModal: null, blockingOverlayCount: 0, toasts: [] }),
}));
