import { useEffect } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import { getVisibleNavigationItems, navigationGroups, type NavigationGroup } from '@/utils/navigation';
import { useAlertCount } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import { useNotificacionesAdminStore } from '@/stores/notificacionesAdminStore';
import { useUiStore } from '@/stores/uiStore';
import { useUpdateStore } from '@/stores/updateStore';

export function Sidebar() {
  const location = useLocation();
  const usuario = useAuthStore((state) => state.usuario);
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);
  const alertCount = useAlertCount();
  const exportacionesPendientes = useNotificacionesAdminStore((state) => state.exportacionesPendientes);
  const loadResumen = useNotificacionesAdminStore((state) => state.loadResumen);
  const clearNotificaciones = useNotificacionesAdminStore((state) => state.clear);
  const updateAvailable = useUpdateStore((state) => state.available);
  const checkUpdate = useUpdateStore((state) => state.check);
  const aiAvailable = useIaAvailabilityStore((state) => state.available);

  // Check for updates once per session when the user role is known, not on every navigation.
  useEffect(() => {
    if (usuario?.rol === 'ADMIN') {
      void checkUpdate();
    }
  }, [checkUpdate, usuario?.rol]);

  // Refresh notification counts on every navigation (lightweight).
  useEffect(() => {
    if (usuario?.rol === 'ADMIN') {
      void loadResumen();
      return;
    }
    clearNotificaciones();
  }, [clearNotificaciones, loadResumen, location.pathname, usuario?.rol]);

  const visibleNavItems = getVisibleNavigationItems(usuario?.rol, { aiAvailable });
  const groupOrder: NavigationGroup[] = ['operacion', 'control', 'sistema'];

  const getBadge = (to: string) => {
    if (to === '/alertas' && alertCount > 0) {
      return <span className="sidebar-alert-badge">{alertCount}</span>;
    }

    if (to === '/exportaciones' && usuario?.rol === 'ADMIN' && exportacionesPendientes > 0) {
      return <span className="sidebar-alert-badge">{exportacionesPendientes}</span>;
    }

    if (to === '/configuracion' && updateAvailable) {
      return <span className="sidebar-update-badge">!</span>;
    }

    return null;
  };

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

  return (
    <aside className={`app-sidebar${sidebarCollapsed ? ' app-sidebar--collapsed' : ''}`} aria-label="Navegación principal">
      <div className="app-brand" aria-label="Atlas Balance">
        <span className="app-brand-logo" aria-hidden="true" />
        <span className="app-brand-text" aria-hidden={sidebarCollapsed}>Atlas Balance</span>
      </div>
      <nav className="app-nav">
        {groupOrder.map((group) => {
          const items = visibleNavItems.filter((item) => item.group === group);
          if (items.length === 0) {
            return null;
          }

          return (
            <div className="app-nav-section" role="group" aria-label={navigationGroups[group].label} key={group}>
              <span className="app-nav-section-label" aria-hidden={sidebarCollapsed}>
                {navigationGroups[group].label}
              </span>
              {items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  aria-label={`${item.label}${getBadgeLabel(item.to)}`}
                  className={({ isActive }) =>
                    isActive ? 'app-nav-link app-nav-link--active' : 'app-nav-link'
                  }
                  title={item.label}
                >
                  <span className="app-nav-icon">{item.icon}</span>
                  <span className="app-nav-label" aria-hidden={sidebarCollapsed}>{item.label}</span>
                  {getBadge(item.to)}
                </NavLink>
              ))}
            </div>
          );
        })}
      </nav>
    </aside>
  );
}
