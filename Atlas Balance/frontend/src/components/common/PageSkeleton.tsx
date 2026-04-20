interface PageSkeletonProps {
  rows?: number;
  variant?: 'default' | 'dashboard' | 'table' | 'form' | 'detail';
}

export function PageSkeleton({ rows = 3, variant = 'default' }: PageSkeletonProps) {
  return (
    <section
      className={`page-placeholder page-skeleton page-skeleton--${variant}`}
      aria-busy="true"
      aria-label="Cargando contenido"
    >
      <div className="skeleton-line skeleton-line--title" />
      {Array.from({ length: rows }).map((_, index) => (
        <div key={`row-${index}`} className="skeleton-line" />
      ))}
    </section>
  );
}
