import { create } from 'zustand';
import type { Usuario } from '@/types';

interface AuthState {
  usuario: Usuario | null;
  csrfToken: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;

  // Actions
  setUsuario: (usuario: Usuario, csrfToken?: string | null) => void;
  logout: () => void;
  setLoading: (loading: boolean) => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  usuario: null,
  csrfToken: null,
  isAuthenticated: false,
  isLoading: true, // true until we check auth status on app load

  setUsuario: (usuario, csrfToken) =>
    set((state) => ({
      usuario,
      csrfToken: csrfToken ?? state.csrfToken,
      isAuthenticated: true,
      isLoading: false,
    })),

  logout: () =>
    set({ usuario: null, csrfToken: null, isAuthenticated: false, isLoading: false }),

  setLoading: (isLoading) => set({ isLoading }),
}));
