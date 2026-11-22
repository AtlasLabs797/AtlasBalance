import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router';
import { SignedAmount } from '@/components/common/SignedAmount';
import { IconAlertaDanger, IconAlertaWarning, IconBell } from '@/components/Icons';
import { formatCurrency } from '@/utils/formatters';
import { useAlertasStore } from '@/stores/alertasStore';

export function AlertBell() {
  const alertasActivas = useAlertasStore((state) => state.alertasActivas);
  const bannerDismissed = useAlertasStore((state) => state.bannerDismissed);
  const dismissBanner = useAlertasStore((state) => state.dismissBanner);
  const restoreBanner = useAlertasStore((state) => state.restoreBanner);
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const total = alertasActivas.length;
  const hayNegativo = alertasActivas.some((alerta) => alerta.saldo_actual < 0);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const label = total === 0 ? 'Alertas: ninguna activa' : `Alertas: ${total} activa${total === 1 ? '' : 's'}`;

  return (
    <div className="alert-bell" ref={rootRef}>
      <button
        type="button"
        className="alert-bell-button"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        aria-haspopup="true"
        aria-label={label}
        title={label}
      >
        <IconBell />
        {total > 0 ? (
          <span className={`alert-bell-count${hayNegativo ? ' alert-bell-count--danger' : ''}`} aria-hidden="true">
            {total > 9 ? '9+' : total}
          </span>
        ) : null}
      </button>
      {open ? (
        <div className="alert-bell-menu" role="region" aria-label="Alertas activas">
          <div className="alert-bell-menu-head">
            <strong>Alertas</strong>
            {total > 0 ? (
              <button type="button" onClick={bannerDismissed ? restoreBanner : dismissBanner}>
                {bannerDismissed ? 'Mostrar aviso emergente' : 'Ocultar aviso emergente'}
              </button>
            ) : null}
          </div>
          {total === 0 ? (
            <p className="alert-bell-empty">No hay cuentas por debajo del mínimo.</p>
          ) : (
            <ul className="alert-bell-list">
              {alertasActivas.map((alerta) => {
                const severity = alerta.saldo_actual < 0 ? 'danger' : 'warning';
                const divisa = alerta.divisa ?? 'EUR';
                return (
                  <li key={`${alerta.alerta_id}:${alerta.cuenta_id}`} className="alert-bell-item">
                    <span className={`alertas-notif-icon alertas-notif-icon--${severity}`} aria-hidden="true">
                      {severity === 'danger' ? <IconAlertaDanger /> : <IconAlertaWarning />}
                    </span>
                    <div className="alert-bell-item-body">
                      <Link
                        to={`/cuentas/${alerta.cuenta_id}`}
                        className="alert-bell-item-name"
                        onClick={() => setOpen(false)}
                      >
                        {alerta.cuenta_nombre}
                      </Link>
                      <span className="alert-bell-item-meta">{alerta.titular_nombre}</span>
                      <span className="alert-bell-item-amounts">
                        <SignedAmount value={alerta.saldo_actual} showSign>
                          {formatCurrency(alerta.saldo_actual, divisa)}
                        </SignedAmount>
                        <span className="alert-bell-item-min">mín. {formatCurrency(alerta.saldo_minimo, divisa)}</span>
                      </span>
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
          <Link to="/alertas" className="alert-bell-link" onClick={() => setOpen(false)}>
            Ver todas las alertas
          </Link>
        </div>
      ) : null}
    </div>
  );
}
