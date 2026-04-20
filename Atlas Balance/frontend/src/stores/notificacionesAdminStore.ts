import { create } from 'zustand';
import api from '@/services/api';

interface NotificacionesAdminResumen {
  exportaciones_pendientes: number;
  total_pendientes: number;
}

interface NotificacionesAdminState {
  exportacionesPendientes: number;
  totalPendientes: number;
  loading: boolean;
  clear: () => void;
  loadResumen: () => Promise<void>;
  markExportacionesRead: () => Promise<void>;
}

export const useNotificacionesAdminStore = create<NotificacionesAdminState>((set, get) => ({
  exportacionesPendientes: 0,
  totalPendientes: 0,
  loading: false,
  clear: () => set({ exportacionesPendientes: 0, totalPendientes: 0, loading: false }),
  loadResumen: async () => {
    set({ loading: true });
    try {
      const { data } = await api.get<NotificacionesAdminResumen>('/notificaciones-admin/resumen');
      set({
        exportacionesPendientes: data.exportaciones_pendientes ?? 0,
        totalPendientes: data.total_pendientes ?? 0,
        loading: false,
      });
    } catch {
      set({ exportacionesPendientes: 0, totalPendientes: 0, loading: false });
    }
  },
  markExportacionesRead: async () => {
    try {
      await api.post('/notificaciones-admin/marcar-leidas', { tipo: 'EXPORTACION' });
      set((state) => ({
        exportacionesPendientes: 0,
        totalPendientes: Math.max(0, state.totalPendientes - state.exportacionesPendientes),
      }));
      await get().loadResumen();
    } catch {
      // Keep page functional even if notification sync fails.
    }
  },
}));
