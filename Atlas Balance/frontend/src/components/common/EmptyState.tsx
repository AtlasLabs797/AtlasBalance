import type { ReactNode } from 'react';

interface EmptyStateProps {
  title: string;
  subtitle?: string;
  icon?: ReactNode;
  primaryAction?: ReactNode;
  secondaryAction?: ReactNode;
  variant?: 'data' | 'error' | 'permission' | 'setup';
}

export function EmptyState({
  title,
  subtitle,
  icon,
  primaryAction,
  secondaryAction,
  variant = 'data',
}: EmptyStateProps) {
  return (
    <div className={`empty-state empty-state--${variant}`} role={variant === 'error' ? 'alert' : 'status'}>
      {icon ? <div className="empty-state-icon">{icon}</div> : null}
      <h3>{title}</h3>
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
