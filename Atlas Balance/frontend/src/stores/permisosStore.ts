import { create } from 'zustand';
import type { PermisoUsuario } from '@/types';
import { useAuthStore } from '@/stores/authStore';

interface PermisosState {
  permisos: PermisoUsuario[];
  setPermisos: (permisos: PermisoUsuario[]) => void;
  clear: () => void;
  canViewCuenta: (cuentaId: string, titularId?: string | null) => boolean;
  canAddInCuenta: (cuentaId: string, titularId?: string | null) => boolean;
  canEditCuenta: (cuentaId: string, titularId?: string | null) => boolean;
  canDeleteInCuenta: (cuentaId: string, titularId?: string | null) => boolean;
  canImportInCuenta: (cuentaId: string, titularId?: string | null) => boolean;
  canViewDashboard: () => boolean;
  getColumnasVisibles: (cuentaId: string, titularId?: string | null) => string[] | null;
  getColumnasEditables: (cuentaId: string, titularId?: string | null) => string[] | null;
}

const isAdmin = () => useAuthStore.getState().usuario?.rol === 'ADMIN';

const grantsGlobalDataAccess = (permiso: PermisoUsuario) =>
  permiso.puede_agregar_lineas ||
  permiso.puede_editar_lineas ||
  permiso.puede_eliminar_lineas ||
  permiso.puede_importar;

const getMatchingPermisos = (
  permisos: PermisoUsuario[],
  cuentaId: string,
  titularId?: string | null
) =>
  permisos.filter(
    (p) =>
      (p.cuenta_id === null || p.cuenta_id === cuentaId) &&
      (p.titular_id === null || p.titular_id === titularId)
  );

const getCuentaPermisos = (
  permisos: PermisoUsuario[],
  cuentaId: string,
  titularId?: string | null
) =>
  getMatchingPermisos(permisos, cuentaId, titularId).filter(
    (p) => p.cuenta_id !== null || p.titular_id !== null || grantsGlobalDataAccess(p)
  );

const mergeColumnRules = (
  permisos: PermisoUsuario[],
  key: 'columnas_visibles' | 'columnas_editables'
): string[] | null => {
  const values = permisos.map((p) => p[key]);
  if (values.some((v) => v === null)) {
    return null;
  }

  const merged = new Set<string>();
  values.forEach((v) => v?.forEach((col) => merged.add(col)));
  return [...merged];
};

export const usePermisosStore = create<PermisosState>((set, get) => ({
  permisos: [],

  setPermisos: (permisos) => set({ permisos }),
  clear: () => set({ permisos: [] }),

  canViewCuenta: (cuentaId, titularId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId).length > 0;
  },

  canAddInCuenta: (cuentaId, titularId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId).some((p) => p.puede_agregar_lineas);
  },

  canEditCuenta: (cuentaId, titularId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId).some((p) => p.puede_editar_lineas);
  },

  canDeleteInCuenta: (cuentaId, titularId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId).some((p) => p.puede_eliminar_lineas);
  },

  canImportInCuenta: (cuentaId, titularId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId).some((p) => p.puede_importar);
  },

  canViewDashboard: () => {
    if (isAdmin()) return true;
    return get().permisos.some((p) => p.puede_ver_dashboard);
  },

  getColumnasVisibles: (cuentaId, titularId) => {
    if (isAdmin()) return null;
    return mergeColumnRules(getCuentaPermisos(get().permisos, cuentaId, titularId), 'columnas_visibles');
  },

  getColumnasEditables: (cuentaId, titularId) => {
    if (isAdmin()) return null;
    return mergeColumnRules(getCuentaPermisos(get().permisos, cuentaId, titularId), 'columnas_editables');
  },
}));
