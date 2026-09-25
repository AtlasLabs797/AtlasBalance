const REFRESH_LOCK_NAME = 'atlas-balance-auth-refresh';
const REFRESH_OUTCOME_STORAGE_KEY = 'atlas-balance-auth-refresh-outcome-v1';
const REFRESH_LEASE_STORAGE_KEY = 'atlas-balance-auth-refresh-lease-v1';
const REFRESH_OUTCOME_MAX_AGE_MS = 5_000;
const REFRESH_LEASE_MS = 10_000;
const REFRESH_LEASE_WAIT_MS = 100;
const REFRESH_LEASE_MAX_WAIT_MS = 15_000;
const REFRESH_LEASE_CLAIM_JITTER_MS = 50;

export interface RefreshSessionPayload {
  csrf_token?: string | null;
  // El coordinador solo transporta el cuerpo no sensible de la respuesta de
  // refresh. Mantenerlo estructural evita importar types/index.ts (que reexporta
  // stores y rompe la compilacion aislada de los tests NodeNext).
  usuario?: Record<string, unknown> | null;
  permisos?: Array<Record<string, unknown>> | null;
}

export class SessionRefreshFailure extends Error {
  public readonly isSessionRefreshFailure = true;

  public constructor() {
    super('La renovación de sesión falló en otra pestaña.');
    this.name = 'SessionRefreshFailure';
  }
}

interface RefreshChannel {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
  close(): void;
}

interface RefreshLockManager {
  request<T>(name: string, options: { mode: 'exclusive' }, callback: () => Promise<T>): Promise<T>;
}

interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

interface RefreshCoordinatorOptions {
  channelFactory?: () => RefreshChannel | null;
  lockManager?: RefreshLockManager | null;
  storage?: StorageLike | null;
  now?: () => number;
  tabId?: string;
}

type RefreshOutcome =
  | {
      type: 'completed';
      id: string;
      completedAt: number;
      sessionKey: string;
      payload: RefreshSessionPayload;
    }
  | {
      type: 'failed';
      id: string;
      completedAt: number;
      sessionKey: string;
    };

interface RefreshLease {
  owner: string;
  expiresAt: number;
}

interface RefreshRequest {
  type: 'request';
  id: string;
  tabId: string;
  sessionKey: string;
  requestedAt: number;
}

const createId = (): string => {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
};

const getDefaultChannel = (): RefreshChannel | null => {
  if (typeof BroadcastChannel === 'undefined') {
    return null;
  }

  return new BroadcastChannel('atlas-balance-auth-refresh-v1') as unknown as RefreshChannel;
};

const getDefaultLockManager = (): RefreshLockManager | null => {
  if (typeof navigator === 'undefined' || !navigator.locks) {
    return null;
  }

  return navigator.locks as unknown as RefreshLockManager;
};

const getDefaultStorage = (): StorageLike | null => {
  if (typeof localStorage === 'undefined') {
    return null;
  }

  try {
    const probeKey = `${REFRESH_OUTCOME_STORAGE_KEY}-probe`;
    localStorage.setItem(probeKey, '1');
    localStorage.removeItem(probeKey);
    return localStorage;
  } catch {
    return null;
  }
};

const isRefreshOutcome = (value: unknown): value is RefreshOutcome => {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Partial<RefreshOutcome>;
  return (candidate.type === 'completed' || candidate.type === 'failed') &&
    typeof candidate.id === 'string' &&
    typeof candidate.completedAt === 'number' &&
    typeof candidate.sessionKey === 'string';
};

const isRefreshLease = (value: unknown): value is RefreshLease => {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Partial<RefreshLease>;
  return typeof candidate.owner === 'string' && typeof candidate.expiresAt === 'number';
};

const isRefreshRequest = (value: unknown): value is RefreshRequest => {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Partial<RefreshRequest>;
  return candidate.type === 'request' &&
    typeof candidate.id === 'string' &&
    typeof candidate.tabId === 'string' &&
    typeof candidate.sessionKey === 'string' &&
    typeof candidate.requestedAt === 'number';
};

const toFailure = (outcome: RefreshOutcome): SessionRefreshFailure => {
  if (outcome.type !== 'failed') {
    throw new Error('Resultado de refresh inesperado.');
  }

  return new SessionRefreshFailure();
};

