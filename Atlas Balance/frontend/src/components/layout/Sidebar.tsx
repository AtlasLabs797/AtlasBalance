import { useEffect, useState } from 'react';
import { NavLink } from 'react-router-dom';
import { PaisScopeSelect } from '@/components/layout/PaisScopeSelect';
import { getVisibleNavigationItems, navigationGroups, type NavigationGroup } from '@/utils/navigation';
import { useNotificacionesAdminQuery } from '@/hooks/queries/useNotificacionesAdminQuery';
import { useUpdateCheckQuery } from '@/hooks/queries/useUpdateCheckQuery';
import { useAlertCount } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import { useNotificacionesAdminStore } from '@/stores/notificacionesAdminStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { useUiStore } from '@/stores/uiStore';
import { useUpdateStore } from '@/stores/updateStore';

// V-02-03: version unica inyectada por Vite desde package.json (appVersion).
// Antes estaba hardcodeada y se desincronizaba de VERSION / Directory.Build.props.
// V-02-03 cierre: el reloj se refresca solo al cambiar de pestaña para no
// molestar a los usuarios que miran la sidebar fijamente.
const APP_VERSION_LABEL = (
  (import.meta.env.VITE_APP_VERSION as string | undefined)?.trim()
  ?? (import.meta.env.PACKAGE_VERSION as string | undefined)?.trim()
  ?? 'desarrollo'
);

function formatSidebarClock(value: Date) {
  return new Intl.DateTimeFormat('es-ES', {
    hour: '2-digit',
    minute: '2-digit',
  }).format(value);
}

export function Sidebar() {
  const usuario = useAuthStore((state) => state.usuario);
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);
  const alertCount = useAlertCount();
  const exportacionesPendientes = useNotificacionesAdminStore((state) => state.exportacionesPendientes);
  const updateAvailable = useUpdateStore((state) => state.available);
  const aiAvailable = useIaAvailabilityStore((state) => state.available);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard());
  const [now, setNow] = useState(() => new Date());

  // Suscripcion a caches de TanStack Query (notificaciones, version).
  // El refetchOnMount:'always' del hook de update garantiza la primera
  // comprobacion tras login; el de notificaciones se gestiona por
  // staleTime + refetchOnWindowFocus.
  useNotificacionesAdminQuery();
  useUpdateCheckQuery(false);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 30_000);
    return () => window.clearInterval(timer);
  }, []);

  const visibleNavItems = getVisibleNavigationItems(usuario?.rol, {
    aiAvailable,
    dashboardAvailable: usuario?.rol === 'ADMIN' || canViewDashboard,
  });
  const groupOrder: NavigationGroup[] = ['operacion', 'control', 'sistema'];

  const getBadge = (to: string) => {
    if (to === '/alertas' && alertCount > 0) {
      return <span className="sidebar-alert-badge" aria-hidden="true">{alertCount}</span>;
    }

    if (to === '/exportaciones' && usuario?.rol === 'ADMIN' && exportacionesPendientes > 0) {
      return <span className="sidebar-alert-badge" aria-hidden="true">{exportacionesPendientes}</span>;
    }

    if (to === '/configuracion' && updateAvailable) {
      return <span className="sidebar-update-badge" aria-hidden="true">!</span>;
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
    <aside
      className={`app-sidebar${sidebarCollapsed ? ' app-sidebar--collapsed' : ''}`}
      aria-label="Navegación principal"
    >
      <div className="app-brand" aria-label="Atlas Balance">
        <span className="app-brand-logo" aria-hidden="true" />
        <span className="app-brand-text" aria-hidden={sidebarCollapsed}>
          <span className="app-brand-name">Atlas Balance</span>
          <span className="app-brand-subtitle">by Atlas Labs</span>
        </span>
      </div>
      <PaisScopeSelect compact={sidebarCollapsed} />
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
      <div className="app-sidebar-footer" aria-hidden={sidebarCollapsed}>
        <span>{APP_VERSION_LABEL}</span>
        <time dateTime={now.toISOString()}>{formatSidebarClock(now)}</time>
      </div>
    </aside>
  );
}
