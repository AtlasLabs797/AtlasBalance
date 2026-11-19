// D-3: componente unico de estado de carga de la app.
// Criterio de uso:
// - variant "default" | "dashboard" | "form" | "detail": esqueleto de pagina
//   (la vista entera esta bloqueada mientras llegan los datos).
// - variant "table": esqueleto de tabla real (cabecera + N filas de celdas) para
//   listas y tablas.
// - variant "inline": esqueleto compacto para un panel o una seccion. Aqui el
//   texto de carga sigue visible (como antes) y el esqueleto lo acompana.
// El mensaje va siempre en un role="status" con aria-live="polite": el lector de
// pantalla anuncia el estado de carga. En las variantes de pagina y tabla el
// texto es solo para lectores de pantalla (sr-only); en la inline se ve.
// No se usa aria-busy en el contenedor: silencia el anuncio de la region viva,
// que es justo lo que este estado debe comunicar.

interface PageSkeletonProps {
  rows?: number;
  variant?: 'default' | 'dashboard' | 'table' | 'form' | 'detail' | 'inline';
  label?: string;
  columns?: number;
}

const DEFAULT_COLUMNS = 6;
const INLINE_ROWS = 2;

export function PageSkeleton({
  rows = 3,
  variant = 'default',
  label = 'Cargando contenido',
  columns = DEFAULT_COLUMNS,
}: PageSkeletonProps) {
  if (variant === 'inline') {
    return (
      <div className="page-skeleton page-skeleton--inline" role="status" aria-live="polite">
        <span className="import-muted">{label}</span>
        {Array.from({ length: INLINE_ROWS }).map((_, index) => (
          <div key={`inline-${index}`} className="skeleton-line skeleton-line--compact" />
        ))}
      </div>
    );
  }

  if (variant === 'table') {
    return (
      <div className="page-skeleton page-skeleton--table" role="status" aria-live="polite">
        <span className="sr-only">{label}</span>
        <div className="skeleton-table">
          <div className="skeleton-table__head">
            {Array.from({ length: columns }).map((_, index) => (
              <div key={`head-${index}`} className="skeleton-line skeleton-line--cell" />
            ))}
          </div>
          {Array.from({ length: rows }).map((_, rowIndex) => (
            <div key={`row-${rowIndex}`} className="skeleton-table__row">
              {Array.from({ length: columns }).map((_, cellIndex) => (
                <div key={`cell-${cellIndex}`} className="skeleton-line skeleton-line--cell" />
              ))}
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <section className={`page-placeholder page-skeleton page-skeleton--${variant}`} role="status" aria-live="polite">
      <span className="sr-only">{label}</span>
      <div className="skeleton-line skeleton-line--title" />
      {Array.from({ length: rows }).map((_, index) => (
        <div key={`row-${index}`} className="skeleton-line" />
      ))}
    </section>
  );
}
