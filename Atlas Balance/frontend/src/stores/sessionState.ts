import { clearQueryClient } from '@/services/queryClient';
import { useAiChatStore } from '@/stores/aiChatStore';
import { useAlertasStore } from '@/stores/alertasStore';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import { useNotificacionesAdminStore } from '@/stores/notificacionesAdminStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { useUiStore } from '@/stores/uiStore';
import { useUpdateStore } from '@/stores/updateStore';
import { advanceSessionGeneration, clearUserScopedStorage } from '@/utils/sessionScope';

export function clearUserScopedState(): void {
  // Invalidar primero la generacion impide que respuestas de la sesion anterior
  // vuelvan a poblar stores despues del logout o del cambio de usuario.
  advanceSessionGeneration();
  clearUserScopedStorage();
  usePermisosStore.getState().clear();
  useAlertasStore.getState().clear();
  usePaisScopeStore.getState().clear();
  useAiChatStore.getState().clear();
  useIaAvailabilityStore.getState().clear();
  useNotificacionesAdminStore.getState().clear();
  useUpdateStore.getState().clear();
  useUiStore.getState().clearSessionTransientState();
  clearQueryClient();
}
