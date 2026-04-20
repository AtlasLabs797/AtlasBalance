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
    if (import.meta.env.DEV) {
      console.error('UI section crashed', error, errorInfo);
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
          <h1>Seccion no disponible</h1>
          <p>Hubo un error inesperado en esta vista. Recarga la pagina para continuar.</p>
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
