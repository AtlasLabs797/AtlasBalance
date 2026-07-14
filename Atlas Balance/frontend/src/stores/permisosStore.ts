import { create } from 'zustand';
import type { PermisoUsuario } from '@/types';
import { useAuthStore } from '@/stores/authStore';

interface PermisosState {
  permisos: PermisoUsuario[];
  setPermisos: (permisos: PermisoUsuario[]) => void;
  clear: () => void;
  canViewCuenta: (cuentaId: string, titularId?: string | null, paisId?: string | null) => boolean;
  canAddInCuenta: (cuentaId: string, titularId?: string | null, paisId?: string | null) => boolean;
  canEditCuenta: (cuentaId: string, titularId?: string | null, paisId?: string | null) => boolean;
  canDeleteInCuenta: (cuentaId: string, titularId?: string | null, paisId?: string | null) => boolean;
  canImportInCuenta: (cuentaId: string, titularId?: string | null, paisId?: string | null) => boolean;
  canConciliarCuenta: (cuentaId: string, titularId?: string | null, paisId?: string | null) => boolean;
  canViewDashboard: () => boolean;
  getColumnasVisibles: (cuentaId: string, titularId?: string | null, paisId?: string | null) => string[] | null;
  getColumnasEditables: (cuentaId: string, titularId?: string | null, paisId?: string | null) => string[] | null;
}

const isAdmin = () => useAuthStore.getState().usuario?.rol === 'ADMIN';

const grantsAccountAccess = (permiso: PermisoUsuario) =>
  permiso.puede_ver_cuentas ||
  permiso.puede_agregar_lineas ||
  permiso.puede_editar_lineas ||
  permiso.puede_eliminar_lineas ||
  permiso.puede_importar ||
  permiso.puede_revisar_lineas ||
  permiso.puede_aprobar_importaciones ||
  permiso.puede_conciliar ||
  permiso.puede_cerrar_conciliacion;

const getMatchingPermisos = (
  permisos: PermisoUsuario[],
  cuentaId: string,
  titularId?: string | null,
  paisId?: string | null
) =>
  permisos.filter(
    (p) =>
      (p.pais_id === null || p.pais_id === paisId) &&
      (p.cuenta_id === null || p.cuenta_id === cuentaId) &&
      (p.titular_id === null || p.titular_id === titularId)
  );

const getCuentaPermisos = (
  permisos: PermisoUsuario[],
  cuentaId: string,
  titularId?: string | null,
  paisId?: string | null
) =>
  getMatchingPermisos(permisos, cuentaId, titularId, paisId).filter(
    (p) => grantsAccountAccess(p)
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

  canViewCuenta: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId, paisId).some((p) => p.puede_ver_cuentas);
  },

  canAddInCuenta: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId, paisId).some((p) => p.puede_agregar_lineas);
  },

  canEditCuenta: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId, paisId).some((p) => p.puede_editar_lineas);
  },

  canDeleteInCuenta: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId, paisId).some((p) => p.puede_eliminar_lineas);
  },

  canImportInCuenta: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId, paisId).some((p) => p.puede_importar);
  },

  canConciliarCuenta: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return true;
    return getCuentaPermisos(get().permisos, cuentaId, titularId, paisId).some((p) => p.puede_conciliar);
  },

  canViewDashboard: () => {
    if (isAdmin()) return true;
    const role = useAuthStore.getState().usuario?.rol;
    return get().permisos.some((p) =>
      role === 'GERENTE'
        ? grantsAccountAccess(p)
        : p.puede_ver_dashboard && grantsAccountAccess(p)
    );
  },

  getColumnasVisibles: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return null;
    return mergeColumnRules(getCuentaPermisos(get().permisos, cuentaId, titularId, paisId), 'columnas_visibles');
  },

  getColumnasEditables: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return null;
    return mergeColumnRules(getCuentaPermisos(get().permisos, cuentaId, titularId, paisId), 'columnas_editables');
  },
}));
