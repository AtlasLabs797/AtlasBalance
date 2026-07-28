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

const MAX_REPORTS_PER_PAGE_LOAD = 10;
let reportCount = 0;

interface ClientErrorInput {
  mensaje: string;
  stack?: string;
  componentStack?: string;
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
    const payload = JSON.stringify({
      mensaje: error.mensaje,
      stack: error.stack,
      componentStack: error.componentStack,
      path: typeof window !== 'undefined' ? window.location.pathname : null,
      timestamp: new Date().toISOString()
    });
    const blob = new Blob([payload], { type: 'application/json' });
    navigator.sendBeacon('/api/telemetria/errores', blob);
  } catch {
    // No hacer nada: no queremos un bucle de errores.
  }
}
