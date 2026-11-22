import { X } from 'lucide-react';
import { Link } from 'react-router';
import { useAlertasStore } from '@/stores/alertasStore';

export function AlertBanner() {
  const alertasActivas = useAlertasStore((state) => state.alertasActivas);
  const bannerDismissed = useAlertasStore((state) => state.bannerDismissed);
  const dismissBanner = useAlertasStore((state) => state.dismissBanner);

  if (bannerDismissed || alertasActivas.length === 0) {
    return null;
  }

  // DESIGN.md §5.1: el banner tiene dos variantes. La severidad sale del dato
  // que ya hay, sin regla de negocio nueva: una cuenta en negativo es peor que
  // una por debajo del minimo, y escala el aviso a peligro.
  const enNegativo = alertasActivas.filter((alerta) => alerta.saldo_actual < 0).length;
  // El aviso operativo es ambar por defecto, como .atl-alertbanner del design
  // system; solo escala a rojo si alguna cuenta esta en negativo.
  const variante = enNegativo > 0 ? 'danger' : 'warning';

  return (
    <section
      className={`alert-banner alert-banner--${variante}`}
      role={variante === 'danger' ? 'alert' : 'status'}
      aria-live="polite"
    >
      <div className="alert-banner-content">
        <span className="alert-banner-dot" aria-hidden="true" />
        <strong>
          {alertasActivas.length} cuenta{alertasActivas.length === 1 ? '' : 's'}{' '}
          {enNegativo > 0 ? 'en negativo' : 'bajo mínimo'}
        </strong>
        <Link to="/alertas">Ver</Link>
      </div>
      <button type="button" onClick={dismissBanner} aria-label="Ocultar aviso">
        <span className="alert-banner-dismiss-label">Ocultar</span>
        <X className="alert-banner-dismiss-icon" size={16} strokeWidth={1.5} aria-hidden="true" />
      </button>
    </section>
  );
}
