import type { ReactNode } from 'react';

interface KpiCardProps {
  title: string;
  value: ReactNode;
  tone?: 'default' | 'positive' | 'negative';
  featured?: boolean;
  helper?: ReactNode;
  /**
   * D-10 (LOTE 8): nivel del encabezado del titulo. Por defecto `h3`; pon `h2`
   * cuando la tarjeta se renderiza directamente bajo el `h1` de la pagina, para
   * que la jerarquia no salte niveles. El CSS mantiene el mismo aspecto.
   */
  headingLevel?: 'h2' | 'h3';
}

export function KpiCard({ title, value, tone = 'default', featured = false, helper, headingLevel = 'h3' }: KpiCardProps) {
  const Heading = headingLevel;
  return (
    <article className={`dashboard-kpi dashboard-kpi--${tone}${featured ? ' dashboard-kpi--featured' : ''}`}>
      <Heading>{title}</Heading>
      <p>{value}</p>
      {helper ? <span className="dashboard-kpi-helper">{helper}</span> : null}
    </article>
  );
}
