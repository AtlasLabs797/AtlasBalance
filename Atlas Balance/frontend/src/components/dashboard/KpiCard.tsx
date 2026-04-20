import type { ReactNode } from 'react';

interface KpiCardProps {
  title: string;
  value: ReactNode;
  tone?: 'default' | 'positive' | 'negative';
  featured?: boolean;
  helper?: ReactNode;
}

export function KpiCard({ title, value, tone = 'default', featured = false, helper }: KpiCardProps) {
  return (
    <article className={`dashboard-kpi dashboard-kpi--${tone}${featured ? ' dashboard-kpi--featured' : ''}`}>
      <h3>{title}</h3>
      <p>{value}</p>
      {helper ? <span className="dashboard-kpi-helper">{helper}</span> : null}
    </article>
  );
}
