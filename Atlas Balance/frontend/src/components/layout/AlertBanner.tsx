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
  const variante = enNegativo > 0 ? 'danger' : 'info';

  return (
    <section
      className={`alert-banner alert-banner--${variante}`}
      role={variante === 'danger' ? 'alert' : 'status'}
      aria-live="polite"
    >
      <div className="alert-banner-content">
        <span className="alert-banner-dot" aria-hidden="true" />
        <span className="alert-banner-pill">{variante === 'danger' ? 'Urgente' : 'Atención'}</span>
        <strong>{enNegativo > 0 ? 'Cuentas en negativo' : 'Saldo bajo detectado'}</strong>
        <span>
          {alertasActivas.length} cuenta{alertasActivas.length === 1 ? '' : 's'} por debajo del mínimo
          {enNegativo > 0 ? `, ${enNegativo} en negativo` : ''}.
        </span>
        <Link to="/alertas">Revisar alertas</Link>
      </div>
      <button type="button" onClick={dismissBanner}>
        Ocultar
      </button>
    </section>
  );
}
