import { Component, type ErrorInfo, type ReactNode } from 'react';

interface AppErrorBoundaryProps {
  children: ReactNode;
  resetKey?: string;
}

interface AppErrorBoundaryState {
  hasError: boolean;
}

export default class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  public constructor(props: AppErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false };
  }

  public static getDerivedStateFromError(): AppErrorBoundaryState {
    return { hasError: true };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    // F-NEW-27 (V-02-03): loggear siempre, tambien en produccion. Si
    // hay un endpoint /api/telemetria/errores disponible, enviar el
    // stack con sendBeacon. Si no, al menos queda en consola para
    // diagnostico local.
    console.error('UI section crashed', error, errorInfo);
    if (typeof navigator !== 'undefined' && typeof navigator.sendBeacon === 'function') {
      try {
        const payload = JSON.stringify({
          mensaje: error.message,
          stack: error.stack,
          componentStack: errorInfo.componentStack,
          path: typeof window !== 'undefined' ? window.location.pathname : null,
          timestamp: new Date().toISOString()
        });
        // sendBeacon es fire-and-forget; el navegador lo envia aunque
        // la pagina se cierre inmediatamente. El endpoint es opcional;
        // si no existe, sendBeacon falla silenciosamente.
        navigator.sendBeacon('/api/telemetria/errores', payload);
      } catch {
        // No hacer nada: no queremos un bucle de errores.
      }
    }
  }

  public componentDidUpdate(prevProps: AppErrorBoundaryProps): void {
    if (this.state.hasError && prevProps.resetKey !== this.props.resetKey) {
      this.setState({ hasError: false });
    }
  }

  public render() {
    if (this.state.hasError) {
      return (
        <section className="page-placeholder">
          <h1>Sección no disponible</h1>
          <p>Hubo un error inesperado en esta vista. Recarga la página para continuar.</p>
          <div className="not-found-actions">
            <button type="button" onClick={() => window.location.reload()}>
              Recargar vista
            </button>
            <a href="/dashboard">Ir al dashboard</a>
          </div>
        </section>
      );
    }

    return this.props.children;
  }
}
