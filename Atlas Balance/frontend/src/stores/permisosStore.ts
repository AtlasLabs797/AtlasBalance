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
  canCerrarConciliacionCuenta: (cuentaId: string, titularId?: string | null, paisId?: string | null) => boolean;
  canRevisarCuenta: (cuentaId: string, titularId?: string | null, paisId?: string | null) => boolean;
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

// Alcance parcial: la dimension de cuenta siempre se exige; las de titular y
// pais solo cuando la pantalla las conoce. `undefined` = dato desconocido (no
// "sin valor"): el backend evalua el alcance contra la cuenta real, asi que
// cuando el frontend no tiene ese dato la fila se toma como aplicable y es
// el backend quien deniega con 403 si el alcance no cuadra. Con el dato
// conocido la exigencia es exacta, igual que en el backend.
const getCuentaPermisosConAlcanceParcial = (
  permisos: PermisoUsuario[],
  cuentaId: string,
  titularId?: string | null,
  paisId?: string | null
) =>
  permisos.filter(
    (p) =>
      (paisId === undefined || p.pais_id === null || p.pais_id === paisId) &&
      (p.cuenta_id === null || p.cuenta_id === cuentaId) &&
      (titularId === undefined || p.titular_id === null || p.titular_id === titularId) &&
      grantsAccountAccess(p)
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

  setPermisos: (permisos) => set({
    // La API omite los null: restaurar los comodines antes de evaluar el alcance.
    permisos: permisos.map((permiso) => ({
      ...permiso,
      pais_id: permiso.pais_id ?? null,
      titular_id: permiso.titular_id ?? null,
      cuenta_id: permiso.cuenta_id ?? null,
      columnas_visibles: permiso.columnas_visibles ?? null,
      columnas_editables: permiso.columnas_editables ?? null,
    })),
  }),
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
    // ConciliacionService exige `puede_conciliar` (estricto) para conciliar.
    return getCuentaPermisosConAlcanceParcial(get().permisos, cuentaId, titularId, paisId)
      .some((p) => p.puede_conciliar);
  },

  canCerrarConciliacionCuenta: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return true;
    // ConciliacionService exige `puede_cerrar_conciliacion` (estricto) para
    // resolver/cerrar: `puede_conciliar` no basta.
    return getCuentaPermisosConAlcanceParcial(get().permisos, cuentaId, titularId, paisId)
      .some((p) => p.puede_cerrar_conciliacion);
  },

  canRevisarCuenta: (cuentaId, titularId, paisId) => {
    if (isAdmin()) return true;
    // UserAccessService.CanReviewCuentaAsync: puede_revisar_lineas O
    // puede_editar_lineas.
    return getCuentaPermisosConAlcanceParcial(get().permisos, cuentaId, titularId, paisId)
      .some((p) => p.puede_revisar_lineas || p.puede_editar_lineas);
  },

  canViewDashboard: () => {
    if (isAdmin()) return true;
    // Espejo de DashboardService: hacen falta `puede_ver_dashboard` Y
    // `puede_ver_cuentas` en la misma fila, sin excepcion por rol (tampoco
    // para GERENTE). Los permisos de escritura no dan acceso a los saldos.
    return get().permisos.some((p) => p.puede_ver_dashboard && p.puede_ver_cuentas);
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
