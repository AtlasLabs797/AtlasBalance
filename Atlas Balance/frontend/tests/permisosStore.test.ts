import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import test from 'node:test';
import { runInNewContext } from 'node:vm';
import ts from 'typescript';

// Payload estructural: types/index.ts reexporta stores con aliases de Vite.
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
  clear: () => void;
  canViewCuenta: (cuenta: string, titular?: string | null, pais?: string | null) => boolean;
  canEditCuenta: (cuenta: string, titular?: string | null, pais?: string | null) => boolean;
  canImportInCuenta: (cuenta: string, titular?: string | null, pais?: string | null) => boolean;
  getColumnasVisibles: (cuenta: string, titular?: string | null, pais?: string | null) => string[] | null;
  getColumnasEditables: (cuenta: string, titular?: string | null, pais?: string | null) => string[] | null;
}

// Ejecuta el store real con Zustand real; solo sustituye auth para aislar el rol.
function createStore(rol: RolUsuario = 'GERENTE') {
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

for (const rol of ['GERENTE', 'EMPLEADO'] as const) {
  test(`${rol}: lectura global serializada permite todas las cuentas sin conceder escritura`, () => {
    const store = createStore(rol);
    const wire = permisoApi();
    assert.equal(Object.prototype.hasOwnProperty.call(wire, 'cuenta_id'), false);
    store.getState().setPermisos([wire]);
    const state = store.getState();
    assert.equal(state.canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
    assert.equal(state.canViewCuenta('cuenta-b', 'titular-b', 'pais-b'), true);
    assert.equal(state.canViewCuenta('sin-pais', 'titular-a', null), true);
    assert.equal(state.canEditCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
    assert.equal(state.canImportInCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
  });

  test(`${rol}: pais concreto no permite cuentas de otro pais`, () => {
    const store = createStore(rol);
    store.getState().setPermisos([permisoApi({ pais_id: 'pais-a' })]);
    const state = store.getState();
    assert.equal(state.canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
    assert.equal(state.canViewCuenta('cuenta-b', 'titular-b', 'pais-a'), true);
    assert.equal(state.canViewCuenta('cuenta-c', 'titular-a', 'pais-b'), false);
    assert.equal(state.canViewCuenta('sin-pais', 'titular-a', null), false);
  });

  test(`${rol}: titular concreto abarca sus cuentas en varios paises`, () => {
    const store = createStore(rol);
    store.getState().setPermisos([permisoApi({ titular_id: 'titular-a' })]);
    const state = store.getState();
    assert.equal(state.canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
    assert.equal(state.canViewCuenta('cuenta-b', 'titular-a', 'pais-b'), true);
    assert.equal(state.canViewCuenta('cuenta-c', 'titular-b', 'pais-a'), false);
  });

  test(`${rol}: pais y titular son una interseccion con cuenta en blanco`, () => {
    const store = createStore(rol);
    store.getState().setPermisos([permisoApi({ pais_id: 'pais-a', titular_id: 'titular-a' })]);
    const state = store.getState();
    assert.equal(state.canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
    assert.equal(state.canViewCuenta('cuenta-b', 'titular-a', 'pais-b'), false);
    assert.equal(state.canViewCuenta('cuenta-c', 'titular-b', 'pais-a'), false);
  });

  test(`${rol}: cuenta concreta conserva todas las restricciones`, () => {
    const store = createStore(rol);
    store.getState().setPermisos([permisoApi({ cuenta_id: 'cuenta-a', titular_id: 'titular-a', pais_id: 'pais-a' })]);
    const state = store.getState();
    assert.equal(state.canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
    assert.equal(state.canViewCuenta('cuenta-b', 'titular-a', 'pais-a'), false);
    assert.equal(state.canViewCuenta('cuenta-a', 'titular-b', 'pais-a'), false);
    assert.equal(state.canViewCuenta('cuenta-a', 'titular-a', 'pais-b'), false);
  });

  test(`${rol}: dashboard o escritura sin Ver cuentas no concede lectura`, () => {
    const store = createStore(rol);
    store.getState().setPermisos([permisoApi({ puede_ver_cuentas: false, puede_editar_lineas: true })]);
    assert.equal(store.getState().canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
    store.getState().setPermisos([permisoApi({ puede_ver_cuentas: false })]);
    assert.equal(store.getState().canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
    store.getState().setPermisos([]);
    assert.equal(store.getState().canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
  });
}

test('reemplazar permisos de sesion elimina el alcance global anterior', () => {
  const store = createStore();
  store.getState().setPermisos([permisoApi()]);
  assert.equal(store.getState().canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
  store.getState().setPermisos([permisoApi({ pais_id: 'pais-b' })]);
  assert.equal(store.getState().canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), false);
  store.getState().clear();
  assert.equal(store.getState().canViewCuenta('cuenta-b', 'titular-b', 'pais-b'), false);
});

test('filas complementarias suman lectura dentro de sus propios alcances', () => {
  const store = createStore();
  store.getState().setPermisos([permisoApi({ pais_id: 'pais-a' }), permisoApi({ titular_id: 'titular-b' })]);
  const state = store.getState();
  assert.equal(state.canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
  assert.equal(state.canViewCuenta('cuenta-b', 'titular-b', 'pais-b'), true);
  assert.equal(state.canViewCuenta('cuenta-c', 'titular-c', 'pais-c'), false);
});

test('columnas omitidas significan sin restriccion y las listas explicitas se conservan', () => {
  const store = createStore();
  store.getState().setPermisos([permisoApi({ pais_id: 'pais-a', titular_id: 'titular-a', cuenta_id: 'cuenta-a' })]);
  assert.equal(store.getState().getColumnasVisibles('cuenta-a', 'titular-a', 'pais-a'), null);
  assert.equal(store.getState().getColumnasEditables('cuenta-a', 'titular-a', 'pais-a'), null);
  store.getState().setPermisos([permisoApi({ columnas_visibles: ['fecha'], columnas_editables: [] })]);
  assert.deepEqual(Array.from(store.getState().getColumnasVisibles('cuenta-a') ?? []), ['fecha']);
  assert.deepEqual(Array.from(store.getState().getColumnasEditables('cuenta-a') ?? []), []);
});

test('ADMIN conserva su acceso independientemente de la matriz', () => {
  assert.equal(createStore('ADMIN').getState().canViewCuenta('cuenta-a', 'titular-a', 'pais-a'), true);
});
