import { EmptyState } from '@/components/common/EmptyState';

interface DashboardBlockSkeletonProps {
  label: string;
  rows?: number;
  tall?: boolean;
}

/** Esqueleto de un bloque concreto del dashboard (no de toda la pagina). */
export function DashboardBlockSkeleton({ label, rows = 3, tall = false }: DashboardBlockSkeletonProps) {
  return (
    <div
      className={`dashboard-block-skeleton${tall ? ' dashboard-block-skeleton--tall' : ''}`}
      role="status"
      aria-busy="true"
      aria-label={`Cargando ${label}`}
    >
      {Array.from({ length: rows }).map((_, index) => (
        <div key={index} className="skeleton-line" />
      ))}
    </div>
  );
}

interface DashboardBlockErrorProps {
  title: string;
  message: string;
  onRetry: () => void;
  retrying?: boolean;
}

/** Error de un bloque: mensaje real de la consulta y reintento de esa consulta. */
export function DashboardBlockError({ title, message, onRetry, retrying = false }: DashboardBlockErrorProps) {
  return (
    <EmptyState
      variant="error"
      title={title}
      subtitle={message}
      primaryAction={
        <button type="button" onClick={onRetry} disabled={retrying}>
          {retrying ? 'Reintentando...' : 'Reintentar'}
        </button>
      }
    />
  );
}

interface DashboardStaleNoticeProps {
  updatedAt: number;
  failed: boolean;
  onRetry: () => void;
}

/** Hora de ultima actualizacion; solo se muestra si alguna consulta fallo al refrescar. */
export function DashboardStaleNotice({ updatedAt, failed, onRetry }: DashboardStaleNoticeProps) {
  if (!failed || !updatedAt) {
    return null;
  }

  const hora = new Date(updatedAt).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });

  return (
    <p className="dashboard-stale-notice" role="status">
      Datos de las {hora}. No se pudieron actualizar todos los bloques.{' '}
      <button type="button" className="dashboard-stale-retry" onClick={onRetry}>
        Reintentar
      </button>
    </p>
  );
}
