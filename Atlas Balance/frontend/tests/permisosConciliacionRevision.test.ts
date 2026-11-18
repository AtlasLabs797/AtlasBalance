import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import test from 'node:test';
import { runInNewContext } from 'node:vm';
import ts from 'typescript';

// LOTE 6 (D-13 y espejo de S-1): el frontend solo debe ocultar las acciones
// que el backend ya deniega. Estas pruebas fijan los predicados que el store
// debe replicar exactamente:
//  - conciliacion  -> puede_conciliar            (ConciliacionService, cerrar: false)
//  - cierre        -> puede_cerrar_conciliacion  (ConciliacionService, cerrar: true)
//  - revision      -> puede_revisar_lineas || puede_editar_lineas (UserAccessService.CanReviewCuentaAsync)
//  - dashboard     -> puede_ver_dashboard && puede_ver_cuentas (DashboardService)
type PermisoPayload = Record<string, string | boolean | string[] | null>;
type RolUsuario = 'ADMIN' | 'GERENTE' | 'EMPLEADO';

const require = createRequire(import.meta.url);
const source = readFileSync(new URL('../../src/stores/permisosStore.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
}).outputText;

interface PermisosState {
  permisos: PermisoPayload[];
  setPermisos: (permisos: PermisoPayload[]) => void;
  canConciliarCuenta: (cuenta: string, titular?: string | null, pais?: string | null) => boolean;
  canCerrarConciliacionCuenta: (cuenta: string, titular?: string | null, pais?: string | null) => boolean;
  canRevisarCuenta: (cuenta: string, titular?: string | null, pais?: string | null) => boolean;
  canViewDashboard: () => boolean;
}

// Ejecuta el store real con Zustand real; solo sustituye auth para aislar el rol.
function createStore(rol: RolUsuario = 'EMPLEADO') {
  const exports = {} as { usePermisosStore: { getState: () => PermisosState } };
  runInNewContext(compiled, {
    exports,
    require: (name: string) => name === '@/stores/authStore'
      ? { useAuthStore: { getState: () => ({ usuario: { rol } }) } }
      : require(name),
  });
  return exports.usePermisosStore;
}

function permisoApi(overrides: PermisoPayload = {}): PermisoPayload {
  const permiso: PermisoPayload = {
    id: 'permiso-1', usuario_id: 'usuario-1',
    pais_id: null, titular_id: null, cuenta_id: null,
    puede_ver_cuentas: true, puede_ver_dashboard: true,
    puede_agregar_lineas: false, puede_editar_lineas: false,
    puede_eliminar_lineas: false, puede_importar: false,
    puede_revisar_lineas: false, puede_aprobar_importaciones: false,
    puede_conciliar: false, puede_cerrar_conciliacion: false,
    columnas_visibles: null, columnas_editables: null,
    ...overrides,
  };
  // Equivale a WhenWritingNull de la API: los null no llegan al navegador.
  return JSON.parse(JSON.stringify(permiso, (_key, value: unknown) => value === null ? undefined : value));
}

test('canViewDashboard: ADMIN entra sin depender de la matriz de permisos', () => {
  const store = createStore('ADMIN');
  store.getState().setPermisos([]);
  assert.equal(store.getState().canViewDashboard(), true);
});

test('canViewDashboard: sin puede_ver_cuentas no hay saldos aunque se pueda escribir', () => {
  const store = createStore('EMPLEADO');
  store.getState().setPermisos([
    permisoApi({ puede_ver_cuentas: false, puede_ver_dashboard: true, puede_importar: true }),
  ]);
  assert.equal(store.getState().canViewDashboard(), false);
});

test('canViewDashboard: puede_ver_cuentas sin puede_ver_dashboard no abre el dashboard', () => {
  const store = createStore('EMPLEADO');
  store.getState().setPermisos([permisoApi({ puede_ver_dashboard: false })]);
  assert.equal(store.getState().canViewDashboard(), false);
});

test('canViewDashboard: el rol GERENTE tambien necesita puede_ver_dashboard', () => {
  const store = createStore('GERENTE');
  store.getState().setPermisos([permisoApi({ puede_ver_dashboard: false })]);
  assert.equal(store.getState().canViewDashboard(), false);
  store.getState().setPermisos([permisoApi()]);
  assert.equal(store.getState().canViewDashboard(), true);
});

test('canViewDashboard: solo puede_importar no abre el dashboard', () => {
  for (const rol of ['GERENTE', 'EMPLEADO'] as const) {
    const store = createStore(rol);
    store.getState().setPermisos([
      permisoApi({ puede_ver_cuentas: false, puede_ver_dashboard: false, puede_importar: true }),
    ]);
    assert.equal(store.getState().canViewDashboard(), false);
  }
});

test('canConciliarCuenta: exige puede_conciliar y respeta el alcance de cuenta', () => {
  const store = createStore('EMPLEADO');
  store.getState().setPermisos([permisoApi({ puede_conciliar: true })]);
  assert.equal(store.getState().canConciliarCuenta('cuenta-a', 'titular-a', 'pais-a'), true);

  store.getState().setPermisos([permisoApi({ puede_editar_lineas: true })]);
  assert.equal(store.getState().canConciliarCuenta('cuenta-a', 'titular-a', 'pais-a'), false);

  store.getState().setPermisos([permisoApi({ puede_conciliar: true, cuenta_id: 'cuenta-b' })]);
  assert.equal(store.getState().canConciliarCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
});

test('canConciliarCuenta: ADMIN concilia cualquier cuenta', () => {
  const store = createStore('ADMIN');
  store.getState().setPermisos([]);
  assert.equal(store.getState().canConciliarCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
});

test('canCerrarConciliacionCuenta: exige puede_cerrar_conciliacion, no basta puede_conciliar', () => {
  const store = createStore('EMPLEADO');
  store.getState().setPermisos([permisoApi({ puede_cerrar_conciliacion: true })]);
  assert.equal(store.getState().canCerrarConciliacionCuenta('cuenta-a', 'titular-a', 'pais-a'), true);

  store.getState().setPermisos([permisoApi({ puede_conciliar: true })]);
  assert.equal(store.getState().canCerrarConciliacionCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
});

test('canRevisarCuenta: el backend admite puede_revisar_lineas o puede_editar_lineas', () => {
  const store = createStore('EMPLEADO');
  store.getState().setPermisos([permisoApi({ puede_revisar_lineas: true })]);
  assert.equal(store.getState().canRevisarCuenta('cuenta-a', 'titular-a', 'pais-a'), true);

  store.getState().setPermisos([permisoApi({ puede_editar_lineas: true })]);
  assert.equal(store.getState().canRevisarCuenta('cuenta-a', 'titular-a', 'pais-a'), true);

  store.getState().setPermisos([permisoApi()]);
  assert.equal(store.getState().canRevisarCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
});

test('alcance: con titular desconocido la fila con titular asignado sigue aplicando', () => {
  const store = createStore('EMPLEADO');
  store.getState().setPermisos([permisoApi({ puede_conciliar: true, titular_id: 'titular-a' })]);

  // La pantalla de conciliacion no conoce el titular de la cuenta: no puede
  // exigir esa coincidencia (el backend sigue denegando con 403 si no cuadra).
  assert.equal(store.getState().canConciliarCuenta('cuenta-a', undefined, 'pais-a'), true);
  // Con titular conocido, la exigencia es exacta.
  assert.equal(store.getState().canConciliarCuenta('cuenta-a', 'titular-b', 'pais-a'), false);
});
