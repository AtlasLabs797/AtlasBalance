import { Link } from 'react-router-dom';
import { useAlertasStore } from '@/stores/alertasStore';

export function AlertBanner() {
  const alertasActivas = useAlertasStore((state) => state.alertasActivas);
  const bannerDismissed = useAlertasStore((state) => state.bannerDismissed);
  const dismissBanner = useAlertasStore((state) => state.dismissBanner);

  if (bannerDismissed || alertasActivas.length === 0) {
    return null;
  }

  return (
    <section className="alert-banner" role="status" aria-live="polite">
      <div className="alert-banner-content">
        <span className="alert-banner-pill">Atencion</span>
        <strong>Saldo bajo detectado</strong>
        <span>
          {alertasActivas.length} cuenta{alertasActivas.length === 1 ? '' : 's'} por debajo del mínimo.
        </span>
        <Link to="/alertas">Revisar alertas</Link>
      </div>
      <button type="button" onClick={dismissBanner}>
        Ocultar
      </button>
    </section>
  );
}
