import assert from 'node:assert/strict';
import test from 'node:test';
import { isSessionInvalidatingError } from '../src/utils/sessionErrors.js';
import {
  createRefreshCoordinator,
  SessionRefreshFailure,
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

const createTwoTabs = () => {
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

  return { firstTab, secondTab };
};

test('isSessionInvalidatingError solo marca 401 y 403 como invalidantes de sesion', () => {
  assert.equal(isSessionInvalidatingError({ status: 401 }), true);
  assert.equal(isSessionInvalidatingError({ response: { status: 403 } }), true);
  assert.equal(isSessionInvalidatingError({ status: 500 }), false);
  assert.equal(isSessionInvalidatingError({ response: { status: 429 } }), false);
  assert.equal(isSessionInvalidatingError({ response: { status: 400 } }), false);
  assert.equal(isSessionInvalidatingError(undefined), false);
  assert.equal(isSessionInvalidatingError(null), false);
  assert.equal(isSessionInvalidatingError('401'), false);
  assert.equal(isSessionInvalidatingError(new Error('Network Error')), false);
});

test('un fallo transitorio del refresh no cierra la sesion en ninguna pestana', async () => {
  const { firstTab, secondTab } = createTwoTabs();
  let firstFailures = 0;
  let secondFailures = 0;
  let peerSuccesses = 0;
  firstTab.subscribeToFailure(() => {
    firstFailures += 1;
  });
  secondTab.subscribeToFailure(() => {
    secondFailures += 1;
  });
  firstTab.subscribeToPeerSuccess(() => {
    peerSuccesses += 1;
  });
  secondTab.subscribeToPeerSuccess(() => {
    peerSuccesses += 1;
  });

  const serverError = Object.assign(new Error('Error interno del servidor'), {
    response: { status: 500 },
  });

  const firstRequest = firstTab.refresh(async () => {
    throw serverError;
  }, 'user-1');
  const secondRequest = secondTab.refresh(async () => {
    throw new Error('la segunda pestana no debe llamar al endpoint');
  }, 'user-1');

  await assert.rejects(firstRequest, /Error interno del servidor/);
  await assert.rejects(secondRequest, (error: unknown) => {
    assert.ok(
      !(error instanceof SessionRefreshFailure),
      'un fallo transitorio no puede llegar como SessionRefreshFailure a la pestana que espera',
    );
    return true;
  });

  assert.equal(firstFailures, 0);
  assert.equal(secondFailures, 0);
  assert.equal(peerSuccesses, 0);

  firstTab.close();
  secondTab.close();
});

test('un 401 al renovar el token invalida la sesion en las demas pestanas', async () => {
  const { firstTab, secondTab } = createTwoTabs();
  let secondFailures = 0;
  secondTab.subscribeToFailure(() => {
    secondFailures += 1;
  });

  const unauthorized = Object.assign(new Error('No autorizado'), {
    response: { status: 401 },
  });

  const firstRequest = firstTab.refresh(async () => {
    throw unauthorized;
  }, 'user-1');
  const secondRequest = secondTab.refresh(async () => {
    throw new Error('la segunda pestana no debe llamar al endpoint');
  }, 'user-1');

  await assert.rejects(firstRequest, /No autorizado/);
  await assert.rejects(secondRequest, SessionRefreshFailure);

  assert.equal(secondFailures, 1);

  firstTab.close();
  secondTab.close();
});
