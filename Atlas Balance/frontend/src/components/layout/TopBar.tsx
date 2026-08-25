import { lazy, Suspense, useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { IconAiFace, IconMenu, IconMoon, IconSalir, IconSun } from '@/components/Icons';
import { navigationItems } from '@/utils/navigation';
import api, { clearSessionState } from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import { useUiStore } from '@/stores/uiStore';

const AiChatPanel = lazy(() =>
  import('@/components/ia/AiChatPanel').then((module) => ({ default: module.AiChatPanel }))
);

function getUserInitials(name?: string | null) {
  const parts = name?.trim().split(/\s+/).filter(Boolean) ?? [];
  const initials = parts.slice(0, 2).map((part) => part[0]?.toUpperCase()).join('');
  return initials || 'AB';
}

export function TopBar() {
  const navigate = useNavigate();
  const location = useLocation();
  const theme = useUiStore((state) => state.theme);
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);
  const blockingOverlayCount = useUiStore((state) => state.blockingOverlayCount);
  const toggleTheme = useUiStore((state) => state.toggleTheme);
  const toggleSidebar = useUiStore((state) => state.toggleSidebar);
  const usuario = useAuthStore((state) => state.usuario);
  const aiAvailable = useIaAvailabilityStore((state) => state.available);
  const [chatOpen, setChatOpen] = useState(false);
  const userName = usuario?.nombre_completo ?? 'Sin sesión';

  const pageContext = useMemo(() => {
    const exact = navigationItems.find((item) => item.to === location.pathname);
    if (exact) {
      return { title: exact.label, breadcrumb: 'Atlas Balance' };
    }

    const section = navigationItems
      .filter((item) => location.pathname.startsWith(`${item.to}/`))
      .sort((a, b) => b.to.length - a.to.length)[0];

    if (section) {
      return { title: section.label, breadcrumb: 'Detalle' };
    }

    return { title: 'Atlas Balance', breadcrumb: 'Operación local' };
  }, [location.pathname]);

  const handleLogout = async () => {
    try {
      await api.post('/auth/logout');
    } catch {
      // no-op
    } finally {
      // V-02.08: clearSessionState() tambien vacia el chat IA y el pais-scope
      // (logout() por si solo no lo hacia), evitando que el siguiente usuario
      // que abra sesion en la misma pestana vea datos financieros ajenos.
      clearSessionState();
      navigate('/login', { replace: true });
    }
  };

  useEffect(() => {
    if (!aiAvailable || blockingOverlayCount > 0) {
      setChatOpen(false);
    }
  }, [aiAvailable, blockingOverlayCount]);

  return (
    <>
      <header className="app-topbar">
        <div className="app-topbar-title">
          <button
            type="button"
            className={`sidebar-toggle${sidebarCollapsed ? ' sidebar-toggle--collapsed' : ''}`}
            onClick={toggleSidebar}
            aria-expanded={!sidebarCollapsed}
            aria-label={sidebarCollapsed ? 'Expandir navegación lateral' : 'Contraer navegación lateral'}
            title={sidebarCollapsed ? 'Expandir navegación lateral' : 'Contraer navegación lateral'}
          >
            <IconMenu />
          </button>
          <div className="app-topbar-heading">
            <span className="app-topbar-page">{pageContext.title}</span>
            <span className="app-topbar-breadcrumb">{pageContext.breadcrumb}</span>
          </div>
        </div>
        <div className="app-topbar-actions">
          <span className="app-topbar-user" title={userName}>
            <span className="app-topbar-avatar" aria-hidden="true">{getUserInitials(usuario?.nombre_completo)}</span>
            <span className="app-topbar-user-copy">
              <span>{userName}</span>
              <small>{usuario?.rol ?? 'Invitado'}</small>
            </span>
          </span>
          <button
            type="button"
            className="theme-toggle"
            onClick={toggleTheme}
            aria-pressed={theme === 'dark'}
            aria-label={`Cambiar a modo ${theme === 'light' ? 'oscuro' : 'claro'}`}
            title={`Cambiar a modo ${theme === 'light' ? 'oscuro' : 'claro'}`}
          >
            {theme === 'light' ? <IconMoon /> : <IconSun />}
          </button>
          <button type="button" className="logout-button" onClick={handleLogout} aria-label="Cerrar sesión">
            <IconSalir />
          </button>
        </div>
      </header>
      {aiAvailable && blockingOverlayCount === 0 ? (
        <div className="ai-floating-widget">
          <button
            type="button"
            className={`ai-floating-button${chatOpen ? ' ai-floating-button--active' : ''}`}
            onClick={() => setChatOpen((current) => !current)}
            aria-expanded={chatOpen}
            aria-label={chatOpen ? 'Cerrar chat IA' : 'Abrir chat IA'}
            title={chatOpen ? 'Cerrar chat IA' : 'Abrir chat IA'}
          >
            <IconAiFace />
          </button>
          {chatOpen ? (
            <div className="ai-floating-chat" role="dialog" aria-modal="false" aria-label="Chat flotante IA">
              <Suspense fallback={null}>
                <AiChatPanel compact onClose={() => setChatOpen(false)} />
              </Suspense>
            </div>
          ) : null}
        </div>
      ) : null}
    </>
  );
}
