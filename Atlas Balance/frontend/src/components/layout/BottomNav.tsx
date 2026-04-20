import { useEffect, useMemo, useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import { IconMenu } from '@/components/Icons';
import { getVisibleNavigationItems } from '@/utils/navigation';
import { useAlertCount } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { useNotificacionesAdminStore } from '@/stores/notificacionesAdminStore';
import { useUpdateStore } from '@/stores/updateStore';

const PRIMARY_ITEM_PATHS = ['/dashboard', '/titulares', '/cuentas', '/extractos'];

export function BottomNav() {
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const usuario = useAuthStore((state) => state.usuario);
  const alertCount = useAlertCount();
  const exportacionesPendientes = useNotificacionesAdminStore((state) => state.exportacionesPendientes);
  const updateAvailable = useUpdateStore((state) => state.available);

  const visibleNavItems = useMemo(() => getVisibleNavigationItems(usuario?.rol), [usuario?.rol]);
  const primaryItems = useMemo(
    () => visibleNavItems.filter((item) => PRIMARY_ITEM_PATHS.includes(item.to)),
    [visibleNavItems]
  );
  const secondaryItems = useMemo(
    () => visibleNavItems.filter((item) => !PRIMARY_ITEM_PATHS.includes(item.to)),
    [visibleNavItems]
  );

  const hiddenBadgeCount = alertCount + exportacionesPendientes + (updateAvailable ? 1 : 0);
  const secondaryActive = secondaryItems.some((item) => location.pathname.startsWith(item.to));

  useEffect(() => {
    setMenuOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!menuOpen) {
      return;
    }

    const previousOverflow = document.body.style.overflow;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMenuOpen(false);
      }
    };

    document.body.style.overflow = 'hidden';
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [menuOpen]);

  return (
    <>
      <nav className="bottom-nav" aria-label="Navegacion inferior">
        {primaryItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) => (isActive ? 'bottom-nav-link bottom-nav-link--active' : 'bottom-nav-link')}
          >
            <span className="bottom-nav-icon">{item.icon}</span>
            <span>{item.short}</span>
          </NavLink>
        ))}
        <button
          type="button"
          className={secondaryActive || menuOpen ? 'bottom-nav-link bottom-nav-link--active' : 'bottom-nav-link'}
          aria-expanded={menuOpen}
          aria-controls="bottom-nav-sheet"
          onClick={() => setMenuOpen((current) => !current)}
        >
          <span className="bottom-nav-icon" aria-hidden="true"><IconMenu /></span>
          <span>Menu</span>
          {hiddenBadgeCount > 0 ? <span className="sidebar-alert-badge">{hiddenBadgeCount}</span> : null}
        </button>
      </nav>

      {menuOpen ? (
        <div className="bottom-nav-sheet-backdrop" onClick={() => setMenuOpen(false)}>
          <section
            id="bottom-nav-sheet"
            className="bottom-nav-sheet"
            role="dialog"
            aria-modal="true"
            aria-label="Menu de accesos"
            onClick={(event) => event.stopPropagation()}
          >
            <header className="bottom-nav-sheet-header">
              <div>
                <strong>Menu</strong>
                <p>Accesos secundarios y herramientas de administracion.</p>
              </div>
              <button
                type="button"
                className="bottom-nav-sheet-close"
                onClick={() => setMenuOpen(false)}
                aria-label="Cerrar menu de accesos"
              >
                Cerrar
              </button>
            </header>

            <div className="bottom-nav-sheet-grid">
              {secondaryItems.map((item) => {
                const badge =
                  item.to === '/alertas'
                    ? alertCount
                    : item.to === '/exportaciones' && usuario?.rol === 'ADMIN'
                      ? exportacionesPendientes
                      : item.to === '/configuracion' && updateAvailable
                        ? 1
                        : 0;

                return (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    className={({ isActive }) =>
                      isActive ? 'bottom-nav-sheet-link bottom-nav-sheet-link--active' : 'bottom-nav-sheet-link'
                    }
                  >
                    <span className="bottom-nav-sheet-icon">{item.icon}</span>
                    <span>{item.label}</span>
                    {badge > 0 ? (
                      <span className={item.to === '/configuracion' ? 'sidebar-update-badge' : 'sidebar-alert-badge'}>
                        {item.to === '/configuracion' ? 'Update' : badge}
                      </span>
                    ) : null}
                  </NavLink>
                );
              })}
            </div>
          </section>
        </div>
      ) : null}
    </>
  );
}
