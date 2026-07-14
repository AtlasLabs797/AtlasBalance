import { create } from 'zustand';

type Theme = 'light' | 'dark';

function normalizeTheme(value: string | null): Theme {
  return value === 'dark' || value === 'light' ? value : 'light';
}

interface Toast {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  message: string;
}

interface UiState {
  theme: Theme;
  sidebarCollapsed: boolean;
  activeModal: string | null;
  blockingOverlayCount: number;
  toasts: Toast[];

  // Actions
  toggleTheme: () => void;
  setTheme: (theme: Theme) => void;
  toggleSidebar: () => void;
  setSidebarCollapsed: (collapsed: boolean) => void;
  openModal: (modalId: string) => void;
  closeModal: () => void;
  registerBlockingOverlay: () => void;
  unregisterBlockingOverlay: () => void;
  addToast: (toast: Omit<Toast, 'id'>) => void;
  removeToast: (id: string) => void;
}

export const useUiStore = create<UiState>((set) => ({
  theme: normalizeTheme(localStorage.getItem('theme')),
  sidebarCollapsed: false,
  activeModal: null,
  blockingOverlayCount: 0,
  toasts: [],

  toggleTheme: () =>
    set((state) => {
      const newTheme = state.theme === 'light' ? 'dark' : 'light';
      localStorage.setItem('theme', newTheme);
      document.documentElement.setAttribute('data-theme', newTheme);
      return { theme: newTheme };
    }),

  setTheme: (theme) => {
    localStorage.setItem('theme', theme);
    document.documentElement.setAttribute('data-theme', theme);
    set({ theme });
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
      toasts: [...state.toasts, { ...toast, id: crypto.randomUUID() }],
    })),

  removeToast: (id) =>
    set((state) => ({
      toasts: state.toasts.filter((t) => t.id !== id),
    })),
}));
