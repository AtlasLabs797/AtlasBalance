import { create } from 'zustand';
import { clearUserScopedState } from '@/stores/sessionState';
import type { Usuario } from '@/types';
import { shouldResetUserScopedState } from '@/utils/sessionScope';

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

export const useAuthStore = create<AuthState>((set, get) => ({
  usuario: null,
  csrfToken: null,
  isAuthenticated: false,
  isLoading: true, // true until we check auth status on app load

  setUsuario: (usuario, csrfToken) => {
    const userChanged = shouldResetUserScopedState(get().usuario?.id ?? null, usuario.id);
    if (userChanged) {
      clearUserScopedState();
    }

    set((state) => ({
      // V-02.06: el backend ahora expone si el usuario actual esta obligado a
      // usar Authenticator. Mantenemos el valor en el store para que ProtectedRoute
      // y la pantalla de login puedan forzar el flujo MFA sin revalidar en cada
      // navegacion. Si el backend no lo manda (versiones antiguas), caemos a
      // una politica conservadora: cualquier usuario con mfa_enabled=true queda
      // marcado como obligatorio (compatible con el comportamiento previo).
      usuario: {
        ...usuario,
        mfa_required:
          typeof usuario.mfa_required === 'boolean'
            ? usuario.mfa_required
            : usuario.mfa_enabled,
      },
      csrfToken: csrfToken ?? (userChanged ? null : state.csrfToken),
      isAuthenticated: true,
      isLoading: false,
    }));
  },

  logout: () => {
    clearUserScopedState();
    set({ usuario: null, csrfToken: null, isAuthenticated: false, isLoading: false });
  },

  setLoading: (isLoading) => set({ isLoading }),
}));
