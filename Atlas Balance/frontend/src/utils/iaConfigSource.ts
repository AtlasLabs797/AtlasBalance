// R-8 (BAJA): la configuracion de IA se consultaba cada 30 s en todas las
// pestañas y vivia en tres fuentes de verdad (el hook, el store del chat y el
// store de disponibilidad). Este modulo es el unico sitio que sabe:
//   - cuanto tiempo vale la respuesta de /ia/config (ventana de frescura),
//   - como se decide si la IA esta disponible para el usuario,
//   - como se avisa de que la configuracion cambio, para refrescar al instante.
//
// Sin imports del alias "@/" a proposito: los tests lo cargan con node --test
// y no resuelven los alias de Vite.

// Ventana de frescura de /ia/config. Sin sondeo periodico la cache caduca a los
// 5 min: la configuracion global solo cambia cuando un admin la guarda, y eso
// se avisa aparte (notifyIaConfigChanged) para refrescar en el acto.
export const IA_CONFIG_STALE_MS = 5 * 60_000;

// Endpoint que devuelve la configuracion de IA del usuario.
export const IA_CONFIG_PATH = '/ia/config';

// Configuracion de IA minima para decidir si el usuario puede usarla.
export interface IaAvailabilityConfig {
  habilitada?: boolean | null;
  usuario_puede_usar?: boolean | null;
}

// La IA esta disponible si el backend la habilito y el usuario puede usarla.
export function resolveIaAvailability(config: IaAvailabilityConfig | null | undefined): boolean {
  return Boolean(config?.habilitada && config?.usuario_puede_usar);
}

// El guardado de la configuracion global es PUT /configuracion y su payload
// lleva el bloque "ia" (provider, modelo, claves). Si esa escritura tiene exito,
// la configuracion de IA puede haber cambiado y hay que refrescar.
export function isIaConfigurationWrite(method: string | undefined, url: string | undefined): boolean {
  if (!method || !url) {
    return false;
  }
  const verb = method.toLowerCase();
  if (verb !== 'put' && verb !== 'patch' && verb !== 'post') {
    return false;
  }
  const path = url.split('?')[0].replace(/\/+$/, '');
  return path === '/configuracion';
}

type IaConfigChangeListener = () => void;

const listeners = new Set<IaConfigChangeListener>();

// Avisa de que la configuracion de IA puede haber cambiado.
export function notifyIaConfigChanged(): void {
  for (const listener of [...listeners]) {
    listener();
  }
}

// Se suscribe a los avisos de cambio. Devuelve la funcion para darse de baja.
export function subscribeIaConfigChanged(listener: IaConfigChangeListener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
