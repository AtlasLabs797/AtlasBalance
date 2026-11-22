import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import test from 'node:test';
import { runInNewContext } from 'node:vm';
import ts from 'typescript';

// F-5: el banner de alertas reaparece al cambiar de pais. El hook reiniciaba
// el descarte mientras cargaba y el store tenia dos escritores sobre el mismo
// estado. Este repo no tiene DOM, asi que aqui se ejecutan el hook y el store
// REALES: se compila el fuente con la API del compilador de TypeScript y se
// evalua en un contexto donde React, React Query y axios estan sustituidos.

const require = createRequire(import.meta.url);
const SRC_URL = new URL('../../src/', import.meta.url);

interface AlertaFalsa {
  alerta_id: string;
  cuenta_id: string;
  titular_id: string;
  cuenta_nombre: string;
  titular_nombre: string;
  saldo_actual: number;
  saldo_minimo: number;
  divisa: string;
}

interface EstadoQuery {
  data: AlertaFalsa[] | undefined;
  error: unknown;
  isLoading: boolean;
}

interface AlertasState {
  alertasActivas: AlertaFalsa[];
  bannerDismissed: boolean;
  loading: boolean;
  lastError: string | null;
  dismissBanner: () => void;
  clear: () => void;
}

interface AlertasStore {
  getState: () => AlertasState;
}

class MemoryStorage {
  private readonly values = new Map<string, string>();

  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }

  removeItem(key: string): void {
    this.values.delete(key);
  }
}

type Modulo = Record<string, unknown>;

// Cargador minimo de modulos CommonJS sobre el fuente real: resuelve los
// alias '@/' contra src/ y deja pasar el resto al require de Node.
function crearCargador(stubs: Record<string, unknown>, storage: MemoryStorage) {
  const cache = new Map<string, Modulo>();

  function resolver(nombre: string): unknown {
    if (Object.prototype.hasOwnProperty.call(stubs, nombre)) {
      return stubs[nombre];
    }
    if (nombre.startsWith('@/')) {
      return cargar(`${nombre.slice(2)}.ts`);
    }
    return require(nombre);
  }

  function cargar(rel: string): Modulo {
    const cacheado = cache.get(rel);
    if (cacheado) {
      return cacheado;
    }
    const modulo: Modulo = {};
    cache.set(rel, modulo);
    const fuente = readFileSync(new URL(rel, SRC_URL), 'utf8');
    const compilado = ts.transpileModule(fuente, {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
    }).outputText;
    runInNewContext(compilado, {
      exports: modulo,
      module: { exports: modulo },
      require: resolver,
      window: { sessionStorage: storage, localStorage: storage },
      console,
    });
    return modulo;
  }

  return { cargar };
}

// Monta el hook y el store reales sobre un estado de query controlado a mano.
function montar(estado: EstadoQuery, storage: MemoryStorage) {
  const stubs: Record<string, unknown> = {
    react: {
      useEffect: (efecto: () => void) => {
        efecto();
      },
    },
    '@tanstack/react-query': { useQuery: () => estado },
    '@/services/api': { default: { get: async () => ({ data: [] }) } },
    '@/queries/queryKeys': { queryKeys: { alertas: { activas: () => 'alertas-activas' } } },
    '@/services/queryClient': { QUERY_STALE_TIMES: { ALERTAS_MS: 10_000 } },
    '@/stores/authStore': {
      useAuthStore: (selector: (estado: { usuario: { id: string } }) => unknown) =>
        selector({ usuario: { id: 'usuario-1' } }),
    },
    '@/stores/paisScopeStore': {
      usePaisScopeStore: (selector: (estado: { selectedPaisId: string }) => unknown) =>
        selector({ selectedPaisId: 'pais-1' }),
    },
  };
  const cargador = crearCargador(stubs, storage);
  const store = cargador.cargar('stores/alertasStore.ts').useAlertasStore as AlertasStore;
  const hook = cargador.cargar('hooks/queries/useAlertasActivasQuery.ts')
    .useAlertasActivasQuery as () => unknown;
  return { store, render: () => hook() };
}

function alert(id: string, saldo: number): AlertaFalsa {
  return {
    alerta_id: id,
    cuenta_id: `cuenta-${id}`,
    titular_id: `titular-${id}`,
    cuenta_nombre: `Cuenta ${id}`,
    titular_nombre: `Titular ${id}`,
    saldo_actual: saldo,
    saldo_minimo: 0,
    divisa: 'EUR',
  };
}

const ALERTA_A = alert('a', -10);
const ALERTA_B = alert('b', -20);

