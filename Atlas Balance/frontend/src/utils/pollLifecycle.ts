// V-03.02 (F-4): los bucles de sondeo (estado de la actualizacion, estado de
// una operacion de backup) siguen consultando aunque el usuario se haya ido
// de la pagina, y pintan toasts cada 2,5 s mientras la API esta caida.
//
// Este helper da a cada sondeo un ciclo de vida: `stop()` limpia el timer
// pendiente y marca el sondeo como parado, de modo que un `await` en vuelo no
// dispare otra iteracion ni actualice el estado de un componente desmontado.
//
// Helper puro y sin dependencias: lo usan ConfiguracionPage y BackupsPage.

export interface PollLifecycle {
  /** true mientras el sondeo debe seguir: todavia no se ha cancelado. */
  readonly active: boolean;
  /** Espera delayMs y devuelve false si el sondeo se cancelo mientras esperaba. */
  wait(delayMs: number): Promise<boolean>;
  /** Cancela el sondeo: limpia el timer pendiente y resuelve la espera actual. */
  stop(): void;
}

export function createPollLifecycle(): PollLifecycle {
  let stopped = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let pending: (() => void) | undefined;

  return {
    get active() {
      return !stopped;
    },

    wait(delayMs: number): Promise<boolean> {
      if (stopped) {
        return Promise.resolve(false);
      }

      return new Promise<boolean>((resolve) => {
        const finish = (result: boolean) => {
          timer = undefined;
          pending = undefined;
          resolve(result);
        };

        pending = () => finish(false);
        timer = setTimeout(() => finish(!stopped), delayMs);
      });
    },

    stop(): void {
      stopped = true;

      if (timer !== undefined) {
        clearTimeout(timer);
        timer = undefined;
      }

      if (pending) {
        const resolvePending = pending;
        pending = undefined;
        resolvePending();
      }
    },
  };
}
