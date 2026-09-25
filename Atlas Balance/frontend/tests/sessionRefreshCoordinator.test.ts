import assert from 'node:assert/strict';
import test from 'node:test';
import {
  createRefreshCoordinator,
  SessionRefreshFailure,
  type RefreshSessionPayload,
} from '../src/services/sessionRefreshCoordinator.js';

type MessageListener = (event: { data: unknown }) => void;

class FakeChannelBus {
  public readonly messages: unknown[] = [];
  private readonly listeners = new Set<MessageListener>();

  public createChannel() {
    const listeners = this.listeners;
    const channel = {
      postMessage: (message: unknown) => {
        this.messages.push(message);
        listeners.forEach((listener) => listener({ data: message }));
      },
      addEventListener(_type: 'message', listener: MessageListener) {
        listeners.add(listener);
      },
      removeEventListener(_type: 'message', listener: MessageListener) {
        listeners.delete(listener);
      },
      close() {
        // El test mantiene el bus vivo para representar otra pestaña.
      },
    };

    return channel;
  }
}

class SerialLockManager {
  private tail = Promise.resolve();

  public async request<T>(_name: string, _options: { mode: 'exclusive' }, callback: () => Promise<T>): Promise<T> {
    const previous = this.tail;
    let release!: () => void;
    this.tail = new Promise<void>((resolve) => {
      release = resolve;
    });

    await previous;
    try {
      return await callback();
    } finally {
      release();
    }
  }
}

class SharedStorage {
  private readonly values = new Map<string, string>();

  public getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  public setItem(key: string, value: string): void {
    this.values.set(key, value);
  }

  public removeItem(key: string): void {
    this.values.delete(key);
  }
}

class FailingStorage extends SharedStorage {
  public override setItem(_key: string, _value: string): void {
    throw new Error('storage bloqueado');
  }
}

const payload: RefreshSessionPayload = {
  csrf_token: 'csrf-from-refresh',
  usuario: {
    id: 'user-1',
    email: 'user@test.local',
    nombre_completo: 'Test User',
    rol: 'EMPLEADO',
    activo: true,
    primer_login: false,
    puede_usar_ia: false,
    mfa_enabled: false,
    mfa_required: false,
    fecha_creacion: '2026-01-01T00:00:00Z',
    fecha_ultima_login: null,
  },
  permisos: [],
};

test('401 simultáneos entre pestañas ejecutan un único refresh y comparten la sesión', async () => {
  const bus = new FakeChannelBus();
  const lockManager = new SerialLockManager();
  const firstTab = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager,
    storage: null,
    tabId: 'tab-a',
  });
  const secondTab = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager,
    storage: null,
    tabId: 'tab-b',
  });
  let refreshCalls = 0;
  let releaseRefresh!: () => void;
  const refreshBlocked = new Promise<void>((resolve) => {
    releaseRefresh = resolve;
  });

  const firstRequest = firstTab.refresh(async () => {
    refreshCalls += 1;
    await refreshBlocked;
    return payload;
  });
  const sameTabRequest = firstTab.refresh(async () => {
    refreshCalls += 1;
    return payload;
  });
  const secondRequest = secondTab.refresh(async () => {
    refreshCalls += 1;
    throw new Error('La segunda pestaña no debe llamar al endpoint');
  });

  releaseRefresh();
  const results = await Promise.all([firstRequest, sameTabRequest, secondRequest]);

  assert.equal(refreshCalls, 1);
  assert.deepEqual(results, [payload, payload, payload]);
  assert.equal(bus.messages.some((message) => JSON.stringify(message).includes('access_token')), false);
  assert.equal(bus.messages.some((message) => JSON.stringify(message).includes('refresh_token')), false);

  firstTab.close();
  secondTab.close();
});

test('un fallo de refresh se propaga a las pestañas relacionadas y no se convierte en replay aceptado', async () => {
  const bus = new FakeChannelBus();
  const lockManager = new SerialLockManager();
  const firstTab = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager,
    storage: null,
    tabId: 'tab-a',
  });
  const secondTab = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager,
    storage: null,
    tabId: 'tab-b',
  });
  let relatedTabFailure = 0;
  secondTab.subscribeToFailure(() => {
    relatedTabFailure += 1;
  });

  const firstRequest = firstTab.refresh(async () => {
    throw new Error('refresh rechazado');
  });
  const secondRequest = secondTab.refresh(async () => {
    throw new Error('no debe reintentar el token');
  });

  await assert.rejects(firstRequest, /refresh rechazado/);
  await assert.rejects(secondRequest, SessionRefreshFailure);
  assert.equal(relatedTabFailure, 1);

  firstTab.close();
  secondTab.close();
});

test('un resultado reciente de otra sesión no se reutiliza tras cambiar de usuario', async () => {
  const bus = new FakeChannelBus();
  const coordinator = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager: null,
    storage: null,
    tabId: 'tab-a',
  });
  let refreshCalls = 0;

  await coordinator.refresh(async () => {
    refreshCalls += 1;
    return payload;
  }, 'user-1');
  await coordinator.refresh(async () => {
    refreshCalls += 1;
    return payload;
  }, 'user-2');

  assert.equal(refreshCalls, 2);
  coordinator.close();
});

