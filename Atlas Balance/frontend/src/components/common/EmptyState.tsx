import type { ReactNode } from 'react';

interface EmptyStateProps {
  title: string;
  subtitle?: string;
  icon?: ReactNode;
  primaryAction?: ReactNode;
  secondaryAction?: ReactNode;
  variant?: 'data' | 'error' | 'permission' | 'setup';
  /**
   * D-10 (LOTE 8): nivel del encabezado del titulo. Por defecto `h3`, que es lo
   * que se ha usado siempre; pon `h2` cuando el componente se renderiza
   * directamente bajo el `h1` de la pagina, para que la jerarquia no salte
   * niveles. El CSS mantiene el mismo aspecto en los dos casos.
   */
  headingLevel?: 'h2' | 'h3';
}

export function EmptyState({
  title,
  subtitle,
  icon,
  primaryAction,
  secondaryAction,
  variant = 'data',
  headingLevel = 'h3',
}: EmptyStateProps) {
  const Heading = headingLevel;
  return (
    <div className={`empty-state empty-state--${variant}`} role={variant === 'error' ? 'alert' : 'status'}>
      {icon ? <div className="empty-state-icon">{icon}</div> : null}
      <Heading>{title}</Heading>
      {subtitle ? <p>{subtitle}</p> : null}
      {primaryAction || secondaryAction ? (
        <div className="empty-state-actions">
          {primaryAction}
          {secondaryAction}
        </div>
      ) : null}
    </div>
  );
}