export const createRefreshCoordinator = (options: RefreshCoordinatorOptions = {}) => {
  const now = options.now ?? (() => Date.now());
  const channel = options.channelFactory ? options.channelFactory() : getDefaultChannel();
  const lockManager = options.lockManager === undefined ? getDefaultLockManager() : options.lockManager;
  const storage = options.storage === undefined ? getDefaultStorage() : options.storage;
  const tabId = options.tabId ?? createId();
  let latestOutcome: RefreshOutcome | null = null;
  let activeRefresh: Promise<RefreshSessionPayload> | null = null;
  let activeSessionKey: string | null = null;
  const failureListeners = new Set<() => void>();
  const peerSuccessListeners = new Set<(payload: RefreshSessionPayload, sessionKey: string) => void>();
  let lastPublishedOutcomeId: string | null = null;
  const pendingRequests = new Map<string, RefreshRequest[]>();

  const publishOutcome = (outcome: RefreshOutcome): void => {
    latestOutcome = outcome;
    lastPublishedOutcomeId = outcome.id;
    try {
      channel?.postMessage(outcome);
    } catch {
      // Un navegador puede cerrar el canal durante la navegación. La rotación
      // local ya terminó y no debe fallar por un aviso entre pestañas.
    }

    if (storage) {
      try {
        const serializedOutcome = JSON.stringify(outcome);
        storage.setItem(REFRESH_OUTCOME_STORAGE_KEY, serializedOutcome);
        setTimeout(() => {
          try {
            if (storage.getItem(REFRESH_OUTCOME_STORAGE_KEY) === serializedOutcome) {
              storage.removeItem(REFRESH_OUTCOME_STORAGE_KEY);
            }
          } catch {
            // La entrada es efimera y no afecta a la seguridad del refresh si
            // el navegador bloquea su limpieza.
          }
        }, REFRESH_OUTCOME_MAX_AGE_MS + 1_000);
      } catch {
        // localStorage puede estar bloqueado por la política de privacidad.
      }
    }
  };

  const notifyFailure = (): void => {
    failureListeners.forEach((listener) => listener());
  };

  const acceptOutcome = (candidate: unknown): void => {
    if (!isRefreshOutcome(candidate)) {
      return;
    }

    if (!latestOutcome || candidate.completedAt >= latestOutcome.completedAt) {
      latestOutcome = candidate;
    }

    if (candidate.type === 'failed' &&
        activeSessionKey !== null && candidate.sessionKey === activeSessionKey) {
      notifyFailure();
    }
  };

  const registerRequest = (request: RefreshRequest): void => {
    if (now() - request.requestedAt > REFRESH_OUTCOME_MAX_AGE_MS) {
      return;
    }

    const current = pendingRequests.get(request.sessionKey) ?? [];
    if (!current.some((candidate) => candidate.id === request.id)) {
      pendingRequests.set(request.sessionKey, [...current, request]);
    }
  };

  // El refresh rota la cookie CSRF compartida. Una pestaña inactiva que no
  // estaba esperando refresh() también debe recibir el token nuevo; si no, su
  // siguiente mutación enviaría el CSRF viejo y acabaría en 403.
  const notifyPeerSuccess = (candidate: unknown): void => {
    if (!isRefreshOutcome(candidate) || candidate.type !== 'completed' || candidate.id === lastPublishedOutcomeId) {
      return;
    }

    peerSuccessListeners.forEach((listener) => listener(candidate.payload, candidate.sessionKey));
  };

  const onChannelMessage = (event: { data: unknown }): void => {
    if (isRefreshRequest(event.data)) {
      registerRequest(event.data);
      return;
    }

    acceptOutcome(event.data);
    notifyPeerSuccess(event.data);
  };

  channel?.addEventListener('message', onChannelMessage);

  // Sin BroadcastChannel, el único aviso entre pestañas es el evento storage.
  const onStorageEvent = (event: StorageEvent): void => {
    if (event.key !== REFRESH_OUTCOME_STORAGE_KEY || !event.newValue) {
      return;
    }

    try {
      const parsed: unknown = JSON.parse(event.newValue);
      acceptOutcome(parsed);
      notifyPeerSuccess(parsed);
    } catch {
      // Entrada corrupta o ajena: se ignora.
    }
  };

  const listensStorageEvents = !channel && !!storage && typeof window !== 'undefined';
  if (listensStorageEvents) {
    window.addEventListener('storage', onStorageEvent);
  }

  const readStoredOutcome = (): RefreshOutcome | null => {
    if (!storage) {
      return null;
    }

    try {
      const raw = storage.getItem(REFRESH_OUTCOME_STORAGE_KEY);
      if (!raw) {
        return null;
      }

      const parsed: unknown = JSON.parse(raw);
      if (!isRefreshOutcome(parsed)) {
        return null;
      }

      acceptOutcome(parsed);
      return parsed;
    } catch {
      return null;
    }
  };

  const getUsableOutcome = (sessionKey: string): RefreshOutcome | null => {
    const stored = readStoredOutcome();
    const candidate = !latestOutcome || (stored && stored.completedAt > latestOutcome.completedAt)
      ? stored
      : latestOutcome;

    if (!candidate || candidate.sessionKey !== sessionKey || now() - candidate.completedAt > REFRESH_OUTCOME_MAX_AGE_MS) {
      return null;
    }

    return candidate;
  };

  const outcomeToPayload = (outcome: RefreshOutcome): RefreshSessionPayload => {
    if (outcome.type === 'failed') {
      throw toFailure(outcome);
    }

    return outcome.payload;
  };

  const wait = (milliseconds: number): Promise<void> =>
    new Promise((resolve) => setTimeout(resolve, milliseconds));

  const claimDelay = (): number => {
    let hash = 0;
    for (const character of tabId) {
      hash = (hash * 31 + character.charCodeAt(0)) >>> 0;
    }
    return hash % REFRESH_LEASE_CLAIM_JITTER_MS;
  };

  const readLease = (): RefreshLease | null => {
    if (!storage) {
      return null;
    }

    try {
      const raw = storage.getItem(REFRESH_LEASE_STORAGE_KEY);
      if (!raw) {
        return null;
      }

      const parsed: unknown = JSON.parse(raw);
      return isRefreshLease(parsed) ? parsed : null;
    } catch {
      return null;
    }
  };

  const releaseLease = (): void => {
    if (!storage) {
      return;
    }

    const current = readLease();
    if (current?.owner !== tabId) {
      return;
    }

    try {
      storage.removeItem(REFRESH_LEASE_STORAGE_KEY);
    } catch {
      // El lease expira por tiempo aunque el navegador bloquee la limpieza.
    }
  };

  const runWithStorageLease = async (
    startedAt: number,
    sessionKey: string,
    runner: () => Promise<RefreshSessionPayload>,
  ): Promise<RefreshSessionPayload> => {
    if (!storage) {
      // En un navegador sin BroadcastChannel ni localStorage no existe una
      // forma segura de coordinar pestañas. Fallar cerrado conserva la
      // detección server-side de replay; el entorno Node de los tests sigue
      // permitiendo la ejecución aislada.
      if (typeof window !== 'undefined') {
        throw new SessionRefreshFailure();
      }
      return runner();
    }

    const deadline = now() + REFRESH_LEASE_MAX_WAIT_MS;
    while (now() <= deadline) {
      const outcome = getUsableOutcome(sessionKey);
      if (outcome && outcome.completedAt >= startedAt - REFRESH_OUTCOME_MAX_AGE_MS) {
        return outcomeToPayload(outcome);
      }

      const current = readLease();
      if (!current || current.expiresAt <= now() || current.owner === tabId) {
        // localStorage no ofrece compare-and-swap. Esperar una ranura
        // determinista por pestaña y volver a leer antes de escribir evita que
        // dos pestañas que observaron el mismo lease vacío se adelanten entre
        // sí. La verificación posterior sigue siendo obligatoria.
        if (current?.owner !== tabId) {
          await wait(claimDelay());
          const currentAfterDelay = readLease();
          if (currentAfterDelay && currentAfterDelay.expiresAt > now()) {
            await wait(REFRESH_LEASE_WAIT_MS);
            continue;
          }
        }

        const candidate: RefreshLease = { owner: tabId, expiresAt: now() + REFRESH_LEASE_MS };
        try {
          storage.setItem(REFRESH_LEASE_STORAGE_KEY, JSON.stringify(candidate));
        } catch {
          // Sin poder publicar el lease no se puede demostrar exclusión entre
          // pestañas. Fallar cerrado evita convertir un problema de storage en
          // una segunda presentación del mismo refresh token.
          throw new SessionRefreshFailure();
        }
        if (readLease()?.owner === tabId) {
          const renewal = setInterval(() => {
            if (readLease()?.owner !== tabId) {
              return;
            }
            try {
              storage.setItem(REFRESH_LEASE_STORAGE_KEY, JSON.stringify({
                owner: tabId,
                expiresAt: now() + REFRESH_LEASE_MS,
              } satisfies RefreshLease));
            } catch {
              // Si el almacenamiento deja de estar disponible, no se puede
              // renovar. El lease existente expira por sí solo.
            }
          }, Math.floor(REFRESH_LEASE_MS / 3));
          try {
            return await runner();
          } finally {
            clearInterval(renewal);
            releaseLease();
          }
        }
      }

      await wait(REFRESH_LEASE_WAIT_MS);
    }

    throw new SessionRefreshFailure();
  };

  const runWithChannelElection = async (
    startedAt: number,
    sessionKey: string,
    runner: () => Promise<RefreshSessionPayload>,
  ): Promise<RefreshSessionPayload> => {
    const request: RefreshRequest = {
      type: 'request',
      id: createId(),
      tabId,
      sessionKey,
      requestedAt: startedAt,
    };
    registerRequest(request);
    try {
      channel?.postMessage(request);
      await wait(25);
      const candidates = (pendingRequests.get(sessionKey) ?? [])
        .filter((candidate) => now() - candidate.requestedAt <= REFRESH_OUTCOME_MAX_AGE_MS)
        .sort((left, right) => left.id.localeCompare(right.id));
      if (candidates[0]?.id === request.id) {
        return await executeOwnRefresh(sessionKey, runner);
      }

      const deadline = now() + REFRESH_LEASE_MAX_WAIT_MS;
      while (now() <= deadline) {
        const outcome = getUsableOutcome(sessionKey);
        if (outcome && outcome.completedAt >= startedAt - REFRESH_OUTCOME_MAX_AGE_MS) {
          return outcomeToPayload(outcome);
        }
        await wait(REFRESH_LEASE_WAIT_MS);
      }

      throw new SessionRefreshFailure();
    } finally {
      const remaining = (pendingRequests.get(sessionKey) ?? []).filter((candidate) => candidate.id !== request.id);
      if (remaining.length > 0) {
        pendingRequests.set(sessionKey, remaining);
      } else {
        pendingRequests.delete(sessionKey);
      }
    }
  };

  const executeOwnRefresh = async (sessionKey: string, runner: () => Promise<RefreshSessionPayload>): Promise<RefreshSessionPayload> => {
    try {
      const payload = await runner();
      publishOutcome({ type: 'completed', id: createId(), completedAt: now(), sessionKey, payload });
      return payload;
    } catch (error) {
      publishOutcome({ type: 'failed', id: createId(), completedAt: now(), sessionKey });
      throw error;
    }
  };

  const refresh = (runner: () => Promise<RefreshSessionPayload>, sessionKey = 'anonymous'): Promise<RefreshSessionPayload> => {
    if (activeRefresh) {
      // Una renovación iniciada antes de logout no puede entregar su usuario
      // a la sesión que acaba de entrar. Esperar a que termine permite que la
      // nueva sesión presente sus propias cookies, sin reutilizar el payload
      // anterior.
      if (activeSessionKey !== sessionKey) {
        return activeRefresh.then(
          () => refresh(runner, sessionKey),
          () => refresh(runner, sessionKey),
        );
      }

      return activeRefresh;
    }

    activeSessionKey = sessionKey;
    const startedAt = now();
    activeRefresh = (async () => {
      const recentOutcome = getUsableOutcome(sessionKey);
      if (recentOutcome && recentOutcome.completedAt >= startedAt - REFRESH_OUTCOME_MAX_AGE_MS) {
        return outcomeToPayload(recentOutcome);
      }

      if (lockManager) {
        return lockManager.request(
          REFRESH_LOCK_NAME,
          { mode: 'exclusive' },
          async () => {
            const peerOutcome = getUsableOutcome(sessionKey);
            if (peerOutcome && peerOutcome.completedAt >= startedAt - REFRESH_OUTCOME_MAX_AGE_MS) {
              return outcomeToPayload(peerOutcome);
            }

            return executeOwnRefresh(sessionKey, runner);
          },
        );
      }

      if (channel) {
        return runWithChannelElection(startedAt, sessionKey, runner);
      }

      return runWithStorageLease(startedAt, sessionKey, () => executeOwnRefresh(sessionKey, runner));
    })().finally(() => {
      activeRefresh = null;
      activeSessionKey = null;
    });

    return activeRefresh;
  };

  const subscribeToFailure = (listener: () => void): (() => void) => {
    failureListeners.add(listener);
    return () => failureListeners.delete(listener);
  };

  const subscribeToPeerSuccess = (listener: (payload: RefreshSessionPayload, sessionKey: string) => void): (() => void) => {
    peerSuccessListeners.add(listener);
    return () => peerSuccessListeners.delete(listener);
  };

  const close = (): void => {
    channel?.removeEventListener('message', onChannelMessage);
    if (listensStorageEvents) {
      window.removeEventListener('storage', onStorageEvent);
    }
    channel?.close();
  };

  return { refresh, subscribeToFailure, subscribeToPeerSuccess, close };
};

export const sessionRefreshCoordinator = createRefreshCoordinator();