test('el fallback de localStorage coordina dos pestañas sin Web Locks', async () => {
  const storage = new SharedStorage();
  const firstTab = createRefreshCoordinator({
    channelFactory: () => null,
    lockManager: null,
    storage,
    tabId: 'tab-a',
  });
  const secondTab = createRefreshCoordinator({
    channelFactory: () => null,
    lockManager: null,
    storage,
    tabId: 'tab-b',
  });
  let refreshCalls = 0;
  let releaseRefresh!: () => void;
  const refreshBlocked = new Promise<void>((resolve) => {
    releaseRefresh = resolve;
  });

  const firstRequest = firstTab.refresh(async () => {
    refreshCalls += 1;
    await refreshBlocked;
    return payload;
  });
  const secondRequest = secondTab.refresh(async () => {
    refreshCalls += 1;
    return payload;
  });

  await new Promise((resolve) => setTimeout(resolve, 100));
  assert.equal(refreshCalls, 1);
  releaseRefresh();
  assert.deepEqual(await Promise.all([firstRequest, secondRequest]), [payload, payload]);
  assert.equal(refreshCalls, 1);

  firstTab.close();
  secondTab.close();
});

test('BroadcastChannel elige una sola pestaña cuando no existe Web Locks', async () => {
  const bus = new FakeChannelBus();
  const firstTab = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager: null,
    storage: null,
    tabId: 'tab-a',
  });
  const secondTab = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager: null,
    storage: null,
    tabId: 'tab-b',
  });
  let refreshCalls = 0;
  const firstRequest = firstTab.refresh(async () => {
    refreshCalls += 1;
    return payload;
  });
  const secondRequest = secondTab.refresh(async () => {
    refreshCalls += 1;
    return payload;
  });

  assert.deepEqual(await Promise.all([firstRequest, secondRequest]), [payload, payload]);
  assert.equal(refreshCalls, 1);
  firstTab.close();
  secondTab.close();
});

test('un fallo al adquirir el lease falla cerrado y no presenta otro refresh', async () => {
  const coordinator = createRefreshCoordinator({
    channelFactory: () => null,
    lockManager: null,
    storage: new FailingStorage(),
    tabId: 'tab-a',
  });
  let refreshCalls = 0;

  await assert.rejects(
    coordinator.refresh(async () => {
      refreshCalls += 1;
      return payload;
    }),
    SessionRefreshFailure,
  );
  assert.equal(refreshCalls, 0);
  coordinator.close();
});

test('una sesión nueva no consume el refresh pendiente de la sesión anterior', async () => {
  const coordinator = createRefreshCoordinator({
    channelFactory: () => null,
    lockManager: null,
    storage: null,
    tabId: 'tab-a',
  });
  let refreshCalls = 0;
  let releaseFirst!: () => void;
  const firstBlocked = new Promise<void>((resolve) => {
    releaseFirst = resolve;
  });
  const oldPayload = { ...payload, usuario: { ...payload.usuario, id: 'user-1' } };
  const newPayload = { ...payload, usuario: { ...payload.usuario, id: 'user-2' } };

  const oldSessionRefresh = coordinator.refresh(async () => {
    refreshCalls += 1;
    await firstBlocked;
    return oldPayload;
  }, 'user-1');
  const newSessionRefresh = coordinator.refresh(async () => {
    refreshCalls += 1;
    return newPayload;
  }, 'user-2');

  let newSessionSettled = false;
  void newSessionRefresh.finally(() => { newSessionSettled = true; });
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.equal(newSessionSettled, false);
  releaseFirst();

  assert.deepEqual(await oldSessionRefresh, oldPayload);
  assert.deepEqual(await newSessionRefresh, newPayload);
  assert.equal(refreshCalls, 2);
  coordinator.close();
});

test('una pestaña inactiva recibe el payload del refresh de otra pestaña', async () => {
  const bus = new FakeChannelBus();
  const lockManager = new SerialLockManager();
  const activeTab = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager,
    storage: null,
    tabId: 'tab-a',
  });
  const idleTab = createRefreshCoordinator({
    channelFactory: () => bus.createChannel(),
    lockManager,
    storage: null,
    tabId: 'tab-b',
  });
  const idleReceived: Array<{ payload: RefreshSessionPayload; sessionKey: string }> = [];
  const activeReceived: RefreshSessionPayload[] = [];
  idleTab.subscribeToPeerSuccess((received, sessionKey) => idleReceived.push({ payload: received, sessionKey }));
  activeTab.subscribeToPeerSuccess((received) => activeReceived.push(received));

  await activeTab.refresh(async () => payload, 'user-1');

  assert.deepEqual(idleReceived, [{ payload, sessionKey: 'user-1' }]);
  assert.equal(activeReceived.length, 0);
  activeTab.close();
  idleTab.close();
});
