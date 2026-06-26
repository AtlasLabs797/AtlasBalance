import { useEffect, useMemo, useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { IconMenu } from '@/components/Icons';
import { PaisScopeSelect } from '@/components/layout/PaisScopeSelect';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import { getVisibleNavigationItems, navigationGroups, type NavigationGroup } from '@/utils/navigation';
import { useAlertCount } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import { useNotificacionesAdminStore } from '@/stores/notificacionesAdminStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { useUpdateStore } from '@/stores/updateStore';

const SECONDARY_GROUP_ORDER: NavigationGroup[] = ['operacion', 'control', 'sistema'];

export function BottomNav() {
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const sheetRef = useDialogFocus<HTMLElement>(menuOpen, {
    onEscape: () => setMenuOpen(false),
  });
  const usuario = useAuthStore((state) => state.usuario);
  const alertCount = useAlertCount();
  const exportacionesPendientes = useNotificacionesAdminStore((state) => state.exportacionesPendientes);
  const updateAvailable = useUpdateStore((state) => state.available);
  const aiAvailable = useIaAvailabilityStore((state) => state.available);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard());

  const visibleNavItems = useMemo(
    () => getVisibleNavigationItems(usuario?.rol, {
      aiAvailable,
      dashboardAvailable: usuario?.rol === 'ADMIN' || canViewDashboard,
    }),
    [aiAvailable, canViewDashboard, usuario?.rol]
  );
  const primaryItemPaths = useMemo(() => {
    const dashboardVisible = usuario?.rol === 'ADMIN' || canViewDashboard;
    return dashboardVisible
      ? ['/dashboard', '/cuentas', '/extractos', '/importacion']
      : ['/extractos', '/cuentas', '/importacion', '/revision'];
  }, [canViewDashboard, usuario?.rol]);
  const primaryItems = useMemo(
    () => primaryItemPaths
      .map((path) => visibleNavItems.find((item) => item.to === path))
      .filter((item): item is NonNullable<typeof item> => Boolean(item)),
    [primaryItemPaths, visibleNavItems]
  );
  const secondaryItems = useMemo(
    () => visibleNavItems.filter((item) => !primaryItemPaths.includes(item.to)),
    [primaryItemPaths, visibleNavItems]
  );

  const hiddenBadgeCount = alertCount + exportacionesPendientes + (updateAvailable ? 1 : 0);
  const secondaryActive = secondaryItems.some((item) => location.pathname.startsWith(item.to));
  const secondaryGroups = useMemo(
    () =>
      SECONDARY_GROUP_ORDER
        .map((group) => ({
          group,
          items: secondaryItems.filter((item) => item.group === group),
        }))
        .filter((item) => item.items.length > 0),
    [secondaryItems]
  );

  const getBadgeLabel = (to: string) => {
    if (to === '/alertas' && alertCount > 0) {
      return `, ${alertCount} alertas activas`;
    }

    if (to === '/exportaciones' && usuario?.rol === 'ADMIN' && exportacionesPendientes > 0) {
      return `, ${exportacionesPendientes} exportaciones pendientes`;
    }

    if (to === '/configuracion' && updateAvailable) {
      return ', actualización disponible';
    }

    return '';
  };

  useEffect(() => {
    setMenuOpen(false);
  }, [location.pathname]);

  return (
    <>
      <nav className="bottom-nav" aria-label="Navegación inferior">
        {primaryItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            aria-label={item.label}
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
          aria-label={`Más opciones${hiddenBadgeCount > 0 ? `, ${hiddenBadgeCount} avisos pendientes` : ''}`}
          onClick={() => setMenuOpen((current) => !current)}
        >
          <span className="bottom-nav-icon" aria-hidden="true"><IconMenu /></span>
          <span>Más</span>
          {hiddenBadgeCount > 0 ? <span className="sidebar-alert-badge" aria-hidden="true">{hiddenBadgeCount}</span> : null}
        </button>
      </nav>

      {menuOpen ? (
        <div className="bottom-nav-sheet-backdrop" onClick={() => setMenuOpen(false)}>
          <section
            ref={sheetRef}
            id="bottom-nav-sheet"
            className="bottom-nav-sheet"
            role="dialog"
            aria-modal="true"
            aria-label="Menú de accesos"
            tabIndex={-1}
            onClick={(event) => event.stopPropagation()}
          >
            <header className="bottom-nav-sheet-header">
              <div>
                <strong>Más</strong>
                <p>Extractos, control y sistema.</p>
              </div>
              <CloseIconButton
                className="bottom-nav-sheet-close"
                onClick={() => setMenuOpen(false)}
                ariaLabel="Cerrar menú de accesos"
              />
            </header>

            <PaisScopeSelect />

            <div className="bottom-nav-sheet-sections">
              {secondaryGroups.map(({ group, items }) => (
                <section className="bottom-nav-sheet-section" key={group} aria-label={navigationGroups[group].label}>
                  <h3>{navigationGroups[group].label}</h3>
                  <div className="bottom-nav-sheet-grid">
                    {items.map((item) => {
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
                          aria-label={`${item.label}${getBadgeLabel(item.to)}`}
                          className={({ isActive }) =>
                            isActive ? 'bottom-nav-sheet-link bottom-nav-sheet-link--active' : 'bottom-nav-sheet-link'
                          }
                        >
                          <span className="bottom-nav-sheet-icon">{item.icon}</span>
                          <span>{item.label}</span>
                          {badge > 0 ? (
                            <span className={item.to === '/configuracion' ? 'sidebar-update-badge' : 'sidebar-alert-badge'} aria-hidden="true">
                              {item.to === '/configuracion' ? 'Nueva' : badge}
                            </span>
                          ) : null}
                        </NavLink>
                      );
                    })}
                  </div>
                </section>
              ))}
            </div>
          </section>
        </div>
      ) : null}
    </>
  );
}
