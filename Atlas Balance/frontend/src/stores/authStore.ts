import { create } from 'zustand';
import { useAiChatStore } from '@/stores/aiChatStore';
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
      csrfToken: csrfToken ?? state.csrfToken,
      isAuthenticated: true,
      isLoading: false,
    })),

  logout: () => {
    // V-02.08: todos los caminos de logout (TopBar, useSessionTimeout,
    // expiracion de sesion en api.ts, etc.) pasan por aqui. Sin este clear, el
    // siguiente usuario que abra sesion en el mismo navegador veria las
    // preguntas y respuestas de IA del usuario anterior en aiChatStore.
    useAiChatStore.getState().clear();
    set({ usuario: null, csrfToken: null, isAuthenticated: false, isLoading: false });
  },

  setLoading: (isLoading) => set({ isLoading }),
}));