test('el descarte sobrevive a una recarga: mientras carga y al volver el mismo contenido sigue oculto', () => {
  const storage = new MemoryStorage();
  const query: EstadoQuery = { data: undefined, error: null, isLoading: true };
  const { store, render } = montar(query, storage);

  query.data = [ALERTA_A];
  query.isLoading = false;
  render();
  assert.equal(store.getState().alertasActivas.length, 1);
  assert.equal(store.getState().bannerDismissed, false);

  store.getState().dismissBanner();
  assert.equal(store.getState().bannerDismissed, true);

  // Recarga o cambio de pais: la query pide datos y aun no hay respuesta.
  query.data = undefined;
  query.isLoading = true;
  render();
  assert.equal(
    store.getState().bannerDismissed,
    true,
    'el descarte no puede reiniciarse mientras la query carga',
  );

  // Llega el mismo contenido: el descarte del usuario sigue vigente.
  query.data = [ALERTA_A];
  query.isLoading = false;
  render();
  assert.equal(store.getState().bannerDismissed, true);
});

test('un contenido distinto hace reaparecer el banner', () => {
  const storage = new MemoryStorage();
  const query: EstadoQuery = { data: undefined, error: null, isLoading: true };
  const { store, render } = montar(query, storage);

  query.data = [ALERTA_A];
  query.isLoading = false;
  render();
  store.getState().dismissBanner();
  assert.equal(store.getState().bannerDismissed, true);

  query.data = [ALERTA_A, ALERTA_B];
  render();
  assert.equal(
    store.getState().bannerDismissed,
    false,
    'una alerta nueva es contenido distinto y el banner debe volver a salir',
  );
});

test('cambiar de pais y volver no borra el descarte del contenido anterior', () => {
  const storage = new MemoryStorage();
  const query: EstadoQuery = { data: undefined, error: null, isLoading: true };
  const { store, render } = montar(query, storage);

  query.data = [ALERTA_A];
  query.isLoading = false;
  render();
  store.getState().dismissBanner();

  // Pais B: contenido distinto, el banner sale.
  query.data = undefined;
  query.isLoading = true;
  render();
  query.data = [ALERTA_B];
  query.isLoading = false;
  render();
  assert.equal(store.getState().bannerDismissed, false);

  // Vuelta al pais A: el contenido es el que el usuario ya habia descartado.
  query.data = undefined;
  query.isLoading = true;
  render();
  query.data = [ALERTA_A];
  query.isLoading = false;
  render();
  assert.equal(
    store.getState().bannerDismissed,
    true,
    'volver al pais anterior no puede hacer reaparecer un banner ya descartado',
  );
});

test('el descarte sobrevive a una recarga de la pagina', () => {
  const storage = new MemoryStorage();
  const query: EstadoQuery = { data: undefined, error: null, isLoading: true };
  const primera = montar(query, storage);

  query.data = [ALERTA_A];
  query.isLoading = false;
  primera.render();
  primera.store.getState().dismissBanner();
  assert.equal(primera.store.getState().bannerDismissed, true);

  // Recarga de la pagina: store nuevo, mismo sessionStorage.
  const segunda = montar(query, storage);
  segunda.render();
  assert.equal(
    segunda.store.getState().bannerDismissed,
    true,
    'el descarte debe persistir en la sesion tras recargar la pagina',
  );
});

test('cerrar la sesion limpia el descarte del banner', () => {
  const storage = new MemoryStorage();
  const query: EstadoQuery = { data: undefined, error: null, isLoading: true };
  const { store, render } = montar(query, storage);

  query.data = [ALERTA_A];
  query.isLoading = false;
  render();
  store.getState().dismissBanner();

  store.getState().clear();
  assert.equal(store.getState().bannerDismissed, false);
});

function ficherosSrc(rel = ''): string[] {
  const salida: string[] = [];
  for (const entrada of readdirSync(new URL(rel, SRC_URL), { withFileTypes: true })) {
    const ruta = `${rel}${entrada.name}`;
    if (entrada.isDirectory()) {
      salida.push(...ficherosSrc(`${ruta}/`));
    } else if (entrada.name.endsWith('.ts') || entrada.name.endsWith('.tsx')) {
      salida.push(ruta);
    }
  }
  return salida;
}

test('el store es el unico escritor del estado de descarte del banner', () => {
  const fuenteHook = readFileSync(new URL('hooks/queries/useAlertasActivasQuery.ts', SRC_URL), 'utf8');
  assert.equal(
    fuenteHook.includes('resetBanner'),
    false,
    'el hook no debe reiniciar el descarte del banner',
  );
  assert.equal(
    fuenteHook.includes('bannerDismissed'),
    false,
    'el hook no debe escribir el estado de descarte del banner',
  );

  const menciones = ficherosSrc()
    .filter((ruta) => readFileSync(new URL(ruta, SRC_URL), 'utf8').includes('bannerDismissed'))
    .sort();
  assert.deepEqual(
    menciones,
    ['components/layout/AlertBanner.tsx', 'components/layout/AlertBell.tsx', 'stores/alertasStore.ts'],
    'solo el store escribe el descarte; el banner y la campana solo lo leen',
  );
});
