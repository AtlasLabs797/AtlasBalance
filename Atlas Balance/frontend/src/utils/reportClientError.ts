// Punto unico de envio de telemetria de errores del cliente hacia
// /api/telemetria/errores. Usado por AppErrorBoundary y por los listeners
// globales de window en main.tsx para no duplicar la construccion del
// payload ni la logica de envio.
//
// Usa sendBeacon envuelto en un Blob de tipo application/json: sendBeacon
// con un string suelto manda Content-Type text/plain y no bindea contra el
// modelo JSON en ASP.NET Core.
//
// Nunca lanza ni reintenta (evita bucles de error) y limita el numero de
// reportes por carga de pagina para que un error en bucle no inunde el
// servidor.

// Topes de cada campo. Deben coincidir con los que aplica el backend al recortar
// en TelemetriaController.cs (MaxMensaje, MaxStack, MaxPath y 40 para el timestamp);
// el contrato del payload vive en backend/src/AtlasBalance.API/DTOs/TelemetriaDtos.cs.
// Si el cliente manda mas, el backend ya no lo rechaza, pero recortar aqui evita
// enviar de mas.
export const LIMITES_TELEMETRIA = {
  mensaje: 500,
  stack: 4000,
  componentStack: 4000,
  path: 200,
  timestamp: 40,
} as const;

const MAX_REPORTS_PER_PAGE_LOAD = 10;
let reportCount = 0;

interface ClientErrorInput {
  mensaje: string;
  stack?: string;
  componentStack?: string;
}

export interface PayloadTelemetria {
  mensaje: string;
  stack?: string;
  componentStack?: string;
  path: string | null;
  timestamp: string;
}

// Corta a maximo unidades UTF-16 sin dejar un surrogate alto suelto al final:
// un surrogate suelto no es UTF-16 valido y se pierde al serializar.
export function recortarTexto(valor: string, maximo: number): string {
  if (valor.length <= maximo) {
    return valor;
  }
  const ultimo = valor.charCodeAt(maximo - 1);
  const esSurrogateAlto = ultimo >= 0xd800 && ultimo <= 0xdbff;
  return valor.slice(0, esSurrogateAlto ? maximo - 1 : maximo);
}

function recortarOpcional(valor: string | undefined, maximo: number): string | undefined {
  return typeof valor === 'string' ? recortarTexto(valor, maximo) : undefined;
}

export function recortarPayloadTelemetria(payload: PayloadTelemetria): PayloadTelemetria {
  return {
    mensaje: recortarTexto(payload.mensaje, LIMITES_TELEMETRIA.mensaje),
    stack: recortarOpcional(payload.stack, LIMITES_TELEMETRIA.stack),
    componentStack: recortarOpcional(payload.componentStack, LIMITES_TELEMETRIA.componentStack),
    path: typeof payload.path === 'string' ? recortarTexto(payload.path, LIMITES_TELEMETRIA.path) : null,
    timestamp: recortarTexto(payload.timestamp, LIMITES_TELEMETRIA.timestamp),
  };
}

export function reportClientError(error: ClientErrorInput): void {
  if (typeof navigator === 'undefined' || typeof navigator.sendBeacon !== 'function') {
    return;
  }

  if (reportCount >= MAX_REPORTS_PER_PAGE_LOAD) {
    return;
  }
  reportCount += 1;

  try {
    const payload = JSON.stringify(
      recortarPayloadTelemetria({
        mensaje: error.mensaje,
        stack: error.stack,
        componentStack: error.componentStack,
        path: typeof window !== 'undefined' ? window.location.pathname : null,
        timestamp: new Date().toISOString()
      })
    );
    const blob = new Blob([payload], { type: 'application/json' });
    navigator.sendBeacon('/api/telemetria/errores', blob);
  } catch {
    // No hacer nada: no queremos un bucle de errores.
  }
}
